namespace Test.Benchmark.Reporting
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using Test.Benchmark.Datasets;

    /// <summary>
    /// Published baselines per dataset, read from benchmarks/baselines.json. Reports use it to show Isis's score next to
    /// published results and the net difference.
    /// </summary>
    public class BaselineCatalog
    {
        #region Public-Members

        /// <summary>
        /// Baselines keyed by dataset name.
        /// </summary>
        public Dictionary<string, DatasetBaselines> Datasets { get; set; } = new Dictionary<string, DatasetBaselines>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the catalog. A missing file yields an empty catalog.
        /// </summary>
        /// <param name="path">Path to baselines.json.</param>
        /// <returns>The catalog.</returns>
        /// <exception cref="InvalidDataException">Thrown when the file exists but cannot be parsed.</exception>
        public static BaselineCatalog Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new BaselineCatalog();
            try
            {
                BaselineCatalog? catalog = JsonSerializer.Deserialize<BaselineCatalog>(File.ReadAllText(path), DatasetStore.Json);
                if (catalog == null) return new BaselineCatalog();
                catalog.Datasets = new Dictionary<string, DatasetBaselines>(catalog.Datasets, StringComparer.OrdinalIgnoreCase);
                return catalog;
            }
            catch (JsonException e)
            {
                throw new InvalidDataException("Unable to parse " + path + ": " + e.Message);
            }
        }

        /// <summary>
        /// Find the baselines for a dataset.
        /// </summary>
        /// <param name="dataset">The dataset name.</param>
        /// <returns>The dataset's entry, or null.</returns>
        public DatasetBaselines? For(string dataset)
        {
            return Datasets.TryGetValue(dataset ?? string.Empty, out DatasetBaselines? entry) ? entry : null;
        }

        #endregion
    }
}
