using Moq;
using backend.Services;

namespace backend.Tests.Helpers;

/// <summary>
/// Factory for creating controllable <see cref="ITimeService"/> mocks.
/// Using a fixed deterministic time keeps unit tests reproducible regardless
/// of when (or in which timezone) the CI machine runs them.
/// </summary>
public static class TimeServiceMockFactory
{
    /// <summary>
    /// A fixed Zurich local datetime used as the default across all tests.
    /// 2026-03-07 10:00:00 — well within CET (+01:00), avoids DST edge cases.
    /// </summary>
    public static readonly DateTime DefaultZurichTime =
        new(2026, 3, 7, 10, 0, 0, DateTimeKind.Unspecified);

    /// <summary>
    /// Creates a <see cref="Mock{ITimeService}"/> whose <c>GetZurichTimeAsync</c>
    /// returns <paramref name="zurichTime"/> (or <see cref="DefaultZurichTime"/> if omitted).
    /// </summary>
    public static Mock<ITimeService> Create(DateTime? zurichTime = null)
    {
        var mock = new Mock<ITimeService>();
        mock.Setup(s => s.GetZurichTimeAsync())
            .ReturnsAsync(zurichTime ?? DefaultZurichTime);
        return mock;
    }

    /// <summary>
    /// Returns the mock's <c>.Object</c> directly — convenient for service constructor injection.
    /// </summary>
    public static ITimeService CreateObject(DateTime? zurichTime = null) =>
        Create(zurichTime).Object;
}
