namespace Isis.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;
    using Isis.Core.Recall;

    /// <summary>
    /// Turns a follow-up chat message ("and for staging?") into a standalone search query using the conversation
    /// before it, so retrieval can find the memories the follow-up is about. The prompt is model-agnostic; a reply that
    /// cannot be used, or a message the model keeps unchanged, yields no rewrite and retrieval uses the message as sent.
    /// </summary>
    public class ConversationRewriter
    {
        #region Public-Members

        /// <summary>
        /// Most earlier messages given to the model, newest kept. Minimum 1, maximum 20, default 6 (three exchanges).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [1, 20].</exception>
        public int MaxTurns
        {
            get
            {
                return _MaxTurns;
            }
            set
            {
                if (value < 1 || value > 20) throw new ArgumentOutOfRangeException(nameof(MaxTurns), "MaxTurns must be between 1 and 20.");
                _MaxTurns = value;
            }
        }

        /// <summary>
        /// Longest text kept from each earlier message; longer messages keep their start. Minimum 100, maximum 10000,
        /// default 1000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [100, 10000].</exception>
        public int MaxTurnChars
        {
            get
            {
                return _MaxTurnChars;
            }
            set
            {
                if (value < 100 || value > 10000) throw new ArgumentOutOfRangeException(nameof(MaxTurnChars), "MaxTurnChars must be between 100 and 10000.");
                _MaxTurnChars = value;
            }
        }

        /// <summary>
        /// Longest wait for the model before retrieving with the message as sent. Minimum 1 second, default 20.
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
        private int _MaxTurns = 6;
        private int _MaxTurnChars = 1000;
        private TimeSpan _Timeout = TimeSpan.FromSeconds(20);

        private const string _SystemPrompt = "You rewrite chat messages into search queries for a retrieval system. You never answer them.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the rewriter.
        /// </summary>
        /// <param name="inferenceService">The inference service used to call the model.</param>
        /// <exception cref="ArgumentNullException">Thrown when inferenceService is null.</exception>
        public ConversationRewriter(InferenceService inferenceService)
        {
            _InferenceService = inferenceService ?? throw new ArgumentNullException(nameof(inferenceService));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Rewrite the latest message as a standalone query.
        /// </summary>
        /// <param name="endpoint">The inference endpoint to ask.</param>
        /// <param name="history">The earlier messages, oldest first. Null or empty skips the model call.</param>
        /// <param name="question">The latest message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The standalone query, or null when there is no history, the message already stands alone, or the
        /// reply could not be used.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or question is null.</exception>
        public async Task<string?> RewriteAsync(ModelEndpoint endpoint, List<ChatTurn>? history, string question, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (question == null) throw new ArgumentNullException(nameof(question));

            string conversation = FormatConversation(history, _MaxTurns, _MaxTurnChars);
            if (conversation.Length == 0) return null;

            string trimmed = question.Trim();
            string prompt =
                "Rewrite the latest message as one standalone search query that can be understood without the conversation. " +
                "Replace references such as \"it\", \"that\", \"those\", or \"the second one\" with the names they refer to, " +
                "keep names and identifiers exactly as written, and do not answer it. " +
                "If the latest message already stands alone, return it unchanged. " +
                "Reply with the query only, on one line.\n\n" +
                "Conversation:\n" + conversation + "\nLatest message: " + trimmed;

            try
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(_Timeout);
                string reply = await _InferenceService.CompleteAsync(endpoint, _SystemPrompt, prompt, cts.Token).ConfigureAwait(false);
                return Parse(reply, trimmed);
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                // Rewriting is an optimization; any failure retrieves with the message as sent.
                return null;
            }
        }

        /// <summary>
        /// Format the most recent earlier messages as "User: ..." and "Assistant: ..." lines, oldest first.
        /// </summary>
        /// <param name="history">The earlier messages, oldest first.</param>
        /// <param name="maxTurns">Most messages to keep, newest kept.</param>
        /// <param name="maxTurnChars">Longest text kept from each message.</param>
        /// <returns>The formatted conversation, or an empty string when there is nothing to show.</returns>
        public static string FormatConversation(List<ChatTurn>? history, int maxTurns, int maxTurnChars)
        {
            if (history == null || history.Count == 0 || maxTurns < 1) return string.Empty;

            List<string> lines = new List<string>();
            for (int i = history.Count - 1; i >= 0 && lines.Count < maxTurns; i--)
            {
                ChatTurn? turn = history[i];
                string content = Regex.Replace((turn?.Content ?? string.Empty).Trim(), "\\s+", " ");
                if (content.Length == 0) continue;
                if (content.Length > maxTurnChars) content = content.Substring(0, maxTurnChars) + "...";
                string role = string.Equals(turn!.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
                lines.Add(role + ": " + content);
            }

            if (lines.Count == 0) return string.Empty;
            lines.Reverse();
            StringBuilder sb = new StringBuilder();
            foreach (string line in lines) sb.Append(line).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Read the standalone query out of a model reply.
        /// </summary>
        /// <param name="reply">The model's reply.</param>
        /// <param name="question">The latest message as sent.</param>
        /// <returns>The query, or null when the reply is empty, repeats the message, or is too long to be a query
        /// (the model answered instead of rewriting).</returns>
        public static string? Parse(string? reply, string question)
        {
            if (string.IsNullOrWhiteSpace(reply)) return null;

            // Reasoning models may prefix their answer with a thinking block.
            string text = Regex.Replace(reply, "<think>.*?</think>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            string? line = null;
            foreach (string candidate in text.Split('\n'))
            {
                if (candidate.Trim().Length == 0) continue;
                line = candidate.Trim();
                break;
            }

            if (line == null) return null;
            line = Regex.Replace(line, "^(standalone\\s+)?(search\\s+)?(query|question|rewrite|rewritten query)\\s*:\\s*", string.Empty, RegexOptions.IgnoreCase);
            line = line.Trim().Trim('"', '\'', '`', '“', '”').Trim();

            int limit = Math.Max(300, (question ?? string.Empty).Length * 4);
            if (line.Length == 0 || line.Length > limit) return null;
            if (string.Equals(line, (question ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            return line;
        }

        #endregion
    }
}
