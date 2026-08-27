namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone test suite that exercises the Isis database driver contract end to end against an
    /// in-process SQLite database. Every case is self-contained: it provisions a fresh, initialized
    /// SQLite driver via <see cref="TempSqlite"/> and asserts positive and negative behaviors.
    /// </summary>
    public static class DatabaseSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the database test suite descriptor.
        /// </summary>
        /// <returns>The test suite descriptor.</returns>
        public static Touchstone.Core.TestSuiteDescriptor Suite()
        {
            return new Touchstone.Core.TestSuiteDescriptor(
                "database",
                "Isis Database Suite",
                new System.Collections.Generic.List<Touchstone.Core.TestCaseDescriptor>
                {
                    // Tenants
                    TestCase.Async("database", "tenant-create-read", "Tenant create and read round trip", TenantCreateReadAsync),
                    TestCase.Async("database", "tenant-read-missing", "Reading a nonexistent tenant returns null", TenantReadMissingAsync),
                    TestCase.Async("database", "tenant-update-name", "Updating a tenant changes its name", TenantUpdateNameAsync),
                    TestCase.Async("database", "tenant-delete", "Deleting a tenant returns true then reads null", TenantDeleteAsync),
                    TestCase.Async("database", "tenant-delete-missing", "Deleting a nonexistent tenant returns false", TenantDeleteMissingAsync),
                    TestCase.Async("database", "tenant-enumerate-pagination", "Tenant enumeration paginates by MaxResults and Skip", TenantEnumeratePaginationAsync),
                    TestCase.Async("database", "tenant-search-term", "Tenant enumeration filters by search term", TenantSearchTermAsync),

                    // Users
                    TestCase.Async("database", "user-create-read", "User create and read by id round trip", UserCreateReadAsync),
                    TestCase.Async("database", "user-read-by-email", "User read by email within a tenant", UserReadByEmailAsync),
                    TestCase.Async("database", "user-read-by-email-wrong-tenant", "User read by email in the wrong tenant returns null", UserReadByEmailWrongTenantAsync),
                    TestCase.Async("database", "user-enumerate", "User enumeration is scoped to the tenant", UserEnumerateAsync),
                    TestCase.Async("database", "user-cross-tenant-read-null", "User read from another tenant returns null", UserCrossTenantReadNullAsync),
                    TestCase.Async("database", "user-update", "User update persists changed fields", UserUpdateAsync),
                    TestCase.Async("database", "user-delete", "User delete removes the record", UserDeleteAsync),

                    // Credentials
                    TestCase.Async("database", "credential-create-read-by-accesskey", "Credential create and read by access key", CredentialCreateReadByAccessKeyAsync),
                    TestCase.Async("database", "credential-read-by-accesskey-unknown", "Credential read by unknown access key returns null", CredentialReadByAccessKeyUnknownAsync),
                    TestCase.Async("database", "credential-enumerate-by-tenant", "Credential enumeration is scoped to the tenant", CredentialEnumerateByTenantAsync),
                    TestCase.Async("database", "credential-cross-tenant-read-null", "Credential read from another tenant returns null", CredentialCrossTenantReadNullAsync),
                    TestCase.Async("database", "credential-delete", "Credential delete removes the record", CredentialDeleteAsync),

                    // Sessions
                    TestCase.Async("database", "session-create-read-by-token", "Session create and read by token", SessionCreateReadByTokenAsync),
                    TestCase.Async("database", "session-read-by-token-unknown", "Session read by unknown token returns null", SessionReadByTokenUnknownAsync),
                    TestCase.Async("database", "session-read-by-id", "Session read by id round trip", SessionReadByIdAsync),
                    TestCase.Async("database", "session-enumerate-by-tenant", "Session enumeration is scoped to the tenant", SessionEnumerateByTenantAsync),
                    TestCase.Async("database", "session-delete", "Session delete removes the record", SessionDeleteAsync),

                    // Scopes
                    TestCase.Async("database", "scope-create-read-by-name", "Scope create and read by name", ScopeCreateReadByNameAsync),
                    TestCase.Async("database", "scope-read-by-name-unknown", "Scope read by unknown name returns null", ScopeReadByNameUnknownAsync),
                    TestCase.Async("database", "scope-read-by-id", "Scope read by id round trip", ScopeReadByIdAsync),
                    TestCase.Async("database", "scope-cross-tenant-read-null", "Scope read from another tenant returns null", ScopeCrossTenantReadNullAsync),
                    TestCase.Async("database", "scope-enumerate", "Scope enumeration is scoped to the tenant", ScopeEnumerateAsync),
                    TestCase.Async("database", "scope-update-description", "Scope update changes its description", ScopeUpdateDescriptionAsync),
                    TestCase.Async("database", "scope-delete", "Scope delete removes the record", ScopeDeleteAsync),
                    TestCase.Async("database", "scope-delete-missing", "Deleting a nonexistent scope returns false", ScopeDeleteMissingAsync),

                    // Categories
                    TestCase.Async("database", "category-create-read-by-name", "Category create and read by name", CategoryCreateReadByNameAsync),
                    TestCase.Async("database", "category-enumerate-by-scope", "Category enumeration is scoped to the scope", CategoryEnumerateByScopeAsync),
                    TestCase.Async("database", "category-scope-isolation", "A category in one scope is not returned by another scope", CategoryScopeIsolationAsync),
                    TestCase.Async("database", "category-update", "Category update persists changed fields", CategoryUpdateAsync),
                    TestCase.Async("database", "category-delete", "Category delete removes the record", CategoryDeleteAsync),

                    // Memories
                    TestCase.Async("database", "memory-create-read-by-slug", "Memory create and read by slug", MemoryCreateReadBySlugAsync),
                    TestCase.Async("database", "memory-read-by-slug-unknown", "Memory read by unknown slug returns null", MemoryReadBySlugUnknownAsync),
                    TestCase.Async("database", "memory-enumerate-category-and-all", "Memory enumeration by category and across all categories", MemoryEnumerateCategoryAndAllAsync),
                    TestCase.Async("database", "memory-category-filter-narrows", "Memory category filter narrows the result set", MemoryCategoryFilterNarrowsAsync),
                    TestCase.Async("database", "memory-json-roundtrip", "Memory tags, links, and metadata round trip through JSON columns", MemoryJsonRoundTripAsync),
                    TestCase.Async("database", "memory-injection-safety", "Memory with SQL metacharacters persists and leaves the table intact", MemoryInjectionSafetyAsync),
                    TestCase.Async("database", "memory-update", "Memory update bumps its fields", MemoryUpdateAsync),
                    TestCase.Async("database", "memory-delete", "Memory delete removes the record", MemoryDeleteAsync),

                    // Model endpoints
                    TestCase.Async("database", "endpoint-embedding-prefix", "Embedding endpoint id uses the eep_ prefix", EndpointEmbeddingPrefixAsync),
                    TestCase.Async("database", "endpoint-inference-prefix", "Inference endpoint id uses the iep_ prefix", EndpointInferencePrefixAsync),
                    TestCase.Async("database", "endpoint-prefix-correction", "Create corrects a mismatched id prefix to match the kind", EndpointPrefixCorrectionAsync),
                    TestCase.Async("database", "endpoint-read", "Model endpoint read round trip", EndpointReadAsync),
                    TestCase.Async("database", "endpoint-enumerate-by-kind", "Model endpoint enumeration filters by kind", EndpointEnumerateByKindAsync),
                    TestCase.Async("database", "endpoint-update", "Model endpoint update persists changed fields", EndpointUpdateAsync),
                    TestCase.Async("database", "endpoint-delete", "Model endpoint delete removes the record", EndpointDeleteAsync),

                    // Request history
                    TestCase.Async("database", "request-history-create", "Request history create with null and populated tenant", RequestHistoryCreateAsync),
                    TestCase.Async("database", "request-history-enumerate-all", "Request history enumerate across all tenants includes null-tenant rows", RequestHistoryEnumerateAllAsync),
                    TestCase.Async("database", "request-history-enumerate-by-tenant", "Request history enumerate by tenant returns only that tenant", RequestHistoryEnumerateByTenantAsync),
                    TestCase.Async("database", "request-history-read-by-id", "Request history read by id round trip", RequestHistoryReadByIdAsync),
                    TestCase.Async("database", "request-history-delete-all-tenant", "Request history delete-all by tenant removes only that tenant's rows", RequestHistoryDeleteAllTenantAsync),
                    TestCase.Async("database", "request-history-delete-all-null", "Request history delete-all with null clears every row", RequestHistoryDeleteAllNullAsync),

                    // Multi-tenant isolation
                    TestCase.Async("database", "multi-tenant-isolation", "Scope enumeration isolates tenants from one another", MultiTenantIsolationAsync)
                });
        }

        #endregion

        #region Tenants

        private static async Task TenantCreateReadAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant created = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            TestCase.Require(!string.IsNullOrEmpty(created.Id), "Created tenant should have an id.");

            Tenant? read = await db.Tenants.ReadAsync(created.Id).ConfigureAwait(false);
            TestCase.Require(read != null, "Expected to read the created tenant back.");
            TestCase.Require(read!.Name == "Acme", "Tenant name did not round trip.");
        }

        private static async Task TenantReadMissingAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant? read = await db.Tenants.ReadAsync("ten_does_not_exist").ConfigureAwait(false);
            TestCase.Require(read == null, "Reading a nonexistent tenant should return null.");
        }

        private static async Task TenantUpdateNameAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant created = await db.Tenants.CreateAsync(new Tenant { Name = "Before" }).ConfigureAwait(false);
            created.Name = "After";
            await db.Tenants.UpdateAsync(created).ConfigureAwait(false);

            Tenant? read = await db.Tenants.ReadAsync(created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.Name == "After", "Tenant update did not persist the new name.");
        }

        private static async Task TenantDeleteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant created = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            bool deleted = await db.Tenants.DeleteAsync(created.Id).ConfigureAwait(false);
            TestCase.Require(deleted, "Deleting an existing tenant should return true.");

            Tenant? read = await db.Tenants.ReadAsync(created.Id).ConfigureAwait(false);
            TestCase.Require(read == null, "Deleted tenant should read as null.");
        }

        private static async Task TenantDeleteMissingAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            bool deleted = await db.Tenants.DeleteAsync("ten_does_not_exist").ConfigureAwait(false);
            TestCase.Require(!deleted, "Deleting a nonexistent tenant should return false.");
        }

        private static async Task TenantEnumeratePaginationAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            for (int i = 1; i <= 5; i++)
            {
                await db.Tenants.CreateAsync(new Tenant { Name = "Tenant " + i }).ConfigureAwait(false);
            }

            EnumerationResult<Tenant> page1 = await db.Tenants.EnumerateAsync(new EnumerationQuery { MaxResults = 2, Skip = 0 }).ConfigureAwait(false);
            TestCase.Require(page1.TotalRecords == 5, "Expected TotalRecords of 5, got " + page1.TotalRecords + ".");
            TestCase.Require(page1.Objects.Count == 2, "Expected 2 objects on the first page, got " + page1.Objects.Count + ".");

            EnumerationResult<Tenant> lastPage = await db.Tenants.EnumerateAsync(new EnumerationQuery { MaxResults = 2, Skip = 4 }).ConfigureAwait(false);
            TestCase.Require(lastPage.TotalRecords == 5, "Expected TotalRecords of 5 on the last page, got " + lastPage.TotalRecords + ".");
            TestCase.Require(lastPage.Objects.Count == 1, "Expected 1 object on the last page, got " + lastPage.Objects.Count + ".");
        }

        private static async Task TenantSearchTermAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            await db.Tenants.CreateAsync(new Tenant { Name = "Northwind" }).ConfigureAwait(false);
            await db.Tenants.CreateAsync(new Tenant { Name = "Contoso" }).ConfigureAwait(false);
            await db.Tenants.CreateAsync(new Tenant { Name = "Northern Lights" }).ConfigureAwait(false);

            EnumerationResult<Tenant> filtered = await db.Tenants.EnumerateAsync(new EnumerationQuery { MaxResults = 100, SearchTerm = "North" }).ConfigureAwait(false);
            TestCase.Require(filtered.TotalRecords == 2, "Expected 2 tenants matching 'North', got " + filtered.TotalRecords + ".");
            foreach (Tenant tenant in filtered.Objects)
            {
                TestCase.Require(tenant.Name.Contains("North", StringComparison.OrdinalIgnoreCase), "Search term returned a non-matching tenant: " + tenant.Name);
            }
        }

        #endregion

        #region Users

        private static async Task UserCreateReadAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            User created = await db.Users.CreateAsync(new User { TenantId = tenant.Id, Email = "alice@acme.test", FirstName = "Alice" }).ConfigureAwait(false);

            User? read = await db.Users.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read != null, "Expected to read the created user back.");
            TestCase.Require(read!.Email == "alice@acme.test" && read.FirstName == "Alice", "User fields did not round trip.");
        }

        private static async Task UserReadByEmailAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            User created = await db.Users.CreateAsync(new User { TenantId = tenant.Id, Email = "bob@acme.test" }).ConfigureAwait(false);

            User? read = await db.Users.ReadByEmailAsync(tenant.Id, "bob@acme.test").ConfigureAwait(false);
            TestCase.Require(read != null && read!.Id == created.Id, "Expected to read the user back by email.");
        }

        private static async Task UserReadByEmailWrongTenantAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
            Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "B" }).ConfigureAwait(false);
            await db.Users.CreateAsync(new User { TenantId = a.Id, Email = "carol@a.test" }).ConfigureAwait(false);

            User? read = await db.Users.ReadByEmailAsync(b.Id, "carol@a.test").ConfigureAwait(false);
            TestCase.Require(read == null, "Reading a user by email in the wrong tenant should return null.");
        }

        private static async Task UserEnumerateAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
            Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "B" }).ConfigureAwait(false);
            await db.Users.CreateAsync(new User { TenantId = a.Id, Email = "u1@a.test" }).ConfigureAwait(false);
            await db.Users.CreateAsync(new User { TenantId = a.Id, Email = "u2@a.test" }).ConfigureAwait(false);
            await db.Users.CreateAsync(new User { TenantId = b.Id, Email = "u3@b.test" }).ConfigureAwait(false);

            EnumerationResult<User> aUsers = await db.Users.EnumerateAsync(a.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(aUsers.TotalRecords == 2, "Tenant A should have 2 users, got " + aUsers.TotalRecords + ".");
        }

        private static async Task UserCrossTenantReadNullAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
            Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "B" }).ConfigureAwait(false);
            User created = await db.Users.CreateAsync(new User { TenantId = a.Id, Email = "dave@a.test" }).ConfigureAwait(false);

            User? leak = await db.Users.ReadAsync(b.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(leak == null, "A user created in tenant A must not be readable from tenant B.");
        }

        private static async Task UserUpdateAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            User created = await db.Users.CreateAsync(new User { TenantId = tenant.Id, Email = "erin@acme.test", FirstName = "Erin" }).ConfigureAwait(false);
            created.LastName = "Smith";
            await db.Users.UpdateAsync(created).ConfigureAwait(false);

            User? read = await db.Users.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.LastName == "Smith", "User update did not persist.");
        }

        private static async Task UserDeleteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            User created = await db.Users.CreateAsync(new User { TenantId = tenant.Id, Email = "frank@acme.test" }).ConfigureAwait(false);

            bool deleted = await db.Users.DeleteAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(deleted, "Deleting an existing user should return true.");
            User? read = await db.Users.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read == null, "Deleted user should read as null.");
        }

        #endregion

        #region Credentials

        private static async Task<(Tenant tenant, User user)> SeedTenantUserAsync(DatabaseDriverBase db, string suffix)
        {
            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Tenant " + suffix }).ConfigureAwait(false);
            User user = await db.Users.CreateAsync(new User { TenantId = tenant.Id, Email = "owner-" + suffix + "@test.local" }).ConfigureAwait(false);
            return (tenant, user);
        }

        private static async Task CredentialCreateReadByAccessKeyAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, User user) = await SeedTenantUserAsync(db, "cred1").ConfigureAwait(false);
            Credential created = await db.Credentials.CreateAsync(new Credential
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Name = "automation",
                AccessKey = "ak_public_123",
                SecretKey = "sk_secret_456"
            }).ConfigureAwait(false);

            Credential? read = await db.Credentials.ReadByAccessKeyAsync("ak_public_123").ConfigureAwait(false);
            TestCase.Require(read != null && read!.Id == created.Id, "Expected to read the credential back by access key.");
            TestCase.Require(read!.TenantId == tenant.Id, "Credential tenant did not round trip.");
        }

        private static async Task CredentialReadByAccessKeyUnknownAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Credential? read = await db.Credentials.ReadByAccessKeyAsync("ak_unknown").ConfigureAwait(false);
            TestCase.Require(read == null, "Reading a credential by an unknown access key should return null.");
        }

        private static async Task CredentialEnumerateByTenantAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant a, User ua) = await SeedTenantUserAsync(db, "credA").ConfigureAwait(false);
            (Tenant b, User ub) = await SeedTenantUserAsync(db, "credB").ConfigureAwait(false);
            await db.Credentials.CreateAsync(new Credential { TenantId = a.Id, UserId = ua.Id, AccessKey = "ak_a1" }).ConfigureAwait(false);
            await db.Credentials.CreateAsync(new Credential { TenantId = a.Id, UserId = ua.Id, AccessKey = "ak_a2" }).ConfigureAwait(false);
            await db.Credentials.CreateAsync(new Credential { TenantId = b.Id, UserId = ub.Id, AccessKey = "ak_b1" }).ConfigureAwait(false);

            EnumerationResult<Credential> aCreds = await db.Credentials.EnumerateAsync(a.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(aCreds.TotalRecords == 2, "Tenant A should have 2 credentials, got " + aCreds.TotalRecords + ".");
        }

        private static async Task CredentialCrossTenantReadNullAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant a, User ua) = await SeedTenantUserAsync(db, "credX").ConfigureAwait(false);
            (Tenant b, User ub) = await SeedTenantUserAsync(db, "credY").ConfigureAwait(false);
            Credential created = await db.Credentials.CreateAsync(new Credential { TenantId = a.Id, UserId = ua.Id, AccessKey = "ak_iso" }).ConfigureAwait(false);

            Credential? leak = await db.Credentials.ReadAsync(b.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(leak == null, "A credential in tenant A must not be readable from tenant B.");
        }

        private static async Task CredentialDeleteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, User user) = await SeedTenantUserAsync(db, "credDel").ConfigureAwait(false);
            Credential created = await db.Credentials.CreateAsync(new Credential { TenantId = tenant.Id, UserId = user.Id, AccessKey = "ak_del" }).ConfigureAwait(false);

            bool deleted = await db.Credentials.DeleteAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(deleted, "Deleting an existing credential should return true.");
            Credential? read = await db.Credentials.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read == null, "Deleted credential should read as null.");
        }

        #endregion

        #region Sessions

        private static async Task SessionCreateReadByTokenAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, User user) = await SeedTenantUserAsync(db, "sess1").ConfigureAwait(false);
            AuthSession created = await db.Sessions.CreateAsync(new AuthSession { TenantId = tenant.Id, UserId = user.Id, Token = "tok_abc123" }).ConfigureAwait(false);

            AuthSession? read = await db.Sessions.ReadByTokenAsync("tok_abc123").ConfigureAwait(false);
            TestCase.Require(read != null && read!.Id == created.Id, "Expected to read the session back by token.");
        }

        private static async Task SessionReadByTokenUnknownAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            AuthSession? read = await db.Sessions.ReadByTokenAsync("tok_unknown").ConfigureAwait(false);
            TestCase.Require(read == null, "Reading a session by an unknown token should return null.");
        }

        private static async Task SessionReadByIdAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, User user) = await SeedTenantUserAsync(db, "sess2").ConfigureAwait(false);
            AuthSession created = await db.Sessions.CreateAsync(new AuthSession { TenantId = tenant.Id, UserId = user.Id, Token = "tok_byid" }).ConfigureAwait(false);

            AuthSession? read = await db.Sessions.ReadAsync(created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.Token == "tok_byid", "Session did not round trip by id.");
        }

        private static async Task SessionEnumerateByTenantAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant a, User ua) = await SeedTenantUserAsync(db, "sessA").ConfigureAwait(false);
            (Tenant b, User ub) = await SeedTenantUserAsync(db, "sessB").ConfigureAwait(false);
            await db.Sessions.CreateAsync(new AuthSession { TenantId = a.Id, UserId = ua.Id, Token = "tok_a1" }).ConfigureAwait(false);
            await db.Sessions.CreateAsync(new AuthSession { TenantId = a.Id, UserId = ua.Id, Token = "tok_a2" }).ConfigureAwait(false);
            await db.Sessions.CreateAsync(new AuthSession { TenantId = b.Id, UserId = ub.Id, Token = "tok_b1" }).ConfigureAwait(false);

            EnumerationResult<AuthSession> aSessions = await db.Sessions.EnumerateAsync(a.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(aSessions.TotalRecords == 2, "Tenant A should have 2 sessions, got " + aSessions.TotalRecords + ".");
        }

        private static async Task SessionDeleteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, User user) = await SeedTenantUserAsync(db, "sessDel").ConfigureAwait(false);
            AuthSession created = await db.Sessions.CreateAsync(new AuthSession { TenantId = tenant.Id, UserId = user.Id, Token = "tok_del" }).ConfigureAwait(false);

            bool deleted = await db.Sessions.DeleteAsync(created.Id).ConfigureAwait(false);
            TestCase.Require(deleted, "Deleting an existing session should return true.");
            AuthSession? read = await db.Sessions.ReadAsync(created.Id).ConfigureAwait(false);
            TestCase.Require(read == null, "Deleted session should read as null.");
        }

        #endregion

        #region Scopes

        private static async Task ScopeCreateReadByNameAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope created = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj", StoreProvider = StoreProviderEnum.Filesystem }).ConfigureAwait(false);

            Scope? read = await db.Scopes.ReadByNameAsync(tenant.Id, "proj").ConfigureAwait(false);
            TestCase.Require(read != null && read!.Id == created.Id, "Expected to read the scope back by name.");
        }

        private static async Task ScopeReadByNameUnknownAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope? read = await db.Scopes.ReadByNameAsync(tenant.Id, "does-not-exist").ConfigureAwait(false);
            TestCase.Require(read == null, "Reading a scope by an unknown name should return null.");
        }

        private static async Task ScopeReadByIdAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope created = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj" }).ConfigureAwait(false);

            Scope? read = await db.Scopes.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.Name == "proj", "Scope did not round trip by id.");
        }

        private static async Task ScopeCrossTenantReadNullAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
            Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "B" }).ConfigureAwait(false);
            Scope created = await db.Scopes.CreateAsync(new Scope { TenantId = a.Id, Name = "sa" }).ConfigureAwait(false);

            Scope? leak = await db.Scopes.ReadAsync(b.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(leak == null, "A scope in tenant A must not be readable from tenant B.");
        }

        private static async Task ScopeEnumerateAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
            Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "B" }).ConfigureAwait(false);
            await db.Scopes.CreateAsync(new Scope { TenantId = a.Id, Name = "sa1" }).ConfigureAwait(false);
            await db.Scopes.CreateAsync(new Scope { TenantId = a.Id, Name = "sa2" }).ConfigureAwait(false);
            await db.Scopes.CreateAsync(new Scope { TenantId = b.Id, Name = "sb1" }).ConfigureAwait(false);

            EnumerationResult<Scope> aScopes = await db.Scopes.EnumerateAsync(a.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(aScopes.TotalRecords == 2, "Tenant A should have 2 scopes, got " + aScopes.TotalRecords + ".");
        }

        private static async Task ScopeUpdateDescriptionAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope created = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj", Description = "before" }).ConfigureAwait(false);
            created.Description = "after";
            await db.Scopes.UpdateAsync(created).ConfigureAwait(false);

            Scope? read = await db.Scopes.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.Description == "after", "Scope update did not persist the new description.");
        }

        private static async Task ScopeDeleteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope created = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj" }).ConfigureAwait(false);

            bool deleted = await db.Scopes.DeleteAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(deleted, "Deleting an existing scope should return true.");
            Scope? read = await db.Scopes.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read == null, "Deleted scope should read as null.");
        }

        private static async Task ScopeDeleteMissingAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            bool deleted = await db.Scopes.DeleteAsync(tenant.Id, "scp_does_not_exist").ConfigureAwait(false);
            TestCase.Require(!deleted, "Deleting a nonexistent scope should return false.");
        }

        #endregion

        #region Categories

        private static async Task<(Tenant tenant, Scope scope)> SeedTenantScopeAsync(DatabaseDriverBase db, string suffix)
        {
            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Tenant " + suffix }).ConfigureAwait(false);
            Scope scope = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "scope-" + suffix, StoreProvider = StoreProviderEnum.Filesystem }).ConfigureAwait(false);
            return (tenant, scope);
        }

        private static async Task CategoryCreateReadByNameAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope) = await SeedTenantScopeAsync(db, "cat1").ConfigureAwait(false);
            Category created = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes" }).ConfigureAwait(false);

            Category? read = await db.Categories.ReadByNameAsync(tenant.Id, scope.Id, "notes").ConfigureAwait(false);
            TestCase.Require(read != null && read!.Id == created.Id, "Expected to read the category back by name.");
        }

        private static async Task CategoryEnumerateByScopeAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope) = await SeedTenantScopeAsync(db, "cat2").ConfigureAwait(false);
            await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes" }).ConfigureAwait(false);
            await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "facts" }).ConfigureAwait(false);

            EnumerationResult<Category> categories = await db.Categories.EnumerateAsync(tenant.Id, scope.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(categories.TotalRecords == 2, "Expected 2 categories in the scope, got " + categories.TotalRecords + ".");
        }

        private static async Task CategoryScopeIsolationAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope scopeA = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "scopeA" }).ConfigureAwait(false);
            Scope scopeB = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "scopeB" }).ConfigureAwait(false);
            await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scopeA.Id, Name = "notes" }).ConfigureAwait(false);

            EnumerationResult<Category> inB = await db.Categories.EnumerateAsync(tenant.Id, scopeB.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(inB.TotalRecords == 0, "A category in scope A must not appear when enumerating scope B.");
        }

        private static async Task CategoryUpdateAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope) = await SeedTenantScopeAsync(db, "cat3").ConfigureAwait(false);
            Category created = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes", Instructions = "before" }).ConfigureAwait(false);
            created.Instructions = "after";
            await db.Categories.UpdateAsync(created).ConfigureAwait(false);

            Category? read = await db.Categories.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.Instructions == "after", "Category update did not persist.");
        }

        private static async Task CategoryDeleteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope) = await SeedTenantScopeAsync(db, "cat4").ConfigureAwait(false);
            Category created = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes" }).ConfigureAwait(false);

            bool deleted = await db.Categories.DeleteAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(deleted, "Deleting an existing category should return true.");
            Category? read = await db.Categories.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read == null, "Deleted category should read as null.");
        }

        #endregion

        #region Memories

        private static async Task<(Tenant tenant, Scope scope, Category category)> SeedTenantScopeCategoryAsync(DatabaseDriverBase db, string suffix)
        {
            (Tenant tenant, Scope scope) = await SeedTenantScopeAsync(db, suffix).ConfigureAwait(false);
            Category category = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes-" + suffix }).ConfigureAwait(false);
            return (tenant, scope, category);
        }

        private static async Task MemoryCreateReadBySlugAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope, Category category) = await SeedTenantScopeCategoryAsync(db, "mem1").ConfigureAwait(false);
            await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = category.Id, Slug = "s1", Title = "T", Body = "B" }).ConfigureAwait(false);

            Memory? read = await db.Memories.ReadBySlugAsync(tenant.Id, scope.Id, category.Id, "s1").ConfigureAwait(false);
            TestCase.Require(read != null && read!.Title == "T" && read.Body == "B", "Memory did not round trip by slug.");
        }

        private static async Task MemoryReadBySlugUnknownAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope, Category category) = await SeedTenantScopeCategoryAsync(db, "mem2").ConfigureAwait(false);
            Memory? read = await db.Memories.ReadBySlugAsync(tenant.Id, scope.Id, category.Id, "nope").ConfigureAwait(false);
            TestCase.Require(read == null, "Reading a memory by an unknown slug should return null.");
        }

        private static async Task MemoryEnumerateCategoryAndAllAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope, Category catA) = await SeedTenantScopeCategoryAsync(db, "mem3").ConfigureAwait(false);
            Category catB = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "second" }).ConfigureAwait(false);

            await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = catA.Id, Slug = "a1", Body = "b" }).ConfigureAwait(false);
            await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = catA.Id, Slug = "a2", Body = "b" }).ConfigureAwait(false);
            await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = catB.Id, Slug = "b1", Body = "b" }).ConfigureAwait(false);

            EnumerationResult<Memory> inA = await db.Memories.EnumerateAsync(tenant.Id, scope.Id, catA.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(inA.TotalRecords == 2, "Category A should have 2 memories, got " + inA.TotalRecords + ".");

            EnumerationResult<Memory> all = await db.Memories.EnumerateAsync(tenant.Id, scope.Id, null, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(all.TotalRecords == 3, "Enumerating all categories should return 3 memories, got " + all.TotalRecords + ".");
        }

        private static async Task MemoryCategoryFilterNarrowsAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope, Category catA) = await SeedTenantScopeCategoryAsync(db, "mem4").ConfigureAwait(false);
            Category catB = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "second" }).ConfigureAwait(false);

            await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = catA.Id, Slug = "a1", Body = "b" }).ConfigureAwait(false);
            await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = catB.Id, Slug = "b1", Body = "b" }).ConfigureAwait(false);
            await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = catB.Id, Slug = "b2", Body = "b" }).ConfigureAwait(false);

            EnumerationResult<Memory> inB = await db.Memories.EnumerateAsync(tenant.Id, scope.Id, catB.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(inB.TotalRecords == 2, "The category filter should narrow the result to 2, got " + inB.TotalRecords + ".");
            foreach (Memory memory in inB.Objects)
            {
                TestCase.Require(memory.CategoryId == catB.Id, "Filtered enumeration returned a memory from the wrong category.");
            }
        }

        private static async Task MemoryJsonRoundTripAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope, Category category) = await SeedTenantScopeCategoryAsync(db, "mem5").ConfigureAwait(false);
            Memory memory = new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = category.Id, Slug = "json", Body = "b" };
            memory.Tags.Add("alpha");
            memory.Tags.Add("beta");
            memory.Links.Add("other-slug");
            memory.Links.Add("second-slug");
            memory.Metadata["k1"] = "v1";
            memory.Metadata["k2"] = "v2";
            await db.Memories.CreateAsync(memory).ConfigureAwait(false);

            Memory? read = await db.Memories.ReadBySlugAsync(tenant.Id, scope.Id, category.Id, "json").ConfigureAwait(false);
            TestCase.Require(read != null, "Expected to read the memory back.");
            TestCase.Require(read!.Tags.Count == 2 && read.Tags.Contains("alpha") && read.Tags.Contains("beta"), "Tags did not round trip through JSON.");
            TestCase.Require(read.Links.Count == 2 && read.Links.Contains("other-slug") && read.Links.Contains("second-slug"), "Links did not round trip through JSON.");
            TestCase.Require(read.Metadata.Count == 2 && read.Metadata["k1"] == "v1" && read.Metadata["k2"] == "v2", "Metadata did not round trip through JSON.");
        }

        private static async Task MemoryInjectionSafetyAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope, Category category) = await SeedTenantScopeCategoryAsync(db, "mem6").ConfigureAwait(false);

            const string nastySlug = "O'Brien'); DROP TABLE memories; --";
            const string nastyBody = "Robert '); DROP TABLE memories; -- O'Brien was here";
            await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = category.Id, Slug = nastySlug, Title = "O'Brien", Body = nastyBody }).ConfigureAwait(false);

            Memory? read = await db.Memories.ReadBySlugAsync(tenant.Id, scope.Id, category.Id, nastySlug).ConfigureAwait(false);
            TestCase.Require(read != null, "The memory with SQL metacharacters should persist and read back.");
            TestCase.Require(read!.Body == nastyBody, "The injection-laden body should persist verbatim.");

            // The table must still exist and remain queryable (the DROP TABLE payload was not executed).
            EnumerationResult<Memory> all = await db.Memories.EnumerateAsync(tenant.Id, scope.Id, null, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(all.TotalRecords == 1, "The memories table must remain intact after the injection attempt, got " + all.TotalRecords + " rows.");
        }

        private static async Task MemoryUpdateAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope, Category category) = await SeedTenantScopeCategoryAsync(db, "mem7").ConfigureAwait(false);
            Memory created = await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = category.Id, Slug = "upd", Title = "V1", Body = "first" }).ConfigureAwait(false);
            created.Title = "V2";
            created.Body = "second";
            created.Version = created.Version + 1;
            await db.Memories.UpdateAsync(created).ConfigureAwait(false);

            Memory? read = await db.Memories.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.Title == "V2" && read.Body == "second" && read.Version == 2, "Memory update did not persist the bumped fields.");
        }

        private static async Task MemoryDeleteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            (Tenant tenant, Scope scope, Category category) = await SeedTenantScopeCategoryAsync(db, "mem8").ConfigureAwait(false);
            Memory created = await db.Memories.CreateAsync(new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = category.Id, Slug = "del", Body = "b" }).ConfigureAwait(false);

            bool deleted = await db.Memories.DeleteAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(deleted, "Deleting an existing memory should return true.");
            Memory? read = await db.Memories.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read == null, "Deleted memory should read as null.");
        }

        #endregion

        #region ModelEndpoints

        private static async Task EndpointEmbeddingPrefixAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            ModelEndpoint created = await db.ModelEndpoints.CreateAsync(new ModelEndpoint
            {
                TenantId = tenant.Id,
                Name = "embed",
                Kind = EndpointKindEnum.Embedding,
                Hostname = "127.0.0.1",
                Port = 11434,
                Dimensionality = 768
            }).ConfigureAwait(false);
            TestCase.Require(created.Id.StartsWith("eep_", StringComparison.Ordinal), "Embedding endpoint id should start with eep_, got " + created.Id + ".");
        }

        private static async Task EndpointInferencePrefixAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            ModelEndpoint created = await db.ModelEndpoints.CreateAsync(new ModelEndpoint
            {
                TenantId = tenant.Id,
                Name = "chat",
                Kind = EndpointKindEnum.Inference,
                Hostname = "127.0.0.1",
                Port = 8080
            }).ConfigureAwait(false);
            TestCase.Require(created.Id.StartsWith("iep_", StringComparison.Ordinal), "Inference endpoint id should start with iep_, got " + created.Id + ".");
        }

        private static async Task EndpointPrefixCorrectionAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);

            // An Embedding endpoint pre-set with an Inference-style id must be corrected to the eep_ prefix.
            ModelEndpoint embedding = await db.ModelEndpoints.CreateAsync(new ModelEndpoint
            {
                TenantId = tenant.Id,
                Name = "mismatch-embed",
                Kind = EndpointKindEnum.Embedding,
                Id = "iep_manually_wrong_prefix",
                Hostname = "127.0.0.1",
                Port = 11434
            }).ConfigureAwait(false);
            TestCase.Require(embedding.Id.StartsWith("eep_", StringComparison.Ordinal), "Create should correct a mismatched id to eep_ for an embedding endpoint, got " + embedding.Id + ".");

            // The inverse: an Inference endpoint pre-set with an Embedding-style id must be corrected to iep_.
            ModelEndpoint inference = await db.ModelEndpoints.CreateAsync(new ModelEndpoint
            {
                TenantId = tenant.Id,
                Name = "mismatch-infer",
                Kind = EndpointKindEnum.Inference,
                Id = "eep_manually_wrong_prefix",
                Hostname = "127.0.0.1",
                Port = 8080
            }).ConfigureAwait(false);
            TestCase.Require(inference.Id.StartsWith("iep_", StringComparison.Ordinal), "Create should correct a mismatched id to iep_ for an inference endpoint, got " + inference.Id + ".");
        }

        private static async Task EndpointReadAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            ModelEndpoint created = await db.ModelEndpoints.CreateAsync(new ModelEndpoint
            {
                TenantId = tenant.Id,
                Name = "embed",
                Kind = EndpointKindEnum.Embedding,
                Model = "nomic-embed-text",
                Dimensionality = 768,
                Port = 11434
            }).ConfigureAwait(false);

            ModelEndpoint? read = await db.ModelEndpoints.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.Model == "nomic-embed-text" && read.Dimensionality == 768, "Model endpoint did not round trip.");
        }

        private static async Task EndpointEnumerateByKindAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            await db.ModelEndpoints.CreateAsync(new ModelEndpoint { TenantId = tenant.Id, Name = "embed", Kind = EndpointKindEnum.Embedding, Port = 11434 }).ConfigureAwait(false);
            await db.ModelEndpoints.CreateAsync(new ModelEndpoint { TenantId = tenant.Id, Name = "chat", Kind = EndpointKindEnum.Inference, Port = 8080 }).ConfigureAwait(false);

            EnumerationResult<ModelEndpoint> embeddings = await db.ModelEndpoints.EnumerateAsync(tenant.Id, EndpointKindEnum.Embedding, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(embeddings.TotalRecords == 1, "Kind filter should return only the 1 embedding endpoint, got " + embeddings.TotalRecords + ".");
            foreach (ModelEndpoint endpoint in embeddings.Objects)
            {
                TestCase.Require(endpoint.Kind == EndpointKindEnum.Embedding, "Kind filter returned a non-embedding endpoint.");
            }
        }

        private static async Task EndpointUpdateAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            ModelEndpoint created = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { TenantId = tenant.Id, Name = "embed", Kind = EndpointKindEnum.Embedding, Dimensionality = 768, Port = 11434 }).ConfigureAwait(false);
            created.Dimensionality = 1024;
            await db.ModelEndpoints.UpdateAsync(created).ConfigureAwait(false);

            ModelEndpoint? read = await db.ModelEndpoints.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.Dimensionality == 1024, "Model endpoint update did not persist.");
        }

        private static async Task EndpointDeleteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            ModelEndpoint created = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { TenantId = tenant.Id, Name = "chat", Kind = EndpointKindEnum.Inference, Port = 8080 }).ConfigureAwait(false);

            bool deleted = await db.ModelEndpoints.DeleteAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(deleted, "Deleting an existing endpoint should return true.");
            ModelEndpoint? read = await db.ModelEndpoints.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read == null, "Deleted endpoint should read as null.");
        }

        #endregion

        #region RequestHistory

        private static async Task RequestHistoryCreateAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);

            RequestHistoryEntry nullTenant = await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = null, Method = "GET", Path = "/anon", StatusCode = 401, DurationMs = 0.5 }).ConfigureAwait(false);
            TestCase.Require(!string.IsNullOrEmpty(nullTenant.Id), "Creating a null-tenant request history entry should succeed.");

            RequestHistoryEntry withTenant = await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = tenant.Id, Method = "GET", Path = "/x", StatusCode = 200, DurationMs = 1.5 }).ConfigureAwait(false);
            TestCase.Require(!string.IsNullOrEmpty(withTenant.Id), "Creating a tenant-scoped request history entry should succeed.");
        }

        private static async Task RequestHistoryEnumerateAllAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = null, Method = "GET", Path = "/anon" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = tenant.Id, Method = "GET", Path = "/x" }).ConfigureAwait(false);

            EnumerationResult<RequestHistoryEntry> all = await db.RequestHistory.EnumerateAsync(null, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(all.TotalRecords == 2, "Enumerating across all tenants should include both rows, got " + all.TotalRecords + ".");

            bool sawNull = false;
            foreach (RequestHistoryEntry entry in all.Objects)
            {
                if (entry.TenantId == null) sawNull = true;
            }
            TestCase.Require(sawNull, "Enumerating across all tenants should include the null-tenant row.");
        }

        private static async Task RequestHistoryEnumerateByTenantAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
            Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "B" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = a.Id, Method = "GET", Path = "/a1" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = a.Id, Method = "GET", Path = "/a2" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = b.Id, Method = "GET", Path = "/b1" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = null, Method = "GET", Path = "/anon" }).ConfigureAwait(false);

            EnumerationResult<RequestHistoryEntry> aHistory = await db.RequestHistory.EnumerateAsync(a.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(aHistory.TotalRecords == 2, "Tenant A history should be 2 rows, got " + aHistory.TotalRecords + ".");
            foreach (RequestHistoryEntry entry in aHistory.Objects)
            {
                TestCase.Require(entry.TenantId == a.Id, "Tenant-scoped history returned a row for another tenant.");
            }
        }

        private static async Task RequestHistoryReadByIdAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            RequestHistoryEntry created = await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = tenant.Id, Method = "POST", Path = "/read-me", StatusCode = 201 }).ConfigureAwait(false);

            RequestHistoryEntry? read = await db.RequestHistory.ReadAsync(created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read!.Path == "/read-me" && read.StatusCode == 201, "Request history entry did not round trip by id.");
        }

        private static async Task RequestHistoryDeleteAllTenantAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
            Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "B" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = a.Id, Method = "GET", Path = "/a1" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = a.Id, Method = "GET", Path = "/a2" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = b.Id, Method = "GET", Path = "/b1" }).ConfigureAwait(false);

            long removed = await db.RequestHistory.DeleteAllAsync(a.Id).ConfigureAwait(false);
            TestCase.Require(removed == 2, "Delete-all for tenant A should report 2 removed, got " + removed + ".");

            EnumerationResult<RequestHistoryEntry> aHistory = await db.RequestHistory.EnumerateAsync(a.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(aHistory.TotalRecords == 0, "Tenant A history should be empty after delete-all.");
            EnumerationResult<RequestHistoryEntry> bHistory = await db.RequestHistory.EnumerateAsync(b.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(bHistory.TotalRecords == 1, "Tenant B history must be untouched by tenant A delete-all.");
        }

        private static async Task RequestHistoryDeleteAllNullAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = a.Id, Method = "GET", Path = "/a1" }).ConfigureAwait(false);
            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = null, Method = "GET", Path = "/anon" }).ConfigureAwait(false);

            long removed = await db.RequestHistory.DeleteAllAsync(null).ConfigureAwait(false);
            TestCase.Require(removed == 2, "Delete-all with null should clear every row, reporting 2 removed, got " + removed + ".");

            EnumerationResult<RequestHistoryEntry> all = await db.RequestHistory.EnumerateAsync(null, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(all.TotalRecords == 0, "All request history should be cleared after delete-all with null.");
        }

        #endregion

        #region Multi-Tenant-Isolation

        private static async Task MultiTenantIsolationAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            DatabaseDriverBase db = t.Db;

            Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
            Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "B" }).ConfigureAwait(false);
            Scope sa = await db.Scopes.CreateAsync(new Scope { TenantId = a.Id, Name = "sa" }).ConfigureAwait(false);
            Scope sb = await db.Scopes.CreateAsync(new Scope { TenantId = b.Id, Name = "sb" }).ConfigureAwait(false);

            EnumerationResult<Scope> aScopes = await db.Scopes.EnumerateAsync(a.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
            TestCase.Require(aScopes.TotalRecords == 1, "Tenant A should see exactly 1 scope, got " + aScopes.TotalRecords + ".");
            TestCase.Require(aScopes.Objects.Count == 1 && aScopes.Objects[0].Id == sa.Id, "Tenant A should see only its own scope.");
            foreach (Scope scope in aScopes.Objects)
            {
                TestCase.Require(scope.Id != sb.Id, "Tenant A enumeration must not include tenant B's scope.");
            }
        }

        #endregion
    }
}
