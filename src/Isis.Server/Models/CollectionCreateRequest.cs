namespace Isis.Server.Models
{
    /// <summary>
    /// A request to create a RecallDB collection via the pass-through.
    /// </summary>
    public class CollectionCreateRequest
    {
        #region Public-Members

        /// <summary>
        /// The collection name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The vector dimensionality.
        /// </summary>
        public int Dimensionality { get; set; } = 0;

        /// <summary>
        /// An optional description.
        /// </summary>
        public string? Description { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a collection create request.
        /// </summary>
        public CollectionCreateRequest()
        {
        }

        #endregion
    }
}
