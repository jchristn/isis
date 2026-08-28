namespace Isis.Server.Settings
{
    /// <summary>
    /// Observability settings controlling metrics (Prometheus) and distributed tracing (OTLP). Instrumented code
    /// always emits into the process meters and activity sources using only the .NET base class library; these
    /// settings only control whether the in-process OpenTelemetry host subscribes to those signals and where it
    /// exports them. When <see cref="Enabled"/> is false the instrumentation remains cheap no-ops.
    /// </summary>
    public class ObservabilitySettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the observability host is started. When false, no metrics endpoint is exposed and no traces are
        /// exported, but the underlying instrumentation stays in place as inert no-ops. Default: true.
        /// </summary>
        public bool Enabled
        {
            get
            {
                return _Enabled;
            }
            set
            {
                _Enabled = value;
            }
        }

        /// <summary>
        /// Logical service name reported on every metric and trace as the OpenTelemetry resource service.name.
        /// Default: isis-server.
        /// </summary>
        public string ServiceName
        {
            get
            {
                return _ServiceName;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) value = "isis-server";
                _ServiceName = value;
            }
        }

        /// <summary>
        /// Optional service instance identifier reported as service.instance.id. When null a value is generated.
        /// </summary>
        public string? ServiceInstanceId
        {
            get
            {
                return _ServiceInstanceId;
            }
            set
            {
                _ServiceInstanceId = value;
            }
        }

        /// <summary>
        /// Whether the in-process Prometheus scrape endpoint is exposed. Default: true.
        /// </summary>
        public bool PrometheusEnabled
        {
            get
            {
                return _PrometheusEnabled;
            }
            set
            {
                _PrometheusEnabled = value;
            }
        }

        /// <summary>
        /// Hostname the Prometheus scrape endpoint binds to. Use "*" or "+" to bind all interfaces (required inside
        /// a container so Prometheus can scrape it). Default: localhost.
        /// </summary>
        public string PrometheusHostname
        {
            get
            {
                return _PrometheusHostname;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) value = "localhost";
                _PrometheusHostname = value;
            }
        }

        /// <summary>
        /// Port the Prometheus scrape endpoint listens on. Default: 9464 (the OpenTelemetry Prometheus default).
        /// Clamped to 1-65535.
        /// </summary>
        public int PrometheusPort
        {
            get
            {
                return _PrometheusPort;
            }
            set
            {
                if (value < 1) value = 1;
                if (value > 65535) value = 65535;
                _PrometheusPort = value;
            }
        }

        /// <summary>
        /// Path the Prometheus scrape endpoint serves metrics on. Default: /metrics.
        /// </summary>
        public string PrometheusPath
        {
            get
            {
                return _PrometheusPath;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) value = "/metrics";
                if (!value.StartsWith("/")) value = "/" + value;
                _PrometheusPath = value;
            }
        }

        /// <summary>
        /// Whether distributed traces are exported over OTLP. Default: true.
        /// </summary>
        public bool TracingEnabled
        {
            get
            {
                return _TracingEnabled;
            }
            set
            {
                _TracingEnabled = value;
            }
        }

        /// <summary>
        /// OTLP endpoint traces are exported to (for example http://tempo:4317 for gRPC or http://tempo:4318 for
        /// HTTP/protobuf). Default: http://localhost:4317.
        /// </summary>
        public string OtlpEndpoint
        {
            get
            {
                return _OtlpEndpoint;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) value = "http://localhost:4317";
                _OtlpEndpoint = value;
            }
        }

        /// <summary>
        /// OTLP protocol: "grpc" (default) or "httpprotobuf".
        /// </summary>
        public string OtlpProtocol
        {
            get
            {
                return _OtlpProtocol;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) value = "grpc";
                _OtlpProtocol = value;
            }
        }

        /// <summary>
        /// Trace sampling ratio between 0.0 (sample nothing) and 1.0 (sample everything). Applied with a
        /// parent-based sampler so children of a sampled request are always recorded. Default: 1.0.
        /// </summary>
        public double SamplingRatio
        {
            get
            {
                return _SamplingRatio;
            }
            set
            {
                if (value < 0.0) value = 0.0;
                if (value > 1.0) value = 1.0;
                _SamplingRatio = value;
            }
        }

        /// <summary>
        /// Whether .NET runtime metrics (GC, heap, threads, JIT) are collected. Default: true.
        /// </summary>
        public bool IncludeRuntimeMetrics
        {
            get
            {
                return _IncludeRuntimeMetrics;
            }
            set
            {
                _IncludeRuntimeMetrics = value;
            }
        }

        /// <summary>
        /// Whether process metrics (memory, uptime, thread count) are collected. Default: true.
        /// </summary>
        public bool IncludeProcessMetrics
        {
            get
            {
                return _IncludeProcessMetrics;
            }
            set
            {
                _IncludeProcessMetrics = value;
            }
        }

        #endregion

        #region Private-Members

        private bool _Enabled = true;
        private string _ServiceName = "isis-server";
        private string? _ServiceInstanceId = null;
        private bool _PrometheusEnabled = true;
        private string _PrometheusHostname = "localhost";
        private int _PrometheusPort = 9464;
        private string _PrometheusPath = "/metrics";
        private bool _TracingEnabled = true;
        private string _OtlpEndpoint = "http://localhost:4317";
        private string _OtlpProtocol = "grpc";
        private double _SamplingRatio = 1.0;
        private bool _IncludeRuntimeMetrics = true;
        private bool _IncludeProcessMetrics = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ObservabilitySettings()
        {
        }

        #endregion
    }
}
