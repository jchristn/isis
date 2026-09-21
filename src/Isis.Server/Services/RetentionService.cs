namespace Isis.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Server.Settings;

    /// <summary>
    /// Background pruner that periodically deletes aged rows from the observability history tables
    /// (request history and operation events), keeping both bounded by a single retention window. Started
    /// and stopped by <see cref="IsisServer"/>; runs a timer loop until stopped.
    /// </summary>
    public class RetentionService : IDisposable
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly RetentionSettings _Settings;
        private readonly Action<string>? _Log;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private Task? _Loop;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the retention service.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="settings">The retention settings.</param>
        /// <param name="log">Optional log callback.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public RetentionService(DatabaseDriverBase database, RetentionSettings settings, Action<string>? log = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Log = log;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the background sweep loop. No-op when retention is disabled or already started.
        /// </summary>
        public void Start()
        {
            if (!_Settings.Enabled || _Loop != null) return;
            _Loop = Task.Run(() => RunAsync(_Cts.Token));
        }

        /// <summary>
        /// Signal the loop to stop. Non-blocking.
        /// </summary>
        public void Stop()
        {
            if (!_Cts.IsCancellationRequested) _Cts.Cancel();
        }

        /// <summary>
        /// Run a single retention sweep across both history tables and return the total rows deleted.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The total number of rows deleted.</returns>
        public async Task<long> SweepOnceAsync(CancellationToken token = default)
        {
            int days = _Settings.MaxAgeDays < 1 ? 1 : _Settings.MaxAgeDays;
            DateTime cutoff = DateTime.UtcNow.AddDays(-days);

            long requests = await _Database.RequestHistory.DeleteOlderThanAsync(cutoff, token).ConfigureAwait(false);
            long operations = await _Database.OperationEvents.DeleteOlderThanAsync(cutoff, token).ConfigureAwait(false);
            return requests + operations;
        }

        /// <summary>
        /// Dispose the retention service, stopping the loop.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            Stop();
            _Cts.Dispose();
            _Disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            int minutes = _Settings.SweepIntervalMinutes < 1 ? 1 : _Settings.SweepIntervalMinutes;
            TimeSpan interval = TimeSpan.FromMinutes(minutes);

            // Sweep once at startup, then on the configured interval.
            while (!token.IsCancellationRequested)
            {
                try
                {
                    long deleted = await SweepOnceAsync(token).ConfigureAwait(false);
                    if (deleted > 0 && _Log != null) _Log("Retention pruned " + deleted + " observability row(s).");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Best-effort maintenance; never crash the loop on a transient failure.
                    if (_Log != null) _Log("Retention sweep failed: " + ex.Message);
                }

                try
                {
                    await Task.Delay(interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        #endregion
    }
}
