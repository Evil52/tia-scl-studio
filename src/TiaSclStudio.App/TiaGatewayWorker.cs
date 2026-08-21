using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TiaSclStudio.App
{
    /// <summary>
    /// Serializes every call that can touch Siemens.Engineering objects on one
    /// long-lived MTA thread. Openness objects are not inherently thread-safe,
    /// and a pool-backed Task.Run does not guarantee thread affinity.
    /// </summary>
    internal sealed class TiaGatewayWorker : IDisposable
    {
        private readonly object syncRoot = new object();
        private readonly BlockingCollection<Action> workItems = new BlockingCollection<Action>();
        private readonly Thread workerThread;
        private bool disposed;
        private int workerThreadId;

        public TiaGatewayWorker()
        {
            workerThread = new Thread(Run)
            {
                IsBackground = true,
                Name = "TIA Openness MTA worker"
            };
            workerThread.SetApartmentState(ApartmentState.MTA);
            workerThread.Start();
        }

        public Task<T> InvokeAsync<T>(Func<T> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() =>
            {
                try
                {
                    completion.SetResult(operation());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            return completion.Task;
        }

        public void Invoke(Action operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (Thread.CurrentThread.ManagedThreadId == workerThreadId)
            {
                operation();
                return;
            }

            InvokeAsync(() =>
            {
                operation();
                return true;
            }).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                workItems.CompleteAdding();
            }

            if (Thread.CurrentThread.ManagedThreadId != workerThreadId)
            {
                workerThread.Join();
            }

            workItems.Dispose();
        }

        private void Enqueue(Action workItem)
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(TiaGatewayWorker));
                }

                workItems.Add(workItem);
            }
        }

        private void Run()
        {
            workerThreadId = Thread.CurrentThread.ManagedThreadId;
            foreach (var workItem in workItems.GetConsumingEnumerable())
            {
                workItem();
            }
        }
    }
}
