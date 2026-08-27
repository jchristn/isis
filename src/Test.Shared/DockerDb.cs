namespace Test.Shared
{
    using System;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;

    /// <summary>
    /// Helpers for spinning up ephemeral database containers for live provider tests.
    /// </summary>
    internal static class DockerDb
    {
        #region Internal-Methods

        /// <summary>
        /// Whether the docker CLI is available.
        /// </summary>
        /// <returns>True when docker responds.</returns>
        internal static bool Available()
        {
            try
            {
                ProcessResult result = Run("version --format {{.Server.Version}}", 15000);
                return result.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Run a docker command.
        /// </summary>
        /// <param name="arguments">The docker arguments.</param>
        /// <param name="timeoutMs">The timeout in milliseconds.</param>
        /// <returns>The process result.</returns>
        internal static ProcessResult Run(string arguments, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo("docker", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            ProcessResult result = new ProcessResult();
            using Process process = new Process { StartInfo = psi };
            StringBuilder output = new StringBuilder();
            StringBuilder error = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(true); } catch (Exception) { }
                result.ExitCode = -1;
                result.Error = "Process timed out.";
                return result;
            }

            process.WaitForExit();
            result.ExitCode = process.ExitCode;
            result.Output = output.ToString();
            result.Error = error.ToString();
            return result;
        }

        /// <summary>
        /// Get a free loopback TCP port.
        /// </summary>
        /// <returns>A free port.</returns>
        internal static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// Repeatedly attempt to connect a driver until it responds or the attempts are exhausted.
        /// </summary>
        /// <param name="factory">A factory that creates a fresh driver each attempt.</param>
        /// <param name="attempts">The number of attempts.</param>
        /// <param name="delayMs">The delay between attempts.</param>
        /// <returns>True when a connection succeeded.</returns>
        internal static async Task<bool> WaitForPingAsync(Func<DatabaseDriverBase> factory, int attempts, int delayMs)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    using DatabaseDriverBase driver = factory();
                    if (await driver.PingAsync().ConfigureAwait(false)) return true;
                }
                catch (Exception)
                {
                }

                await Task.Delay(delayMs).ConfigureAwait(false);
            }

            return false;
        }

        #endregion
    }
}
