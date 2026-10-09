using System;
using System.Threading.Tasks;

namespace Typedown.WinUI.Services
{
    // New since the fork: live review. Everything done to the saved reviews (ReviewStore) goes through one queue, in the order it was
    // asked for: a save asked for before a remove cannot run after it and bring a stopped review back, two saves cannot finish in the
    // wrong order and leave the older state, and a read waits for the writes asked for before it. `Close` is for the end of the program:
    // it waits for what is queued and refuses anything after.
    //
    // Plain .NET (no WinUI), so the tests in Caret.ConverterTests compile it as it is.
    internal sealed class ReviewStoreQueue
    {
        private readonly object gate = new();
        private readonly Action<Exception> onError;
        private Task tail = Task.CompletedTask;
        private bool closed;

        public ReviewStoreQueue(Action<Exception> onError = null) => this.onError = onError;

        // Runs `work` after everything queued before it. False when the queue is closed (nothing is run).
        public bool Post(Action work)
        {
            lock (gate)
            {
                if (closed) return false;
                tail = tail.ContinueWith(_ => Run(work), TaskScheduler.Default);
                return true;
            }
        }

        // Runs `work` after everything queued before it and returns what it returned, or `fallback` when it failed, when the queue
        // is closed or when it was not done in `timeout` (the caller is the UI: it does not wait for ever).
        public T Get<T>(Func<T> work, T fallback, TimeSpan timeout)
        {
            Task<T> task;
            lock (gate)
            {
                if (closed) return fallback;
                task = tail.ContinueWith(_ => Run(work, fallback), TaskScheduler.Default);
                tail = task;
            }
            return task.Wait(timeout) ? task.Result : fallback;
        }

        // Waits until what is queued now has been done; false when `timeout` came first.
        public bool Drain(TimeSpan timeout)
        {
            Task now;
            lock (gate) now = tail;
            return now.Wait(timeout);
        }

        // Waits for what is queued and refuses everything after it.
        public bool Close(TimeSpan timeout)
        {
            Task now;
            lock (gate)
            {
                closed = true;
                now = tail;
            }
            return now.Wait(timeout);
        }

        private void Run(Action work)
        {
            try { work(); }
            catch (Exception ex) { onError?.Invoke(ex); }
        }

        private T Run<T>(Func<T> work, T fallback)
        {
            try { return work(); }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                return fallback;
            }
        }
    }
}