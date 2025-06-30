using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.UnitTests.Helpers;
internal static class AssertHelpers
{
    /// <summary>
    /// Executes an assertion action repeatedly until it succeeds or a timeout is reached.
    /// This is useful for flaky tests involving asynchronous operations.
    /// </summary>
    /// <param name="assertion">The assertion action to execute.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <param name="pollInterval">The time to wait between attempts.</param>
    public static async Task AssertWithRetryAsync(Action assertion, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        pollInterval ??= TimeSpan.FromMilliseconds(100);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Exception? lastException = null;

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                assertion();
                return; // Success
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(pollInterval.Value);
        }

        // If we exit the loop, it timed out. Throw the last captured exception.
        throw new TimeoutException($"Assertion failed to succeed within the {timeout.Value.TotalSeconds}s timeout.", lastException);
    }
}
