using System.Text;

namespace Domain.Catalog;

public static class Slug
{
    public static string From(string value)
    {
        var builder = new StringBuilder(value.Length);
        var dash = false;
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) { builder.Append(c); dash = false; }
            else if (!dash && builder.Length > 0) { builder.Append('-'); dash = true; }
        }
        return builder.ToString().TrimEnd('-');
    }
}
