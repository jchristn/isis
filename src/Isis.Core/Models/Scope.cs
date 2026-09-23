namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;

    /// <summary>
    /// A named memory space within a tenant (a project, a book, or "global"). Maps to one memory store
    /// backend and, for RecallDB, one collection with a fixed embedding dimension.
    /// </summary>
    public class Scope
    {
        #region Public-Members

        /// <summary>
        /// Scope identifier. Defaults to a generated value; may not be set to null or empty.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Owning tenant identifier. May not be set to null or empty.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId));
                _TenantId = value;
            }
        }

        /// <summary>
        /// Human-readable scope name. Unique within a tenant. May not be set to null or empty.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Optional description of what this scope holds.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// The memory store backend for this scope. Determines available retrieval capabilities.
        /// </summary>
        public StoreProviderEnum StoreProvider { get; set; } = StoreProviderEnum.RecallDb;

        /// <summary>
        /// For the RecallDB store, the backing collection identifier.
        /// </summary>
        public string? RecallCollectionId { get; set; } = null;

        /// <summary>
        /// The embedding vector dimensionality fixed for this scope (RecallDB). Zero when not applicable.
        /// Changing this after creation requires a new collection and re-embedding.
        /// </summary>
        public int Dimensionality
        {
            get
            {
                return _Dimensionality;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(Dimensionality), "Dimensionality may not be negative.");
                _Dimensionality = value;
            }
        }

        /// <summary>
        /// Identifier of the embedding endpoint used for this scope (RecallDB), if any.
        /// </summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>
        /// For the filesystem store, the layout mode.
        /// </summary>
        public FilesystemLayoutEnum FilesystemLayout { get; set; } = FilesystemLayoutEnum.Hierarchy;

        /// <summary>
        /// For the filesystem store, the target path where memory files are written.
        /// </summary>
        public string? TargetPath { get; set; } = null;

        /// <summary>
        /// When memory bodies in this scope are chunked for embedding. Default OnOverflow (only when a body
        /// exceeds the embedding model's token budget).
        /// </summary>
        public ChunkingModeEnum ChunkingMode { get; set; } = ChunkingModeEnum.OnOverflow;

        /// <summary>
        /// The chunking strategy name (maps to the chunking library's strategy, e.g. FixedTokenCount, Recursive,
        /// SentenceBased, ParagraphBased). Default FixedTokenCount, which packs each chunk to the token budget
        /// with the configured overlap — the predictable choice for embedding regardless of body structure.
        /// </summary>
        public string ChunkStrategy { get; set; } = "FixedTokenCount";

        /// <summary>
        /// The per-chunk token budget. Zero means use the embedding endpoint's resolved input budget.
        /// </summary>
        public int ChunkMaxTokens
        {
            get
            {
                return _ChunkMaxTokens;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(ChunkMaxTokens), "ChunkMaxTokens may not be negative.");
                _ChunkMaxTokens = value;
            }
        }

        /// <summary>
        /// The number of tokens of overlap between adjacent chunks. Default 64.
        /// </summary>
        public int ChunkOverlapTokens
        {
            get
            {
                return _ChunkOverlapTokens;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(ChunkOverlapTokens), "ChunkOverlapTokens may not be negative.");
                _ChunkOverlapTokens = value;
            }
        }

        /// <summary>
        /// Indicates whether the scope is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// UTC timestamp when the scope was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the scope was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Scope();
        private string _TenantId = String.Empty;
        private string _Name = String.Empty;
        private int _Dimensionality = 0;
        private int _ChunkMaxTokens = 0;
        private int _ChunkOverlapTokens = 64;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a scope.
        /// </summary>
        public Scope()
        {
        }

        #endregion
    }
}
