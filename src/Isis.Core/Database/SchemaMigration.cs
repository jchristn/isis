namespace Isis.Core.Database
{
    using System;
    using Isis.Core.Helpers;

    /// <summary>
    /// A record that a named schema migration has been applied to the database.
    /// </summary>
    public class SchemaMigration
    {
        #region Public-Members

        /// <summary>
        /// Migration record identifier.
        /// </summary>
        public string Id { get; set; } = IdGenerator.Token();

        /// <summary>
        /// The unique migration name.
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
        /// UTC timestamp when the migration was applied.
        /// </summary>
        public DateTime AppliedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the migration completed successfully.
        /// </summary>
        public bool Success { get; set; } = true;

        #endregion

        #region Private-Members

        private string _Name = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a schema migration record.
        /// </summary>
        public SchemaMigration()
        {
        }

        #endregion
    }
}
