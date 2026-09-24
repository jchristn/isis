namespace Isis.Core.Stores.RecallDb
{
    using global::RecallDb.Sdk.Models;

    /// <summary>
    /// A chunk document after hybrid fusion, with its fused score and the per-leg evidence behind it.
    /// </summary>
    public class FusedDocument
    {
        #region Public-Members

        /// <summary>
        /// The chunk document (as returned by whichever leg surfaced it first).
        /// </summary>
        public DocumentRecord Document { get; set; }

        /// <summary>
        /// Fused score normalized to [0, 1]: 1.0 means ranked first in every contributing signal.
        /// </summary>
        public double Score { get; set; } = 0.0;

        /// <summary>
        /// Similarity score from the vector leg, or null when the vector leg did not return this document.
        /// </summary>
        public double? VectorScore { get; set; } = null;

        /// <summary>
        /// Relevance score from the full-text leg, or null when the text leg did not return this document.
        /// </summary>
        public double? TextScore { get; set; } = null;

        /// <summary>
        /// 1-based rank in the vector leg, or null when absent.
        /// </summary>
        public int? VectorRank { get; set; } = null;

        /// <summary>
        /// 1-based rank in the text leg, or null when absent.
        /// </summary>
        public int? TextRank { get; set; } = null;

        /// <summary>
        /// 1-based recency rank of the document's memory among the candidates (1 = most recently written), or null
        /// when recency was not applied.
        /// </summary>
        public int? RecencyRank { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="document">The chunk document.</param>
        public FusedDocument(DocumentRecord document)
        {
            Document = document;
        }

        #endregion
    }
}
