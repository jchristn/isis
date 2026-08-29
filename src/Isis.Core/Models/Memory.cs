namespace Isis.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;

    /// <summary>
    /// One atomic memory. The body and embedding live in the scope's memory store; this record is the
    /// Isis-owned index row carrying identity, structure, and provenance.
    /// </summary>
    public class Memory
    {
        #region Public-Members

        /// <summary>
        /// Memory identifier. Defaults to a generated value; may not be set to null or empty.
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
        /// Owning scope identifier. May not be set to null or empty.
        /// </summary>
        public string ScopeId
        {
            get
            {
                return _ScopeId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ScopeId));
                _ScopeId = value;
            }
        }

        /// <summary>
        /// Owning category identifier. May not be set to null or empty.
        /// </summary>
        public string CategoryId
        {
            get
            {
                return _CategoryId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(CategoryId));
                _CategoryId = value;
            }
        }

        /// <summary>
        /// Stable, link-addressable slug, unique within a (scope, category). May not be set to null or empty.
        /// </summary>
        public string Slug
        {
            get
            {
                return _Slug;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Slug));
                _Slug = value;
            }
        }

        /// <summary>
        /// The join key into the scope's memory store (for RecallDB, the document key).
        /// </summary>
        public string? StoreKey { get; set; } = null;

        /// <summary>
        /// Human-readable title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Classification of the memory.
        /// </summary>
        public MemoryTypeEnum Type { get; set; } = MemoryTypeEnum.Project;

        /// <summary>
        /// A one-line recall hook, cheap to return in listings and search results.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Optional URI reference to the underlying resource this memory describes (for example a
        /// console link, document URL, or repository path). Maps to the OKF <c>resource</c> field.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// The full memory body. Stored as the memory store document content.
        /// </summary>
        public string Body
        {
            get
            {
                return _Body;
            }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(Body));
                _Body = value;
            }
        }

        /// <summary>
        /// Free-form tags.
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Slugs of related memories, forming the link graph.
        /// </summary>
        public List<string> Links { get; set; } = new List<string>();

        /// <summary>
        /// Extensible structured metadata (for example referenced files, confidence).
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Ranking signal in the range 0.0 to 1.0, bumped when a memory is read. Default 0.5.
        /// </summary>
        public double Salience
        {
            get
            {
                return _Salience;
            }
            set
            {
                if (value < 0.0) value = 0.0;
                if (value > 1.0) value = 1.0;
                _Salience = value;
            }
        }

        /// <summary>
        /// Who authored the memory (agent or human).
        /// </summary>
        public string? Author { get; set; } = null;

        /// <summary>
        /// The session that authored the memory, if applicable.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// The model that authored the memory, if applicable.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Monotonic version counter, incremented on update.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// UTC timestamp when the memory was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the memory was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the memory was last read, if ever.
        /// </summary>
        public DateTime? LastAccessedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Memory();
        private string _TenantId = String.Empty;
        private string _ScopeId = String.Empty;
        private string _CategoryId = String.Empty;
        private string _Slug = String.Empty;
        private string _Body = String.Empty;
        private double _Salience = 0.5;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a memory.
        /// </summary>
        public Memory()
        {
        }

        #endregion
    }
}
