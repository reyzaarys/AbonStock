using AbonStock.Api.Data;
using AbonStock.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AbonStock.Tests.Integration;

public class ReservationSchemaTests(PostgresTestFixture fixture) : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _fixture = fixture;

    private async Task<Product> CreateProductAsync(AbonStockDbContext db, string prefix = "RES-SCHEMA")
    {
        var product = new Product
        {
            Sku = $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}",
            Name = "Reservation Schema Test Product",
            UnitPrice = 30.00m,
            QuantityOnHand = 100,
            IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task InventoryBatch_DefaultReservedQuantity_IsZero()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var product = await CreateProductAsync(db, "RES-DEF");
        var batch = new InventoryBatch
        {
            ProductId = product.Id,
            BatchNo = $"BAT-DEF-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            InitialQuantity = 50,
            PhysicalQuantity = 50,
            Status = BatchStatus.Released
        };

        db.InventoryBatches.Add(batch);
        await db.SaveChangesAsync();

        // Re-read from database as no tracking
        var fetched = await db.InventoryBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batch.Id);
        Assert.NotNull(fetched);
        Assert.Equal(0, fetched.ReservedQuantity);
    }

    [Fact]
    public async Task InventoryBatch_NegativeReservedQuantity_ViolatesCheckConstraint()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var product = await CreateProductAsync(db, "RES-NEG");
        var batch = new InventoryBatch
        {
            ProductId = product.Id,
            BatchNo = $"BAT-NEG-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            InitialQuantity = 50,
            PhysicalQuantity = 50,
            ReservedQuantity = -5,
            Status = BatchStatus.Released
        };

        db.InventoryBatches.Add(batch);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pgEx.SqlState);
        Assert.Equal("CK_InventoryBatches_ReservedQuantity_Valid", pgEx.ConstraintName);
    }

    [Fact]
    public async Task InventoryBatch_ReservedQuantityExceedingPhysicalQuantity_ViolatesCheckConstraint()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var product = await CreateProductAsync(db, "RES-EXCEED");
        var batch = new InventoryBatch
        {
            ProductId = product.Id,
            BatchNo = $"BAT-EXC-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            InitialQuantity = 20,
            PhysicalQuantity = 20,
            ReservedQuantity = 25, // Exceeds PhysicalQuantity of 20
            Status = BatchStatus.Released
        };

        db.InventoryBatches.Add(batch);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pgEx.SqlState);
        Assert.Equal("CK_InventoryBatches_ReservedQuantity_Valid", pgEx.ConstraintName);
    }

    [Fact]
    public async Task OrderReservation_CanBeCreatedAndLoadedWithValidRelationships()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var product = await CreateProductAsync(db, "RES-REL");
        var batch = new InventoryBatch
        {
            ProductId = product.Id,
            BatchNo = $"BAT-REL-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            InitialQuantity = 100,
            PhysicalQuantity = 100,
            ReservedQuantity = 10,
            Status = BatchStatus.Released
        };
        db.InventoryBatches.Add(batch);

        var order = new Order
        {
            OrderNumber = $"ORD-{Guid.NewGuid().ToString("N")[..8]}",
            CustomerName = "John Doe",
            CustomerEmail = "john@example.com",
            Status = OrderStatus.Confirmed,
            ConfirmedAtUtc = DateTime.UtcNow,
            TotalAmount = 300.00m
        };
        db.Orders.Add(order);

        var item = new OrderItem
        {
            Order = order,
            ProductId = product.Id,
            ProductSku = product.Sku,
            ProductName = product.Name,
            Quantity = 10,
            UnitPrice = product.UnitPrice,
            LineTotal = 300.00m
        };
        db.OrderItems.Add(item);

        var reservation = new OrderReservation
        {
            Order = order,
            OrderItem = item,
            InventoryBatch = batch,
            Quantity = 10,
            Status = ReservationStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.OrderReservations.Add(reservation);

        await db.SaveChangesAsync();

        // Verify loaded with navigation properties
        var loaded = await db.OrderReservations
            .Include(r => r.Order)
            .Include(r => r.OrderItem)
            .Include(r => r.InventoryBatch)
            .FirstOrDefaultAsync(r => r.Id == reservation.Id);

        Assert.NotNull(loaded);
        Assert.Equal(10, loaded.Quantity);
        Assert.Equal(ReservationStatus.Active, loaded.Status);
        Assert.Equal(order.Id, loaded.OrderId);
        Assert.Equal(item.Id, loaded.OrderItemId);
        Assert.Equal(batch.Id, loaded.InventoryBatchId);
        Assert.Null(loaded.ReleasedAtUtc);
    }

    [Fact]
    public async Task OrderReservation_ZeroOrNegativeQuantity_ViolatesCheckConstraint()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var product = await CreateProductAsync(db, "RES-ZEROQTY");
        var batch = new InventoryBatch
        {
            ProductId = product.Id,
            BatchNo = $"BAT-ZQ-{Guid.NewGuid().ToString("N")[..8]}",
            ProductionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            InitialQuantity = 50,
            PhysicalQuantity = 50,
            Status = BatchStatus.Released
        };
        db.InventoryBatches.Add(batch);

        var order = new Order
        {
            OrderNumber = $"ORD-ZQ-{Guid.NewGuid().ToString("N")[..8]}",
            CustomerName = "Jane Doe",
            Status = OrderStatus.Pending,
            TotalAmount = 0
        };
        db.Orders.Add(order);

        var item = new OrderItem
        {
            Order = order,
            ProductId = product.Id,
            ProductSku = product.Sku,
            ProductName = product.Name,
            Quantity = 5,
            UnitPrice = product.UnitPrice,
            LineTotal = 150.00m
        };
        db.OrderItems.Add(item);

        var invalidReservation = new OrderReservation
        {
            Order = order,
            OrderItem = item,
            InventoryBatch = batch,
            Quantity = 0, // Violates CK_OrderReservations_Quantity_Positive
            Status = ReservationStatus.Active
        };
        db.OrderReservations.Add(invalidReservation);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pgEx = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pgEx.SqlState);
        Assert.Equal("CK_OrderReservations_Quantity_Positive", pgEx.ConstraintName);
    }

    [Fact]
    public async Task Order_CancelledAtUtcAndCancellationReason_ArePersisted()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbonStockDbContext>();

        var cancelTime = DateTime.UtcNow;
        var order = new Order
        {
            OrderNumber = $"ORD-CANC-{Guid.NewGuid().ToString("N")[..8]}",
            CustomerName = "Cancellation Test",
            Status = OrderStatus.Cancelled,
            TotalAmount = 0,
            CancelledAtUtc = cancelTime,
            CancellationReason = "Customer changed mind before processing"
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var fetched = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == order.Id);
        Assert.NotNull(fetched);
        Assert.Equal(OrderStatus.Cancelled, fetched.Status);
        Assert.NotNull(fetched.CancelledAtUtc);
        Assert.Equal("Customer changed mind before processing", fetched.CancellationReason);
    }
}
