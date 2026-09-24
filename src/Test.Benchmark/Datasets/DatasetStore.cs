namespace Test.Benchmark.Datasets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads and writes benchmark datasets in the neutral JSON format.
    /// </summary>
    public static class DatasetStore
    {
        #region Public-Members

        /// <summary>
        /// Serializer options shared by the benchmark (camelCase, case-insensitive, nulls omitted).
        /// </summary>
        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load a dataset file and validate its structure.
        /// </summary>
        /// <param name="path">Path to the dataset JSON.</param>
        /// <returns>The dataset.</returns>
        /// <exception cref="InvalidDataException">Thrown when the file is not a valid dataset.</exception>
        public static BenchmarkDataset Load(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            using FileStream stream = File.OpenRead(path);
            BenchmarkDataset? dataset = JsonSerializer.Deserialize<BenchmarkDataset>(stream, Json);
            if (dataset == null || dataset.Corpora == null || dataset.Corpora.Count == 0)
                throw new InvalidDataException("'" + path + "' is not a benchmark dataset (no corpora).");
            if (string.IsNullOrEmpty(dataset.Name)) dataset.Name = Path.GetFileNameWithoutExtension(path);

            foreach (BenchmarkCorpus corpus in dataset.Corpora)
            {
                HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (BenchmarkDocument document in corpus.Documents)
                {
                    if (!ids.Add(document.Id)) throw new InvalidDataException("Corpus '" + corpus.Id + "' has duplicate document id '" + document.Id + "'.");
                }

                foreach (BenchmarkQuery query in corpus.Queries)
                {
                    foreach (string relevant in query.Relevant)
                    {
                        if (!ids.Contains(relevant))
                            throw new InvalidDataException("Query '" + query.Id + "' in corpus '" + corpus.Id + "' references unknown document '" + relevant + "'.");
                    }
                }
            }

            return dataset;
        }

        /// <summary>
        /// Write a dataset file.
        /// </summary>
        /// <param name="dataset">The dataset.</param>
        /// <param name="path">Destination path.</param>
        public static void Save(BenchmarkDataset dataset, string path)
        {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using FileStream stream = File.Create(path);
            JsonSerializer.Serialize(stream, dataset, Json);
        }

        #endregion
    }
}
