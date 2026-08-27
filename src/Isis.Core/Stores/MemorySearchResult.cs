namespace Isis.Core.Stores
{
    using System.Collections.Generic;
    using Isis.Core.Enums;

    /// <summary>
    /// The result of a memory store search.
    /// </summary>
    public class MemorySearchResult
    {
        #region Public-Members

        /// <summary>
        /// The ranked hits.
        /// </summary>
        public List<MemorySearchHit> Hits { get; set; } = new List<MemorySearchHit>();

        /// <summary>
        /// The retrieval strategy actually used, which may differ from the requested mode when the
        /// provider cannot honor it.
        /// </summary>
        public SearchModeEnum EffectiveMode { get; set; } = SearchModeEnum.Keyword;

        /// <summary>
        /// An optional notice explaining any degradation (for example, semantic requested but not supported).
        /// </summary>
        public string? Notice { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a memory search result.
        /// </summary>
        public MemorySearchResult()
        {
        }

        #endregion
    }
}
