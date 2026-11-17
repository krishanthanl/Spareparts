using Application.Catalog;
using Application.Identity;
using Application.Inventory;
using Domain.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/grns")]
[Authorize(Policy = PermissionKeys.InventoryGrn)]
public sealed class GrnsController(IGrnService grns) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<GrnSummaryDto>> List([FromQuery] DocumentListRequest request, CancellationToken ct) => grns.ListAsync(request, ct);

    [HttpGet("{id:guid}")]
    public Task<GrnDto> Get(Guid id, CancellationToken ct) => grns.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<GrnDto>> Create(GrnInput input, CancellationToken ct)
    {
        var created = await grns.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<GrnDto> Update(Guid id, GrnInput input, CancellationToken ct) => grns.UpdateAsync(id, input, ct);

    [HttpPost("{id:guid}/post")]
    public Task<GrnDto> Post(Guid id, CancellationToken ct) => grns.PostAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<GrnDto> Cancel(Guid id, CancellationToken ct) => grns.CancelAsync(id, ct);

    [HttpPost("{id:guid}/reverse")]
    [Authorize(Policy = PermissionKeys.InventoryReverse)]
    public Task<GrnDto> Reverse(Guid id, ReverseInput input, CancellationToken ct) => grns.ReverseAsync(id, input, ct);

    [HttpGet("/api/suppliers/suggestions")]
    public Task<IReadOnlyList<string>> Suppliers([FromQuery] string? q, CancellationToken ct) => grns.SupplierSuggestionsAsync(q, ct);
}

/// <summary>Adjustments and opening stock share one service; the route decides the kind.</summary>
public abstract class StockDocumentsControllerBase(IStockDocumentService documents, StockDocumentKind kind) : ControllerBase
{
    protected IStockDocumentService Documents => documents;

    [HttpGet]
    public Task<PagedResult<StockDocumentSummaryDto>> List([FromQuery] DocumentListRequest request, CancellationToken ct) => documents.ListAsync(kind, request, ct);

    [HttpGet("{id:guid}")]
    public Task<StockDocumentDto> Get(Guid id, CancellationToken ct) => documents.GetAsync(kind, id, ct);

    [HttpPost]
    public async Task<ActionResult<StockDocumentDto>> Create(StockDocumentInput input, CancellationToken ct) =>
        StatusCode(201, await documents.CreateAsync(kind, input, ct));

    [HttpPut("{id:guid}")]
    public Task<StockDocumentDto> Update(Guid id, StockDocumentInput input, CancellationToken ct) => documents.UpdateAsync(kind, id, input, ct);

    [HttpPost("{id:guid}/post")]
    public Task<StockDocumentDto> Post(Guid id, CancellationToken ct) => documents.PostAsync(kind, id, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<StockDocumentDto> Cancel(Guid id, CancellationToken ct) => documents.CancelAsync(kind, id, ct);
}

[ApiController]
[Route("api/stock-adjustments")]
[Authorize(Policy = PermissionKeys.InventoryAdjust)]
public sealed class StockAdjustmentsController(IStockDocumentService documents) : StockDocumentsControllerBase(documents, StockDocumentKind.Adjustment);

[ApiController]
[Route("api/opening-stock")]
[Authorize(Policy = PermissionKeys.InventoryAdjust)]
public sealed class OpeningStockController(IStockDocumentService documents) : StockDocumentsControllerBase(documents, StockDocumentKind.Opening)
{
    public const long MaxCsvBytes = 512 * 1024;

    /// <summary>CSV: sku,quantity,unit_cost,selling_price. Dry run by default.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(MaxCsvBytes + 64 * 1024)]
    public async Task<OpeningImportResult> Import(IFormFile file, [FromQuery] bool dryRun = true, CancellationToken ct = default)
    {
        if (file.Length > MaxCsvBytes) throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("File", "The file is too large (512 KB at most).")]);
        using var reader = new StreamReader(file.OpenReadStream());
        return await Documents.ImportOpeningAsync(await reader.ReadToEndAsync(ct), dryRun, ct);
    }
}

[ApiController]
[Route("api/stocktakes")]
[Authorize(Policy = PermissionKeys.InventoryAdjust)]
public sealed class StocktakesController(IStocktakeService stocktakes) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<StocktakeSummaryDto>> List([FromQuery] DocumentListRequest request, CancellationToken ct) => stocktakes.ListAsync(request, ct);

    [HttpGet("{id:guid}")]
    public Task<StocktakeDto> Get(Guid id, CancellationToken ct) => stocktakes.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<StocktakeDto>> Create(StocktakeInput input, CancellationToken ct) => StatusCode(201, await stocktakes.CreateAsync(input, ct));

    [HttpPut("{id:guid}")]
    public Task<StocktakeDto> Update(Guid id, StocktakeInput input, CancellationToken ct) => stocktakes.UpdateAsync(id, input, ct);

    [HttpPost("{id:guid}/post")]
    public Task<StocktakeDto> Post(Guid id, CancellationToken ct) => stocktakes.PostAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<StocktakeDto> Cancel(Guid id, CancellationToken ct) => stocktakes.CancelAsync(id, ct);
}

[ApiController]
[Authorize(Policy = PermissionKeys.InventoryReports)]
public sealed class StockController(IStockQueryService stock, IPriceService prices) : ControllerBase
{
    [HttpGet("api/stock")]
    public Task<StockReport> Stock([FromQuery] StockRequest request, CancellationToken ct) => stock.StockAsync(request, ct);

    [HttpGet("api/stock/low")]
    public Task<IReadOnlyList<StockRow>> Low(CancellationToken ct) => stock.LowStockAsync(ct);

    [HttpGet("api/items/{id:guid}/stock-card")]
    public Task<StockCard> Card(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => stock.StockCardAsync(id, from, to, ct);

    [HttpGet("api/items/{id:guid}/price-history")]
    public Task<IReadOnlyList<PriceHistoryRow>> PriceHistory(Guid id, CancellationToken ct) => prices.HistoryAsync(id, ct);

    [HttpPost("api/items/{id:guid}/price")]
    [Authorize(Policy = PermissionKeys.InventoryPrice)]
    public Task<PriceHistoryRow> ChangePrice(Guid id, PriceChangeInput input, CancellationToken ct) => prices.ChangeAsync(id, input, ct);
}
