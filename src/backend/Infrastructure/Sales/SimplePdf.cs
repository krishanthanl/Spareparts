using System.Globalization;
using System.Text;

namespace Infrastructure.Sales;

/// <summary>
/// A small PDF writer for printed documents (ADR 0005, D16). It uses only the standard Helvetica fonts,
/// which every PDF reader has built in, so there are no font files, no native libraries and no licence
/// to track. Text is WinAnsi (Latin-1); other characters are reduced to their base letter.
/// Coordinates are in points from the top-left of an A4 page.
/// </summary>
internal sealed class SimplePdf
{
    public const double PageWidth = 595.28;
    public const double PageHeight = 841.89;

    private readonly List<StringBuilder> pages = [];
    private StringBuilder current = null!;

    public SimplePdf() => NewPage();

    public int PageCount => pages.Count;

    public void NewPage()
    {
        current = new StringBuilder();
        pages.Add(current);
    }

    public void Text(double x, double y, string text, double size = 10, bool bold = false, double gray = 0)
    {
        var encoded = Encode(text);
        current.Append(CultureInfo.InvariantCulture, $"{gray:0.##} g BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {PageHeight - y:0.##} Td (");
        foreach (var b in encoded)
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\') current.Append('\\');
            if (b < 32 || b > 126) current.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else current.Append((char)b);
        }
        current.Append(") Tj ET 0 g\n");
    }

    public void TextRight(double right, double y, string text, double size = 10, bool bold = false) =>
        Text(right - Width(text, size, bold), y, text, size, bold);

    public void Line(double x1, double y1, double x2, double y2, double width = 0.5, double gray = 0.6) =>
        current.Append(CultureInfo.InvariantCulture, $"{gray:0.##} G {width:0.##} w {x1:0.##} {PageHeight - y1:0.##} m {x2:0.##} {PageHeight - y2:0.##} l S 0 G\n");

    public void FillRect(double x, double y, double w, double h, double gray = 0.93) =>
        current.Append(CultureInfo.InvariantCulture, $"{gray:0.##} g {x:0.##} {PageHeight - y - h:0.##} {w:0.##} {h:0.##} re f 0 g\n");

    /// <summary>Cuts <paramref name="text"/> to fit <paramref name="maxWidth"/>, adding "..." when shortened.</summary>
    public static string Fit(string text, double maxWidth, double size = 10, bool bold = false)
    {
        if (Width(text, size, bold) <= maxWidth) return text;
        var s = text;
        while (s.Length > 0 && Width(s + "...", size, bold) > maxWidth) s = s[..^1];
        return s.TrimEnd() + "...";
    }

    /// <summary>Splits text into lines no wider than <paramref name="maxWidth"/>.</summary>
    public static IReadOnlyList<string> Wrap(string text, double maxWidth, double size = 10, bool bold = false)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (Width(next, size, bold) <= maxWidth) line = next;
                else
                {
                    if (line.Length > 0) lines.Add(line);
                    line = Fit(word, maxWidth, size, bold);
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    public static double Width(string text, double size = 10, bool bold = false)
    {
        var widths = bold ? BoldWidths : RegularWidths;
        double total = 0;
        foreach (var b in Encode(text)) total += b is >= 32 and <= 126 ? widths[b - 32] : 556;
        return total * size / 1000;
    }

    public byte[] Build()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "", // pages, filled below
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };
        var kids = new List<int>();
        foreach (var page in pages)
        {
            var content = page.ToString();
            objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
            var contentId = objects.Count;
            objects.Add(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth:0.##} {PageHeight:0.##}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>"));
            kids.Add(objects.Count);
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>";

        using var output = new MemoryStream();
        void Write(string s) { var bytes = Encoding.Latin1.GetBytes(s); output.Write(bytes); }
        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    /// <summary>Latin-1 bytes; anything else falls back to its base letter (e.g. a macron vowel) or '?'.</summary>
    private static byte[] Encode(string text)
    {
        var result = new List<byte>(text.Length);
        foreach (var ch in text)
        {
            var c = ch switch { '–' or '—' => '-', '‘' or '’' => '\'', '“' or '”' => '"', _ => ch };
            if (c <= 'ÿ') { result.Add(c < ' ' ? (byte)' ' : (byte)c); continue; }
            var baseChar = c.ToString().Normalize(NormalizationForm.FormD)[0];
            result.Add(baseChar <= 'ÿ' ? (byte)baseChar : (byte)'?');
        }
        return result.ToArray();
    }

    // Standard Type 1 font metrics for characters 32..126 (Adobe AFM), in 1/1000 em.
    private static readonly int[] RegularWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly int[] BoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];
}
