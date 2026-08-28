namespace Isis.Server.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    using OpenTelemetry;
    using OpenTelemetry.Exporter;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Resources;
    using OpenTelemetry.Trace;

    using Isis.Core.Observability;
    using Isis.Server.Settings;

    /// <summary>
    /// Owns the process-wide OpenTelemetry pipeline for Isis. A single instance is created at startup and
    /// disposed at shutdown. It builds a <see cref="MeterProvider"/> (exposed via an in-process Prometheus scrape
    /// endpoint, plus optional runtime and process metrics) and a <see cref="TracerProvider"/> (exported over OTLP),
    /// subscribing by name to the meters and activity sources Watson and the Isis instrumentation emit into. The
    /// instrumented code itself takes no dependency on this host or on OpenTelemetry.
    /// </summary>
    public sealed class ObservabilityHost : IDisposable
    {
        #region Private-Members

        private const string _Header = "[Observability] ";

        private readonly ObservabilitySettings _Settings;
        private readonly Action<string>? _Log;

        private MeterProvider? _MeterProvider;
        private TracerProvider? _TracerProvider;
        private Meter? _ProcessMeter;
        private readonly List<Instrument> _ProcessInstruments = new List<Instrument>();
        private readonly Process _Process = Process.GetCurrentProcess();
        private readonly DateTime _StartUtc = DateTime.UtcNow;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        private ObservabilityHost(ObservabilitySettings settings, Action<string>? log)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Log = log;
        }

        /// <summary>
        /// Build and start the observability host. Returns null (and logs) when observability is disabled by
        /// settings or when the pipeline cannot be constructed, so a telemetry failure never prevents the server
        /// from starting.
        /// </summary>
        /// <param name="settings">Observability settings.</param>
        /// <param name="log">Optional log callback (null-safe).</param>
        /// <returns>A started host, or null when disabled or on failure.</returns>
        public static ObservabilityHost? Start(ObservabilitySettings settings, Action<string>? log)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (!settings.Enabled)
            {
                log?.Invoke(_Header + "observability disabled by settings");
                return null;
            }

            ObservabilityHost host = new ObservabilityHost(settings, log);
            try
            {
                host.Build();
                return host;
            }
            catch (Exception e)
            {
                log?.Invoke(_Header + "failed to start observability pipeline: " + e.Message);
                host.Dispose();
                return null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Flush any buffered telemetry and tear down the pipeline.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            try { _MeterProvider?.ForceFlush(2000); } catch { }
            try { _TracerProvider?.ForceFlush(2000); } catch { }

            foreach (Instrument instrument in _ProcessInstruments)
            {
                if (instrument is IDisposable disposable)
                {
                    try { disposable.Dispose(); } catch { }
                }
            }

            try { _ProcessMeter?.Dispose(); } catch { }
            try { _MeterProvider?.Dispose(); } catch { }
            try { _TracerProvider?.Dispose(); } catch { }
        }

        #endregion

        #region Private-Methods

        private void Log(string message)
        {
            _Log?.Invoke(message);
        }

        private void Build()
        {
            ResourceBuilder resourceBuilder = ResourceBuilder.CreateDefault().AddService(
                serviceName: _Settings.ServiceName,
                serviceVersion: null,
                autoGenerateServiceInstanceId: string.IsNullOrEmpty(_Settings.ServiceInstanceId),
                serviceInstanceId: _Settings.ServiceInstanceId);

            // Build metrics and traces independently so a failure in one pipeline (for example, the Prometheus
            // HTTP listener failing to bind) never prevents the other from starting.
            try
            {
                BuildMetrics(resourceBuilder);
            }
            catch (Exception e)
            {
                Log(_Header + "metrics pipeline failed to start: " + e.Message + (e.InnerException != null ? " (" + e.InnerException.Message + ")" : ""));
            }

            try
            {
                BuildTraces(resourceBuilder);
            }
            catch (Exception e)
            {
                Log(_Header + "trace pipeline failed to start: " + e.Message + (e.InnerException != null ? " (" + e.InnerException.Message + ")" : ""));
            }
        }

        private void BuildMetrics(ResourceBuilder resourceBuilder)
        {
            MeterProviderBuilder builder = Sdk.CreateMeterProviderBuilder();
            builder.SetResourceBuilder(resourceBuilder);

            // Subscribe by name to Watson's built-in meter and the Isis instrumentation meter. The service name
            // is included so any meter created with the service name (e.g. process gauges) is also collected.
            builder.AddMeter("Watson");
            builder.AddMeter(IsisTelemetry.MeterName);
            builder.AddMeter(_Settings.ServiceName);

            // The default OpenTelemetry histogram buckets are not tuned for second-scale latencies, which would
            // collapse most requests into a single bucket and make quantiles meaningless. Apply explicit
            // latency buckets (5ms - 10s) to every duration histogram so p50/p95/p99 are accurate.
            double[] latencyBuckets = new double[] { 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10 };
            foreach (string durationInstrument in new[]
            {
                "isis.db.query.duration",
                "isis.memory.upsert.duration",
                "isis.memory.search.duration",
                "isis.memory.delete.duration",
                "isis.store.search.duration",
                "isis.store.upsert.duration",
                "isis.store.op.duration",
                "isis.inference.duration",
                "isis.inference.ttfb.duration",
                "isis.embedding.duration",
                "isis.chat.ask.duration"
            })
            {
                builder.AddView(durationInstrument, new ExplicitBucketHistogramConfiguration { Boundaries = latencyBuckets });
            }

            if (_Settings.IncludeRuntimeMetrics)
                builder.AddRuntimeInstrumentation();

            if (_Settings.IncludeProcessMetrics)
                RegisterProcessMetrics();

            if (_Settings.PrometheusEnabled)
            {
                // The Prometheus exporter builds a System.Uri from Host/Port, and System.Uri cannot parse the
                // wildcard hosts ("*", "+", "0.0.0.0") that HttpListener needs in order to bind all interfaces and
                // match any Host header (as a container-network scrape requires). When a wildcard is requested we
                // give the exporter a parseable placeholder host, then override the listener's prefixes directly
                // (HttpListener accepts "http://*:<port>/") via ConfigureHttpListener. Concrete hostnames are used
                // as-is.
                int port = _Settings.PrometheusPort;
                string configuredHost = _Settings.PrometheusHostname;
                bool wildcard =
                    string.IsNullOrEmpty(configuredHost)
                    || configuredHost == "*"
                    || configuredHost == "+"
                    || configuredHost == "0.0.0.0";

                builder.AddPrometheusHttpListener(options =>
                {
                    options.Host = wildcard ? "localhost" : configuredHost;
                    options.Port = port;
                    options.ScrapeEndpointPath = _Settings.PrometheusPath;

                    if (wildcard)
                    {
                        options.ConfigureHttpListener = (listenerOptions, listener) =>
                        {
                            listener.Prefixes.Clear();
                            listener.Prefixes.Add("http://*:" + port + "/");
                        };
                    }
                });

                Log(_Header + "Prometheus scrape endpoint on http://" + (wildcard ? "*" : configuredHost) + ":" + port + _Settings.PrometheusPath);
            }

            _MeterProvider = builder.Build();
        }

        private void BuildTraces(ResourceBuilder resourceBuilder)
        {
            if (!_Settings.TracingEnabled)
            {
                Log(_Header + "tracing disabled by settings");
                return;
            }

            TracerProviderBuilder builder = Sdk.CreateTracerProviderBuilder();
            builder.SetResourceBuilder(resourceBuilder);
            builder.AddSource("Watson");
            builder.AddSource(IsisTelemetry.ActivitySourceName);
            builder.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(_Settings.SamplingRatio)));

            builder.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(_Settings.OtlpEndpoint);
                options.Protocol = _Settings.OtlpProtocol != null && _Settings.OtlpProtocol.Equals("httpprotobuf", StringComparison.OrdinalIgnoreCase)
                    ? OtlpExportProtocol.HttpProtobuf
                    : OtlpExportProtocol.Grpc;
            });

            _TracerProvider = builder.Build();
            Log(_Header + "trace export via OTLP to " + _Settings.OtlpEndpoint + " (" + _Settings.OtlpProtocol + ")");
        }

        private void RegisterProcessMetrics()
        {
            _ProcessMeter = new Meter(_Settings.ServiceName);

            _ProcessInstruments.Add(_ProcessMeter.CreateObservableGauge<long>(
                "process.memory.usage",
                () => { _Process.Refresh(); return _Process.WorkingSet64; },
                "By",
                "Process working set memory."));

            _ProcessInstruments.Add(_ProcessMeter.CreateObservableGauge<double>(
                "process.uptime",
                () => (DateTime.UtcNow - _StartUtc).TotalSeconds,
                "s",
                "Process uptime in seconds."));

            _ProcessInstruments.Add(_ProcessMeter.CreateObservableGauge<int>(
                "process.thread.count",
                () => { _Process.Refresh(); return _Process.Threads.Count; },
                "{thread}",
                "Number of OS threads in the process."));
        }

        #endregion
    }
}
