namespace AbonStock.Api.Dtos.Orders;

public record ConfirmOrderRequest
{
    public uint? RowVersion { get; init; }
}
