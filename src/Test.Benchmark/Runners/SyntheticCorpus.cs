namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Test.Benchmark.Datasets;

    /// <summary>
    /// Builds a corpus of a requested size for load and scale tests, either by cycling the documents of a real
    /// dataset (realistic text, so full-text search behaves normally) or from a deterministic pseudo-vocabulary.
    /// </summary>
    public class SyntheticCorpus
    {
        #region Public-Members

        /// <summary>
        /// The corpus as a single-corpus dataset (so the scope provisioner can ingest and reuse it).
        /// </summary>
        public BenchmarkDataset Dataset { get; } = new BenchmarkDataset();

        /// <summary>
        /// Query texts to draw search operations from.
        /// </summary>
        public List<string> Queries { get; } = new List<string>();

        /// <summary>
        /// Body texts to draw short upserts from.
        /// </summary>
        public List<string> ShortBodies { get; } = new List<string>();

        /// <summary>
        /// Long body texts (several thousand tokens, so they chunk) to draw long upserts from.
        /// </summary>
        public List<string> LongBodies { get; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build a corpus.
        /// </summary>
        /// <param name="source">Optional dataset whose documents and queries are used as templates.</param>
        /// <param name="size">Number of documents.</param>
        /// <param name="seed">Random seed.</param>
        /// <returns>The corpus.</returns>
        public static SyntheticCorpus Build(BenchmarkDataset? source, int size, int seed)
        {
            SyntheticCorpus corpus = new SyntheticCorpus();
            Random random = new Random(seed);
            BenchmarkCorpus target = new BenchmarkCorpus { Id = "n" + size };
            target.Categories.Add(new BenchmarkCategory { Name = "notes", Description = "Preloaded corpus" });
            target.Categories.Add(new BenchmarkCategory { Name = "load", Description = "Memories written during the load test" });

            List<string> templates = new List<string>();
            if (source != null)
            {
                foreach (BenchmarkCorpus c in source.Corpora)
                {
                    templates.AddRange(c.Documents.Select(d => d.Body).Where(b => !string.IsNullOrWhiteSpace(b)));
                    corpus.Queries.AddRange(c.Queries.Select(q => q.Text));
                }
            }

            List<string> vocabulary = BuildVocabulary(random);
            if (templates.Count == 0)
            {
                for (int i = 0; i < 2000; i++) templates.Add(RandomText(random, vocabulary, 40 + random.Next(110)));
            }

            if (corpus.Queries.Count == 0)
            {
                for (int i = 0; i < 1000; i++) corpus.Queries.Add(RandomText(random, vocabulary, 3 + random.Next(6)));
            }

            for (int i = 0; i < size; i++)
            {
                string template = templates[i % templates.Count];
                if (template.Length > 1500) template = template.Substring(0, 1500);
                int variant = i / templates.Count;
                target.Documents.Add(new BenchmarkDocument
                {
                    Id = "d" + i.ToString("D7"),
                    Category = "notes",
                    Title = "Document " + i,
                    Body = variant == 0 ? template : template + "\n\n(variant " + variant + ")"
                });
            }

            corpus.ShortBodies.AddRange(templates.Take(500).Select(t => t.Length > 1500 ? t.Substring(0, 1500) : t));
            for (int i = 0; i < 20; i++)
            {
                StringBuilder builder = new StringBuilder();
                while (builder.Length < 9000) builder.Append(templates[random.Next(templates.Count)]).Append("\n\n");
                corpus.LongBodies.Add(builder.ToString());
            }

            corpus.Dataset.Name = "load" + (source != null ? "-" + source.Name : "-synthetic");
            corpus.Dataset.Description = "Synthetic load corpus of " + size + " documents" + (source != null ? " cycled from " + source.Name : string.Empty) + ".";
            corpus.Dataset.Corpora.Add(target);
            return corpus;
        }

        #endregion

        #region Private-Methods

        private static List<string> BuildVocabulary(Random random)
        {
            string[] syllables = new string[] { "ka", "lo", "mi", "ren", "sa", "tor", "vel", "qui", "dan", "fe", "gor", "hal", "is", "jun", "nex", "op", "pra", "ul", "wen", "zy" };
            HashSet<string> words = new HashSet<string>(StringComparer.Ordinal);
            while (words.Count < 3000)
            {
                int parts = 2 + random.Next(2);
                StringBuilder word = new StringBuilder();
                for (int i = 0; i < parts; i++) word.Append(syllables[random.Next(syllables.Length)]);
                words.Add(word.ToString());
            }

            return words.ToList();
        }

        private static string RandomText(Random random, List<string> vocabulary, int words)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < words; i++)
            {
                if (i > 0) text.Append(' ');
                // Zipf-like skew so some words are common, as in real text.
                int index = (int)(vocabulary.Count * Math.Pow(random.NextDouble(), 3));
                text.Append(vocabulary[Math.Min(index, vocabulary.Count - 1)]);
            }

            return text.ToString();
        }

        #endregion
    }
}
