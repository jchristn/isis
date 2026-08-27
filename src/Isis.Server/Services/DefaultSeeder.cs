namespace Isis.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Settings;

    /// <summary>
    /// Seeds a default tenant, administrator user, and access-key credential on first boot so a fresh
    /// deployment is immediately usable. Idempotent.
    /// </summary>
    public static class DefaultSeeder
    {
        #region Public-Members

        /// <summary>
        /// The identifier of the seeded default tenant.
        /// </summary>
        public static readonly string DefaultTenantId = "ten_default";

        /// <summary>
        /// The identifier of the seeded default administrator user.
        /// </summary>
        public static readonly string DefaultUserId = "usr_admin";

        /// <summary>
        /// The identifier of the seeded non-admin service user that owns the default credential. Credentials
        /// are least-privilege, so the default automation credential is deliberately NOT owned by the admin.
        /// </summary>
        public static readonly string DefaultServiceUserId = "usr_service";

        /// <summary>
        /// The identifier of the seeded default credential.
        /// </summary>
        public static readonly string DefaultCredentialId = "crd_default";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed default records if they do not already exist.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="auth">Authentication settings (provides the default access key).</param>
        /// <param name="log">A log callback.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Awaitable task.</returns>
        public static async Task SeedAsync(DatabaseDriverBase database, AuthSettings auth, Action<string> log, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (auth == null) throw new ArgumentNullException(nameof(auth));

            Tenant? tenant = await database.Tenants.ReadAsync(DefaultTenantId, token).ConfigureAwait(false);
            if (tenant == null)
            {
                tenant = new Tenant { Id = DefaultTenantId, Name = "Default", Protected = true };
                await database.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);
                log?.Invoke("seeded default tenant '" + DefaultTenantId + "'");
            }

            User? user = await database.Users.ReadAsync(DefaultTenantId, DefaultUserId, token).ConfigureAwait(false);
            if (user == null)
            {
                user = new User
                {
                    Id = DefaultUserId,
                    TenantId = DefaultTenantId,
                    FirstName = "Default",
                    LastName = "Administrator",
                    Email = auth.SeedAdminEmail,
                    PasswordSha256 = PasswordHasher.Hash(auth.SeedAdminPassword),
                    IsAdmin = true,
                    IsTenantAdmin = true,
                    Protected = true
                };
                await database.Users.CreateAsync(user, token).ConfigureAwait(false);
                log?.Invoke("seeded default admin user '" + DefaultUserId + "' (" + auth.SeedAdminEmail + ")");
            }
            else if (String.IsNullOrEmpty(user.PasswordSha256))
            {
                // Repair a default admin seeded before password login existed: backfill the seed password and
                // ensure system-admin so a fresh login works. Only runs when no password is set, so a password
                // an operator has already chosen is never overwritten.
                user.PasswordSha256 = PasswordHasher.Hash(auth.SeedAdminPassword);
                user.IsAdmin = true;
                await database.Users.UpdateAsync(user, token).ConfigureAwait(false);
                log?.Invoke("repaired default admin user '" + DefaultUserId + "' (set seed password + IsAdmin)");
            }

            User? serviceUser = await database.Users.ReadAsync(DefaultTenantId, DefaultServiceUserId, token).ConfigureAwait(false);
            if (serviceUser == null)
            {
                serviceUser = new User
                {
                    Id = DefaultServiceUserId,
                    TenantId = DefaultTenantId,
                    FirstName = "Default",
                    LastName = "Service",
                    Email = "service@isis.local",
                    Protected = true
                };
                await database.Users.CreateAsync(serviceUser, token).ConfigureAwait(false);
                log?.Invoke("seeded default service user '" + DefaultServiceUserId + "' (owns the default credential)");
            }

            Credential? credential = await database.Credentials.ReadAsync(DefaultTenantId, DefaultCredentialId, token).ConfigureAwait(false);
            if (credential == null)
            {
                credential = new Credential
                {
                    Id = DefaultCredentialId,
                    TenantId = DefaultTenantId,
                    UserId = DefaultServiceUserId,
                    Name = "default",
                    AccessKey = auth.DefaultAccessKey,
                    SecretKey = auth.DefaultSecretKey,
                    Protected = true
                };
                await database.Credentials.CreateAsync(credential, token).ConfigureAwait(false);
                log?.Invoke("seeded default credential '" + DefaultCredentialId + "' (access/secret key from settings)");
            }
        }

        #endregion
    }
}
