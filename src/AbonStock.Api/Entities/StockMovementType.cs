namespace AbonStock.Api.Entities;

public enum StockMovementType
{
    Receipt = 1,
    Adjustment = 2,
    OrderDeduction = 3,
    OrderCancellation = 4,
    WriteOff = 5
}
