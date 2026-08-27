namespace Isis.McpServer
{
    using System;

    /// <summary>
    /// Application entry point.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point. Runs the MCP server, or the "install" command when requested.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                if (string.Equals(args[0], "install", StringComparison.OrdinalIgnoreCase))
                {
                    return McpInstaller.Run(args[1..]);
                }

                if (string.Equals(args[0], "mcp", StringComparison.OrdinalIgnoreCase)
                    && args.Length > 1
                    && string.Equals(args[1], "install", StringComparison.OrdinalIgnoreCase))
                {
                    return McpInstaller.Run(args[2..]);
                }
            }

            Bootstrapper.Run(args ?? Array.Empty<string>());
            return 0;
        }
    }
}
