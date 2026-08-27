namespace Isis.Server.Services
{
    using System;
    using Isis.Core.Security;

    /// <summary>
    /// Evaluates coarse authorization decisions from a request context. System administrators bypass all
    /// checks; tenant principals are confined to their own tenant.
    /// </summary>
    public class AuthorizationService
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the authorization service.
        /// </summary>
        public AuthorizationService()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the principal may manage the set of tenants (create, list all, delete).
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <returns>True when permitted.</returns>
        public bool CanManageTenants(RequestContext context)
        {
            return context != null && context.IsAuthenticated && context.IsAdmin;
        }

        /// <summary>
        /// Whether the principal may access resources within a tenant.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="tenantId">The target tenant identifier.</param>
        /// <returns>True when permitted.</returns>
        public bool CanAccessTenant(RequestContext context, string tenantId)
        {
            if (context == null || !context.IsAuthenticated) return false;
            if (context.IsAdmin) return true;
            return !String.IsNullOrEmpty(tenantId) && String.Equals(context.TenantId, tenantId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether the principal may administer a tenant (update tenant-level settings, manage members).
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="tenantId">The target tenant identifier.</param>
        /// <returns>True when permitted.</returns>
        public bool CanAdministerTenant(RequestContext context, string tenantId)
        {
            if (context == null || !context.IsAuthenticated) return false;
            if (context.IsAdmin) return true;
            return context.IsTenantAdmin && String.Equals(context.TenantId, tenantId, StringComparison.Ordinal);
        }

        #endregion
    }
}
