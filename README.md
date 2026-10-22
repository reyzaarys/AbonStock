# AbonStock API

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Entity Framework Core](https://img.shields.io/badge/EF%20Core-8.0-purple.svg)](https://learn.microsoft.com/en-us/ef/core/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue.svg)](https://www.postgresql.org/)
[![Tests](https://img.shields.io/badge/Tests-113%20Passed-brightgreen.svg)]()
[![License](https://img.shields.io/badge/License-MIT-green.svg)]()

Production-grade Inventory Tracking and Order Fulfillment Web API for artisanal food manufacturers (specifically Indonesian Abon / Shredded Meat producers). Built with **ASP.NET Core 8**, **Entity Framework Core 8**, and **PostgreSQL 16**.

---

## 1. Project Overview & Problem Solved

Artisanal food manufacturing (such as shredded beef and chicken—*Abon*) requires strict batch traceability, quality control, deterministic inventory allocation, and transaction integrity. Small-to-medium producers often suffer from:
- **Overselling & Race Conditions:** Concurrent customer orders competing for the same inventory batches leading to negative stock or over-commitment.
- **Lack of Traceability:** Inability to pinpoint which production batch fulfilled which customer order, creating recall and compliance risks.
- **Double Deductions:** Lack of database-level idempotency leading to accidental duplicate inventory deductions during fulfillment retries.
- **Uncontrolled Allocations:** Non-deterministic batch selection causing older batches to expire before being shipped.

**AbonStock** addresses these operational challenges with a robust backend architecture featuring:
- **Strict FIFO Allocation:** Batches are allocated by earliest production date (`ProductionDate ASC, Id ASC`).
- **Pessimistic & Optimistic Concurrency:** Canonical PostgreSQL row-level locking (`FOR UPDATE`) combined with PostgreSQL system column `xmin` concurrency tokens.
- **Double-Deduction Prevention:** Application-level state validation reinforced by a PostgreSQL partial unique index on `StockMovements`.
- **Atomic Stock Ledger:** Every physical change produces an immutable audit movement with before/after balances for both the batch and the product.

---

## 2. Key Features & Business Workflows

```
[ Production ] ──> Receipt (PendingRelease) ──> QC Pass ──> Released
                                                                │
[ Sales ] ──> Create Order (Pending)                            ▼
                    │                                   FIFO Allocation
                    ▼                                           │
             Confirm Order ─────────────────────────────> Active Reservation
                    │                                           │
          ┌─────────┴─────────┐                                 ▼
          ▼                   ▼                           Fulfill Order
    Cancel Order        Fulfill Order                           │
          │                   │                                 ▼
   Release Reserve     Deduct Physical Stock            StockMovement Ledger
          │                   │                                 │
          ▼                   ▼                                 ▼
      Cancelled           Completed                    Physical Stock Decremented
```

1. **Product Catalog Management:**
   - SKU uniqueness enforcement and normalization.
   - Active status toggles and server-authoritative unit pricing.
   - Aggregate inventory tracking (`QuantityOnHand`).

2. **Batch Production & QC Release:**
   - Batches enter as `PendingRelease`.
   - Explicit 3-point QC gate (`VisualCheckPassed`, `AromaCheckPassed`, `PackagingCheckPassed`) before transitioning to `Released`.
   - Batch numbers unique per product (`IX_InventoryBatches_ProductId_BatchNo`).

3. **Stock Adjustments & Waste Tracking:**
   - Physical stock corrections with required audit reasons.
   - Enforces invariant: physical quantity cannot fall below active reserved quantity.
   - Auto-transitions batch to `Depleted` when physical quantity reaches zero.

4. **Order Management & FIFO Reservations:**
   - **Order Creation:** Orders start as `Pending` with server-calculated line totals; no stock is reserved or deducted at creation.
   - **Order Confirmation:** Atomically evaluates eligible released batches, acquires row-level locks, and creates `OrderReservation` records using strict FIFO (`ProductionDate ASC, Id ASC`).
   - **Order Cancellation:** Cancelling a `Confirmed` order atomically rolls back reservations without losing the historical audit trail.

5. **Atomic Order Fulfillment:**
   - Fulfills `Confirmed` orders atomically across multiple products and multi-batch allocations.
   - Decrements physical stock and reserved stock simultaneously.
   - Marks reservations as `Fulfilled` and orders as `Completed`.
   - Writes immutable `OrderDeduction` entries to `StockMovements`.
   - Protected against duplicate fulfillment via database-level partial unique constraints.

---

## 3. Technology Stack

- **Framework:** .NET 8 (C# 12)
- **Web API:** ASP.NET Core Web API with minimal controllers, Problem Details (RFC 7807), and global exception handling.
- **ORM:** Entity Framework Core 8.0.8
- **Database Driver:** Npgsql.EntityFrameworkCore.PostgreSQL 8.0.8
- **Database:** PostgreSQL 16 (Alpine container)
- **Documentation:** OpenAPI / Swagger (Swashbuckle 6.6.2)
- **Testing:** xUnit 2.5.3, `Microsoft.AspNetCore.Mvc.Testing` 8.0.8, Testcontainers-ready architecture with isolated test database fixtures.

---

## 4. Architecture & Directory Structure

```
Upwork/
├── .env.example                     # Environment template for local Docker setup
├── .gitignore                       # Standard .NET / Docker ignore rules
├── AbonStock.sln                    # Visual Studio solution file
├── docker-compose.yml               # PostgreSQL 16 local container definition
├── docs/
│   ├── architecture.md              # In-depth architectural & concurrency design
│   ├── demo-walkthrough.md          # Step-by-step Swagger UI verification guide
│   └── upwork-portfolio.md          # Technical summary & portfolio case study
├── src/
│   └── AbonStock.Api/
│       ├── Common/
│       │   ├── Exceptions/          # Custom domain exceptions (NotFound, Conflict, Validation)
│       │   └── PagedResult.cs       # Pagination envelope
│       ├── Controllers/             # API Endpoints (Products, Inventory, Orders)
│       ├── Data/
│       │   ├── AbonStockDbContext.cs# EF Core DbContext, constraints & index mappings
│       │   ├── AbonStockDataSeeder.cs# Idempotent startup demo data seeder
│       │   └── PostgresErrorCodes.cs# PostgreSQL SQLState constant mappings
│       ├── Dtos/                    # Strongly-typed request/response records
│       ├── Entities/                # Relational domain models
│       ├── Middleware/              # RFC 7807 GlobalExceptionHandler
│       ├── Migrations/              # EF Core database migrations
│       ├── Services/                # Business logic & transactional operations
│       ├── appsettings.json         # Base configuration
│       ├── appsettings.Development.json # Development configuration
│       └── Program.cs               # Host configuration & DI pipeline
└── tests/
    └── AbonStock.Tests/
        ├── Common/                  # Test helpers & orderers
        ├── Integration/             # PostgreSQL database integration tests
        │   ├── DataSeederIntegrationTests.cs
        │   ├── InventoryIntegrationTests.cs
        │   ├── OrderIntegrationTests.cs
        │   ├── PostgresTestFixture.cs
        │   ├── ProductIntegrationTests.cs
        │   └── ReservationSchemaTests.cs
        └── Unit/                    # Domain validation unit tests
            ├── InventoryValidationTests.cs
            ├── OrderValidationTests.cs
            └── ProductServiceTests.cs
```

---

## 5. Prerequisites & Local Installation

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (v8.0.x or higher)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) or Docker Engine
- PowerShell 7+ or Bash terminal

### 1. Clone the Repository
```bash
git clone <repository-url>
cd Upwork
```

### 2. Configure Environment Variables
Copy `.env.example` to `.env` (or configure your local environment):
```bash
cp .env.example .env
```

Configuration parameters defined in `.env.example`:
- `POSTGRES_USER`: Database username
- `POSTGRES_PASSWORD`: Database user password
- `POSTGRES_DB`: Target development database name (`abonstock_db`)
- `POSTGRES_PORT`: Exposed host port (`5432`)

> [!NOTE]
> Review `.env.example` for local defaults. For staging or production environments, always set strong passwords via your secret management system. Never commit real credentials to version control.

### 3. Start PostgreSQL Container
```bash
docker compose up -d
```
Verify the container is healthy:
```bash
docker ps --filter "name=abonstock-postgres"
```

---

## 6. Database Migrations & Running the API

### Apply Database Migrations
Run the EF Core migration command pointing to the development database:
```bash
dotnet ef database update --project src/AbonStock.Api
```

### Run the Web API
```bash
dotnet run --project src/AbonStock.Api --launch-profile http
```
The API starts on `http://localhost:5299`.

### Access Swagger UI
Open your browser and navigate to:
```
http://localhost:5299/swagger
```

---

## 7. Development Data Seeding

When the API runs in the `Development` environment (`app.Environment.IsDevelopment()`), `AbonStockDataSeeder` automatically populates realistic demo data:

| Entity | Details |
| :--- | :--- |
| **Products** | • `ABON-SAPI-ORI-100` (Abon Sapi Original 100g, Rp 35.000)<br>• `ABON-SAPI-PDS-100` (Abon Sapi Pedas 100g, Rp 37.500)<br>• `ABON-AYAM-GRH-100` (Abon Ayam Gurih 100g, Rp 28.000) |
| **Batches** | • `BAT-202609-SO01`: Depleted historical batch (0 phys / 0 res)<br>• `BAT-202610-SO02`: Released batch (100 phys / 20 res)<br>• `BAT-202610-SO03`: Released batch without expiry (50 phys / 0 res)<br>• `BAT-202610-SP01`: Released batch (80 phys / 15 res)<br>• `BAT-202610-AG01`: Released batch (60 phys / 0 res) |
| **Orders** | • `ORD-20260915-DEMO-001`: **Completed** (historical fulfillment)<br>• `ORD-20261018-DEMO-002`: **Confirmed** (active reservations, ready to fulfill)<br>• `ORD-20261019-DEMO-003`: **Pending** (ready to confirm & fulfill in demo)<br>• `ORD-20261017-DEMO-004`: **Cancelled** (audit trail of released reservation) |

### Seeder Concurrency & Idempotency
- **Natural Key Checks:** Avoids redundant writes if demo SKUs already exist.
- **PostgreSQL Advisory Lock:** Acquires `pg_try_advisory_xact_lock(0x41626F6E53746F63)` inside an explicit transaction, ensuring safety across multiple concurrent instances or container restarts.
- **Environment Boundary:** Seeding is completely disabled during automated testing (`builder.UseEnvironment("Testing")`), ensuring test database isolation.

---

## 8. Running Automated Tests

The solution contains a comprehensive test suite of **113 automated tests** (40 unit tests and 73 PostgreSQL integration tests).

```bash
dotnet test
```

### Test Suite Structure
1. **Unit Tests (`tests/AbonStock.Tests/Unit/`):**
   - In-memory domain validation tests for order lines, product SKUs, prices, batch statuses, and QC rules.
2. **Integration Tests (`tests/AbonStock.Tests/Integration/`):**
   - Executed against a dedicated PostgreSQL database (`abonstock_test_db`).
   - Covers concurrency conflicts, multi-batch fulfillment, deadlocks, transaction rollbacks, partial unique constraints, and seeder idempotency.

---

## 9. Concurrency & Transactional Integrity

### A. Optimistic Concurrency via PostgreSQL `xmin`
All mutable entities (`Product`, `InventoryBatch`, `Order`) map an unsigned integer property `Version` to PostgreSQL's native system column `xmin` using EF Core's `IsRowVersion()`:
- When a client sends a request with `RowVersion`, EF Core appends `WHERE "xmin" = @pOriginalVersion` to update queries.
- If another transaction modified the row, PostgreSQL updates 0 rows, triggering `DbUpdateConcurrencyException`, translated by the API into `409 Conflict` (RFC 7807 `ProblemDetails`).

### B. Pessimistic Row-Level Locking & Canonical Lock Hierarchy
To prevent circular-wait deadlocks during multi-item orders, `OrderService` locks database rows in a strictly ordered canonical sequence:
1. **Order Row:** `SELECT "Id" FROM "Orders" WHERE "Id" = {id} FOR UPDATE`
2. **Inventory Batches:** `SELECT ... FROM "InventoryBatches" WHERE "Id" = ANY(...) ORDER BY "ProductionDate" ASC, "Id" ASC FOR UPDATE`
3. **Products:** `SELECT ... FROM "Products" WHERE "Id" = ANY(...) ORDER BY "Id" ASC FOR UPDATE`

### C. Double-Fulfillment Prevention
Double stock deduction is prevented at two independent layers:
1. **Application Logic:** Orders must strictly be in `Confirmed` status. If already `Completed`, the request is rejected with `409 Conflict`.
2. **PostgreSQL Partial Unique Index:**
   ```sql
   CREATE UNIQUE INDEX "IX_StockMovements_Order_Batch_Deduction" 
   ON "StockMovements" ("ReferenceOrderId", "InventoryBatchId") 
   WHERE "ReferenceOrderId" IS NOT NULL AND "InventoryBatchId" IS NOT NULL AND "MovementType" = 3;
   ```
   If two concurrent requests attempt to deduct stock from the same batch for the same order, PostgreSQL enforces this unique constraint and rejects the second transaction with `409 Conflict`.

---

## 10. Known Limitations (Milestone 1 Scope)

This repository represents Milestone 1 (M1) focusing on core inventory and fulfillment mechanics:
- **Authentication & Authorization:** M1 is an open internal API without JWT or Identity scaffolding.
- **Payment Gateway:** Payment processing is out of scope; orders are placed directly by internal staff or sales representatives.
- **Location Support:** Single-facility inventory only (`LOC_KDR_PROD`). Multi-warehouse routing is slated for future milestones.
- **UTC Date Evaluation:** Expiry validation compares UTC dates directly against PostgreSQL `date` columns without local WIB timezone shifts.

---

## 11. Documentation Links

For deeper technical deep-dives:
- [Architecture & Concurrency Guide](docs/architecture.md)
- [Interactive Swagger Demo Walkthrough](docs/demo-walkthrough.md)
- [Upwork Portfolio Case Study](docs/upwork-portfolio.md)
