using AbonStock.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AbonStock.Api.Data;

public static class AbonStockDataSeeder
{
    public const string SkuAbonSapiOri = "ABON-SAPI-ORI-100";
    public const string SkuAbonSapiPds = "ABON-SAPI-PDS-100";
    public const string SkuAbonAyamGrh = "ABON-AYAM-GRH-100";

    public const string BatchNoSapiOriDepleted = "BAT-202609-SO01";
    public const string BatchNoSapiOriReleased = "BAT-202610-SO02";
    public const string BatchNoSapiOriNoExpiry = "BAT-202610-SO03";
    public const string BatchNoSapiPdsReleased = "BAT-202610-SP01";
    public const string BatchNoAyamGrhReleased = "BAT-202610-AG01";

    public const string OrderNoCompleted = "ORD-20260915-DEMO-001";
    public const string OrderNoConfirmed = "ORD-20261018-DEMO-002";
    public const string OrderNoPending   = "ORD-20261019-DEMO-003";
    public const string OrderNoCancelled = "ORD-20261017-DEMO-004";

    // Application advisory lock key for concurrent startup protection
    private const long SeederLockKey = 0x41626F6E53746F63; // "AbonStoc"

    public static async Task SeedAsync(AbonStockDbContext dbContext, CancellationToken ct = default)
    {
        // 1. Quick pre-check before opening transaction
        if (await dbContext.Products.AnyAsync(p => p.Sku == SkuAbonSapiOri, ct))
        {
            return;
        }

        // 2. Open an explicit transaction with advisory lock to protect against concurrent startup instances
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"SELECT pg_try_advisory_xact_lock({SeederLockKey});";

            var lockAcquired = (bool)(await command.ExecuteScalarAsync(ct) ?? false);
            if (!lockAcquired)
            {
                // Another instance is actively seeding; wait is not required, exit gracefully
                return;
            }

            // Re-check inside advisory lock
            if (await dbContext.Products.AnyAsync(p => p.Sku == SkuAbonSapiOri, ct))
            {
                return;
            }

            var now = DateTime.UtcNow;

            // ==========================================
            // 1. PRODUCTS
            // ==========================================
            var productSapiOri = new Product
            {
                Sku = SkuAbonSapiOri,
                Name = "Abon Sapi Original 100g",
                Description = "Abon sapi asli khas Kediri rasa original gurih manis, kemasan pouch 100g",
                UnitPrice = 35000.00m,
                QuantityOnHand = 150, // Batch 1B (100) + Batch 1C (50)
                IsActive = true,
                CreatedAtUtc = now.AddDays(-60),
                UpdatedAtUtc = now.AddDays(-6)
            };

            var productSapiPds = new Product
            {
                Sku = SkuAbonSapiPds,
                Name = "Abon Sapi Pedas 100g",
                Description = "Abon sapi premium olahan bumbu rempah pedas cabai pilihan 100g",
                UnitPrice = 37500.00m,
                QuantityOnHand = 80, // Batch 2A (80)
                IsActive = true,
                CreatedAtUtc = now.AddDays(-45),
                UpdatedAtUtc = now.AddDays(-3)
            };

            var productAyamGrh = new Product
            {
                Sku = SkuAbonAyamGrh,
                Name = "Abon Ayam Gurih 100g",
                Description = "Abon daging ayam gurih renyah tanpa bahan pengawet 100g",
                UnitPrice = 28000.00m,
                QuantityOnHand = 60, // Batch 3A (60)
                IsActive = true,
                CreatedAtUtc = now.AddDays(-30),
                UpdatedAtUtc = now.AddDays(-4)
            };

            dbContext.Products.AddRange(productSapiOri, productSapiPds, productAyamGrh);
            await dbContext.SaveChangesAsync(ct);

            // ==========================================
            // 2. INVENTORY BATCHES
            // ==========================================
            // Batch 1A: Depleted historical batch (produced Sept 1, fulfilled Sept 15)
            var batch1A = new InventoryBatch
            {
                ProductId = productSapiOri.Id,
                BatchNo = BatchNoSapiOriDepleted,
                ProductionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 9, 1),
                LocationId = InventoryBatch.DefaultLocationId,
                InitialQuantity = 50,
                PhysicalQuantity = 0,
                ReservedQuantity = 0,
                Status = BatchStatus.Depleted,
                CreatedAtUtc = now.AddDays(-50),
                UpdatedAtUtc = now.AddDays(-36)
            };

            // Batch 1B: Released batch with future expiry date (produced Oct 1, expires Oct 2027)
            // 100 physical, 20 reserved for Order 2 (Confirmed)
            var batch1B = new InventoryBatch
            {
                ProductId = productSapiOri.Id,
                BatchNo = BatchNoSapiOriReleased,
                ProductionDate = new DateOnly(2026, 10, 1),
                ExpiryDate = new DateOnly(2027, 10, 1),
                LocationId = InventoryBatch.DefaultLocationId,
                InitialQuantity = 100,
                PhysicalQuantity = 100,
                ReservedQuantity = 20,
                Status = BatchStatus.Released,
                CreatedAtUtc = now.AddDays(-20),
                UpdatedAtUtc = now.AddDays(-3)
            };

            // Batch 1C: Released batch with ExpiryDate = null (traditional non-expiring preserve)
            var batch1C = new InventoryBatch
            {
                ProductId = productSapiOri.Id,
                BatchNo = BatchNoSapiOriNoExpiry,
                ProductionDate = new DateOnly(2026, 10, 15),
                ExpiryDate = null,
                LocationId = InventoryBatch.DefaultLocationId,
                InitialQuantity = 50,
                PhysicalQuantity = 50,
                ReservedQuantity = 0,
                Status = BatchStatus.Released,
                CreatedAtUtc = now.AddDays(-6),
                UpdatedAtUtc = now.AddDays(-6)
            };

            // Batch 2A: Released batch for Abon Sapi Pedas (produced Oct 5, expires Oct 2027)
            // 80 physical, 15 reserved for Order 2 (Confirmed)
            var batch2A = new InventoryBatch
            {
                ProductId = productSapiPds.Id,
                BatchNo = BatchNoSapiPdsReleased,
                ProductionDate = new DateOnly(2026, 10, 5),
                ExpiryDate = new DateOnly(2027, 10, 5),
                LocationId = InventoryBatch.DefaultLocationId,
                InitialQuantity = 80,
                PhysicalQuantity = 80,
                ReservedQuantity = 15,
                Status = BatchStatus.Released,
                CreatedAtUtc = now.AddDays(-16),
                UpdatedAtUtc = now.AddDays(-3)
            };

            // Batch 3A: Released batch for Abon Ayam Gurih (produced Oct 10, expires Oct 2027)
            var batch3A = new InventoryBatch
            {
                ProductId = productAyamGrh.Id,
                BatchNo = BatchNoAyamGrhReleased,
                ProductionDate = new DateOnly(2026, 10, 10),
                ExpiryDate = new DateOnly(2027, 10, 10),
                LocationId = InventoryBatch.DefaultLocationId,
                InitialQuantity = 60,
                PhysicalQuantity = 60,
                ReservedQuantity = 0,
                Status = BatchStatus.Released,
                CreatedAtUtc = now.AddDays(-11),
                UpdatedAtUtc = now.AddDays(-4)
            };

            dbContext.InventoryBatches.AddRange(batch1A, batch1B, batch1C, batch2A, batch3A);
            await dbContext.SaveChangesAsync(ct);

            // ==========================================
            // 3. INITIAL STOCK MOVEMENTS (Receipts)
            // ==========================================
            var movementReceipt1A = new StockMovement
            {
                ProductId = productSapiOri.Id,
                MovementType = StockMovementType.Receipt,
                QuantityChange = 50,
                BalanceAfter = 50,
                InventoryBatchId = batch1A.Id,
                BatchBalanceAfter = 50,
                Reason = "Penerimaan batch produksi perdana Abon Sapi Ori",
                CreatedAtUtc = now.AddDays(-50)
            };

            var movementReceipt1B = new StockMovement
            {
                ProductId = productSapiOri.Id,
                MovementType = StockMovementType.Receipt,
                QuantityChange = 100,
                BalanceAfter = 100, // Saldo produk naik dari 0 ke 100 (setelah order 1 terpenuhi)
                InventoryBatchId = batch1B.Id,
                BatchBalanceAfter = 100,
                Reason = "Penerimaan batch reguler Abon Sapi Ori",
                CreatedAtUtc = now.AddDays(-20)
            };

            var movementReceipt1C = new StockMovement
            {
                ProductId = productSapiOri.Id,
                MovementType = StockMovementType.Receipt,
                QuantityChange = 50,
                BalanceAfter = 150, // Saldo produk naik dari 100 ke 150
                InventoryBatchId = batch1C.Id,
                BatchBalanceAfter = 50,
                Reason = "Penerimaan batch resep tradisional tanpa kedaluwarsa",
                CreatedAtUtc = now.AddDays(-6)
            };

            var movementReceipt2A = new StockMovement
            {
                ProductId = productSapiPds.Id,
                MovementType = StockMovementType.Receipt,
                QuantityChange = 80,
                BalanceAfter = 80,
                InventoryBatchId = batch2A.Id,
                BatchBalanceAfter = 80,
                Reason = "Penerimaan batch produksi Abon Sapi Pedas",
                CreatedAtUtc = now.AddDays(-16)
            };

            var movementReceipt3A = new StockMovement
            {
                ProductId = productAyamGrh.Id,
                MovementType = StockMovementType.Receipt,
                QuantityChange = 60,
                BalanceAfter = 60,
                InventoryBatchId = batch3A.Id,
                BatchBalanceAfter = 60,
                Reason = "Penerimaan batch produksi Abon Ayam Gurih",
                CreatedAtUtc = now.AddDays(-11)
            };

            dbContext.StockMovements.AddRange(
                movementReceipt1A,
                movementReceipt1B,
                movementReceipt1C,
                movementReceipt2A,
                movementReceipt3A);
            await dbContext.SaveChangesAsync(ct);

            // ==========================================
            // 4. ORDERS & TRANSACTIONS
            // ==========================================

            // --- Order 1: Completed (Historical fulfillment) ---
            var orderCompleted = new Order
            {
                OrderNumber = OrderNoCompleted,
                CustomerName = "Hendra Wijaya (Toko Berkah)",
                CustomerEmail = "hendra@tokoberkah.com",
                Status = OrderStatus.Completed,
                TotalAmount = 50 * 35000.00m,
                CreatedAtUtc = now.AddDays(-36),
                UpdatedAtUtc = now.AddDays(-36),
                ConfirmedAtUtc = now.AddDays(-36).AddMinutes(30),
                CompletedAtUtc = now.AddDays(-36).AddHours(2)
            };

            var orderItem1 = new OrderItem
            {
                Order = orderCompleted,
                ProductId = productSapiOri.Id,
                ProductSku = productSapiOri.Sku,
                ProductName = productSapiOri.Name,
                Quantity = 50,
                UnitPrice = 35000.00m,
                LineTotal = 50 * 35000.00m
            };
            orderCompleted.Items.Add(orderItem1);

            dbContext.Orders.Add(orderCompleted);
            await dbContext.SaveChangesAsync(ct);

            var resOrder1 = new OrderReservation
            {
                OrderId = orderCompleted.Id,
                OrderItemId = orderItem1.Id,
                InventoryBatchId = batch1A.Id,
                Quantity = 50,
                Status = ReservationStatus.Fulfilled,
                CreatedAtUtc = orderCompleted.ConfirmedAtUtc!.Value
            };
            dbContext.OrderReservations.Add(resOrder1);

            var movementDeduction1 = new StockMovement
            {
                ProductId = productSapiOri.Id,
                MovementType = StockMovementType.OrderDeduction,
                QuantityChange = -50,
                BalanceAfter = 0,
                InventoryBatchId = batch1A.Id,
                BatchBalanceAfter = 0,
                ReferenceOrderId = orderCompleted.Id,
                Reason = $"Order {orderCompleted.OrderNumber} fulfillment",
                CreatedAtUtc = orderCompleted.CompletedAtUtc!.Value
            };
            dbContext.StockMovements.Add(movementDeduction1);
            await dbContext.SaveChangesAsync(ct);

            // --- Order 2: Confirmed (Active reservations, ready to be fulfilled in Swagger) ---
            var orderConfirmed = new Order
            {
                OrderNumber = OrderNoConfirmed,
                CustomerName = "Dewi Lestari (Resto Nusantara)",
                CustomerEmail = "dewi@restonusantara.id",
                Status = OrderStatus.Confirmed,
                TotalAmount = (20 * 35000.00m) + (15 * 37500.00m),
                CreatedAtUtc = now.AddDays(-3),
                UpdatedAtUtc = now.AddDays(-3),
                ConfirmedAtUtc = now.AddDays(-3).AddMinutes(15)
            };

            var orderItem2A = new OrderItem
            {
                Order = orderConfirmed,
                ProductId = productSapiOri.Id,
                ProductSku = productSapiOri.Sku,
                ProductName = productSapiOri.Name,
                Quantity = 20,
                UnitPrice = 35000.00m,
                LineTotal = 20 * 35000.00m
            };

            var orderItem2B = new OrderItem
            {
                Order = orderConfirmed,
                ProductId = productSapiPds.Id,
                ProductSku = productSapiPds.Sku,
                ProductName = productSapiPds.Name,
                Quantity = 15,
                UnitPrice = 37500.00m,
                LineTotal = 15 * 37500.00m
            };

            orderConfirmed.Items.Add(orderItem2A);
            orderConfirmed.Items.Add(orderItem2B);
            dbContext.Orders.Add(orderConfirmed);
            await dbContext.SaveChangesAsync(ct);

            var resOrder2A = new OrderReservation
            {
                OrderId = orderConfirmed.Id,
                OrderItemId = orderItem2A.Id,
                InventoryBatchId = batch1B.Id,
                Quantity = 20,
                Status = ReservationStatus.Active,
                CreatedAtUtc = orderConfirmed.ConfirmedAtUtc!.Value
            };

            var resOrder2B = new OrderReservation
            {
                OrderId = orderConfirmed.Id,
                OrderItemId = orderItem2B.Id,
                InventoryBatchId = batch2A.Id,
                Quantity = 15,
                Status = ReservationStatus.Active,
                CreatedAtUtc = orderConfirmed.ConfirmedAtUtc!.Value
            };

            dbContext.OrderReservations.AddRange(resOrder2A, resOrder2B);
            await dbContext.SaveChangesAsync(ct);

            // --- Order 3: Pending (Ready to be confirmed in Swagger) ---
            var orderPending = new Order
            {
                OrderNumber = OrderNoPending,
                CustomerName = "Siti Rahayu (Catering Berkah)",
                CustomerEmail = "siti@cateringberkah.com",
                Status = OrderStatus.Pending,
                TotalAmount = (10 * 35000.00m) + (10 * 28000.00m),
                CreatedAtUtc = now.AddDays(-1),
                UpdatedAtUtc = now.AddDays(-1)
            };

            var orderItem3A = new OrderItem
            {
                Order = orderPending,
                ProductId = productSapiOri.Id,
                ProductSku = productSapiOri.Sku,
                ProductName = productSapiOri.Name,
                Quantity = 10,
                UnitPrice = 35000.00m,
                LineTotal = 10 * 35000.00m
            };

            var orderItem3B = new OrderItem
            {
                Order = orderPending,
                ProductId = productAyamGrh.Id,
                ProductSku = productAyamGrh.Sku,
                ProductName = productAyamGrh.Name,
                Quantity = 10,
                UnitPrice = 28000.00m,
                LineTotal = 10 * 28000.00m
            };

            orderPending.Items.Add(orderItem3A);
            orderPending.Items.Add(orderItem3B);
            dbContext.Orders.Add(orderPending);
            await dbContext.SaveChangesAsync(ct);

            // --- Order 4: Cancelled (Audit trail of cancelled order with released reservation) ---
            var orderCancelled = new Order
            {
                OrderNumber = OrderNoCancelled,
                CustomerName = "Bambang Pratama",
                CustomerEmail = "bambang@pratama.net",
                Status = OrderStatus.Cancelled,
                TotalAmount = 5 * 28000.00m,
                CreatedAtUtc = now.AddDays(-4),
                UpdatedAtUtc = now.AddDays(-4).AddMinutes(45),
                ConfirmedAtUtc = now.AddDays(-4).AddMinutes(15),
                CancelledAtUtc = now.AddDays(-4).AddMinutes(45),
                CancellationReason = "Permintaan pembatalan dari pelanggan sebelum pengiriman"
            };

            var orderItem4 = new OrderItem
            {
                Order = orderCancelled,
                ProductId = productAyamGrh.Id,
                ProductSku = productAyamGrh.Sku,
                ProductName = productAyamGrh.Name,
                Quantity = 5,
                UnitPrice = 28000.00m,
                LineTotal = 5 * 28000.00m
            };
            orderCancelled.Items.Add(orderItem4);
            dbContext.Orders.Add(orderCancelled);
            await dbContext.SaveChangesAsync(ct);

            var resOrder4 = new OrderReservation
            {
                OrderId = orderCancelled.Id,
                OrderItemId = orderItem4.Id,
                InventoryBatchId = batch3A.Id,
                Quantity = 5,
                Status = ReservationStatus.Released,
                CreatedAtUtc = orderCancelled.ConfirmedAtUtc!.Value,
                ReleasedAtUtc = orderCancelled.CancelledAtUtc!.Value
            };
            dbContext.OrderReservations.Add(resOrder4);
            await dbContext.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
