namespace Api.Configuration;

public sealed class CorsSettings
{
    public string[] Origins { get; init; } = [];
    public static bool IsValidOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && !value.EndsWith('/') && !uri.Host.Contains('*');
}
