namespace Isis.Core.Stores
{
    /// <summary>
    /// Connection options for the external memory stores (RecallDB, Verbex), supplied when constructing a
    /// store for a scope.
    /// </summary>
    public class StoreOptions
    {
        #region Public-Members

        /// <summary>
        /// The RecallDB server endpoint (for example http://127.0.0.1:8600).
        /// </summary>
        public string? RecallDbEndpoint { get; set; } = null;

        /// <summary>
        /// The RecallDB admin API key used server-side.
        /// </summary>
        public string? RecallDbAdminKey { get; set; } = null;

        /// <summary>
        /// The Verbex server endpoint.
        /// </summary>
        public string? VerbexEndpoint { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate store options.
        /// </summary>
        public StoreOptions()
        {
        }

        #endregion
    }
}
