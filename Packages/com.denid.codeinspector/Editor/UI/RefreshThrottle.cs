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
        long dueAt = -1;

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
            dueAt = d < cap ? d : cap;
            return Math.Max(0, dueAt - nowMs);
        }

        public bool IsDue(long nowMs)
        {
            return dueAt >= 0 && nowMs >= dueAt;
        }

        public bool Pending => dueAt >= 0;

        public void Reset()
        {
            firstEventAt = -1;
            dueAt = -1;
        }
    }
}
