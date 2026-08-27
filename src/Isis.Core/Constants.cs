namespace Isis.Core
{
    /// <summary>
    /// Application-wide constants for Isis.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Product name.
        /// </summary>
        public static readonly string ProductName = "Isis";

        /// <summary>
        /// Product version.
        /// </summary>
        public static readonly string ProductVersion = "0.1.0";

        /// <summary>
        /// Default length used when generating PrettyId K-sortable identifiers (excluding the prefix).
        /// </summary>
        public static readonly int IdLength = 32;

        /// <summary>
        /// Default length used when generating opaque bearer tokens and secret keys.
        /// </summary>
        public static readonly int TokenLength = 64;

        /// <summary>
        /// Tenant identifier prefix.
        /// </summary>
        public static readonly string TenantPrefix = "ten_";

        /// <summary>
        /// User identifier prefix.
        /// </summary>
        public static readonly string UserPrefix = "usr_";

        /// <summary>
        /// Credential identifier prefix.
        /// </summary>
        public static readonly string CredentialPrefix = "crd_";

        /// <summary>
        /// Authentication session identifier prefix.
        /// </summary>
        public static readonly string SessionPrefix = "ses_";

        /// <summary>
        /// Role identifier prefix.
        /// </summary>
        public static readonly string RolePrefix = "rol_";

        /// <summary>
        /// Permission identifier prefix.
        /// </summary>
        public static readonly string PermissionPrefix = "perm_";

        /// <summary>
        /// Scope identifier prefix.
        /// </summary>
        public static readonly string ScopePrefix = "scp_";

        /// <summary>
        /// Category identifier prefix.
        /// </summary>
        public static readonly string CategoryPrefix = "cat_";

        /// <summary>
        /// Memory identifier prefix.
        /// </summary>
        public static readonly string MemoryPrefix = "mem_";

        /// <summary>
        /// Memory link identifier prefix.
        /// </summary>
        public static readonly string LinkPrefix = "lnk_";

        /// <summary>
        /// Policy identifier prefix.
        /// </summary>
        public static readonly string PolicyPrefix = "pol_";

        /// <summary>
        /// Seed pack identifier prefix.
        /// </summary>
        public static readonly string SeedPackPrefix = "seed_";

        /// <summary>
        /// Embedding endpoint identifier prefix.
        /// </summary>
        public static readonly string EmbeddingEndpointPrefix = "eep_";

        /// <summary>
        /// Inference endpoint identifier prefix.
        /// </summary>
        public static readonly string InferenceEndpointPrefix = "iep_";

        /// <summary>
        /// Chat session identifier prefix.
        /// </summary>
        public static readonly string ChatSessionPrefix = "cht_";

        /// <summary>
        /// Chat message identifier prefix.
        /// </summary>
        public static readonly string ChatMessagePrefix = "cmsg_";

        /// <summary>
        /// Request history entry identifier prefix.
        /// </summary>
        public static readonly string RequestPrefix = "req_";
    }
}
