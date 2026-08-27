namespace Test.Shared
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Services;
    using Isis.Server.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Tests for authorization decisions and first-boot seeding.
    /// </summary>
    public static class AuthSuite
    {
        #region Public-Methods

        /// <summary>
        /// Get the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Suite()
        {
            return new TestSuiteDescriptor(
                "auth",
                "Isis Auth Suite",
                new List<TestCaseDescriptor>
                {
                    TestCase.Sync("auth", "authz-manage-tenants", "Authorization: manage tenants", ManageTenants),
                    TestCase.Sync("auth", "authz-access-tenant", "Authorization: access tenant", AccessTenant),
                    TestCase.Sync("auth", "authz-administer-tenant", "Authorization: administer tenant", AdministerTenant),
                    TestCase.Async("auth", "seeder-creates-defaults", "Seeder creates defaults", SeederCreatesDefaultsAsync),
                    TestCase.Async("auth", "seeder-idempotent", "Seeder is idempotent", SeederIdempotentAsync),
                    TestCase.Async("auth", "seeder-credential-accesskey", "Seeder credential uses the configured access key", SeederAccessKeyAsync),
                    TestCase.Sync("auth", "password-hash-verify", "Password hash + verify round trips", PasswordHashVerify),
                    TestCase.Async("auth", "enumerate-users-by-email", "Users are discoverable by email across tenants", EnumerateByEmailAsync)
                });
        }

        #endregion

        #region Private-Methods

        private static RequestContext Admin()
        {
            return new RequestContext { IsAuthenticated = true, IsAdmin = true };
        }

        private static RequestContext TenantUser(string tenantId, bool tenantAdmin)
        {
            return new RequestContext { IsAuthenticated = true, IsAdmin = false, IsTenantAdmin = tenantAdmin, TenantId = tenantId };
        }

        private static void ManageTenants()
        {
            AuthorizationService authz = new AuthorizationService();
            TestCase.Require(authz.CanManageTenants(Admin()), "Admin should manage tenants.");
            TestCase.Require(!authz.CanManageTenants(TenantUser("ten_a", true)), "Tenant admin should not manage all tenants.");
            TestCase.Require(!authz.CanManageTenants(RequestContext.Unauthenticated()), "Unauthenticated should not manage tenants.");
        }

        private static void AccessTenant()
        {
            AuthorizationService authz = new AuthorizationService();
            TestCase.Require(authz.CanAccessTenant(Admin(), "ten_anything"), "Admin should access any tenant.");
            TestCase.Require(authz.CanAccessTenant(TenantUser("ten_a", false), "ten_a"), "Tenant principal should access its own tenant.");
            TestCase.Require(!authz.CanAccessTenant(TenantUser("ten_a", false), "ten_b"), "Tenant principal should not access another tenant.");
            TestCase.Require(!authz.CanAccessTenant(RequestContext.Unauthenticated(), "ten_a"), "Unauthenticated should not access a tenant.");
        }

        private static void AdministerTenant()
        {
            AuthorizationService authz = new AuthorizationService();
            TestCase.Require(authz.CanAdministerTenant(Admin(), "ten_a"), "Admin should administer any tenant.");
            TestCase.Require(authz.CanAdministerTenant(TenantUser("ten_a", true), "ten_a"), "Tenant admin should administer its own tenant.");
            TestCase.Require(!authz.CanAdministerTenant(TenantUser("ten_a", false), "ten_a"), "Non-admin tenant user should not administer its tenant.");
            TestCase.Require(!authz.CanAdministerTenant(TenantUser("ten_a", true), "ten_b"), "Tenant admin should not administer another tenant.");
        }

        private static async Task SeederCreatesDefaultsAsync()
        {
            using TempSqlite temp = await TempSqlite.CreateAsync().ConfigureAwait(false);
            AuthSettings auth = new AuthSettings { DefaultAccessKey = "seedkey" };
            await DefaultSeeder.SeedAsync(temp.Db, auth, _ => { }).ConfigureAwait(false);

            Tenant? tenant = await temp.Db.Tenants.ReadAsync(DefaultSeeder.DefaultTenantId).ConfigureAwait(false);
            TestCase.Require(tenant != null, "Default tenant should be seeded.");

            User? user = await temp.Db.Users.ReadAsync(DefaultSeeder.DefaultTenantId, DefaultSeeder.DefaultUserId).ConfigureAwait(false);
            TestCase.Require(user != null && user.IsAdmin, "Default admin user should be seeded as a system admin.");
            TestCase.Require(PasswordHasher.Verify("isisadmin", user!.PasswordSha256), "Default admin user should have the seeded password hash.");

            Credential? credential = await temp.Db.Credentials.ReadAsync(DefaultSeeder.DefaultTenantId, DefaultSeeder.DefaultCredentialId).ConfigureAwait(false);
            TestCase.Require(credential != null && !string.IsNullOrEmpty(credential!.SecretKey), "Default credential should be seeded with a secret key.");
        }

        private static void PasswordHashVerify()
        {
            string hash = PasswordHasher.Hash("correct horse battery staple");
            TestCase.Require(hash.Length == 64, "SHA-256 hex hash should be 64 characters.");
            TestCase.Require(PasswordHasher.Verify("correct horse battery staple", hash), "Verify should accept the correct password.");
            TestCase.Require(!PasswordHasher.Verify("wrong password", hash), "Verify should reject an incorrect password.");
            TestCase.Require(!PasswordHasher.Verify("anything", null), "Verify should reject a null hash.");
        }

        private static async Task EnumerateByEmailAsync()
        {
            using TempSqlite temp = await TempSqlite.CreateAsync().ConfigureAwait(false);
            AuthSettings auth = new AuthSettings { SeedAdminEmail = "admin@isis.local", SeedAdminPassword = "pw" };
            await DefaultSeeder.SeedAsync(temp.Db, auth, _ => { }).ConfigureAwait(false);

            System.Collections.Generic.List<User> found = await temp.Db.Users.EnumerateByEmailAsync("admin@isis.local").ConfigureAwait(false);
            TestCase.Require(found.Count == 1, "Exactly one user should match the seeded admin email.");
            TestCase.Require(found[0].TenantId == DefaultSeeder.DefaultTenantId, "The matched user should belong to the default tenant.");

            System.Collections.Generic.List<User> none = await temp.Db.Users.EnumerateByEmailAsync("nobody@isis.local").ConfigureAwait(false);
            TestCase.Require(none.Count == 0, "An unknown email should match no users.");
        }

        private static async Task SeederIdempotentAsync()
        {
            using TempSqlite temp = await TempSqlite.CreateAsync().ConfigureAwait(false);
            AuthSettings auth = new AuthSettings { DefaultAccessKey = "seedkey" };
            await DefaultSeeder.SeedAsync(temp.Db, auth, _ => { }).ConfigureAwait(false);
            await DefaultSeeder.SeedAsync(temp.Db, auth, _ => { }).ConfigureAwait(false);

            EnumerationResult<Tenant> tenants = await temp.Db.Tenants.EnumerateAsync(new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(tenants.TotalRecords == 1, "Re-seeding should not duplicate the default tenant.");
        }

        private static async Task SeederAccessKeyAsync()
        {
            using TempSqlite temp = await TempSqlite.CreateAsync().ConfigureAwait(false);
            AuthSettings auth = new AuthSettings { DefaultAccessKey = "abc123" };
            await DefaultSeeder.SeedAsync(temp.Db, auth, _ => { }).ConfigureAwait(false);

            Credential? credential = await temp.Db.Credentials.ReadByAccessKeyAsync("abc123").ConfigureAwait(false);
            TestCase.Require(credential != null, "The seeded credential should be resolvable by the configured access key.");
        }

        #endregion
    }
}
