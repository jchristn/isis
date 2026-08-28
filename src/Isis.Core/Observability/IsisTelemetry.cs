namespace Isis.Core.Observability
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Core telemetry contract: the <see cref="System.Diagnostics.Metrics.Meter"/> and
    /// <see cref="System.Diagnostics.ActivitySource"/> that the data-access, store, and application layers emit
    /// into. The names ("Isis") are a stable contract that the observability host subscribes to by name.
    /// Instrumented code depends only on the .NET base class library; all OpenTelemetry wiring is owned by the
    /// host, so these instruments are cheap no-ops until something subscribes.
    /// </summary>
    public static class IsisTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Meter name for the Isis instrumentation. Subscribe to this name to collect its metrics.
        /// </summary>
        public const string MeterName = "Isis";

        /// <summary>
        /// Activity source name for the Isis instrumentation. Subscribe to this name to collect its traces.
        /// </summary>
        public const string ActivitySourceName = "Isis";

        /// <summary>
        /// Isis meter.
        /// </summary>
        public static readonly Meter Meter = new Meter(MeterName);

        /// <summary>
        /// Isis activity source.
        /// </summary>
        public static readonly ActivitySource ActivitySource = new ActivitySource(ActivitySourceName);

        // ----- Tag keys (dotted, lowercase; low cardinality only) -----

        /// <summary>Metric/span tag key for the SQL operation verb (select, insert, update, delete, ddl, other).</summary>
        public const string TagDbOperation = "db.operation";

        /// <summary>Metric/span tag key for the outcome (success or error).</summary>
        public const string TagOutcome = "isis.outcome";

        /// <summary>Metric/span tag key for the search mode.</summary>
        public const string TagSearchMode = "search.mode";

        /// <summary>Metric/span tag key for a remote endpoint (host).</summary>
        public const string TagEndpoint = "endpoint";

        /// <summary>Metric/span tag key for a model identifier.</summary>
        public const string TagModel = "model";

        /// <summary>Metric/span tag key for a scope identifier.</summary>
        public const string TagScope = "scope";

        /// <summary>Metric/span tag key for a backing store name.</summary>
        public const string TagStore = "store";

        /// <summary>Metric/span tag key for an operation name.</summary>
        public const string TagOperation = "operation";

        /// <summary>Metric/span tag key indicating whether a request was streamed.</summary>
        public const string TagStreaming = "streaming";

        // ----- Database instruments -----

        /// <summary>Duration of a single database query, in seconds.</summary>
        public static readonly Histogram<double> DbQueryDuration = Meter.CreateHistogram<double>(
            "isis.db.query.duration", "s", "Duration of a database query.");

        /// <summary>Count of database queries executed.</summary>
        public static readonly Counter<long> DbQueries = Meter.CreateCounter<long>(
            "isis.db.queries", "{query}", "Count of database queries executed.");

        /// <summary>Database queries currently executing.</summary>
        public static readonly UpDownCounter<long> DbActiveQueries = Meter.CreateUpDownCounter<long>(
            "isis.db.active_queries", "{query}", "Database queries currently executing.");

        /// <summary>Rows returned by a database query that produced a result set.</summary>
        public static readonly Histogram<long> DbRowsReturned = Meter.CreateHistogram<long>(
            "isis.db.rows_returned", "{row}", "Rows returned by a database query.");

        // ----- Memory (application) instruments -----

        /// <summary>Duration of a memory upsert, in seconds.</summary>
        public static readonly Histogram<double> MemoryUpsertDuration = Meter.CreateHistogram<double>(
            "isis.memory.upsert.duration", "s", "Duration of a memory upsert.");

        /// <summary>Count of memory upserts.</summary>
        public static readonly Counter<long> MemoryUpserts = Meter.CreateCounter<long>(
            "isis.memory.upsert", "{upsert}", "Count of memory upserts.");

        /// <summary>Duration of a memory search, in seconds.</summary>
        public static readonly Histogram<double> MemorySearchDuration = Meter.CreateHistogram<double>(
            "isis.memory.search.duration", "s", "Duration of a memory search.");

        /// <summary>Count of memory searches.</summary>
        public static readonly Counter<long> MemorySearches = Meter.CreateCounter<long>(
            "isis.memory.search", "{search}", "Count of memory searches.");

        /// <summary>Number of hits returned by a memory search.</summary>
        public static readonly Histogram<long> MemorySearchResults = Meter.CreateHistogram<long>(
            "isis.memory.search.results", "{hit}", "Hits returned by a memory search.");

        /// <summary>Duration of a memory delete, in seconds.</summary>
        public static readonly Histogram<double> MemoryDeleteDuration = Meter.CreateHistogram<double>(
            "isis.memory.delete.duration", "s", "Duration of a memory delete.");

        /// <summary>Count of memory deletes.</summary>
        public static readonly Counter<long> MemoryDeletes = Meter.CreateCounter<long>(
            "isis.memory.delete", "{delete}", "Count of memory deletes.");

        // ----- Store instruments -----

        /// <summary>Duration of a store search, in seconds.</summary>
        public static readonly Histogram<double> StoreSearchDuration = Meter.CreateHistogram<double>(
            "isis.store.search.duration", "s", "Duration of a store search.");

        /// <summary>Count of store searches.</summary>
        public static readonly Counter<long> StoreSearches = Meter.CreateCounter<long>(
            "isis.store.search", "{search}", "Count of store searches.");

        /// <summary>Duration of a store upsert, in seconds.</summary>
        public static readonly Histogram<double> StoreUpsertDuration = Meter.CreateHistogram<double>(
            "isis.store.upsert.duration", "s", "Duration of a store upsert.");

        /// <summary>Count of store upserts.</summary>
        public static readonly Counter<long> StoreUpserts = Meter.CreateCounter<long>(
            "isis.store.upsert", "{upsert}", "Count of store upserts.");

        /// <summary>Duration of a store operation, in seconds.</summary>
        public static readonly Histogram<double> StoreOpDuration = Meter.CreateHistogram<double>(
            "isis.store.op.duration", "s", "Duration of a store operation.");

        /// <summary>Count of store operations.</summary>
        public static readonly Counter<long> StoreOps = Meter.CreateCounter<long>(
            "isis.store.op", "{operation}", "Count of store operations.");

        // ----- Inference instruments -----

        /// <summary>Duration of an inference request, in seconds.</summary>
        public static readonly Histogram<double> InferenceDuration = Meter.CreateHistogram<double>(
            "isis.inference.duration", "s", "Duration of an inference request.");

        /// <summary>Count of inference requests.</summary>
        public static readonly Counter<long> InferenceRequests = Meter.CreateCounter<long>(
            "isis.inference.requests", "{request}", "Count of inference requests.");

        /// <summary>Time-to-first-byte of a streaming inference request, in seconds.</summary>
        public static readonly Histogram<double> InferenceTtfbDuration = Meter.CreateHistogram<double>(
            "isis.inference.ttfb.duration", "s", "Time-to-first-byte of a streaming inference request.");

        /// <summary>Count of streamed inference chunks.</summary>
        public static readonly Counter<long> InferenceStreamChunks = Meter.CreateCounter<long>(
            "isis.inference.stream.chunks", "{chunk}", "Count of streamed inference chunks.");

        // ----- Embedding instruments -----

        /// <summary>Duration of an embedding request, in seconds.</summary>
        public static readonly Histogram<double> EmbeddingDuration = Meter.CreateHistogram<double>(
            "isis.embedding.duration", "s", "Duration of an embedding request.");

        /// <summary>Count of embedding requests.</summary>
        public static readonly Counter<long> EmbeddingRequests = Meter.CreateCounter<long>(
            "isis.embedding.requests", "{request}", "Count of embedding requests.");

        // ----- Chat instruments -----

        /// <summary>Duration of a chat-with-memory ask, in seconds.</summary>
        public static readonly Histogram<double> ChatAskDuration = Meter.CreateHistogram<double>(
            "isis.chat.ask.duration", "s", "Duration of a chat-with-memory ask.");

        /// <summary>Count of chat-with-memory asks.</summary>
        public static readonly Counter<long> ChatAsks = Meter.CreateCounter<long>(
            "isis.chat.ask", "{ask}", "Count of chat-with-memory asks.");

        /// <summary>Number of memories placed into a chat context.</summary>
        public static readonly Histogram<long> ChatContextMemories = Meter.CreateHistogram<long>(
            "isis.chat.context.memories", "{memory}", "Memories placed into a chat context.");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Derive a low-cardinality SQL operation label from a query string by inspecting its leading verb.
        /// </summary>
        /// <param name="sql">SQL query text.</param>
        /// <returns>One of select, insert, update, delete, ddl, or other.</returns>
        public static string DeriveSqlOperation(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return "other";

            int i = 0;
            while (i < sql.Length && char.IsWhiteSpace(sql[i])) i++;

            int start = i;
            while (i < sql.Length && (char.IsLetter(sql[i]))) i++;
            if (i == start) return "other";

            string verb = sql.Substring(start, i - start).ToLowerInvariant();
            switch (verb)
            {
                case "select":
                case "with":
                    return "select";
                case "insert":
                    return "insert";
                case "update":
                    return "update";
                case "delete":
                    return "delete";
                case "create":
                case "drop":
                case "alter":
                case "truncate":
                case "pragma":
                    return "ddl";
                default:
                    return "other";
            }
        }

        /// <summary>
        /// Record an exception on an activity as a standard OpenTelemetry exception event and set the error status.
        /// Null-safe: does nothing when the activity is null (for example when nothing is sampling the trace).
        /// </summary>
        /// <param name="activity">Activity, may be null.</param>
        /// <param name="e">Exception.</param>
        public static void RecordException(Activity? activity, Exception e)
        {
            if (activity == null || e == null) return;

            ActivityTagsCollection tags = new ActivityTagsCollection();
            tags["exception.type"] = e.GetType().FullName;
            tags["exception.message"] = e.Message;
            if (!string.IsNullOrEmpty(e.StackTrace)) tags["exception.stacktrace"] = e.StackTrace;

            activity.AddEvent(new ActivityEvent("exception", default, tags));
            activity.SetStatus(ActivityStatusCode.Error, e.Message);
        }

        #endregion
    }
}
