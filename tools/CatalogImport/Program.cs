// Imports the sample catalogue (test-project/src/data/parts.json + public/parts images) into the database.
// Usage: dotnet run --project tools/CatalogImport -- --source <test-project folder> [--update] [--dry-run] [--storage <folder>]
using System.Text.Json;
using Application.Catalog;
using Application.Common;
using Domain.Catalog;
using Infrastructure.Imaging;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var options = ImportOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: CatalogImport --source <test-project folder> [--update] [--dry-run] [--storage <folder>]");
    return 2;
}

var dataFile = Path.Combine(options.Source, "src", "data", "parts.json");
var imageRoot = Path.GetFullPath(Path.Combine(options.Source, "public"));
if (!File.Exists(dataFile)) { Console.Error.WriteLine($"Not found: {dataFile}"); return 2; }

var parts = JsonSerializer.Deserialize<List<SamplePart>>(await File.ReadAllTextAsync(dataFile), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
Console.WriteLine($"Read {parts.Count} parts in {parts.Select(x => x.Category).Distinct().Count()} categories.");

var processor = new SkiaImageProcessor();
string ImagePath(SamplePart part)
{
    var path = Path.GetFullPath(Path.Combine(imageRoot, part.Image.TrimStart('/')));
    if (!path.StartsWith(imageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"{part.Id}: image path leaves the source folder.");
    return path;
}

if (options.DryRun)
{
    var failures = 0;
    foreach (var part in parts)
    {
        try
        {
            await using var file = File.OpenRead(ImagePath(part));
            var image = await processor.ProcessAsync(file, CancellationToken.None);
            Console.WriteLine($"ok   {ToSku(part.Id)}  {image.Width}x{image.Height}  {part.Name}");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"FAIL {part.Id}: {Describe(ex)}");
        }
    }
    Console.WriteLine(failures == 0 ? "Dry run passed. Nothing was written." : $"Dry run found {failures} problem(s).");
    return failures == 0 ? 0 : 1;
}

var config = new ConfigurationBuilder().AddUserSecrets(typeof(ImportOptions).Assembly, optional: true).AddEnvironmentVariables().Build();
var connection = config["ConnectionStrings:DefaultConnection"];
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("Set ConnectionStrings:DefaultConnection with user-secrets or the ConnectionStrings__DefaultConnection environment variable.");
    return 2;
}

var storage = new LocalDiskFileStorage(new StorageSettings { LocalRoot = options.Storage });
Console.WriteLine($"Images will be stored in {storage.Root}");
var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection)
    .AddInterceptors(new AuditSaveChangesInterceptor(new ImportActor(), TimeProvider.System)).Options;
await using var db = new AppDbContext(dbOptions);

var written = new List<string>();
var obsolete = new List<string>();
var (created, updated, skipped) = (0, 0, 0);
await using var transaction = await db.Database.BeginTransactionAsync();
try
{
    var order = 0;
    foreach (var group in parts.GroupBy(x => x.Category))
    {
        var slug = Slug.From(group.Key);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Slug == slug);
        if (category is null)
        {
            category = new Category { Name = group.Key, Slug = slug };
            db.Categories.Add(category);
        }
        category.SortOrder = order++ * 10;
        // Every sample item in a category carries the same text, so it is really the category's description.
        category.Description ??= group.Select(x => x.Description).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        // Showcase photos are whole vehicle builds, not parts: keep them, but hidden (ADR 0002).
        if (slug == "showcase") category.IsActive = false;
        await db.SaveChangesAsync();

        foreach (var part in group)
        {
            var sku = ToSku(part.Id);
            var item = await db.Items.Include(x => x.Images).SingleOrDefaultAsync(x => x.Sku == sku);
            if (item is not null && !options.Update) { skipped++; continue; }
            if (item is null)
            {
                item = new Item { Sku = sku };
                db.Items.Add(item);
                created++;
            }
            else updated++;
            item.Name = part.Name.Trim();
            item.Description = part.Description?.Trim();
            item.CategoryId = category.Id;
            item.IsActive = true;

            await using var file = File.OpenRead(ImagePath(part));
            var image = await processor.ProcessAsync(file, CancellationToken.None);
            foreach (var old in item.Images.ToList())
            {
                obsolete.AddRange([old.StorageKey, old.ThumbnailKey]);
                item.Images.Remove(old);
                db.ItemImages.Remove(old);
            }
            await db.SaveChangesAsync();
            var name = Guid.NewGuid().ToString("N");
            var key = $"items/{item.Id:N}/{name}{ProcessedImage.Extension}";
            var thumbKey = $"items/{item.Id:N}/{name}-thumb{ProcessedImage.Extension}";
            await storage.SaveAsync(key, new MemoryStream(image.Full), ProcessedImage.ContentType, CancellationToken.None);
            written.Add(key);
            await storage.SaveAsync(thumbKey, new MemoryStream(image.Thumbnail), ProcessedImage.ContentType, CancellationToken.None);
            written.Add(thumbKey);
            item.Images.Add(new ItemImage
            {
                StorageKey = key, ThumbnailKey = thumbKey, ContentType = ProcessedImage.ContentType, ByteSize = image.Full.Length,
                Width = image.Width, Height = image.Height, IsPrimary = true, SortOrder = 0, AltText = item.Name,
            });
            await db.SaveChangesAsync();
            Console.WriteLine($"{sku}  {item.Name}");
        }
    }
    await transaction.CommitAsync();
}
catch (Exception ex)
{
    await transaction.RollbackAsync();
    foreach (var key in written) await storage.DeleteAsync(key, CancellationToken.None);
    Console.Error.WriteLine($"Import failed and was rolled back: {Describe(ex)}");
    return 1;
}
foreach (var key in obsolete) await storage.DeleteAsync(key, CancellationToken.None);
Console.WriteLine($"Done. Created {created}, updated {updated}, skipped {skipped} (already present; use --update to overwrite).");
return 0;

static string ToSku(string id) => Item.NormalizeSku(id.StartsWith("part-", StringComparison.OrdinalIgnoreCase) ? "TP-" + id[5..] : id);

// Only print exception types and validation messages; provider exceptions can contain connection details.
static string Describe(Exception ex) => ex is FluentValidation.ValidationException v
    ? string.Join("; ", v.Errors.Select(e => e.ErrorMessage))
    : ex is InvalidOperationException or FileNotFoundException ? ex.Message : ex.GetType().Name;

internal sealed record SamplePart(string Id, string Name, string Category, string Image, string? Description, bool InStock);

internal sealed class ImportActor : ICurrentActor { public string ActorId => "system:import"; }

internal sealed record ImportOptions(string Source, string Storage, bool Update, bool DryRun)
{
    public static ImportOptions? Parse(string[] args)
    {
        string? source = null;
        var storage = Path.Combine(FindRepoRoot(), "src", "backend", "Api", ".data", "media");
        bool update = false, dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source" when i + 1 < args.Length: source = args[++i]; break;
                case "--storage" when i + 1 < args.Length: storage = args[++i]; break;
                case "--update": update = true; break;
                case "--dry-run": dryRun = true; break;
                default: return null;
            }
        }
        return source is null ? null : new(Path.GetFullPath(source), Path.GetFullPath(storage), update, dryRun);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Spareparts.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
