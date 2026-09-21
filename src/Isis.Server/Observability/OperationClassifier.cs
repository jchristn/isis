namespace Isis.Server.Observability
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Classifies a handled HTTP request into a semantic operation (resource type + operation verb) for the
    /// operation-events observability feed. Classification is path- and method-based so it stays consistent
    /// with, and derived from, the same post-routing capture that records request history — and it captures
    /// agentic (MCP) activity automatically, since the MCP server proxies those calls through the REST API.
    /// </summary>
    public static class OperationClassifier
    {
        #region Public-Members

        /// <summary>Scope resource type.</summary>
        public const string ResourceScope = "scope";

        /// <summary>Memory resource type.</summary>
        public const string ResourceMemory = "memory";

        /// <summary>Category resource type.</summary>
        public const string ResourceCategory = "category";

        /// <summary>Instruction resource type.</summary>
        public const string ResourceInstruction = "instruction";

        /// <summary>Model endpoint resource type.</summary>
        public const string ResourceEndpoint = "endpoint";

        /// <summary>RecallDB collection resource type.</summary>
        public const string ResourceCollection = "collection";

        /// <summary>Search / recall resource type.</summary>
        public const string ResourceSearch = "search";

        /// <summary>Chat / RAG resource type.</summary>
        public const string ResourceChat = "chat";

        /// <summary>Authentication resource type.</summary>
        public const string ResourceAuth = "auth";

        /// <summary>Tenant resource type.</summary>
        public const string ResourceTenant = "tenant";

        /// <summary>User resource type.</summary>
        public const string ResourceUser = "user";

        /// <summary>Credential resource type.</summary>
        public const string ResourceCredential = "credential";

        /// <summary>Server settings resource type.</summary>
        public const string ResourceSettings = "settings";

        #endregion

        #region Private-Members

        // Path segments that are collection names, action keywords, or auth verbs — never resource identifiers.
        private static readonly HashSet<string> _NonIdentifierSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "scopes", "memories", "categories", "instructions", "endpoints", "collections", "users",
            "credentials", "tenants", "chat", "stream", "search", "batch", "batch-get", "batch-delete",
            "endpoint-health", "settings", "restart", "token", "whoami", "tenants-for-email",
            "requests", "operations"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Attempt to classify a request into an operation event descriptor.
        /// </summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="pathWithoutQuery">The request path with the query string removed.</param>
        /// <param name="resourceType">The classified resource type when the method returns true.</param>
        /// <param name="operation">The classified operation verb when the method returns true.</param>
        /// <param name="resourceId">The targeted resource identifier, when the path carried one.</param>
        /// <param name="scopeId">The targeted scope identifier, when the path was scope-qualified.</param>
        /// <returns>True when the request maps to a trackable operation; false when it should be ignored.</returns>
        public static bool TryClassify(string method, string pathWithoutQuery, out string resourceType, out string operation, out string? resourceId, out string? scopeId)
        {
            resourceType = string.Empty;
            operation = string.Empty;
            resourceId = null;
            scopeId = null;

            if (string.IsNullOrEmpty(pathWithoutQuery)) return false;

            string normalized = pathWithoutQuery.Trim();
            const string prefix = "/v1.0/api";
            int index = normalized.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;

            string remainder = normalized.Substring(index + prefix.Length).Trim('/');
            if (string.IsNullOrEmpty(remainder)) return false;

            string[] segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return false;

            string upper = method?.ToUpperInvariant() ?? "GET";
            string last = segments[segments.Length - 1];

            // Observability meta endpoints and infrastructure are not themselves operations.
            if (Equals(segments[0], "requests") || Equals(segments[0], "operations") ||
                Equals(segments[0], "health") || Equals(segments[0], "metrics") ||
                Equals(segments[0], "openapi") || Equals(segments[0], "swagger"))
            {
                return false;
            }

            // Scope qualifier: the identifier immediately following a "scopes" segment (unless it is an action).
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (Equals(segments[i], "scopes") && !_NonIdentifierSegments.Contains(segments[i + 1]))
                {
                    scopeId = segments[i + 1];
                    break;
                }
            }

            // Trailing resource identifier for item routes.
            if (!_NonIdentifierSegments.Contains(last)) resourceId = last;

            // Authentication verbs.
            if (Equals(last, "token"))
            {
                resourceType = ResourceAuth;
                operation = upper == "DELETE" ? "logout" : "login";
                resourceId = null;
                return true;
            }
            if (Equals(last, "whoami") || Equals(last, "tenants-for-email"))
            {
                resourceType = ResourceAuth;
                operation = "read";
                resourceId = null;
                return true;
            }

            // Search and chat are distinct agentic resource types.
            if (Equals(last, "search"))
            {
                resourceType = ResourceSearch;
                operation = "search";
                return true;
            }
            if (Equals(last, "chat") || Equals(last, "stream"))
            {
                resourceType = ResourceChat;
                operation = "chat";
                return true;
            }
            if (Equals(last, "endpoint-health"))
            {
                resourceType = ResourceEndpoint;
                operation = "read";
                return true;
            }

            resourceType = ResolveResourceType(segments);
            if (resourceType.Length == 0) return false;

            operation = ResolveOperation(upper, last);
            return true;
        }

        #endregion

        #region Private-Methods

        private static string ResolveResourceType(string[] segments)
        {
            // Walk the path and keep the deepest recognized resource collection.
            string resolved = string.Empty;
            foreach (string segment in segments)
            {
                switch (segment.ToLowerInvariant())
                {
                    case "scopes": resolved = ResourceScope; break;
                    case "memories": resolved = ResourceMemory; break;
                    case "categories": resolved = ResourceCategory; break;
                    case "instructions": resolved = ResourceInstruction; break;
                    case "endpoints": resolved = ResourceEndpoint; break;
                    case "collections": resolved = ResourceCollection; break;
                    case "users": resolved = ResourceUser; break;
                    case "credentials": resolved = ResourceCredential; break;
                    case "settings": resolved = ResourceSettings; break;
                    case "tenants": resolved = ResourceTenant; break;
                    default: break;
                }
            }

            return resolved;
        }

        private static string ResolveOperation(string upperMethod, string lastSegment)
        {
            if (Equals(lastSegment, "batch-get")) return "read";
            if (Equals(lastSegment, "batch-delete")) return "delete";
            if (Equals(lastSegment, "batch")) return "create";
            if (Equals(lastSegment, "restart")) return "update";

            switch (upperMethod)
            {
                case "GET": return "read";
                case "POST": return "create";
                case "PUT": return "update";
                case "PATCH": return "update";
                case "DELETE": return "delete";
                default: return upperMethod.ToLowerInvariant();
            }
        }

        private static bool Equals(string value, string other)
        {
            return string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
