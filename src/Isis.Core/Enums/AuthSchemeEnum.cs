namespace Isis.Core.Enums
{
    /// <summary>
    /// The authentication scheme used to establish a request's principal.
    /// </summary>
    public enum AuthSchemeEnum
    {
        /// <summary>
        /// A bearer token presented in the Authorization header.
        /// </summary>
        BearerToken,

        /// <summary>
        /// A token presented in an x-token header.
        /// </summary>
        XToken,

        /// <summary>
        /// Email and password presented via headers.
        /// </summary>
        PasswordHeaders,

        /// <summary>
        /// An access key and secret key pair.
        /// </summary>
        AccessKeySecret,

        /// <summary>
        /// An access key and request signature.
        /// </summary>
        AccessKeySignature
    }
}
