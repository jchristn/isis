namespace Test.Benchmark.Datasets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;

    /// <summary>
    /// Converts a BEIR-format dataset directory (corpus.jsonl, queries.jsonl, qrels/{split}.tsv) into the
    /// neutral format. Only queries present in the chosen qrels split are kept.
    /// </summary>
    public static class BeirConverter
    {
        #region Public-Methods

        /// <summary>
        /// Convert a BEIR directory.
        /// </summary>
        /// <param name="directory">The extracted BEIR dataset directory.</param>
        /// <param name="name">Dataset name.</param>
        /// <param name="split">Qrels split (test, dev, train).</param>
        /// <returns>The dataset with a single corpus.</returns>
        public static BenchmarkDataset Convert(string directory, string name, string split)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentNullException(nameof(directory));

            Dictionary<string, Dictionary<string, int>> qrels = ReadQrels(Path.Combine(directory, "qrels", split + ".tsv"));

            BenchmarkCorpus corpus = new BenchmarkCorpus { Id = "corpus" };
            corpus.Categories.Add(new BenchmarkCategory { Name = "corpus", Description = "BEIR " + name + " corpus" });

            foreach (string line in File.ReadLines(Path.Combine(directory, "corpus.jsonl")))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using JsonDocument doc = JsonDocument.Parse(line);
                string id = doc.RootElement.GetProperty("_id").GetString() ?? string.Empty;
                string title = doc.RootElement.TryGetProperty("title", out JsonElement t) ? (t.GetString() ?? string.Empty) : string.Empty;
                string text = doc.RootElement.TryGetProperty("text", out JsonElement x) ? (x.GetString() ?? string.Empty) : string.Empty;
                corpus.Documents.Add(new BenchmarkDocument
                {
                    Id = id,
                    Category = "corpus",
                    Title = string.IsNullOrWhiteSpace(title) ? null : title,
                    Body = string.IsNullOrWhiteSpace(title) ? text : title + "\n\n" + text
                });
            }

            foreach (string line in File.ReadLines(Path.Combine(directory, "queries.jsonl")))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using JsonDocument doc = JsonDocument.Parse(line);
                string id = doc.RootElement.GetProperty("_id").GetString() ?? string.Empty;
                if (!qrels.TryGetValue(id, out Dictionary<string, int>? grades)) continue;

                BenchmarkQuery query = new BenchmarkQuery
                {
                    Id = id,
                    Text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty,
                    Type = "beir",
                    Grades = grades
                };
                foreach (KeyValuePair<string, int> grade in grades)
                {
                    if (grade.Value > 0) query.Relevant.Add(grade.Key);
                }

                corpus.Queries.Add(query);
            }

            BenchmarkDataset dataset = new BenchmarkDataset
            {
                Name = name,
                Description = "BEIR " + name + " (" + split + " split): " + corpus.Documents.Count + " documents, " + corpus.Queries.Count + " queries."
            };
            dataset.Corpora.Add(corpus);
            return dataset;
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, Dictionary<string, int>> ReadQrels(string path)
        {
            Dictionary<string, Dictionary<string, int>> qrels = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            bool first = true;
            foreach (string line in File.ReadLines(path))
            {
                if (first)
                {
                    first = false;
                    if (line.StartsWith("query-id", StringComparison.OrdinalIgnoreCase)) continue;
                }

                string[] parts = line.Split('\t');
                if (parts.Length < 3) continue;
                if (!qrels.TryGetValue(parts[0], out Dictionary<string, int>? grades))
                {
                    grades = new Dictionary<string, int>(StringComparer.Ordinal);
                    qrels[parts[0]] = grades;
                }

                grades[parts[1]] = int.Parse(parts[2], CultureInfo.InvariantCulture);
            }

            return qrels;
        }

        #endregion
    }
}
