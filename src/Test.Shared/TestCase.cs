namespace Test.Shared
{
    using System;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Factory helpers for constructing Touchstone test cases uniformly across the Isis test suites.
    /// </summary>
    public static class TestCase
    {
        #region Public-Methods

        /// <summary>
        /// Create an asynchronous test case.
        /// </summary>
        /// <param name="suiteId">The owning suite identifier.</param>
        /// <param name="caseId">The case identifier.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="executeAsync">The asynchronous test body.</param>
        /// <returns>A test case descriptor.</returns>
        public static TestCaseDescriptor Async(string suiteId, string caseId, string displayName, Func<Task> executeAsync)
        {
            return new TestCaseDescriptor(
                suiteId,
                caseId,
                displayName,
                async token =>
                {
                    token.ThrowIfCancellationRequested();
                    await executeAsync().ConfigureAwait(false);
                },
                new[] { suiteId });
        }

        /// <summary>
        /// Create a synchronous test case.
        /// </summary>
        /// <param name="suiteId">The owning suite identifier.</param>
        /// <param name="caseId">The case identifier.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="execute">The synchronous test body.</param>
        /// <returns>A test case descriptor.</returns>
        public static TestCaseDescriptor Sync(string suiteId, string caseId, string displayName, Action execute)
        {
            return new TestCaseDescriptor(
                suiteId,
                caseId,
                displayName,
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    execute();
                    return Task.CompletedTask;
                },
                new[] { suiteId });
        }

        /// <summary>
        /// Create an asynchronous test case that may be skipped.
        /// </summary>
        /// <param name="suiteId">The owning suite identifier.</param>
        /// <param name="caseId">The case identifier.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="executeAsync">The asynchronous test body.</param>
        /// <param name="skip">Whether to skip the case.</param>
        /// <param name="skipReason">The reason for skipping.</param>
        /// <returns>A test case descriptor.</returns>
        public static TestCaseDescriptor Skippable(string suiteId, string caseId, string displayName, Func<Task> executeAsync, bool skip, string skipReason)
        {
            return new TestCaseDescriptor(
                suiteId,
                caseId,
                displayName,
                async token =>
                {
                    token.ThrowIfCancellationRequested();
                    await executeAsync().ConfigureAwait(false);
                },
                new[] { suiteId })
            {
                Skip = skip,
                SkipReason = skip ? skipReason : null
            };
        }

        /// <summary>
        /// Assert that a condition holds, throwing a descriptive exception otherwise.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="message">The failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Assert that an asynchronous action throws an exception of the given type.
        /// </summary>
        /// <typeparam name="T">The expected exception type.</typeparam>
        /// <param name="action">The action.</param>
        /// <param name="message">The failure message when no matching exception is thrown.</param>
        /// <returns>Awaitable task.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the expected exception is not thrown.</exception>
        public static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (T)
            {
                return;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(message + " (threw " + e.GetType().Name + " instead of " + typeof(T).Name + ")");
            }

            throw new InvalidOperationException(message + " (no exception was thrown; expected " + typeof(T).Name + ")");
        }

        /// <summary>
        /// Assert that a synchronous action throws an exception of the given type.
        /// </summary>
        /// <typeparam name="T">The expected exception type.</typeparam>
        /// <param name="action">The action.</param>
        /// <param name="message">The failure message when no matching exception is thrown.</param>
        /// <exception cref="InvalidOperationException">Thrown when the expected exception is not thrown.</exception>
        public static void Throws<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(message + " (threw " + e.GetType().Name + " instead of " + typeof(T).Name + ")");
            }

            throw new InvalidOperationException(message + " (no exception was thrown; expected " + typeof(T).Name + ")");
        }

        #endregion
    }
}
