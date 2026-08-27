namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Helpers;

    /// <summary>
    /// A top-level isolation boundary. Every tenant-owned record references a tenant.
    /// </summary>
    public class Tenant
    {
        #region Public-Members

        /// <summary>
        /// Tenant identifier. Defaults to a generated value; may not be set to null or empty.
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
        /// Human-readable tenant name. May not be set to null or empty.
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
        /// Indicates whether the tenant is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Indicates whether the tenant is protected from deletion.
        /// </summary>
        public bool Protected { get; set; } = false;

        /// <summary>
        /// UTC timestamp when the tenant was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the tenant was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Tenant();
        private string _Name = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a tenant.
        /// </summary>
        public Tenant()
        {
        }

        #endregion
    }
}
