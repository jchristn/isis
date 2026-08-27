namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Server;
    using Isis.Server.Services;
    using Isis.Server.Settings;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// Boots an in-process Isis server against a temporary SQLite database and filesystem workspace for
    /// integration tests, and tears it down cleanly.
    /// </summary>
    internal sealed class ServerHarness : IDisposable
    {
        #region Internal-Members

        internal IsisServer Server { get; private set; } = null!;
        internal DatabaseDriverBase Database { get; private set; } = null!;
        internal int Port { get; private set; }
        internal string WorkDir { get; private set; } = string.Empty;
        internal string AdminEmail { get; private set; } = "admin@isis.local";
        internal string AdminPassword { get; private set; } = "isisadmin";
        internal string AdminToken { get; private set; } = string.Empty;
        internal string AccessKey { get; private set; } = "isisdefaultkey";
        internal string SecretKey { get; private set; } = "isisdefaultsecret";
        internal string TenantId { get; private set; } = DefaultSeeder.DefaultTenantId;

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private ServerHarness()
        {
        }

        /// <summary>
        /// Start a fresh server harness.
        /// </summary>
        /// <returns>The started harness.</returns>
        internal static async Task<ServerHarness> StartAsync()
        {
            ServerHarness harness = new ServerHarness();
            harness.WorkDir = Path.Combine(Path.GetTempPath(), "isis-test-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(harness.WorkDir);
            harness.Port = GetFreePort();

            IsisSettings settings = new IsisSettings();
            settings.NodeId = "test";
            settings.Rest = new RestSettings { Hostname = "127.0.0.1", Port = harness.Port, Ssl = false };
            settings.Database = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(harness.WorkDir, "isis.db") };
            settings.Auth = new AuthSettings
            {
                SeedAdminEmail = harness.AdminEmail,
                SeedAdminPassword = harness.AdminPassword,
                DefaultAccessKey = harness.AccessKey,
                DefaultSecretKey = harness.SecretKey
            };

            harness.Database = DatabaseDriverFactory.Create(settings.Database);
            await harness.Database.InitializeAsync().ConfigureAwait(false);
            await DefaultSeeder.SeedAsync(harness.Database, settings.Auth, _ => { }).ConfigureAwait(false);

            AuthenticationService auth = new AuthenticationService(harness.Database, settings.Auth);
            AuthorizationService authz = new AuthorizationService();
            MemoryService memory = new MemoryService(harness.Database);

            harness.Server = new IsisServer(settings, harness.Database, auth, authz, memory);
            harness.Server.Start();

            await harness.WaitForHealthAsync().ConfigureAwait(false);
            harness.AdminToken = await harness.LoginAdminAsync().ConfigureAwait(false);
            return harness;
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// An HTTP client authenticated as the seeded system administrator (session bearer token).
        /// </summary>
        /// <returns>The client.</returns>
        internal HttpClient AdminClient()
        {
            HttpClient client = NewClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + AdminToken);
            return client;
        }

        /// <summary>
        /// An HTTP client authenticated as the default tenant credential (x-access-key + x-secret-key).
        /// </summary>
        /// <returns>The client.</returns>
        internal HttpClient AccessClient()
        {
            HttpClient client = NewClient();
            client.DefaultRequestHeaders.Add("x-access-key", AccessKey);
            client.DefaultRequestHeaders.Add("x-secret-key", SecretKey);
            return client;
        }

        /// <summary>
        /// An unauthenticated HTTP client.
        /// </summary>
        /// <returns>The client.</returns>
        internal HttpClient AnonymousClient()
        {
            return NewClient();
        }

        /// <summary>
        /// Dispose the harness, stopping the server and deleting temporary files.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            try { Server?.Stop(); } catch { }
            try { Server?.Dispose(); } catch { }
            try { Database?.Dispose(); } catch { }
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true); } catch { }
        }

        #endregion

        #region Private-Methods

        private HttpClient NewClient()
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri("http://127.0.0.1:" + Port);
            return client;
        }

        private async Task<string> LoginAdminAsync()
        {
            using HttpClient client = NewClient();
            string body = JsonSerializer.Serialize(new { email = AdminEmail, password = AdminPassword, tenantId = TenantId });
            using StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync("/v1.0/api/token", content).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Admin login failed (" + (int)response.StatusCode + "): " + text);
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.GetProperty("token").GetString() ?? throw new InvalidOperationException("Login response contained no token.");
        }

        private async Task WaitForHealthAsync()
        {
            using HttpClient client = NewClient();
            for (int attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    HttpResponseMessage response = await client.GetAsync("/v1.0/api/health").ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK) return;
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new InvalidOperationException("Server did not become healthy in time.");
        }

        private static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
