namespace Isis.Core.Enums
{
    /// <summary>
    /// The authentication mechanism Isis applies to outbound requests to a model endpoint (embedding or
    /// inference). Unlike <see cref="AuthSchemeEnum"/> (which governs Isis's own inbound principal auth),
    /// this describes how Isis presents a credential to a remote model provider.
    /// </summary>
    public enum EndpointAuthTypeEnum
    {
        /// <summary>
        /// No authentication is applied.
        /// </summary>
        None,

        /// <summary>
        /// A bearer token presented as <c>Authorization: Bearer &lt;secret&gt;</c>.
        /// </summary>
        BearerToken,

        /// <summary>
        /// An API key presented in a caller-named request header (name from AuthHeaderName, value from AuthSecret).
        /// </summary>
        ApiKeyHeader,

        /// <summary>
        /// An API key presented in a caller-named query-string parameter (name from AuthQueryParam, value from AuthSecret).
        /// </summary>
        QueryParam,

        /// <summary>
        /// HTTP Basic authentication (username from AuthKeyId, password from AuthSecret).
        /// </summary>
        BasicAuth,

        /// <summary>
        /// An access-key/secret-key pair presented as two request headers (access key in AuthHeaderName,
        /// secret key in AuthSecretHeaderName).
        /// </summary>
        AccessKeySecret
    }
}
