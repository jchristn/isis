namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Helpers;

    /// <summary>
    /// A named bucket of memories with a description and usage instructions. The instructions are the
    /// contract an agent reads to know when to write here and what a good entry looks like.
    /// </summary>
    public class Category
    {
        #region Public-Members

        /// <summary>
        /// Category identifier. Defaults to a generated value; may not be set to null or empty.
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
        /// Short category name (also used as the RecallDB label). May not be set to null or empty.
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
        /// A one-line description of what this category holds.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Usage instructions telling the agent when and how to write memories in this category.
        /// </summary>
        public string? Instructions { get; set; } = null;

        /// <summary>
        /// Indicates whether the category is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// UTC timestamp when the category was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the category was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Category();
        private string _TenantId = String.Empty;
        private string _ScopeId = String.Empty;
        private string _Name = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a category.
        /// </summary>
        public Category()
        {
        }

        #endregion
    }
}
