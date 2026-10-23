namespace PollyAssay.Adapter;

// Test-only controlled time in the shell. No wall-clock sleeping is required.
public sealed class ControlledTime : TimeProvider
{
    private readonly Lock gate = new();
    private readonly List<ControlledTimer> timers = [];
    private long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (gate) { return ticks; } }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ControlledTimer(this, callback, state);
        lock (gate) { timers.Add(timer); timer.Change(dueTime, period); }
        return timer;
    }
    public bool AdvanceNext()
    {
        long next;
        lock (gate) { next = timers.Where(t => t.Due != long.MaxValue).Select(t => t.Due).DefaultIfEmpty(long.MaxValue).Min(); }
        if (next == long.MaxValue) { return false; }
        Advance(TimeSpan.FromTicks(Math.Max(0, next - GetTimestamp())));
        return true;
    }
    public void Advance(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);
        var target = checked(GetTimestamp() + interval.Ticks);
        while (true)
        {
            ControlledTimer? timer;
            lock (gate)
            {
                timer = timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                if (timer is null) { ticks = target; return; }
                ticks = timer.Due;
                timer.Due = timer.Period > 0 ? checked(ticks + timer.Period) : long.MaxValue;
            }
            timer.Fire();
        }
    }
    private sealed class ControlledTimer(ControlledTime owner, TimerCallback callback, object? state) : ITimer
    {
        public long Due { get; set; } = long.MaxValue;
        public long Period { get; private set; }
        private bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                if (disposed) { return false; }
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(owner.ticks + dueTime.Ticks);
                Period = period.Ticks;
                return true;
            }
        }
        public void Fire() { if (!disposed) { callback(state); } }
        public void Dispose() { lock (owner.gate) { disposed = true; Due = long.MaxValue; owner.timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
