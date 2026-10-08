using RestaurantManagement.Web.Services;

/// <summary>
/// S2-08 Task 4: tác vụ tự đặt lại "Tạm hết" hoạt động liên tục qua mốc 00:00 (Asia/Ho_Chi_Minh) — dùng đồng hồ giả lập,
/// không cần database, không phải chờ tới nửa đêm. Phần đặt lại thật trong database: TemporaryOutResetVerification,
/// TemporaryOutAcceptanceVerification (DbTool).
/// </summary>
internal static class TemporaryOutResetWorkerTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        static DateTimeOffset Utc(int y, int mo, int d, int h, int mi, int s = 0) => new(y, mo, d, h, mi, s, TimeSpan.Zero);
        // 21:00 ngày 06/10/2026 giờ Việt Nam = 14:00 UTC.
        var clock = new ManualClock(Utc(2026, 10, 6, 14, 0));
        var runs = new System.Collections.Concurrent.ConcurrentQueue<DateTime>();
        var worker = TemporaryOutResetWorker.Create(clock, (scheduledUtc, _) => { runs.Enqueue(scheduledUtc); return Task.FromResult(0); });
        await worker.StartAsync(CancellationToken.None);
        try
        {
            async Task<bool> WaitFor(Func<bool> condition)
            {
                for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
                return condition();
            }
            DateTime At(int y, int mo, int d, int h) => new(y, mo, d, h, 0, 0, DateTimeKind.Utc);

            check(await WaitFor(() => runs.Count == 1 && clock.PendingTimers == 1) && runs.Single() == At(2026, 10, 5, 17),
                "S2-08 worker: on start (21:00 Vietnam) it checks today's 00:00 Vietnam (05/10 17:00 UTC) once, then waits");

            clock.AdvanceTo(Utc(2026, 10, 6, 16, 59, 59)); // 23:59:59 giờ Việt Nam
            await Task.Delay(100);
            check(runs.Count == 1, "S2-08 worker: nothing happens before 00:00 Vietnam (23:59:59)");

            clock.AdvanceTo(Utc(2026, 10, 6, 17, 0)); // 00:00 giờ Việt Nam ngày 07/10
            check(await WaitFor(() => runs.Count == 2 && clock.PendingTimers == 1) && runs.Last() == At(2026, 10, 6, 17),
                "S2-08 worker: at 00:00 Vietnam (17:00 UTC) the reset runs for the new day while the system keeps running");

            clock.AdvanceTo(Utc(2026, 10, 7, 12, 0)); // 19:00 giờ Việt Nam ngày 07/10
            await Task.Delay(100);
            check(runs.Count == 2, "S2-08 worker: no extra reset during the day");

            clock.AdvanceTo(Utc(2026, 10, 7, 17, 0));
            check(await WaitFor(() => runs.Count == 3) && runs.Last() == At(2026, 10, 7, 17),
                "S2-08 worker: the reset repeats every day at 00:00 Vietnam");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Lỗi database: thử lại sau 1 phút, không bỏ qua ngày.
        var failing = new ManualClock(Utc(2026, 10, 6, 17, 0, 5));
        var attempts = 0;
        var retrying = TemporaryOutResetWorker.Create(failing, (_, _) => ++attempts == 1 ? throw new InvalidOperationException("database offline") : Task.FromResult(2));
        await retrying.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 0; i < 200 && !(attempts == 1 && failing.PendingTimers == 1); i++) await Task.Delay(10);
            failing.AdvanceTo(Utc(2026, 10, 6, 17, 1, 5));
            for (var i = 0; i < 200 && attempts < 2; i++) await Task.Delay(10);
            check(attempts == 2, "S2-08 worker: when the database is unavailable at 00:00 it retries one minute later");
        }
        finally
        {
            await retrying.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Đồng hồ giả lập: thời gian chỉ trôi khi gọi AdvanceTo; hẹn giờ (Task.Delay) chạy khi tới hạn.</summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = start;

        public int PendingTimers { get { lock (_gate) return _timers.Count; } }

        public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void AdvanceTo(DateTimeOffset target)
        {
            while (true)
            {
                ManualTimer? next;
                lock (_gate)
                {
                    next = _timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null) { _now = target; return; }
                    _now = next.Due;
                    _timers.Remove(next);
                }
                next.Fire();
            }
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._gate)
                {
                    clock._timers.Remove(this);
                    if (dueTime == Timeout.InfiniteTimeSpan) return true;
                    Due = clock._now + dueTime;
                    clock._timers.Add(this);
                }
                return true;
            }

            public void Fire() => callback(state);

            public void Dispose() { lock (clock._gate) clock._timers.Remove(this); }

            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
