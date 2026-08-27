namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using Isis.McpServer;
    using Touchstone.Core;

    /// <summary>
    /// Automated suite for <see cref="McpInstaller"/>. Verifies that installing the "isis" MCP entry into an agent
    /// client configuration file writes the expected shape, preserves unrelated servers and unknown keys, backs up
    /// any existing file, honours the chosen auth header, is idempotent, and creates missing directories.
    /// </summary>
    public static class InstallSuite
    {
        #region Public-Methods

        /// <summary>
        /// Get the Isis MCP install Touchstone test suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Suite()
        {
            return new TestSuiteDescriptor(
                "install",
                "Isis MCP Install Suite",
                new List<TestCaseDescriptor>
                {
                    TestCase.Sync("install", "fresh-install", "Fresh install writes an http isis entry", FreshInstall),
                    TestCase.Sync("install", "preserve-existing", "Install preserves other servers and unknown keys", PreserveExisting),
                    TestCase.Sync("install", "backup-created", "Install backs up an existing file", BackupCreated),
                    TestCase.Sync("install", "access-key-header", "Install honours the x-access-key header", AccessKeyHeader),
                    TestCase.Sync("install", "idempotent", "Installing twice leaves a single isis entry", Idempotent),
                    TestCase.Sync("install", "idempotent-updates-url", "Re-installing updates the isis url", IdempotentUpdatesUrl),
                    TestCase.Sync("install", "empty-existing-file", "Install repairs an empty existing file", EmptyExistingFile),
                    TestCase.Sync("install", "missing-directory-created", "Install creates a missing target directory", MissingDirectoryCreated)
                });
        }

        #endregion

        #region Private-Methods-Cases

        private static void FreshInstall()
        {
            string tmp = TempFile();
            try
            {
                McpInstaller.Install(tmp, "http://127.0.0.1:8720/mcp", Creds());

                using JsonDocument doc = Load(tmp);
                JsonElement isis = doc.RootElement.GetProperty("mcpServers").GetProperty("isis");
                Require(isis.GetProperty("type").GetString() == "http", "Expected type 'http'.");
                Require(isis.GetProperty("url").GetString() == "http://127.0.0.1:8720/mcp", "Expected the url to match.");
                JsonElement freshHeaders = isis.GetProperty("headers");
                Require(freshHeaders.GetProperty("x-access-key").GetString() == "isisdefaultkey", "Expected the x-access-key header to be 'isisdefaultkey'.");
                Require(freshHeaders.GetProperty("x-secret-key").GetString() == "isisdefaultsecret", "Expected the x-secret-key header to be 'isisdefaultsecret'.");
                Require(!freshHeaders.TryGetProperty("x-api-key", out _), "Expected no x-api-key header.");
            }
            finally
            {
                Cleanup(tmp);
            }
        }

        private static void PreserveExisting()
        {
            string tmp = TempFile();
            try
            {
                File.WriteAllText(tmp, "{\"mcpServers\":{\"other\":{\"command\":\"foo\",\"args\":[\"bar\"]}},\"topKey\":42}");
                McpInstaller.Install(tmp, "http://127.0.0.1:8720/mcp", Creds());

                using JsonDocument doc = Load(tmp);
                JsonElement root = doc.RootElement;

                JsonElement other = root.GetProperty("mcpServers").GetProperty("other");
                Require(other.GetProperty("command").GetString() == "foo", "Expected the 'other' server command to be preserved.");
                Require(other.GetProperty("args")[0].GetString() == "bar", "Expected the 'other' server args to be preserved.");
                Require(root.GetProperty("topKey").GetInt32() == 42, "Expected the unknown top-level key to be preserved.");
                Require(root.GetProperty("mcpServers").TryGetProperty("isis", out _), "Expected the isis entry to be added.");
                Require(File.Exists(tmp + ".bak"), "Expected a backup file to be written.");
            }
            finally
            {
                Cleanup(tmp);
            }
        }

        private static void BackupCreated()
        {
            string tmp = TempFile();
            try
            {
                string original = "{\"mcpServers\":{\"other\":{\"command\":\"foo\"}}}";
                File.WriteAllText(tmp, original);
                McpInstaller.Install(tmp, "http://127.0.0.1:8720/mcp", Creds());

                Require(File.Exists(tmp + ".bak"), "Expected a backup file to be written.");
                Require(File.ReadAllText(tmp + ".bak") == original, "Expected the backup to contain the original content.");
            }
            finally
            {
                Cleanup(tmp);
            }
        }

        private static void AccessKeyHeader()
        {
            string tmp = TempFile();
            try
            {
                Dictionary<string, string> headers = new Dictionary<string, string> { ["x-access-key"] = "tok", ["x-secret-key"] = "shh" };
                McpInstaller.Install(tmp, "http://127.0.0.1:8720/mcp", headers);

                using JsonDocument doc = Load(tmp);
                JsonElement written = doc.RootElement.GetProperty("mcpServers").GetProperty("isis").GetProperty("headers");
                Require(written.GetProperty("x-access-key").GetString() == "tok", "Expected the x-access-key header to be 'tok'.");
                Require(written.GetProperty("x-secret-key").GetString() == "shh", "Expected the x-secret-key header to be 'shh'.");
                Require(!written.TryGetProperty("x-api-key", out _), "Expected no x-api-key header when installing an access key.");
            }
            finally
            {
                Cleanup(tmp);
            }
        }

        private static void Idempotent()
        {
            string tmp = TempFile();
            try
            {
                McpInstaller.Install(tmp, "http://127.0.0.1:8720/mcp", Creds());
                McpInstaller.Install(tmp, "http://127.0.0.1:8720/mcp", Creds());

                using JsonDocument doc = Load(tmp);
                JsonElement servers = doc.RootElement.GetProperty("mcpServers");
                int isisCount = 0;
                foreach (JsonProperty property in servers.EnumerateObject())
                {
                    if (property.Name == "isis") isisCount++;
                }

                Require(isisCount == 1, "Expected exactly one isis entry after two installs, got " + isisCount + ".");
                Require(servers.GetProperty("isis").GetProperty("url").GetString() == "http://127.0.0.1:8720/mcp", "Expected the url to remain valid.");
            }
            finally
            {
                Cleanup(tmp);
            }
        }

        private static void IdempotentUpdatesUrl()
        {
            string tmp = TempFile();
            try
            {
                McpInstaller.Install(tmp, "http://127.0.0.1:8720/mcp", Creds());
                McpInstaller.Install(tmp, "http://127.0.0.1:9999/mcp", Creds());

                using JsonDocument doc = Load(tmp);
                JsonElement isis = doc.RootElement.GetProperty("mcpServers").GetProperty("isis");
                Require(isis.GetProperty("url").GetString() == "http://127.0.0.1:9999/mcp", "Expected the latest url to win.");
            }
            finally
            {
                Cleanup(tmp);
            }
        }

        private static void EmptyExistingFile()
        {
            string tmp = TempFile();
            try
            {
                File.WriteAllText(tmp, "   ");
                McpInstaller.Install(tmp, "http://127.0.0.1:8720/mcp", Creds());

                using JsonDocument doc = Load(tmp);
                JsonElement isis = doc.RootElement.GetProperty("mcpServers").GetProperty("isis");
                Require(isis.GetProperty("url").GetString() == "http://127.0.0.1:8720/mcp", "Expected a valid isis entry after repairing an empty file.");
            }
            finally
            {
                Cleanup(tmp);
            }
        }

        private static void MissingDirectoryCreated()
        {
            string dir = Path.Combine(Path.GetTempPath(), "isis-install-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            string tmp = Path.Combine(dir, ".mcp.json");
            try
            {
                Require(!Directory.Exists(dir), "Test precondition: the target directory should not yet exist.");
                McpInstaller.Install(tmp, "http://127.0.0.1:8720/mcp", Creds());

                Require(File.Exists(tmp), "Expected the config file to be created in the new directory.");
                using JsonDocument doc = Load(tmp);
                Require(doc.RootElement.GetProperty("mcpServers").TryGetProperty("isis", out _), "Expected the isis entry to be present.");
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
        }

        #endregion

        #region Private-Methods-Helpers

        private static Dictionary<string, string> Creds()
        {
            return new Dictionary<string, string> { ["x-access-key"] = "isisdefaultkey", ["x-secret-key"] = "isisdefaultsecret" };
        }

        private static string TempFile()
        {
            return Path.Combine(Path.GetTempPath(), "isis-install-" + Guid.NewGuid().ToString("N") + ".json");
        }

        private static JsonDocument Load(string path)
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }

        private static void Require(bool condition, string message)
        {
            TestCase.Require(condition, message);
        }

        private static void Cleanup(string tmp)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            try { if (File.Exists(tmp + ".bak")) File.Delete(tmp + ".bak"); } catch { }
        }

        #endregion
    }
}
