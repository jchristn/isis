namespace Isis.Core.Enums
{
    /// <summary>
    /// The type of authenticated principal behind a request.
    /// </summary>
    public enum PrincipalTypeEnum
    {
        /// <summary>
        /// A system administrator with platform-wide privileges.
        /// </summary>
        Administrator,

        /// <summary>
        /// An interactive user scoped to a tenant.
        /// </summary>
        User,

        /// <summary>
        /// A non-interactive credential (automation/integration) scoped to a tenant and owning user.
        /// </summary>
        Credential
    }
}
