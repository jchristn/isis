namespace Isis.Core.Helpers
{
    using Isis.Core;
    using Isis.Core.Enums;

    /// <summary>
    /// Generates prefixed, K-sortable application identifiers and opaque secrets using PrettyId.
    /// </summary>
    public static class IdGenerator
    {
        #region Private-Members

        private static readonly PrettyId.IdGenerator _Generator = new PrettyId.IdGenerator();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a tenant identifier.
        /// </summary>
        /// <returns>Tenant identifier.</returns>
        public static string Tenant()
        {
            return _Generator.GenerateKSortable(Constants.TenantPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a user identifier.
        /// </summary>
        /// <returns>User identifier.</returns>
        public static string User()
        {
            return _Generator.GenerateKSortable(Constants.UserPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a credential identifier.
        /// </summary>
        /// <returns>Credential identifier.</returns>
        public static string Credential()
        {
            return _Generator.GenerateKSortable(Constants.CredentialPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate an authentication session identifier.
        /// </summary>
        /// <returns>Session identifier.</returns>
        public static string Session()
        {
            return _Generator.GenerateKSortable(Constants.SessionPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a role identifier.
        /// </summary>
        /// <returns>Role identifier.</returns>
        public static string Role()
        {
            return _Generator.GenerateKSortable(Constants.RolePrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a permission identifier.
        /// </summary>
        /// <returns>Permission identifier.</returns>
        public static string Permission()
        {
            return _Generator.GenerateKSortable(Constants.PermissionPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a scope identifier.
        /// </summary>
        /// <returns>Scope identifier.</returns>
        public static string Scope()
        {
            return _Generator.GenerateKSortable(Constants.ScopePrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a category identifier.
        /// </summary>
        /// <returns>Category identifier.</returns>
        public static string Category()
        {
            return _Generator.GenerateKSortable(Constants.CategoryPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a memory identifier.
        /// </summary>
        /// <returns>Memory identifier.</returns>
        public static string Memory()
        {
            return _Generator.GenerateKSortable(Constants.MemoryPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a memory link identifier.
        /// </summary>
        /// <returns>Link identifier.</returns>
        public static string Link()
        {
            return _Generator.GenerateKSortable(Constants.LinkPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a policy identifier.
        /// </summary>
        /// <returns>Policy identifier.</returns>
        public static string Policy()
        {
            return _Generator.GenerateKSortable(Constants.PolicyPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate an instruction identifier.
        /// </summary>
        /// <returns>Instruction identifier.</returns>
        public static string Instruction()
        {
            return _Generator.GenerateKSortable(Constants.InstructionPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a seed pack identifier.
        /// </summary>
        /// <returns>Seed pack identifier.</returns>
        public static string SeedPack()
        {
            return _Generator.GenerateKSortable(Constants.SeedPackPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate an embedding endpoint identifier.
        /// </summary>
        /// <returns>Embedding endpoint identifier.</returns>
        public static string EmbeddingEndpoint()
        {
            return _Generator.GenerateKSortable(Constants.EmbeddingEndpointPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate an inference endpoint identifier.
        /// </summary>
        /// <returns>Inference endpoint identifier.</returns>
        public static string InferenceEndpoint()
        {
            return _Generator.GenerateKSortable(Constants.InferenceEndpointPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a model endpoint identifier appropriate for the given kind.
        /// </summary>
        /// <param name="kind">The endpoint kind.</param>
        /// <returns>An endpoint identifier.</returns>
        public static string Endpoint(EndpointKindEnum kind)
        {
            return kind == EndpointKindEnum.Inference ? InferenceEndpoint() : EmbeddingEndpoint();
        }

        /// <summary>
        /// Generate a chat session identifier.
        /// </summary>
        /// <returns>Chat session identifier.</returns>
        public static string ChatSession()
        {
            return _Generator.GenerateKSortable(Constants.ChatSessionPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a chat message identifier.
        /// </summary>
        /// <returns>Chat message identifier.</returns>
        public static string ChatMessage()
        {
            return _Generator.GenerateKSortable(Constants.ChatMessagePrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a request history identifier.
        /// </summary>
        /// <returns>Request history identifier.</returns>
        public static string Request()
        {
            return _Generator.GenerateKSortable(Constants.RequestPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate an opaque secret such as a bearer token or credential secret key.
        /// </summary>
        /// <returns>Opaque token string.</returns>
        public static string Token()
        {
            return _Generator.Generate(Constants.TokenLength);
        }

        #endregion
    }
}
