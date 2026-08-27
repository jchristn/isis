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
        /// The endpoint hostname.
        /// </summary>
        public string Hostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// The endpoint port. Range 0 to 65535.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                if (value < 0 || value > 65535) throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 0 and 65535.");
                _Port = value;
            }
        }

        /// <summary>
        /// Whether the endpoint uses TLS.
        /// </summary>
        public bool UseSsl { get; set; } = false;

        /// <summary>
        /// The API key or bearer token for the endpoint, if required.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>
        /// The model identifier to request (for example an embedding or completion model name).
        /// </summary>
        public string? Model { get; set; } = null;

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
        private int _Port = 0;
        private int _Dimensionality = 0;

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
        /// Get the base URL for the endpoint.
        /// </summary>
        /// <returns>The base URL.</returns>
        public string GetBaseUrl()
        {
            string scheme = UseSsl ? "https" : "http";
            return scheme + "://" + Hostname + ":" + Port;
        }

        #endregion
    }
}
