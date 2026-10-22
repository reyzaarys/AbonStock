namespace AbonStock.Api.Dtos.Orders;

public record FulfillOrderRequest
{
    /// <summary>
    /// Optional PostgreSQL xmin concurrency token to protect against stale state.
    /// </summary>
    public uint? RowVersion { get; init; }
}
