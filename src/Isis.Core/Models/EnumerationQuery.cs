namespace Isis.Core.Models
{
    using System;

    /// <summary>
    /// Query parameters for paginated enumeration of records.
    /// </summary>
    public class EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of records to return. Minimum 1, maximum 1000, default 100.
        /// </summary>
        public int MaxResults
        {
            get
            {
                return _MaxResults;
            }
            set
            {
                if (value < 1) value = 1;
                if (value > 1000) value = 1000;
                _MaxResults = value;
            }
        }

        /// <summary>
        /// Number of records to skip from the start of the ordered result set. Minimum 0.
        /// </summary>
        public int Skip
        {
            get
            {
                return _Skip;
            }
            set
            {
                if (value < 0) value = 0;
                _Skip = value;
            }
        }

        /// <summary>
        /// Optional case-insensitive search term applied to the primary text column(s).
        /// </summary>
        public string? SearchTerm { get; set; } = null;

        /// <summary>
        /// Optional continuation token from a prior enumeration.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        #endregion

        #region Private-Members

        private int _MaxResults = 100;
        private int _Skip = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an enumeration query.
        /// </summary>
        public EnumerationQuery()
        {
        }

        #endregion
    }
}
