using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Api.Identity;
using Application.Catalog;
using Domain.Catalog;
using Infrastructure.Imaging;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Backend.UnitTests;

public sealed class CatalogValidationTests
{
    private static readonly TimeProvider Clock = new FrozenClock(); // 2026
    private static ItemInput Item(string sku = "ab-1", int? level = null, int? qty = null, params FitmentInput[] fitments) =>
        new(sku, "Brake pad set", null, Guid.NewGuid(), null, level, qty, true, fitments, null);

    [Theory]
    [InlineData((short)2016, (short)2022, true)]
    [InlineData((short)2022, (short)2022, true)]
    [InlineData((short)2023, (short)2022, false)]
    [InlineData((short)1949, null, false)]
    [InlineData(null, (short)2027, true)]
    [InlineData(null, (short)2028, false)]
    [InlineData(null, null, true)]
    public void Fitment_years_are_ordered_and_bounded(short? from, short? to, bool valid)
    {
        var result = new ItemInputValidator(Clock).Validate(Item(fitments: new FitmentInput(Guid.NewGuid(), from, to)));
        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(0, 1, true)]
    [InlineData(-1, null, false)]
    [InlineData(null, 0, false)]
    public void Reorder_settings_are_optional_but_not_negative(int? level, int? qty, bool valid) =>
        Assert.Equal(valid, new ItemInputValidator(Clock).Validate(Item(level: level, qty: qty)).IsValid);

    [Theory]
    [InlineData("TP-001", true)]
    [InlineData(" bosch/0986.494_1 ", true)]
    [InlineData("", false)]
    [InlineData("-lead", false)]
    [InlineData("has space", false)]
    [InlineData("<script>", false)]
    public void Sku_format(string sku, bool valid) => Assert.Equal(valid, new ItemInputValidator(Clock).Validate(Item(sku)).IsValid);

    [Fact]
    public void Duplicate_fitments_are_rejected()
    {
        var fitment = new FitmentInput(Guid.NewGuid(), 2016, 2020);
        Assert.False(new ItemInputValidator(Clock).Validate(Item(fitments: [fitment, fitment])).IsValid);
    }

    [Theory]
    [InlineData(1, 20, null, true)]
    [InlineData(0, 20, null, false)]
    [InlineData(1, 101, null, false)]
    [InlineData(1, 20, "-sku", true)]
    [InlineData(1, 20, "CreatedBy", false)]
    public void Paging_and_sort_are_bounded_and_whitelisted(int page, int size, string? sort, bool valid) =>
        Assert.Equal(valid, new PageRequestValidator().Validate(new PageRequest(page, size, sort)).IsValid);

    [Theory]
    [InlineData("Exhaust & Performance", "exhaust-performance")]
    [InlineData("  Tail Lights ", "tail-lights")]
    [InlineData("!!!", "")]
    public void Slugs_are_lowercase_ascii(string name, string slug) => Assert.Equal(slug, Slug.From(name));

    [Fact]
    public void Sku_is_normalised_and_fitment_years_are_inclusive()
    {
        Assert.Equal("TP-001", Domain.Catalog.Item.NormalizeSku("  tp-001 "));
        var fitment = new ItemFitment { YearFrom = 2016, YearTo = 2022 };
        Assert.True(fitment.Fits(2016));
        Assert.True(fitment.Fits(2022));
        Assert.False(fitment.Fits(2023));
        Assert.True(new ItemFitment { YearFrom = 2020 }.Fits(2030));
        Assert.False(new ItemFitment { YearTo = 2010 }.Fits(2011));
    }
}

public sealed class ImageProcessingTests
{
    private readonly SkiaImageProcessor processor = new();

    private static byte[] MakeImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.OrangeRed);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, 90).ToArray();
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Accepts_supported_formats_and_makes_a_small_webp_thumbnail(SKEncodedImageFormat format)
    {
        var result = await processor.ProcessAsync(new MemoryStream(MakeImage(1600, 1200, format)), CancellationToken.None);
        Assert.Equal((1600, 1200), (result.Width, result.Height));
        using var thumb = SKBitmap.Decode(result.Thumbnail);
        Assert.Equal((480, 360), (thumb.Width, thumb.Height));
        using var codec = SKCodec.Create(new MemoryStream(result.Full));
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
    }

    public static TheoryData<string, byte[]> Rejected => new()
    {
        { "empty", [] },
        { "text renamed to .jpg", "just some text"u8.ToArray() },
        { "html", "<html><script>alert(1)</script></html>"u8.ToArray() },
        { "svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray() },
        { "jpeg header only", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46] },
        { "gif header", "GIF89a0001000000010000"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public async Task Rejects_files_that_are_not_supported_images(string name, byte[] bytes)
    {
        Assert.NotNull(name);
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => processor.ProcessAsync(new MemoryStream(bytes), CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_images_with_too_many_pixels()
    {
        var bytes = MakeImage(ImageRules.MaxDimension + 1, 8, SKEncodedImageFormat.Png);
        var error = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => processor.ProcessAsync(new MemoryStream(bytes), CancellationToken.None));
        Assert.Contains("pixels", error.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task Rejects_files_over_the_size_limit_without_reading_them_all()
    {
        var stream = new MemoryStream(new byte[ImageRules.MaxBytes + 1]);
        var error = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => processor.ProcessAsync(stream, CancellationToken.None));
        Assert.Contains("MB", error.Errors.Single().ErrorMessage);
    }
}

public sealed class FileStorageTests
{
    [Theory]
    [InlineData("../secret.webp")]
    [InlineData("items/../../x.webp")]
    [InlineData("items/abc.exe")]
    [InlineData("/etc/passwd")]
    [InlineData("items\\abc.webp")]
    [InlineData("Items/ABC.webp")]
    public async Task Rejects_unsafe_keys(string key)
    {
        var storage = new LocalDiskFileStorage(new StorageSettings { LocalRoot = Path.Combine(Path.GetTempPath(), "sp-" + Guid.NewGuid().ToString("N")) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(key, new MemoryStream([1]), "image/webp", CancellationToken.None));
    }

    [Fact]
    public async Task Saves_reads_and_deletes_inside_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "sp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalDiskFileStorage(new StorageSettings { LocalRoot = root });
            await storage.SaveAsync("items/abc/f00d.webp", new MemoryStream([1, 2, 3]), "image/webp", CancellationToken.None);
            await using (var read = await storage.OpenReadAsync("items/abc/f00d.webp", CancellationToken.None)) Assert.Equal(3, read!.Length);
            Assert.Equal("/media/items/abc/f00d.webp", storage.GetPublicUrl("items/abc/f00d.webp"));
            await storage.DeleteAsync("items/abc/f00d.webp", CancellationToken.None);
            Assert.Null(await storage.OpenReadAsync("items/abc/f00d.webp", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class CatalogModelTests
{
    private static AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=test").Options);

    [Fact]
    public void Migration_snapshot_matches_the_model()
    {
        using var db = NewContext();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("20261116115720_Phase2Catalog", db.Database.GetMigrations());
    }

    [Fact]
    public void Categories_and_items_are_soft_deleted_with_partial_unique_keys()
    {
        using var db = NewContext();
        foreach (var type in new[] { typeof(Category), typeof(Domain.Catalog.Item) })
            Assert.NotEmpty(db.Model.FindEntityType(type)!.GetDeclaredQueryFilters());
        var sku = db.Model.FindEntityType(typeof(Domain.Catalog.Item))!.GetIndexes().Single(x => x.Properties.Single().Name == "Sku");
        Assert.True(sku.IsUnique);
        Assert.Equal("\"IsDeleted\" = false", sku.GetFilter());
        Assert.Contains("\"IsDeleted\"", db.Items.ToQueryString());
    }

    [Fact]
    public void Every_catalog_table_has_audit_columns()
    {
        using var db = NewContext();
        foreach (var type in db.Model.GetEntityTypes())
            foreach (var column in new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" })
                Assert.NotNull(type.FindProperty(column));
    }
}

public sealed class CatalogApiTests
{
    [Theory]
    [InlineData("GET", "/api/categories")]
    [InlineData("POST", "/api/categories")]
    [InlineData("GET", "/api/items")]
    [InlineData("DELETE", "/api/items/6c8f8f0e-4a3b-4c55-9d0f-0e1a2b3c4d5e")]
    [InlineData("POST", "/api/vehicle-makes")]
    public async Task Catalog_management_fails_closed_until_sign_in_exists(string method, string url)
    {
        await using var factory = new FoundationFactory();
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { }),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Public_catalogue_does_not_require_sign_in()
    {
        await using var factory = new FoundationFactory();
        var response = await factory.CreateClient().GetAsync("/api/public/items");
        // The test database is unreachable, so the request fails later — but never on authorization.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Signed_in_user_without_the_permission_is_forbidden()
    {
        await using var factory = new SignedInFactory(permission: null);
        var response = await factory.CreateClient().GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_field_errors_before_touching_the_database()
    {
        await using var factory = new SignedInFactory(Permissions.CatalogManage);
        var response = await factory.CreateClient().PostAsJsonAsync("/api/items", new
        {
            sku = "", name = "", categoryId = Guid.Empty, reorderLevel = -1, isActive = true,
            fitments = new[] { new { vehicleModelId = Guid.NewGuid(), yearFrom = 2022, yearTo = 2016 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        foreach (var field in new[] { "Sku", "Name", "CategoryId", "ReorderLevel", "Fitments[0].YearTo" })
            Assert.True(errors.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task Bad_sort_field_is_rejected()
    {
        await using var factory = new SignedInFactory(Permissions.CatalogManage);
        var response = await factory.CreateClient().GetAsync("/api/items?sort=CreatedBy");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

/// <summary>Test-only sign-in. Phase 1 supplies real authentication.</summary>
public sealed class SignedInFactory(string? permission) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Timeout=1;Pooling=false",
            ["Cors:Origins:0"] = "http://localhost:5173",
            ["Storage:LocalRoot"] = Path.Combine(Path.GetTempPath(), "sp-tests-media"),
        }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(new TestPermission(permission));
            services.AddAuthentication(TestAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        });
    }
}

public sealed record TestPermission(string? Value);

public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPermission permission)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test-user") };
        if (permission.Value is not null) claims.Add(new Claim(Permissions.ClaimType, permission.Value));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
}
