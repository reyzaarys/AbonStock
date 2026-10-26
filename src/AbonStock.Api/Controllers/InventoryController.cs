using AbonStock.Api.Common;
using AbonStock.Api.Dtos.Inventory;
using AbonStock.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbonStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class InventoryController(IInventoryService inventoryService) : ControllerBase
{
    private readonly IInventoryService _inventoryService = inventoryService;

    [HttpPost("receipts")]
    [ProducesResponseType(typeof(StockReceiptResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReceiveStock([FromBody] StockReceiptRequest request, CancellationToken ct)
    {
        var response = await _inventoryService.ReceiveStockAsync(request, ct);
        return CreatedAtAction(nameof(GetBatchById), new { id = response.BatchId }, response);
    }

    [HttpPost("batches/{id:int}/release")]
    [ProducesResponseType(typeof(BatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReleaseBatch(int id, [FromBody] ReleaseBatchRequest request, CancellationToken ct)
    {
        var response = await _inventoryService.ReleaseBatchAsync(id, request, ct);
        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("adjustments")]
    [ProducesResponseType(typeof(StockAdjustmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AdjustStock([FromBody] StockAdjustmentRequest request, CancellationToken ct)
    {
        var response = await _inventoryService.AdjustStockAsync(request, ct);
        return Ok(response);
    }

    [HttpGet("batches")]
    [ProducesResponseType(typeof(PagedResult<BatchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBatches([FromQuery] BatchListQuery query, CancellationToken ct)
    {
        var response = await _inventoryService.GetBatchesAsync(query, ct);
        return Ok(response);
    }

    [HttpGet("batches/{id:int}")]
    [ProducesResponseType(typeof(BatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatchById(int id, CancellationToken ct)
    {
        var response = await _inventoryService.GetBatchByIdAsync(id, ct);
        return Ok(response);
    }

    [HttpGet("movements")]
    [ProducesResponseType(typeof(PagedResult<StockMovementResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMovements([FromQuery] StockMovementListQuery query, CancellationToken ct)
    {
        var response = await _inventoryService.GetMovementsAsync(query, ct);
        return Ok(response);
    }
}
