namespace Test.Benchmark.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A point-in-time scrape of the Isis Prometheus endpoint. Two snapshots bracket a benchmark phase; their
    /// difference gives the server-side time spent per stage (embedding, store search, DB, ...) using the
    /// histograms Isis already exports, without any extra instrumentation.
    /// </summary>
    public class PrometheusSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Histogram families (without the _sum/_count suffix) reported in a stage breakdown, in pipeline order.
        /// </summary>
        public static readonly string[] StageFamilies = new string[]
        {
            "isis_memory_search_duration_seconds",
            "isis_embedding_duration_seconds",
            "isis_store_search_duration_seconds",
            "isis_memory_upsert_duration_seconds",
            "isis_store_upsert_duration_seconds",
            "isis_store_op_duration_seconds",
            "isis_db_query_duration_seconds",
            "isis_chat_ask_duration_seconds",
            "isis_inference_duration_seconds"
        };

        /// <summary>
        /// True when the scrape succeeded.
        /// </summary>
        public bool Available { get; private set; } = false;

        #endregion

        #region Private-Members

        private readonly Dictionary<string, double> _Totals = new Dictionary<string, double>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Scrape a Prometheus text endpoint. A failed scrape yields an unavailable snapshot rather than throwing,
        /// because metrics are optional context for a benchmark.
        /// </summary>
        /// <param name="http">HTTP client.</param>
        /// <param name="url">Metrics URL, or null to skip.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The snapshot.</returns>
        public static async Task<PrometheusSnapshot> CaptureAsync(HttpClient http, string? url, CancellationToken token)
        {
            PrometheusSnapshot snapshot = new PrometheusSnapshot();
            if (string.IsNullOrEmpty(url)) return snapshot;

            try
            {
                string text = await http.GetStringAsync(url, token).ConfigureAwait(false);
                snapshot.Parse(text);
                snapshot.Available = true;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
            }

            return snapshot;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Per-stage mean latency and call count between an earlier snapshot and this one.
        /// </summary>
        /// <param name="before">The earlier snapshot.</param>
        /// <returns>Stage family to breakdown; empty when either snapshot is unavailable.</returns>
        public Dictionary<string, StageBreakdown> Since(PrometheusSnapshot before)
        {
            Dictionary<string, StageBreakdown> stages = new Dictionary<string, StageBreakdown>(StringComparer.Ordinal);
            if (!Available || before == null || !before.Available) return stages;

            foreach (string family in StageFamilies)
            {
                double count = Value(family + "_count") - before.Value(family + "_count");
                double sum = Value(family + "_sum") - before.Value(family + "_sum");
                if (count <= 0) continue;
                stages[family.Replace("isis_", string.Empty).Replace("_duration_seconds", string.Empty)] = new StageBreakdown
                {
                    Count = (long)Math.Round(count),
                    MeanMs = Math.Round(sum * 1000.0 / count, 2),
                    TotalMs = Math.Round(sum * 1000.0, 1)
                };
            }

            return stages;
        }

        #endregion

        #region Private-Methods

        private double Value(string name)
        {
            return _Totals.TryGetValue(name, out double value) ? value : 0.0;
        }

        private void Parse(string text)
        {
            // Sum each metric across all label sets: "name{labels} value" or "name value".
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int brace = line.IndexOf('{');
                int space = line.LastIndexOf(' ');
                if (space <= 0) continue;
                string name = brace > 0 && brace < space ? line.Substring(0, brace) : line.Substring(0, line.IndexOf(' '));
                if (!name.StartsWith("isis_", StringComparison.Ordinal)) continue;
                if (!double.TryParse(line.Substring(space + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) continue;

                _Totals[name] = (_Totals.TryGetValue(name, out double existing) ? existing : 0.0) + value;
            }
        }

        #endregion
    }
}
