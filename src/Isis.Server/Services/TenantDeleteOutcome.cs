namespace Isis.Server.Services
{
    /// <summary>
    /// The outcome of a cascading tenant delete.
    /// </summary>
    public enum TenantDeleteOutcome
    {
        /// <summary>
        /// The tenant and all of its records were deleted.
        /// </summary>
        Deleted,

        /// <summary>
        /// The tenant was not found.
        /// </summary>
        NotFound,

        /// <summary>
        /// The tenant is protected and cannot be deleted.
        /// </summary>
        Protected
    }
}
