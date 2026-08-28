namespace Isis.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Helpers;
    using Isis.Core.Models;
    using Isis.Core.Security;

    /// <summary>
    /// Owns tenant provisioning (create a tenant plus its default admin user, credential, and instruction set)
    /// and cascading tenant deletion (nuke: tear down every tenant-scoped record and external store content).
    /// Cascades are performed with batch deletes over enumerated child identifiers.
    /// </summary>
    public class TenantLifecycleService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly MemoryService _MemoryService;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="memoryService">The memory service (used to tear down scope store content).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public TenantLifecycleService(DatabaseDriverBase database, MemoryService memoryService)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _MemoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Provision a new tenant: create the tenant, an auto-generated tenant-admin user, a credential, and the
        /// default instruction set. The generated password and raw secret key are returned once for the caller
        /// to surface to the operator.
        /// </summary>
        /// <param name="tenant">The tenant to create (name required).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provisioning result including the generated credentials.</returns>
        public async Task<TenantProvisionResult> ProvisionAsync(Tenant tenant, CancellationToken token = default)
        {
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));

            Tenant created = await _Database.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);

            string password = IdGenerator.Token().Substring(0, 20);
            User admin = new User
            {
                TenantId = created.Id,
                FirstName = "Tenant",
                LastName = "Administrator",
                Email = "admin@" + created.Id + ".local",
                PasswordSha256 = PasswordHasher.Hash(password),
                IsAdmin = false,
                IsTenantAdmin = true
            };
            await _Database.Users.CreateAsync(admin, token).ConfigureAwait(false);

            string accessKey = "access_" + IdGenerator.Token();
            string secretKey = "secret_" + IdGenerator.Token();
            Credential credential = new Credential
            {
                TenantId = created.Id,
                UserId = admin.Id,
                Name = "default",
                AccessKey = accessKey,
                SecretKey = secretKey
            };
            await _Database.Credentials.CreateAsync(credential, token).ConfigureAwait(false);

            await _Database.Instructions.CreateManyAsync(DefaultInstructions.For(created.Id), token).ConfigureAwait(false);

            return new TenantProvisionResult
            {
                Tenant = created,
                AdminUserId = admin.Id,
                AdminEmail = admin.Email,
                AdminPassword = password,
                CredentialId = credential.Id,
                AccessKey = accessKey,
                SecretKey = secretKey
            };
        }

        /// <summary>
        /// Cascade-delete (nuke) a tenant and everything it owns: scopes (with store content), categories,
        /// memories, instructions, endpoints, credentials, sessions, permissions, request history, and users.
        /// Protected tenants are refused.
        /// </summary>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A result indicating not-found, protected, or success.</returns>
        public async Task<TenantDeleteOutcome> DeleteTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            Tenant? tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false);
            if (tenant == null) return TenantDeleteOutcome.NotFound;
            if (tenant.Protected) return TenantDeleteOutcome.Protected;

            // Scopes (with categories, memories, and external store content) via the scope cascade.
            while (true)
            {
                EnumerationResult<Scope> scopes = await _Database.Scopes.EnumerateAsync(tenantId, Page(), token).ConfigureAwait(false);
                if (scopes.Objects.Count == 0) break;
                foreach (Scope scope in scopes.Objects)
                {
                    await _MemoryService.DeleteScopeAsync(scope, token).ConfigureAwait(false);
                }
            }

            // Remaining tenant-scoped records, drained via batch delete.
            await DrainAsync<Credential>((q, t) => _Database.Credentials.EnumerateAsync(tenantId, q, t), c => c.Id, (ids, t) => _Database.Credentials.DeleteManyAsync(tenantId, ids, t), token).ConfigureAwait(false);
            await DrainAsync<AuthSession>((q, t) => _Database.Sessions.EnumerateAsync(tenantId, q, t), s => s.Id, (ids, t) => _Database.Sessions.DeleteManyAsync(tenantId, ids, t), token).ConfigureAwait(false);
            await DrainAsync<ModelEndpoint>((q, t) => _Database.ModelEndpoints.EnumerateAsync(tenantId, null, q, t), e => e.Id, (ids, t) => _Database.ModelEndpoints.DeleteManyAsync(tenantId, ids, t), token).ConfigureAwait(false);
            await DrainAsync<Instruction>((q, t) => _Database.Instructions.EnumerateAsync(tenantId, q, t), i => i.Id, (ids, t) => _Database.Instructions.DeleteManyAsync(tenantId, ids, t), token).ConfigureAwait(false);
            await DrainAsync<Permission>((q, t) => _Database.Permissions.EnumerateAsync(tenantId, null, q, t), p => p.Id, (ids, t) => _Database.Permissions.DeleteManyAsync(tenantId, ids, t), token).ConfigureAwait(false);
            await DrainAsync<User>((q, t) => _Database.Users.EnumerateAsync(tenantId, q, t), u => u.Id, (ids, t) => _Database.Users.DeleteManyAsync(tenantId, ids, t), token).ConfigureAwait(false);

            await _Database.RequestHistory.DeleteAllAsync(tenantId, token).ConfigureAwait(false);
            await _Database.Tenants.DeleteAsync(tenantId, token).ConfigureAwait(false);
            return TenantDeleteOutcome.Deleted;
        }

        #endregion

        #region Private-Methods

        private static EnumerationQuery Page()
        {
            return new EnumerationQuery { MaxResults = 500 };
        }

        private static async Task DrainAsync<T>(
            Func<EnumerationQuery, CancellationToken, Task<EnumerationResult<T>>> enumerate,
            Func<T, string> idOf,
            Func<List<string>, CancellationToken, Task> deleteMany,
            CancellationToken token)
        {
            while (true)
            {
                EnumerationResult<T> page = await enumerate(Page(), token).ConfigureAwait(false);
                if (page.Objects.Count == 0) break;
                List<string> ids = page.Objects.Select(idOf).ToList();
                await deleteMany(ids, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
