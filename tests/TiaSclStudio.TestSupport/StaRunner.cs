using System;
using System.Threading;

namespace TiaSclStudio.TestSupport
{
    /// <summary>
    /// Runs a delegate on a dedicated STA thread. WPF types refuse to be created
    /// on the MTA threads that the test runner uses, so the end-to-end suite
    /// needs an explicit apartment. Exceptions are rethrown on the caller.
    /// </summary>
    public static class StaRunner
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

        public static void Run(Action action, TimeSpan? timeout = null)
        {
            Run<object>(
                () =>
                {
                    action();
                    return null;
                },
                timeout);
        }

        public static T Run<T>(Func<T> action, TimeSpan? timeout = null)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            var effectiveTimeout = timeout ?? DefaultTimeout;
            var result = default(T);
            Exception failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            if (!thread.Join(effectiveTimeout))
            {
                throw new TimeoutException(
                    "The STA action did not finish within " + effectiveTimeout + ".");
            }

            if (failure != null)
            {
                throw new StaInvocationException(failure);
            }

            return result;
        }
    }

    /// <summary>
    /// Preserves the original stack trace of a failure raised on the STA thread
    /// instead of losing it to a rethrow on the calling thread.
    /// </summary>
    public sealed class StaInvocationException : Exception
    {
        public StaInvocationException(Exception inner)
            : base("The action running on the STA thread failed: " + inner.Message, inner)
        {
        }
    }
}
