using System;
using System.Collections.Generic;
using System.Threading;
using Typedown.WinUI.Services;
using Xunit;

namespace Caret.ConverterTests
{
    public class ReviewStoreQueueTests
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        [Fact]
        public void Work_runs_in_the_order_it_was_asked_for_even_when_an_early_one_is_slow()
        {
            var queue = new ReviewStoreQueue();
            var order = new List<int>();
            queue.Post(() => { Thread.Sleep(150); lock (order) order.Add(1); });
            queue.Post(() => { lock (order) order.Add(2); });
            queue.Post(() => { lock (order) order.Add(3); });
            Assert.True(queue.Drain(Wait));
            Assert.Equal(new[] { 1, 2, 3 }, order);
        }

        [Fact]
        public void A_remove_asked_for_after_a_save_is_not_undone_by_it()
        {
            var queue = new ReviewStoreQueue();
            var present = false;
            queue.Post(() => { Thread.Sleep(100); present = true; });
            queue.Post(() => present = false);
            queue.Drain(Wait);
            Assert.False(present);
        }

        [Fact]
        public void A_read_sees_the_writes_asked_for_before_it()
        {
            var queue = new ReviewStoreQueue();
            var value = "old";
            queue.Post(() => { Thread.Sleep(100); value = "new"; });
            Assert.Equal("new", queue.Get(() => value, "fallback", Wait));
        }

        [Fact]
        public void A_failing_job_is_reported_and_the_ones_after_it_still_run()
        {
            var errors = new List<string>();
            var queue = new ReviewStoreQueue(ex => errors.Add(ex.Message));
            var ran = false;
            queue.Post(() => throw new InvalidOperationException("boom"));
            queue.Post(() => ran = true);
            Assert.Equal("fallback", queue.Get<string>(() => throw new InvalidOperationException("again"), "fallback", Wait));
            queue.Drain(Wait);
            Assert.True(ran);
            Assert.Equal(new[] { "boom", "again" }, errors);
        }

        [Fact]
        public void A_read_that_takes_too_long_gives_the_fallback_and_does_not_hold_the_caller()
        {
            var queue = new ReviewStoreQueue();
            queue.Post(() => Thread.Sleep(600));
            Assert.Equal("fallback", queue.Get(() => "value", "fallback", TimeSpan.FromMilliseconds(50)));
            queue.Drain(Wait);
        }

        [Fact]
        public void Close_waits_for_what_is_queued_and_refuses_what_comes_after()
        {
            var queue = new ReviewStoreQueue();
            var done = false;
            queue.Post(() => { Thread.Sleep(150); done = true; });
            Assert.True(queue.Close(Wait));
            Assert.True(done);
            var late = false;
            Assert.False(queue.Post(() => late = true));
            Assert.Equal("fallback", queue.Get(() => "value", "fallback", Wait));
            Thread.Sleep(50);
            Assert.False(late);
        }
    }
}