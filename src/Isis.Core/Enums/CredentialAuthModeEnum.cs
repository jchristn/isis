namespace Isis.Core.Enums
{
    /// <summary>
    /// The manner in which a credential authenticates.
    /// </summary>
    public enum CredentialAuthModeEnum
    {
        /// <summary>
        /// The secret is presented directly in a header.
        /// </summary>
        DirectHeader,

        /// <summary>
        /// Requests are signed with the secret.
        /// </summary>
        SignedRequest,

        /// <summary>
        /// The credential is exchanged for a short-lived session token.
        /// </summary>
        SessionExchange,

        /// <summary>
        /// A combination of direct-header and signed-request modes.
        /// </summary>
        Hybrid
    }
}
