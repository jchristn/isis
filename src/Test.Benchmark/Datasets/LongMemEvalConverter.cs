namespace Test.Benchmark.Datasets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Converts LongMemEval (longmemeval_s_cleaned.json / longmemeval_m_cleaned.json) into the neutral format.
    /// Every question has its own haystack of chat sessions, so each question becomes its own corpus: sessions are
    /// documents (id = session id) and the relevant documents are the question's evidence sessions. Abstention
    /// questions (ids ending in _abs) have no evidence and are kept as unanswerable queries.
    /// </summary>
    public static class LongMemEvalConverter
    {
        #region Public-Methods

        /// <summary>
        /// Convert a LongMemEval file, optionally sampling a stratified subset of questions.
        /// </summary>
        /// <param name="path">Path to the LongMemEval JSON file.</param>
        /// <param name="name">Dataset name.</param>
        /// <param name="limit">Maximum questions to keep (0 = all). Sampling is stratified by question type.</param>
        /// <param name="seed">Sampling seed, so a limited run is reproducible.</param>
        /// <returns>The dataset.</returns>
        public static BenchmarkDataset Convert(string path, string name, int limit, int seed)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            using FileStream stream = File.OpenRead(path);
            using JsonDocument doc = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 256 });

            List<JsonElement> questions = new List<JsonElement>();
            foreach (JsonElement question in doc.RootElement.EnumerateArray()) questions.Add(question);

            List<JsonElement> selected = limit > 0 && limit < questions.Count ? Sample(questions, limit, seed) : questions;

            BenchmarkDataset dataset = new BenchmarkDataset { Name = name };
            foreach (JsonElement question in selected)
            {
                dataset.Corpora.Add(ConvertQuestion(question));
            }

            dataset.Description = "LongMemEval (" + Path.GetFileName(path) + "): " + dataset.Corpora.Count + " of " + questions.Count
                + " questions, one haystack corpus each" + (limit > 0 ? " (stratified sample, seed " + seed + ")" : string.Empty) + ".";
            return dataset;
        }

        #endregion

        #region Private-Methods

        private static string TypeOf(JsonElement question)
        {
            string id = question.GetProperty("question_id").GetString() ?? string.Empty;
            if (id.EndsWith("_abs", StringComparison.Ordinal)) return "abstention";
            return question.GetProperty("question_type").GetString() ?? "unknown";
        }

        private static List<JsonElement> Sample(List<JsonElement> questions, int limit, int seed)
        {
            // Deterministic stratified sample: order each type's questions by a seeded hash of the id, then take
            // round-robin across types so every type is represented.
            SortedDictionary<string, List<JsonElement>> byType = new SortedDictionary<string, List<JsonElement>>(StringComparer.Ordinal);
            foreach (JsonElement question in questions)
            {
                string type = TypeOf(question);
                if (!byType.TryGetValue(type, out List<JsonElement>? list))
                {
                    list = new List<JsonElement>();
                    byType[type] = list;
                }

                list.Add(question);
            }

            List<Queue<JsonElement>> queues = new List<Queue<JsonElement>>();
            foreach (List<JsonElement> list in byType.Values)
            {
                queues.Add(new Queue<JsonElement>(list.OrderBy(q => StableHash((q.GetProperty("question_id").GetString() ?? string.Empty) + ":" + seed))));
            }

            List<JsonElement> selected = new List<JsonElement>();
            while (selected.Count < limit && queues.Any(q => q.Count > 0))
            {
                foreach (Queue<JsonElement> queue in queues)
                {
                    if (selected.Count >= limit) break;
                    if (queue.Count > 0) selected.Add(queue.Dequeue());
                }
            }

            return selected;
        }

        private static BenchmarkCorpus ConvertQuestion(JsonElement question)
        {
            string questionId = question.GetProperty("question_id").GetString() ?? string.Empty;
            BenchmarkCorpus corpus = new BenchmarkCorpus { Id = questionId };
            corpus.Categories.Add(new BenchmarkCategory { Name = "sessions", Description = "Past chat sessions with the user." });

            List<string> sessionIds = question.GetProperty("haystack_session_ids").EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
            List<string> dates = question.GetProperty("haystack_dates").EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
            int index = 0;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement session in question.GetProperty("haystack_sessions").EnumerateArray())
            {
                string sessionId = index < sessionIds.Count ? sessionIds[index] : "session-" + index;
                string date = index < dates.Count ? dates[index] : string.Empty;
                index++;
                if (!seen.Add(sessionId)) continue;

                StringBuilder body = new StringBuilder();
                if (!string.IsNullOrEmpty(date)) body.Append("Session date: ").Append(date).Append("\n\n");
                foreach (JsonElement turn in session.EnumerateArray())
                {
                    string role = turn.TryGetProperty("role", out JsonElement r) ? (r.GetString() ?? "user") : "user";
                    string content = turn.TryGetProperty("content", out JsonElement c) ? (c.GetString() ?? string.Empty) : string.Empty;
                    body.Append(role).Append(": ").Append(content.Trim()).Append("\n\n");
                }

                corpus.Documents.Add(new BenchmarkDocument
                {
                    Id = sessionId,
                    Category = "sessions",
                    Title = "Chat session " + date,
                    Body = body.ToString().TrimEnd(),
                    Date = date
                });
            }

            BenchmarkQuery query = new BenchmarkQuery
            {
                Id = questionId,
                Text = question.GetProperty("question").GetString() ?? string.Empty,
                Type = TypeOf(question),
                Answer = question.GetProperty("answer").ToString(),
                Date = question.TryGetProperty("question_date", out JsonElement qd) ? qd.GetString() : null
            };

            if (query.Type != "abstention")
            {
                foreach (JsonElement evidence in question.GetProperty("answer_session_ids").EnumerateArray())
                {
                    string id = evidence.GetString() ?? string.Empty;
                    if (seen.Contains(id) && !query.Relevant.Contains(id)) query.Relevant.Add(id);
                }
            }
            else
            {
                query.Answer = "NOT_IN_MEMORY";
            }

            corpus.Queries.Add(query);
            return corpus;
        }

        private static ulong StableHash(string value)
        {
            // FNV-1a: string.GetHashCode is randomized per process, which would break reproducible sampling.
            ulong hash = 14695981039346656037UL;
            foreach (char ch in value)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }

            return hash;
        }

        #endregion
    }
}
