namespace Application.Common;

public sealed class ShopTime
{
    public const string TimeZoneId = "Pacific/Auckland";
    public const string Currency = "NZD";
    public const string Locale = "en-NZ";
    private readonly TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, zone);

    public (DateTimeOffset Start, DateTimeOffset EndExclusive) GetUtcDayRange(DateOnly day)
    {
        DateTimeOffset Boundary(DateOnly value) => new(TimeZoneInfo.ConvertTimeToUtc(value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        return (Boundary(day), Boundary(day.AddDays(1)));
    }
}
