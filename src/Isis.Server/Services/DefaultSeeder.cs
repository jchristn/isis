namespace Isis.Server.Services
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;
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
        /// <param name="seedEndpoints">When true, seed default embedding/inference endpoints if none exist.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Awaitable task.</returns>
        public static async Task SeedAsync(DatabaseDriverBase database, AuthSettings auth, Action<string> log, bool seedEndpoints = true, CancellationToken token = default)
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

            EnumerationResult<Instruction> instructions = await database.Instructions.EnumerateAsync(DefaultTenantId, null, new EnumerationQuery { MaxResults = 1 }, token).ConfigureAwait(false);
            if (instructions.TotalRecords == 0)
            {
                await database.Instructions.CreateManyAsync(DefaultInstructions.For(DefaultTenantId), token).ConfigureAwait(false);
                log?.Invoke("seeded default instructions for '" + DefaultTenantId + "'");
            }

            if (seedEndpoints) await SeedEndpointsAsync(database, log, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static async Task SeedEndpointsAsync(DatabaseDriverBase database, Action<string>? log, CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery { MaxResults = 1 };

            // Hostname the default Ollama endpoints point at. Defaults to 'localhost' for a bare local run; the
            // Docker stack sets ISIS_DEFAULT_ENDPOINT_HOST='ollama' so the in-container endpoints reach the
            // bundled Ollama service (localhost inside the container would be the container itself).
            // Base URL the default Ollama endpoints point at. ISIS_DEFAULT_ENDPOINT_BASEURL sets it outright;
            // otherwise ISIS_DEFAULT_ENDPOINT_HOST supplies the host of a standard Ollama (port 11434). The
            // Docker stack sets the host to 'ollama' so in-container endpoints reach the bundled service.
            string baseUrl = Environment.GetEnvironmentVariable("ISIS_DEFAULT_ENDPOINT_BASEURL") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                string endpointHost = Environment.GetEnvironmentVariable("ISIS_DEFAULT_ENDPOINT_HOST") ?? "localhost";
                if (string.IsNullOrWhiteSpace(endpointHost)) endpointHost = "localhost";
                baseUrl = "http://" + endpointHost + ":11434";
            }

            EnumerationResult<ModelEndpoint> embeddings = await database.ModelEndpoints.EnumerateAsync(DefaultTenantId, EndpointKindEnum.Embedding, query, token).ConfigureAwait(false);
            if (embeddings.TotalRecords == 0)
            {
                ModelEndpoint embedding = new ModelEndpoint
                {
                    Id = IdGenerator.EmbeddingEndpoint(),
                    TenantId = DefaultTenantId,
                    Name = "Default Embedding (Ollama all-minilm)",
                    Kind = EndpointKindEnum.Embedding,
                    ApiFormat = ApiFormatEnum.Ollama,
                    BaseUrl = baseUrl,
                    AuthType = EndpointAuthTypeEnum.None,
                    Model = "all-minilm",
                    Dimensionality = 384,
                    // MaxInputTokens is left at 0 (auto): TextChunker knows all-minilm's real 256-token context,
                    // and MemoryChunker reserves the WordPiece [CLS]/[SEP] tokens. Set a value here only to pin a
                    // model whose real context the chunker library does not already know.
                    HealthCheckUrl = "/api/tags"
                };
                await database.ModelEndpoints.CreateAsync(embedding, token).ConfigureAwait(false);
                log?.Invoke("seeded default embedding endpoint (Ollama all-minilm @ " + baseUrl + ")");
            }

            EnumerationResult<ModelEndpoint> inference = await database.ModelEndpoints.EnumerateAsync(DefaultTenantId, EndpointKindEnum.Inference, query, token).ConfigureAwait(false);
            if (inference.TotalRecords == 0)
            {
                ModelEndpoint completion = new ModelEndpoint
                {
                    Id = IdGenerator.InferenceEndpoint(),
                    TenantId = DefaultTenantId,
                    Name = "Default Inference (Ollama gemma3:4b)",
                    Kind = EndpointKindEnum.Inference,
                    ApiFormat = ApiFormatEnum.Ollama,
                    BaseUrl = baseUrl,
                    AuthType = EndpointAuthTypeEnum.None,
                    Model = "gemma3:4b",
                    HealthCheckUrl = "/api/tags"
                };
                await database.ModelEndpoints.CreateAsync(completion, token).ConfigureAwait(false);
                log?.Invoke("seeded default inference endpoint (Ollama gemma3:4b @ " + baseUrl + ")");
            }
        }

        /// <summary>
        /// Seed a Rerank endpoint for the default tenant when ISIS_DEFAULT_RERANK_BASEURL is set (the reference stack's
        /// optional reranker, a Text Embeddings Inference server). The reranker may start after Isis or not at all, so
        /// this waits for its /health to answer, polling for up to <paramref name="maxWait"/>, and seeds only then. New
        /// RecallDB scopes attach the seeded endpoint automatically. Idempotent: does nothing when the tenant already
        /// has a Rerank endpoint.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="http">HTTP client for the health probe.</param>
        /// <param name="maxWait">How long to wait for the reranker.</param>
        /// <param name="log">Optional log callback.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when an endpoint was seeded.</returns>
        /// <exception cref="ArgumentNullException">Thrown when database or http is null.</exception>
        public static async Task<bool> SeedRerankEndpointAsync(DatabaseDriverBase database, HttpClient http, TimeSpan maxWait, Action<string>? log = null, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (http == null) throw new ArgumentNullException(nameof(http));

            string baseUrl = (Environment.GetEnvironmentVariable("ISIS_DEFAULT_RERANK_BASEURL") ?? string.Empty).Trim().TrimEnd('/');
            if (baseUrl.Length == 0) return false;
            string model = Environment.GetEnvironmentVariable("ISIS_DEFAULT_RERANK_MODEL") ?? "cross-encoder/ms-marco-MiniLM-L-6-v2";

            EnumerationQuery query = new EnumerationQuery { MaxResults = 1 };
            EnumerationResult<ModelEndpoint> existing = await database.ModelEndpoints.EnumerateAsync(DefaultTenantId, EndpointKindEnum.Rerank, query, token).ConfigureAwait(false);
            if (existing.TotalRecords > 0) return false;

            DateTime deadline = DateTime.UtcNow.Add(maxWait);
            while (true)
            {
                try
                {
                    using CancellationTokenSource probe = CancellationTokenSource.CreateLinkedTokenSource(token);
                    probe.CancelAfter(TimeSpan.FromSeconds(5));
                    using HttpResponseMessage response = await http.GetAsync(baseUrl + "/health", probe.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) break;
                }
                catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !token.IsCancellationRequested))
                {
                    // Not up yet.
                }

                if (DateTime.UtcNow >= deadline)
                {
                    log?.Invoke("reranker at " + baseUrl + " did not answer; no rerank endpoint seeded");
                    return false;
                }

                await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            }

            ModelEndpoint rerank = new ModelEndpoint
            {
                Id = IdGenerator.RerankEndpoint(),
                TenantId = DefaultTenantId,
                Name = "Default Rerank (TEI " + model + ")",
                Kind = EndpointKindEnum.Rerank,
                ApiFormat = ApiFormatEnum.Tei,
                BaseUrl = baseUrl,
                AuthType = EndpointAuthTypeEnum.None,
                Model = model,
                // A reranker slower than this falls back to retrieval order rather than holding up the search.
                TimeoutMs = 3000,
                HealthCheckUrl = "/health"
            };
            await database.ModelEndpoints.CreateAsync(rerank, token).ConfigureAwait(false);
            log?.Invoke("seeded default rerank endpoint (" + model + " @ " + baseUrl + ")");
            return true;
        }

        #endregion
    }
}
