using Application.Common;

namespace Backend.UnitTests;

public sealed class ShopTimeTests
{
    [Theory]
    [InlineData(2026, 4, 5, 25)]
    [InlineData(2026, 9, 27, 23)]
    [InlineData(2026, 6, 15, 24)]
    public void Day_boundaries_follow_Auckland_daylight_saving(int year, int month, int day, int hours)
    {
        var (start, end) = new ShopTime().GetUtcDayRange(new DateOnly(year, month, day));
        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(hours, (end - start).TotalHours);
        Assert.Equal(new DateOnly(year, month, day), DateOnly.FromDateTime(new ShopTime().ToLocal(start).DateTime));
    }

    [Fact]
    public void Shop_time_is_independent_of_machine_timezone()
    {
        var local = new ShopTime().ToLocal(DateTimeOffset.Parse("2026-12-31T12:00:00Z"));
        Assert.Equal(2027, local.Year);
        Assert.Equal(1, local.Hour);
    }
}
