using NutriLog.Features.Targets;

namespace NutriLog.Tests.Features.Targets;

// Null days stand for RunStats being unreachable.
public sealed class FakeRunStats(Func<DateOnly, Task<RunDay?>>? days = null) : IRunStats
{
    public FakeRunStats(Func<DateOnly, RunDay?> days) : this(date => Task.FromResult(days(date)))
    {
    }

    public List<DateOnly> Asked { get; } = [];

    public Task<RunDay?> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        Asked.Add(date);
        return days is null ? Task.FromResult<RunDay?>(RunDay.None) : days(date);
    }
}
