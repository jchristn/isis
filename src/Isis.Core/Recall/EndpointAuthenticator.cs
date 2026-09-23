namespace Isis.Core.Recall
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// Applies a model endpoint's configured authentication to an outbound HTTP request, and produces a
    /// stable descriptor of that auth for health-check deduplication. Centralizes the auth logic so the
    /// embedding client, the inference client (via <see cref="EndpointAuthHandler"/>), and the health prober
    /// all present credentials identically.
    /// </summary>
    public static class EndpointAuthenticator
    {
        #region Public-Methods

        /// <summary>
        /// Apply the endpoint's authentication to a request: sets headers and/or rewrites the request URI to
        /// carry a query-string credential. A no-op for <see cref="EndpointAuthTypeEnum.None"/> or when the
        /// required credential material is absent.
        /// </summary>
        /// <param name="request">The request to mutate.</param>
        /// <param name="endpoint">The endpoint whose auth to apply.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static void Apply(HttpRequestMessage request, ModelEndpoint endpoint)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            switch (endpoint.AuthType)
            {
                case EndpointAuthTypeEnum.BearerToken:
                    if (!String.IsNullOrEmpty(endpoint.AuthSecret))
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.AuthSecret);
                    break;

                case EndpointAuthTypeEnum.ApiKeyHeader:
                    if (!String.IsNullOrEmpty(endpoint.AuthHeaderName) && !String.IsNullOrEmpty(endpoint.AuthSecret))
                        request.Headers.TryAddWithoutValidation(endpoint.AuthHeaderName, endpoint.AuthSecret);
                    break;

                case EndpointAuthTypeEnum.QueryParam:
                    if (!String.IsNullOrEmpty(endpoint.AuthQueryParam) && !String.IsNullOrEmpty(endpoint.AuthSecret) && request.RequestUri != null)
                        request.RequestUri = AppendQuery(request.RequestUri, endpoint.AuthQueryParam, endpoint.AuthSecret);
                    break;

                case EndpointAuthTypeEnum.BasicAuth:
                    if (!String.IsNullOrEmpty(endpoint.AuthKeyId) || !String.IsNullOrEmpty(endpoint.AuthSecret))
                    {
                        string raw = (endpoint.AuthKeyId ?? String.Empty) + ":" + (endpoint.AuthSecret ?? String.Empty);
                        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                    }
                    break;

                case EndpointAuthTypeEnum.AccessKeySecret:
                    if (!String.IsNullOrEmpty(endpoint.AuthHeaderName) && !String.IsNullOrEmpty(endpoint.AuthKeyId))
                        request.Headers.TryAddWithoutValidation(endpoint.AuthHeaderName, endpoint.AuthKeyId);
                    if (!String.IsNullOrEmpty(endpoint.AuthSecretHeaderName) && !String.IsNullOrEmpty(endpoint.AuthSecret))
                        request.Headers.TryAddWithoutValidation(endpoint.AuthSecretHeaderName, endpoint.AuthSecret);
                    break;

                case EndpointAuthTypeEnum.None:
                default:
                    break;
            }
        }

        /// <summary>
        /// Produce a stable, secret-safe descriptor of the endpoint's auth, used to deduplicate health-check
        /// probes: endpoints that would present identical credentials the same way share a descriptor.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>A descriptor string; "none" when no auth is applied.</returns>
        public static string DedupDescriptor(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            switch (endpoint.AuthType)
            {
                case EndpointAuthTypeEnum.BearerToken:
                    return "bearer:" + Hash(endpoint.AuthSecret);
                case EndpointAuthTypeEnum.ApiKeyHeader:
                    return "header:" + (endpoint.AuthHeaderName ?? String.Empty) + ":" + Hash(endpoint.AuthSecret);
                case EndpointAuthTypeEnum.QueryParam:
                    return "query:" + (endpoint.AuthQueryParam ?? String.Empty) + ":" + Hash(endpoint.AuthSecret);
                case EndpointAuthTypeEnum.BasicAuth:
                    return "basic:" + (endpoint.AuthKeyId ?? String.Empty) + ":" + Hash(endpoint.AuthSecret);
                case EndpointAuthTypeEnum.AccessKeySecret:
                    return "accesssecret:" + (endpoint.AuthHeaderName ?? String.Empty) + "/" + (endpoint.AuthSecretHeaderName ?? String.Empty) + ":" + Hash(endpoint.AuthKeyId) + ":" + Hash(endpoint.AuthSecret);
                case EndpointAuthTypeEnum.None:
                default:
                    return "none";
            }
        }

        #endregion

        #region Private-Methods

        private static Uri AppendQuery(Uri uri, string name, string value)
        {
            UriBuilder builder = new UriBuilder(uri);
            string encoded = Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
            builder.Query = String.IsNullOrEmpty(builder.Query) ? encoded : builder.Query.TrimStart('?') + "&" + encoded;
            return builder.Uri;
        }

        private static string Hash(string? value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToBase64String(bytes);
        }

        #endregion
    }
}
