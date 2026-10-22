using AbonStock.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbonStock.Api.Data;

public class AbonStockDbContext : DbContext
{
    public AbonStockDbContext(DbContextOptions<AbonStockDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryBatch> InventoryBatches => Set<InventoryBatch>();
    public DbSet<OrderReservation> OrderReservations => Set<OrderReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- Product Configuration ---
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products", t =>
            {
                t.HasCheckConstraint("CK_Products_UnitPrice_NonNegative", "\"UnitPrice\" >= 0");
                t.HasCheckConstraint("CK_Products_QuantityOnHand_NonNegative", "\"QuantityOnHand\" >= 0");
            });

            entity.HasKey(p => p.Id);

            entity.Property(p => p.Sku)
                .HasMaxLength(50)
                .IsRequired();

            entity.HasIndex(p => p.Sku)
                .IsUnique()
                .HasDatabaseName("IX_Products_Sku");

            entity.Property(p => p.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(p => p.Description)
                .HasMaxLength(1000);

            entity.Property(p => p.UnitPrice)
                .HasPrecision(18, 2);

            entity.Property(p => p.QuantityOnHand)
                .HasDefaultValue(0);

            entity.Property(p => p.IsActive)
                .HasDefaultValue(true);

            // Npgsql 8.x: uint property with IsRowVersion() maps directly to PostgreSQL xmin system column
            entity.Property(p => p.Version)
                .IsRowVersion();

            entity.HasMany(p => p.StockMovements)
                .WithOne(sm => sm.Product)
                .HasForeignKey(sm => sm.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(p => p.OrderItems)
                .WithOne(oi => oi.Product)
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // --- Order Configuration ---
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");

            entity.HasKey(o => o.Id);

            entity.Property(o => o.OrderNumber)
                .HasMaxLength(50)
                .IsRequired();

            entity.HasIndex(o => o.OrderNumber)
                .IsUnique()
                .HasDatabaseName("IX_Orders_OrderNumber");

            entity.Property(o => o.CustomerName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(o => o.CustomerEmail)
                .HasMaxLength(200);

            entity.Property(o => o.TotalAmount)
                .HasPrecision(18, 2);

            entity.Property(o => o.CancellationReason)
                .HasMaxLength(500);

            // Npgsql 8.x: uint property with IsRowVersion() maps directly to PostgreSQL xmin system column
            entity.Property(o => o.Version)
                .IsRowVersion();

            entity.HasMany(o => o.Items)
                .WithOne(oi => oi.Order)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(o => o.StockMovements)
                .WithOne(sm => sm.ReferenceOrder)
                .HasForeignKey(sm => sm.ReferenceOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // --- OrderItem Configuration ---
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems", t =>
            {
                t.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "\"Quantity\" > 0");
            });

            entity.HasKey(oi => oi.Id);

            // Enforce at most one OrderItem per ProductId within a single Order
            entity.HasIndex(oi => new { oi.OrderId, oi.ProductId })
                .IsUnique()
                .HasDatabaseName("IX_OrderItems_OrderId_ProductId");

            entity.Property(oi => oi.ProductSku)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(oi => oi.ProductName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(oi => oi.UnitPrice)
                .HasPrecision(18, 2);

            entity.Property(oi => oi.LineTotal)
                .HasPrecision(18, 2);

            entity.HasOne(oi => oi.Product)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // --- StockMovement Configuration ---
        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("StockMovements");

            entity.HasKey(sm => sm.Id);

            entity.Property(sm => sm.Reason)
                .HasMaxLength(500)
                .IsRequired();

            // Partial unique index: prevents double stock deduction for a batch in an order
            entity.HasIndex(sm => new { sm.ReferenceOrderId, sm.InventoryBatchId }, "IX_StockMovements_Order_Batch_Deduction")
                .IsUnique()
                .HasFilter("\"ReferenceOrderId\" IS NOT NULL AND \"InventoryBatchId\" IS NOT NULL AND \"MovementType\" = 3"); // 3 = OrderDeduction

            // Partial unique index: prevents double stock restoration for an order item
            entity.HasIndex(sm => new { sm.ReferenceOrderId, sm.ProductId }, "IX_StockMovements_Order_Product_Cancellation")
                .IsUnique()
                .HasFilter("\"ReferenceOrderId\" IS NOT NULL AND \"MovementType\" = 4"); // 4 = OrderCancellation

            entity.HasOne(sm => sm.InventoryBatch)
                .WithMany(b => b.StockMovements)
                .HasForeignKey(sm => sm.InventoryBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // --- InventoryBatch Configuration ---
        modelBuilder.Entity<InventoryBatch>(entity =>
        {
            entity.ToTable("InventoryBatches", t =>
            {
                t.HasCheckConstraint("CK_InventoryBatches_PhysicalQuantity_NonNegative", "\"PhysicalQuantity\" >= 0");
                t.HasCheckConstraint("CK_InventoryBatches_InitialQuantity_Positive", "\"InitialQuantity\" > 0");
                t.HasCheckConstraint("CK_InventoryBatches_ReservedQuantity_Valid", "\"ReservedQuantity\" >= 0 AND \"ReservedQuantity\" <= \"PhysicalQuantity\"");
            });

            entity.HasKey(b => b.Id);

            entity.Property(b => b.BatchNo)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(b => new { b.ProductId, b.BatchNo })
                .IsUnique()
                .HasDatabaseName("IX_InventoryBatches_ProductId_BatchNo");

            entity.Property(b => b.LocationId)
                .HasMaxLength(50)
                .HasDefaultValue(InventoryBatch.DefaultLocationId)
                .IsRequired();

            entity.Property(b => b.ReservedQuantity)
                .HasDefaultValue(0);

            entity.Property(b => b.Status)
                .HasConversion<int>()
                .IsRequired();

            // Npgsql 8.x: uint property with IsRowVersion() maps directly to PostgreSQL xmin system column
            entity.Property(b => b.Version)
                .IsRowVersion();

            entity.HasOne(b => b.Product)
                .WithMany(p => p.Batches)
                .HasForeignKey(b => b.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(b => b.StockMovements)
                .WithOne(sm => sm.InventoryBatch)
                .HasForeignKey(sm => sm.InventoryBatchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(b => b.OrderReservations)
                .WithOne(r => r.InventoryBatch)
                .HasForeignKey(r => r.InventoryBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // --- OrderReservation Configuration ---
        modelBuilder.Entity<OrderReservation>(entity =>
        {
            entity.ToTable("OrderReservations", t =>
            {
                t.HasCheckConstraint("CK_OrderReservations_Quantity_Positive", "\"Quantity\" > 0");
            });

            entity.HasKey(r => r.Id);

            entity.Property(r => r.Quantity)
                .IsRequired();

            entity.Property(r => r.Status)
                .HasConversion<int>()
                .IsRequired();

            entity.HasIndex(r => r.OrderId)
                .HasDatabaseName("IX_OrderReservations_OrderId");

            entity.HasIndex(r => r.OrderItemId)
                .HasDatabaseName("IX_OrderReservations_OrderItemId");

            entity.HasIndex(r => r.InventoryBatchId)
                .HasDatabaseName("IX_OrderReservations_InventoryBatchId");

            entity.HasOne(r => r.Order)
                .WithMany(o => o.Reservations)
                .HasForeignKey(r => r.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.OrderItem)
                .WithMany(oi => oi.Reservations)
                .HasForeignKey(r => r.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.InventoryBatch)
                .WithMany(b => b.OrderReservations)
                .HasForeignKey(r => r.InventoryBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
