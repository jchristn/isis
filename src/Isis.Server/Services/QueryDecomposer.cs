namespace Isis.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;
    using Isis.Core.Recall;

    /// <summary>
    /// Splits a question that asks about several distinct things into self-contained search queries, using whatever
    /// inference endpoint the caller supplies. The prompt is model-agnostic; a reply that cannot be read, or a question
    /// the model keeps whole, yields no sub-queries and the search runs on the original question alone.
    /// </summary>
    public class QueryDecomposer
    {
        #region Public-Members

        /// <summary>
        /// Most sub-queries returned. Minimum 1, maximum 4, default 3.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [1, 4].</exception>
        public int MaxSubQueries
        {
            get
            {
                return _MaxSubQueries;
            }
            set
            {
                if (value < 1 || value > 4) throw new ArgumentOutOfRangeException(nameof(MaxSubQueries), "MaxSubQueries must be between 1 and 4.");
                _MaxSubQueries = value;
            }
        }

        /// <summary>
        /// Questions with fewer words than this are not sent for splitting. Minimum 1, default 6.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below 1.</exception>
        public int MinWords
        {
            get
            {
                return _MinWords;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MinWords), "MinWords must be at least 1.");
                _MinWords = value;
            }
        }

        /// <summary>
        /// Longest wait for the model before searching the original question alone. Minimum 1 second, default 20.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below 1 second.</exception>
        public TimeSpan Timeout
        {
            get
            {
                return _Timeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be at least 1 second.");
                _Timeout = value;
            }
        }

        #endregion

        #region Private-Members

        private readonly InferenceService _InferenceService;
        private int _MaxSubQueries = 3;
        private int _MinWords = 6;
        private TimeSpan _Timeout = TimeSpan.FromSeconds(20);

        private const string _SystemPrompt = "You rewrite search questions for a retrieval system. You never answer them.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the decomposer.
        /// </summary>
        /// <param name="inferenceService">The inference service used to call the model.</param>
        /// <exception cref="ArgumentNullException">Thrown when inferenceService is null.</exception>
        public QueryDecomposer(InferenceService inferenceService)
        {
            _InferenceService = inferenceService ?? throw new ArgumentNullException(nameof(inferenceService));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Split a question into sub-queries.
        /// </summary>
        /// <param name="endpoint">The inference endpoint to ask.</param>
        /// <param name="question">The question.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The sub-queries, or an empty list when the question asks about one thing or the reply could not be
        /// used. Never contains the original question itself.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or question is null.</exception>
        public async Task<List<string>> DecomposeAsync(ModelEndpoint endpoint, string question, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (question == null) throw new ArgumentNullException(nameof(question));

            string trimmed = question.Trim();
            if (trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < _MinWords) return new List<string>();

            string prompt =
                "If the question below asks about two or more distinct things, rewrite it as up to " + _MaxSubQueries +
                " short, self-contained search queries, one per thing, keeping names and identifiers exactly as written. " +
                "If it asks about one thing, return it unchanged as the only query. " +
                "Reply with JSON only, in the form {\"queries\": [\"...\"]}.\n\nQuestion: " + trimmed;

            try
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(_Timeout);
                string reply = await _InferenceService.CompleteAsync(endpoint, _SystemPrompt, prompt, cts.Token).ConfigureAwait(false);
                return Parse(reply, trimmed, _MaxSubQueries);
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                // Splitting is an optimization; any failure searches the original question alone.
                return new List<string>();
            }
        }

        /// <summary>
        /// Read the sub-queries out of a model reply.
        /// </summary>
        /// <param name="reply">The model's reply.</param>
        /// <param name="question">The original question.</param>
        /// <param name="max">Most sub-queries to keep.</param>
        /// <returns>The sub-queries, or an empty list when the reply holds fewer than two distinct ones.</returns>
        public static List<string> Parse(string? reply, string question, int max)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(reply)) return result;

            // Reasoning models may prefix their answer with a thinking block.
            string text = Regex.Replace(reply, "<think>.*?</think>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            int start = text.IndexOfAny(new[] { '{', '[' });
            int end = Math.Max(text.LastIndexOf('}'), text.LastIndexOf(']'));
            if (start < 0 || end <= start) return result;

            try
            {
                using JsonDocument document = JsonDocument.Parse(text.Substring(start, end - start + 1));
                JsonElement array = document.RootElement;
                if (array.ValueKind == JsonValueKind.Object && !array.TryGetProperty("queries", out array)) return result;
                if (array.ValueKind != JsonValueKind.Array) return result;

                foreach (JsonElement item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    string query = (item.GetString() ?? string.Empty).Trim();
                    if (query.Length == 0 || result.Contains(query, StringComparer.OrdinalIgnoreCase)) continue;
                    result.Add(query);
                    if (result.Count >= max) break;
                }
            }
            catch (JsonException)
            {
                return new List<string>();
            }

            // A single query (the question kept whole) means there is nothing to split.
            if (result.Count < 2) return new List<string>();
            result.RemoveAll(q => string.Equals(q, question, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        #endregion
    }
}
