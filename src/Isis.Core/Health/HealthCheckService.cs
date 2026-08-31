namespace Isis.Core.Health
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// Probes model endpoints for health, deduplicating by method, normalized URL, and auth so that
    /// endpoints sharing a target are probed once per round and the single result is applied to all.
    /// </summary>
    public class HealthCheckService
    {
        #region Private-Members

        private readonly HttpClient _HttpClient;
        private readonly ConcurrentDictionary<string, EndpointHealthStatus> _States = new ConcurrentDictionary<string, EndpointHealthStatus>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the health-check service.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to probe endpoints.</param>
        /// <exception cref="ArgumentNullException">Thrown when httpClient is null.</exception>
        public HealthCheckService(HttpClient httpClient)
        {
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the deduplication key for an endpoint: method, normalized scheme/host/port/path, and a
        /// hashed auth component. Endpoints that share this key are probed once per round.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The deduplication key.</returns>
        public static string BuildKey(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            string method = endpoint.HealthCheckMethod == HealthCheckMethodEnum.HEAD ? "HEAD" : "GET";

            string auth = "none";
            if (endpoint.HealthCheckUseAuth && !string.IsNullOrEmpty(endpoint.ApiKey))
            {
                string headerName = endpoint.ApiFormat == ApiFormatEnum.Gemini ? "x-goog-api-key" : "Authorization";
                auth = headerName + ":" + HashSecret(endpoint.ApiKey);
            }

            Uri uri = new Uri(BuildProbeUrl(endpoint));
            string normalized = uri.Scheme.ToLowerInvariant() + "://" + uri.Host.ToLowerInvariant() + ":" + uri.Port + uri.PathAndQuery;
            return method + " " + normalized + " auth=" + auth;
        }

        /// <summary>
        /// Perform one probe round over the given endpoints, deduplicating shared targets. Applies the
        /// result to every endpoint in a group and updates their health state.
        /// </summary>
        /// <param name="endpoints">The endpoints to probe.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of distinct HTTP probes actually performed.</returns>
        public async Task<int> ProbeOnceAsync(IReadOnlyList<ModelEndpoint> endpoints, CancellationToken token = default)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

            Dictionary<string, List<ModelEndpoint>> groups = new Dictionary<string, List<ModelEndpoint>>();
            foreach (ModelEndpoint endpoint in endpoints)
            {
                if (!endpoint.Active) continue;
                string key = BuildKey(endpoint);
                if (!groups.TryGetValue(key, out List<ModelEndpoint>? members))
                {
                    members = new List<ModelEndpoint>();
                    groups[key] = members;
                }

                members.Add(endpoint);
            }

            int probes = 0;
            foreach (KeyValuePair<string, List<ModelEndpoint>> group in groups)
            {
                token.ThrowIfCancellationRequested();
                ModelEndpoint representative = group.Value[0];
                int timeout = group.Value.Max(e => e.HealthCheckTimeoutMs);
                ProbeOutcome outcome = await ProbeAsync(representative, timeout, token).ConfigureAwait(false);
                probes++;

                foreach (ModelEndpoint member in group.Value)
                {
                    ApplyResult(member, outcome);
                }
            }

            return probes;
        }

        /// <summary>
        /// Get the current health status of an endpoint, if it has been probed.
        /// </summary>
        /// <param name="endpointId">The endpoint identifier.</param>
        /// <returns>The status, or null when not yet probed.</returns>
        public EndpointHealthStatus? GetStatus(string endpointId)
        {
            return _States.TryGetValue(endpointId, out EndpointHealthStatus? status) ? status : null;
        }

        /// <summary>
        /// Get a snapshot of all known endpoint health statuses.
        /// </summary>
        /// <returns>The statuses.</returns>
        public IReadOnlyList<EndpointHealthStatus> Snapshot()
        {
            return _States.Values.ToList();
        }

        #endregion

        #region Private-Methods

        private async Task<ProbeOutcome> ProbeAsync(ModelEndpoint endpoint, int timeoutMs, CancellationToken token)
        {
            ProbeOutcome outcome = new ProbeOutcome();
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs > 0 ? timeoutMs : 5000);

            long start = Stopwatch.GetTimestamp();
            try
            {
                HttpMethod method = endpoint.HealthCheckMethod == HealthCheckMethodEnum.HEAD ? HttpMethod.Head : HttpMethod.Get;
                using HttpRequestMessage request = new HttpRequestMessage(method, BuildProbeUrl(endpoint));
                if (endpoint.HealthCheckUseAuth && !string.IsNullOrEmpty(endpoint.ApiKey))
                {
                    if (endpoint.ApiFormat == ApiFormatEnum.Gemini) request.Headers.TryAddWithoutValidation("x-goog-api-key", endpoint.ApiKey);
                    else request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.ApiKey);
                }

                HttpResponseMessage response = await _HttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                outcome.StatusCode = (int)response.StatusCode;
                outcome.Success = outcome.StatusCode == endpoint.HealthCheckExpectedStatusCode;
                if (!outcome.Success) outcome.Error = "Unexpected status code " + outcome.StatusCode + ".";
            }
            catch (Exception e)
            {
                outcome.Success = false;
                outcome.Error = e.Message;
            }

            outcome.LatencyMs = (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return outcome;
        }

        private void ApplyResult(ModelEndpoint endpoint, ProbeOutcome outcome)
        {
            EndpointHealthStatus status = _States.GetOrAdd(endpoint.Id, id => new EndpointHealthStatus { EndpointId = id });

            lock (status)
            {
                DateTime now = DateTime.UtcNow;

                // Accumulate observed up/down time over the interval since the previous probe, attributed to the
                // health state that held during that interval. Sampled — only as dense as probes occur.
                if (status.LastCheckUtc.HasValue)
                {
                    long elapsedMs = (long)(now - status.LastCheckUtc.Value).TotalMilliseconds;
                    if (elapsedMs > 0)
                    {
                        if (status.IsHealthy) status.TotalUptimeMs += elapsedMs;
                        else status.TotalDowntimeMs += elapsedMs;
                    }
                }

                bool wasHealthy = status.IsHealthy;
                if (!status.FirstCheckUtc.HasValue) status.FirstCheckUtc = now;
                status.Probed = true;
                status.LastCheckUtc = now;
                status.LastStatusCode = outcome.StatusCode;
                status.LastLatencyMs = outcome.LatencyMs;

                if (outcome.Success)
                {
                    status.ConsecutiveSuccesses++;
                    status.ConsecutiveFailures = 0;
                    status.LastError = null;
                    status.LastHealthyUtc = now;
                    if (status.ConsecutiveSuccesses >= endpoint.HealthyThreshold) status.IsHealthy = true;
                }
                else
                {
                    status.ConsecutiveFailures++;
                    status.ConsecutiveSuccesses = 0;
                    status.LastError = outcome.Error;
                    status.LastUnhealthyUtc = now;
                    if (status.ConsecutiveFailures >= endpoint.UnhealthyThreshold) status.IsHealthy = false;
                }

                if (status.IsHealthy != wasHealthy || !status.LastStateChangeUtc.HasValue) status.LastStateChangeUtc = now;
            }
        }

        private static string BuildProbeUrl(ModelEndpoint endpoint)
        {
            string baseUrl = endpoint.GetBaseUrl();
            string path = string.IsNullOrEmpty(endpoint.HealthCheckUrl) ? "/" : endpoint.HealthCheckUrl;
            if (!path.StartsWith("/", StringComparison.Ordinal)) path = "/" + path;
            return baseUrl + path;
        }

        private static string HashSecret(string secret)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
            return Convert.ToBase64String(hash);
        }

        #endregion
    }
}
