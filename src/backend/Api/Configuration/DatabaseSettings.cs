using Npgsql;

namespace Api.Configuration;

public sealed class DatabaseSettings
{
    public string DefaultConnection { get; init; } = string.Empty;
    public static bool IsValid(string value)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(value);
            return !string.IsNullOrWhiteSpace(builder.Host) && !string.IsNullOrWhiteSpace(builder.Database);
        }
        catch (ArgumentException) { return false; }
    }
}
