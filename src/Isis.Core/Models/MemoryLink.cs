namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Helpers;

    /// <summary>
    /// A typed directed edge between two memories within a scope.
    /// </summary>
    public class MemoryLink
    {
        #region Public-Members

        /// <summary>
        /// Link identifier. Defaults to a generated value; may not be set to null or empty.
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
        /// Source memory identifier. May not be set to null or empty.
        /// </summary>
        public string FromMemoryId
        {
            get
            {
                return _FromMemoryId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(FromMemoryId));
                _FromMemoryId = value;
            }
        }

        /// <summary>
        /// Target memory slug (edges are asserted by slug and may precede the target's creation).
        /// May not be set to null or empty.
        /// </summary>
        public string ToSlug
        {
            get
            {
                return _ToSlug;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ToSlug));
                _ToSlug = value;
            }
        }

        /// <summary>
        /// Optional relationship type (for example "relates-to", "supersedes", "depends-on").
        /// </summary>
        public string? Relationship { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the link was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Link();
        private string _TenantId = String.Empty;
        private string _ScopeId = String.Empty;
        private string _FromMemoryId = String.Empty;
        private string _ToSlug = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a memory link.
        /// </summary>
        public MemoryLink()
        {
        }

        #endregion
    }
}
