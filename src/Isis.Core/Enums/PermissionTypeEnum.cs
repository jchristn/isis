namespace Isis.Core.Enums
{
    /// <summary>
    /// Whether a permission grants or explicitly denies access. Deny beats Permit during evaluation.
    /// </summary>
    public enum PermissionTypeEnum
    {
        /// <summary>
        /// Grants access to the covered resources and operations.
        /// </summary>
        Permit,

        /// <summary>
        /// Explicitly denies access; overrides any matching Permit.
        /// </summary>
        Deny
    }
}
