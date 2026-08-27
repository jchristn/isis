namespace Isis.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of a paginated enumeration.
    /// </summary>
    /// <typeparam name="T">The type of record enumerated.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>
        /// The maximum number of records requested.
        /// </summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>
        /// The number of records skipped.
        /// </summary>
        public int Skip { get; set; } = 0;

        /// <summary>
        /// The total number of records matching the query, ignoring pagination.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// The number of records remaining after this page.
        /// </summary>
        public long RecordsRemaining { get; set; } = 0;

        /// <summary>
        /// Indicates whether the end of the result set has been reached.
        /// </summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>
        /// A continuation token for retrieving the next page, when more records remain.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>
        /// The records returned for this page.
        /// </summary>
        public List<T> Objects { get; set; } = new List<T>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an enumeration result.
        /// </summary>
        public EnumerationResult()
        {
        }

        #endregion
    }
}
