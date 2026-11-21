using Api.Identity;
using Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

internal static class Uploads
{
    // Slightly above the image limit so the processor, not the server, reports the friendly size error.
    public const long RequestLimit = ImageRules.MaxBytes + 512 * 1024;
}

[ApiController]
[Route("api/categories")]
[Authorize(Policy = Permissions.CatalogManage)]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<CategoryDto>> List([FromQuery] PageRequest request, CancellationToken ct)
    {
        if (request.IncludeDeleted) await this.RequirePermissionAsync(Application.Identity.PermissionKeys.CatalogDeletedView);
        return await categories.ListAsync(request, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<CategoryDto> Get(Guid id, CancellationToken ct) => categories.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CategoryInput input, CancellationToken ct)
    {
        var created = await categories.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<CategoryDto> Update(Guid id, CategoryInput input, CancellationToken ct) => categories.UpdateAsync(id, input, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public Task<CategoryDto> Restore(Guid id, CancellationToken ct) => categories.RestoreAsync(id, ct);

    [HttpPut("{id:guid}/image")]
    [RequestSizeLimit(Uploads.RequestLimit)]
    public async Task<CategoryDto> SetImage(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return await categories.SetImageAsync(id, stream, ct);
    }

    [HttpDelete("{id:guid}/image")]
    public Task<CategoryDto> RemoveImage(Guid id, CancellationToken ct) => categories.RemoveImageAsync(id, ct);
}

[ApiController]
[Route("api/items")]
[Authorize(Policy = Permissions.CatalogManage)]
public sealed class ItemsController(IItemService items) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<ItemSummaryDto>> List([FromQuery] PageRequest request, [FromQuery] ItemFilter filter, CancellationToken ct)
    {
        if (request.IncludeDeleted) await this.RequirePermissionAsync(Application.Identity.PermissionKeys.CatalogDeletedView);
        return await items.ListAsync(request, filter, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<ItemDto> Get(Guid id, CancellationToken ct) => items.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<ItemDto>> Create(ItemInput input, CancellationToken ct)
    {
        var created = await items.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<ItemDto> Update(Guid id, ItemInput input, CancellationToken ct) => items.UpdateAsync(id, input, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await items.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public Task<ItemDto> Restore(Guid id, CancellationToken ct) => items.RestoreAsync(id, ct);

    [HttpPost("{id:guid}/images")]
    [RequestSizeLimit(Uploads.RequestLimit)]
    public async Task<ItemDto> AddImage(Guid id, IFormFile file, [FromForm] string? altText, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return await items.AddImageAsync(id, stream, altText, ct);
    }

    [HttpPut("{id:guid}/images/{imageId:guid}/primary")]
    public Task<ItemDto> SetPrimary(Guid id, Guid imageId, CancellationToken ct) => items.SetPrimaryImageAsync(id, imageId, ct);

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    public Task<ItemDto> RemoveImage(Guid id, Guid imageId, CancellationToken ct) => items.RemoveImageAsync(id, imageId, ct);
}

[ApiController]
[Route("api/vehicle-makes")]
[Authorize(Policy = Permissions.CatalogManage)]
public sealed class VehicleMakesController(IVehicleService vehicles) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<VehicleMakeDto>> List(CancellationToken ct) => vehicles.ListAsync(ct);

    [HttpPost]
    public Task<VehicleMakeDto> CreateMake(NameInput input, CancellationToken ct) => vehicles.CreateMakeAsync(input, ct);

    [HttpPost("{makeId:guid}/models")]
    public Task<VehicleModelDto> CreateModel(Guid makeId, NameInput input, CancellationToken ct) => vehicles.CreateModelAsync(makeId, input, ct);
}

/// <summary>Anonymous storefront reads (ADR 0002 D9). Returns public DTOs only.</summary>
[ApiController]
[Route("api/public")]
[AllowAnonymous]
public sealed class PublicCatalogController(IPublicCatalogService catalog) : ControllerBase
{
    [HttpGet("categories")]
    [ResponseCache(Duration = 60)]
    public Task<IReadOnlyList<PublicCategoryDto>> Categories(CancellationToken ct) => catalog.CategoriesAsync(ct);

    [HttpGet("items")]
    [ResponseCache(Duration = 60)]
    public Task<PagedResult<PublicItemDto>> Items([FromQuery] PublicItemQuery query, CancellationToken ct) =>
        catalog.ItemsAsync(query, ct);

    [HttpGet("items/{id:guid}")]
    [ResponseCache(Duration = 60)]
    public Task<PublicItemDetail> Detail(Guid id, CancellationToken ct) => catalog.DetailAsync(id, ct);

    [HttpGet("vehicles/makes")]
    [ResponseCache(Duration = 60)]
    public Task<IReadOnlyList<PublicVehicleMake>> Makes(CancellationToken ct) => catalog.MakesAsync(ct);

    [HttpGet("vehicles/models")]
    [ResponseCache(Duration = 60)]
    public Task<IReadOnlyList<VehicleModelDto>> Models([FromQuery] Guid makeId, CancellationToken ct) =>
        catalog.ModelsAsync(makeId, ct);

    [HttpPost("cart/validate")]
    [RequestSizeLimit(8192)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IReadOnlyList<PublicCartResult>> ValidateCart(PublicCartRequest request, CancellationToken ct) =>
        catalog.ValidateCartAsync(request, ct);

    [HttpGet("sitemap.xml")]
    [ResponseCache(Duration = 60)]
    public async Task<IActionResult> Sitemap([FromServices] IConfiguration configuration, CancellationToken ct)
    {
        if (!Uri.TryCreate(configuration["Storefront:Origin"], UriKind.Absolute, out var origin)
            || origin.Scheme is not ("https" or "http"))
            return NotFound();
        System.Xml.Linq.XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var ids = await catalog.SitemapIdsAsync(ct);
        var paths = new[] { "/", "/parts", "/delivery-and-returns" }.Concat(ids.Select(id => $"/parts/{id}"));
        var xml = new System.Xml.Linq.XElement(ns + "urlset", paths.Select(path =>
            new System.Xml.Linq.XElement(ns + "url", new System.Xml.Linq.XElement(ns + "loc", new Uri(origin, path).AbsoluteUri))));
        return Content(xml.ToString(), "application/xml");
    }
}
