using System;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Debounce with a guaranteed max wait: an event schedules a fire at
    /// now+debounce, but a continuous stream of events still fires within
    /// MaxWaitMs of the first pending event.
    /// </summary>
    public sealed class RefreshThrottle
    {
        public long DebounceMs { get; set; }
        public long MaxWaitMs { get; set; }

        long firstEventAt = -1;

        public RefreshThrottle(long debounceMs, long maxWaitMs)
        {
            DebounceMs = debounceMs;
            MaxWaitMs = maxWaitMs;
        }

        /// <summary>Records an event; returns ms until the refresh is due.</summary>
        public long Ping(long nowMs)
        {
            if (firstEventAt < 0)
                firstEventAt = nowMs;
            var d = nowMs + DebounceMs;
            var cap = firstEventAt + MaxWaitMs;
            var dueAt = d < cap ? d : cap;
            return Math.Max(0, dueAt - nowMs);
        }

        public void Reset()
        {
            firstEventAt = -1;
        }
    }
}
