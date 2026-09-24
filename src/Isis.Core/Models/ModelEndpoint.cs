namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;

    /// <summary>
    /// A configured AI model endpoint (embedding or inference), including health-check parameters. Isis
    /// computes embeddings and runs inference through these endpoints; RecallDB is bring-your-own-vector.
    /// </summary>
    public class ModelEndpoint
    {
        #region Public-Members

        /// <summary>
        /// Endpoint identifier. Defaults to a generated value; may not be set to null or empty.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Owning tenant identifier. May not be set to null or empty.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId));
                _TenantId = value;
            }
        }

        /// <summary>
        /// Human-readable endpoint name. May not be set to null or empty.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Whether this endpoint provides embeddings or inference.
        /// </summary>
        public EndpointKindEnum Kind { get; set; } = EndpointKindEnum.Embedding;

        /// <summary>
        /// The wire format the endpoint speaks.
        /// </summary>
        public ApiFormatEnum ApiFormat { get; set; } = ApiFormatEnum.OpenAI;

        /// <summary>
        /// The full base URL of the endpoint, onto which the API-format-specific path (for example
        /// <c>/api/embed</c> or <c>/v1/embeddings</c>) is appended. Example:
        /// <c>http://conductor.example.com:8900/v1.0/api/all-minilm-latest</c>.
        /// </summary>
        public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

        /// <summary>
        /// The authentication mechanism Isis applies when calling this endpoint.
        /// </summary>
        public EndpointAuthTypeEnum AuthType { get; set; } = EndpointAuthTypeEnum.None;

        /// <summary>
        /// For <see cref="EndpointAuthTypeEnum.ApiKeyHeader"/>, the request header name that carries the key
        /// (for example <c>x-api-key</c>). For <see cref="EndpointAuthTypeEnum.AccessKeySecret"/>, the header
        /// that carries the access key.
        /// </summary>
        public string? AuthHeaderName { get; set; } = null;

        /// <summary>
        /// For <see cref="EndpointAuthTypeEnum.AccessKeySecret"/>, the request header name that carries the
        /// secret key.
        /// </summary>
        public string? AuthSecretHeaderName { get; set; } = null;

        /// <summary>
        /// For <see cref="EndpointAuthTypeEnum.QueryParam"/>, the query-string parameter name that carries the
        /// key (for example <c>key</c>).
        /// </summary>
        public string? AuthQueryParam { get; set; } = null;

        /// <summary>
        /// The credential identifier: the username for <see cref="EndpointAuthTypeEnum.BasicAuth"/>, or the
        /// access key for <see cref="EndpointAuthTypeEnum.AccessKeySecret"/>. Unused by other schemes.
        /// </summary>
        public string? AuthKeyId { get; set; } = null;

        /// <summary>
        /// The secret credential material: the bearer token, header value, query value, Basic password, or
        /// secret key, depending on <see cref="AuthType"/>.
        /// </summary>
        public string? AuthSecret { get; set; } = null;

        /// <summary>
        /// The model identifier to request (for example an embedding or completion model name).
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Optional override of the model's maximum input token budget used for chunk sizing. Zero means the
        /// budget is resolved automatically from the API format and model name.
        /// </summary>
        public int MaxInputTokens
        {
            get
            {
                return _MaxInputTokens;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(MaxInputTokens), "MaxInputTokens may not be negative.");
                _MaxInputTokens = value;
            }
        }

        /// <summary>
        /// For embedding endpoints, the vector dimensionality produced. Zero when unknown.
        /// </summary>
        public int Dimensionality
        {
            get
            {
                return _Dimensionality;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(Dimensionality), "Dimensionality may not be negative.");
                _Dimensionality = value;
            }
        }

        /// <summary>
        /// Request timeout in milliseconds. Default 60000.
        /// </summary>
        public int TimeoutMs { get; set; } = 60000;

        /// <summary>
        /// Indicates whether the endpoint is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// The health-check path appended to the base URL. Default "/".
        /// </summary>
        public string HealthCheckUrl { get; set; } = "/";

        /// <summary>
        /// The health-check HTTP method.
        /// </summary>
        public HealthCheckMethodEnum HealthCheckMethod { get; set; } = HealthCheckMethodEnum.GET;

        /// <summary>
        /// The health-check interval in milliseconds. Default 5000.
        /// </summary>
        public int HealthCheckIntervalMs { get; set; } = 5000;

        /// <summary>
        /// The health-check timeout in milliseconds. Default 5000.
        /// </summary>
        public int HealthCheckTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// The HTTP status code considered healthy. Default 200.
        /// </summary>
        public int HealthCheckExpectedStatusCode { get; set; } = 200;

        /// <summary>
        /// Consecutive healthy probes required before flipping to healthy. Default 2.
        /// </summary>
        public int HealthyThreshold { get; set; } = 2;

        /// <summary>
        /// Consecutive unhealthy probes required before flipping to unhealthy. Default 2.
        /// </summary>
        public int UnhealthyThreshold { get; set; } = 2;

        /// <summary>
        /// Whether the health-check request includes the endpoint's auth credential.
        /// </summary>
        public bool HealthCheckUseAuth { get; set; } = false;

        /// <summary>
        /// UTC timestamp when the endpoint was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the endpoint was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.EmbeddingEndpoint();
        private string _TenantId = String.Empty;
        private string _Name = String.Empty;
        private int _Dimensionality = 0;
        private int _MaxInputTokens = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a model endpoint.
        /// </summary>
        public ModelEndpoint()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the normalized base URL for the endpoint (any trailing slash removed), onto which
        /// API-format-specific paths are appended.
        /// </summary>
        /// <returns>The base URL.</returns>
        public string GetBaseUrl()
        {
            return String.IsNullOrEmpty(BaseUrl) ? String.Empty : BaseUrl.TrimEnd('/');
        }

        #endregion
    }
}
