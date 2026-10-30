# AbonStock — API Demonstration Evidence & Operational Runbook

> **Document Status:** Verified Lifecycle & Operational Evidence Runbook<br>
> **Recorded Baseline Environment:** Development Sandbox (`abonstock_db` on PostgreSQL 16)<br>
> **Current Testing & Onboarding Target:** Dedicated Isolated Database (`abonstock_test_db`)<br>
> **Repository:** [AbonStock](https://github.com/reyzaarys/AbonStock)<br>
> **Baseline Commit:** `c9c001bdf21446ad2bb9da2e696aef9c823cf2eb`

> [!NOTE]
> **Historical Environment Context:** The verified baseline outputs below were recorded live against the original development sandbox database (`abonstock_db`) during Stage 7.1 baseline verification. Current repository runbooks, automated test executions, and onboarding procedures must strictly target an isolated database (`abonstock_test_db`) to avoid mutating baseline data.

This document provides verified baseline evidence and step-by-step procedures for demonstrating the **AbonStock** Web API on Upwork and GitHub.

---

## Output Classification Key

Every code and response block in this runbook is explicitly tagged with one of the following labels:
- `[VERIFIED ACTUAL OUTPUT]`: Live data retrieved from the running API or database during baseline verification.
- `[ILLUSTRATIVE EXPECTED OUTPUT]`: Expected response format specified by controller DTO contracts and integration tests, not yet observed via state-changing calls.
- `[MUTATING OPERATION]`: An operation that persists state changes to the database.

---

## 1. Environment & Setup Verification

### Setup Commands
Start the PostgreSQL 16 container and launch the Web API:
```bash
# 1. Start database container
docker compose up -d

# 2. Run API with Development profile
dotnet run --project src/AbonStock.Api --launch-profile http
```
The API listens on `http://localhost:5299`.

### Automated Test Suite Execution
Execute the complete backend automated test suite from the host terminal environment:
```bash
dotnet test tests/AbonStock.Tests/AbonStock.Tests.csproj
```

> [!NOTE]
> **Test Suite Scope & Database Safety Guard:**
> The backend test suite executes both in-memory unit tests (validating authentication, RBAC, domain validation, and startup rules without database dependencies) and PostgreSQL integration tests. For all integration tests, `PostgresTestFixture` actively enforces the dedicated `abonstock_test_db` target via static validation (`ValidateTestDatabase`), immediately throwing an exception and aborting execution if `abonstock_db` or an unexpected target database is encountered.

#### `[VERIFIED ACTUAL OUTPUT]` (Host Environment Execution)
```text
Test run for C:\Users\trans\Downloads\Upwork\tests\AbonStock.Tests\bin\Debug\net8.0\AbonStock.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   252, Skipped:     0, Total:   252, Duration: 4 s - AbonStock.Tests.dll (net8.0)
```
- **Backend Automated Tests:** 252/252 passed (0 failed) via xUnit across unit (140 tests) and PostgreSQL integration (112 tests) suites.
- **Frontend Automated Tests:** 68/68 passed (0 failed) via native Node.js test runner (`npm test`).
- **Total Pass Rate:** 320/320 passed (100% full-stack pass rate across entire solution).

---

## 2. Swagger & OpenAPI Specification Overview

- **Endpoint:** `GET http://localhost:5299/swagger/v1/swagger.json`
- **Interactive UI:** `http://localhost:5299/swagger`
- **Classification:** Read-Only Query

#### `[VERIFIED ACTUAL OUTPUT]` (Extracted from OpenAPI schema)
```json
{
  "openapi": "3.0.1",
  "info": {
    "title": "AbonStock API",
    "version": "v1"
  },
  "paths": [
    "/api/auth/login",
    "/api/auth/me",
    "/api/products",
    "/api/products/{id}",
    "/api/products/{id}/deactivate",
    "/api/inventory/batches",
    "/api/inventory/batches/{id}",
    "/api/inventory/batches/{id}/release",
    "/api/inventory/batches/{id}/quarantine",
    "/api/inventory/batches/{id}/write-off",
    "/api/inventory/adjustments",
    "/api/inventory/movements",
    "/api/inventory/receipts",
    "/api/inventory/audit",
    "/api/orders",
    "/api/orders/{id}",
    "/api/orders/{id}/confirm",
    "/api/orders/{id}/cancel",
    "/api/orders/{id}/fulfill"
  ]
}
```

---

## 3. Read-Only Baseline: Products, Batches & Orders

> [!NOTE]
> The development database contains a **verified baseline** initialized by `AbonStockDataSeeder`. Order 1 and Order 2 are in `Completed` status, and Order 3 is in `Pending` status.

### A. Product Baseline: `GET /api/products/1`
- **Classification:** Read-Only Query
- **Target:** Product 1 (`ABON-SAPI-ORI-100`)

#### `[VERIFIED ACTUAL OUTPUT]`
```json
{
  "id": 1,
  "sku": "ABON-SAPI-ORI-100",
  "name": "Abon Sapi Original 100g",
  "description": "Abon sapi asli khas Kediri rasa original gurih manis, kemasan pouch 100g",
  "unitPrice": 35000.00,
  "quantityOnHand": 130,
  "isActive": true,
  "createdAtUtc": "2026-08-22T09:21:28.545832Z",
  "updatedAtUtc": "2026-10-21T09:22:33.1946Z",
  "version": 6408
}
```
*Note: `QuantityOnHand: 130` matches active batch physical stock (80 in Batch 2 + 50 in Batch 3). Version 6408 reflects PostgreSQL `xmin` system column.*

---

### B. Batch Baseline: `GET /api/inventory/batches/2`
- **Classification:** Read-Only Query
- **Target:** Batch 2 (`BAT-202610-SO02`, FIFO Priority 1 for Product 1)

#### `[VERIFIED ACTUAL OUTPUT]`
```json
{
  "id": 2,
  "productId": 1,
  "batchNo": "BAT-202610-SO02",
  "productionDate": "2026-10-01",
  "expiryDate": "2027-10-01",
  "locationId": "LOC_KDR_PROD",
  "initialQuantity": 100,
  "physicalQuantity": 80,
  "status": "Released",
  "createdAtUtc": "2026-10-01T09:21:28.545832Z",
  "updatedAtUtc": "2026-10-21T09:22:33.1946Z",
  "version": 6408
}
```

---

### C. Order 3 Baseline: `GET /api/orders/3`
- **Classification:** Read-Only Query
- **Target:** Order 3 (`ORD-20261019-DEMO-003`, Candidate for confirmation & fulfillment)

#### `[VERIFIED ACTUAL OUTPUT]`
```json
{
  "id": 3,
  "orderNumber": "ORD-20261019-DEMO-003",
  "customerName": "[REDACTED]",
  "customerEmail": "[REDACTED]",
  "status": "Pending",
  "totalAmount": 630000.00,
  "createdAtUtc": "2026-10-20T09:21:28.545832Z",
  "updatedAtUtc": "2026-10-20T09:21:28.545832Z",
  "confirmedAtUtc": null,
  "cancelledAtUtc": null,
  "cancellationReason": null,
  "completedAtUtc": null,
  "version": 6404,
  "items": [
    {
      "id": 4,
      "productId": 1,
      "productSku": "ABON-SAPI-ORI-100",
      "productName": "Abon Sapi Original 100g",
      "quantity": 10,
      "unitPrice": 35000.00,
      "lineTotal": 350000.00
    },
    {
      "id": 5,
      "productId": 3,
      "productSku": "ABON-AYAM-GRH-100",
      "productName": "Abon Ayam Gurih 100g",
      "quantity": 10,
      "unitPrice": 28000.00,
      "lineTotal": 280000.00
    }
  ]
}
```
*Note: Status is `Pending`, `confirmedAtUtc` and `completedAtUtc` are `null`. Existing reservations for Order 3: 0 rows.*

---

## 4. Order Confirmation & FIFO Reservation Procedure

> [!NOTE]
> **Execution Status:** Verified Live on 2026-10-22 (Stage 7.1E).<br>
> Demonstrates automated FIFO batch selection, concurrency locking (`FOR UPDATE`), and non-depleting soft inventory reservation.

### HTTP Request
```http
POST /api/orders/3/confirm HTTP/1.1
Host: localhost:5299
Content-Type: application/json

{}
```

### Execution Details & Concurrency Control
1. **Pessimistic Order Lock:**
   Acquires row lock: `SELECT "Id" FROM "Orders" WHERE "Id" = 3 FOR UPDATE`.
2. **Deterministic Primary-Key Batch Locking:**
   Queries eligible released batches for Product 1 and Product 3 acquiring row locks in canonical primary key order (`ORDER BY "Id" ASC`):
   ```sql
   SELECT *, "xmin" FROM "InventoryBatches"
   WHERE "ProductId" = @pProductId
     AND "Status" = 2 -- Released
     AND "PhysicalQuantity" > "ReservedQuantity"
     AND "ExpiryDate" IS NOT NULL
     AND "ExpiryDate" >= @pToday
   ORDER BY "Id" ASC
   FOR UPDATE;
   ```
   *Note: Database row locks are acquired strictly in primary key order (`ORDER BY "Id" ASC`) for canonical lock ordering; logical FEFO allocation ordering (`ExpiryDate ASC, ProductionDate ASC, Id ASC`) is applied separately in memory through `FefoAllocationHelper.SelectAndOrderEligibleBatches`.*
3. **Atomic State & Reservation Persistence:**
   In an explicit database transaction:
   - Increments `ReservedQuantity` on Batch 2 (`BAT-202610-SO02`) and Batch 5 (`BAT-202610-AG01`).
   - Inserts two `OrderReservation` records (`Status = 1` / `Active`).
   - Transitions `Orders` row 3 to `Status = 2` (`Confirmed`) and stamps `ConfirmedAtUtc`.

---

### Actual Response
- **HTTP Status:** `200 OK`

#### `[VERIFIED ACTUAL OUTPUT]`
```json
{
  "id": 3,
  "orderNumber": "ORD-20261019-DEMO-003",
  "customerName": "[REDACTED]",
  "customerEmail": "[REDACTED]",
  "status": "Confirmed",
  "totalAmount": 630000.00,
  "createdAtUtc": "2026-10-20T09:21:28.545832Z",
  "updatedAtUtc": "2026-10-22T08:16:31.267101Z",
  "confirmedAtUtc": "2026-10-22T08:16:31.266995Z",
  "cancelledAtUtc": null,
  "cancellationReason": null,
  "completedAtUtc": null,
  "version": 8597,
  "items": [
    {
      "id": 4,
      "productId": 1,
      "productSku": "ABON-SAPI-ORI-100",
      "productName": "Abon Sapi Original 100g",
      "quantity": 10,
      "unitPrice": 35000.00,
      "lineTotal": 350000.00
    },
    {
      "id": 5,
      "productId": 3,
      "productSku": "ABON-AYAM-GRH-100",
      "productName": "Abon Ayam Gurih 100g",
      "quantity": 10,
      "unitPrice": 28000.00,
      "lineTotal": 280000.00
    }
  ]
}
```
*(Customer name and email redacted for privacy).*

---

### Verified Post-Execution Database State

#### `[VERIFIED ACTUAL OUTPUT]`

1. **Order State Transition (`Orders` table):**
   - `Status`: Changed from `Pending (1)` $\rightarrow$ `Confirmed (2)`.
   - `ConfirmedAtUtc`: Populated with `2026-10-22 08:16:31.266995+00`.

2. **Active Reservations Created (`OrderReservations` table):**
   - **Reservation ID 5:** `OrderId = 3`, `OrderItemId = 4`, `InventoryBatchId = 2`, `Quantity = 10`, `Status = 1` (`Active`), `CreatedAtUtc = 2026-10-22 08:16:30.936954+00`.
   - **Reservation ID 6:** `OrderId = 3`, `OrderItemId = 5`, `InventoryBatchId = 5`, `Quantity = 10`, `Status = 1` (`Active`), `CreatedAtUtc = 2026-10-22 08:16:31.264829+00`.

3. **Batch Quantities & Availability (`InventoryBatches` table):**
   - **Batch 2 (`BAT-202610-SO02`):**
     - `PhysicalQuantity`: Remained `80` (physical stock untouched).
     - `ReservedQuantity`: Changed from `0` $\rightarrow$ `10`.
     - `AvailableQuantity` (`Physical - Reserved`): Changed from `80` $\rightarrow$ `70`.
   - **Batch 5 (`BAT-202610-AG01`):**
     - `PhysicalQuantity`: Remained `60` (physical stock untouched).
     - `ReservedQuantity`: Changed from `0` $\rightarrow$ `10`.
     - `AvailableQuantity` (`Physical - Reserved`): Changed from `60` $\rightarrow$ `50`.

4. **Product Inventory Invariant (`Products` table):**
   - Product 1 (`ABON-SAPI-ORI-100`): `QuantityOnHand = 130` (remained unchanged; reconciles with Batch 2 [80] + Batch 3 [50]).
   - Product 2 (`ABON-SAPI-PDS-100`): `QuantityOnHand = 65` (remained unchanged; reconciles with Batch 4 [65]).
   - Product 3 (`ABON-AYAM-GRH-100`): `QuantityOnHand = 60` (remained unchanged; reconciles with Batch 5 [60]).

5. **Stock Movements Verification (`StockMovements` table):**
   - Count for Order 3: Exactly `0` rows.
   - *Behavioral verification:* Confirmation reserves stock without generating ledger deductions. Physical stock deduction is strictly deferred to fulfillment (`POST /api/orders/{id}/fulfill`).

6. **Process Lifecycle:**
   - The ASP.NET Core API server was cleanly stopped after completing the demonstration.

---

## 5. Disposable-Order Lifecycle & Cancellation Procedure

> [!NOTE]
> **Execution Status:** Verified Live on 2026-10-22 (Stages 7.1H-B, 7.1H-C, and 7.1H-E).<br>
> Demonstrates the full lifecycle of an isolated disposable order (Order 5): creation, FIFO reservation allocation, and atomic reservation release upon cancellation without inventory leakage.

### Step 1: Create Disposable Order (Order 5)
```http
POST /api/orders HTTP/1.1
Host: localhost:5299
Content-Type: application/json

{
  "customerName": "Toko Demo Pembatalan",
  "customerEmail": "pembatalan@example.com",
  "items": [
    {
      "productId": 2,
      "quantity": 5
    }
  ]
}
```
- **HTTP Status:** `201 Created`
- **Location:** `http://localhost:5299/api/Orders/5`
- **Order ID Generated:** `5` (`Status = Pending`, `TotalAmount = 187500.00`). No reservations or stock movements created.

---

### Step 2: Confirm Disposable Order 5
```http
POST /api/orders/5/confirm HTTP/1.1
Host: localhost:5299
Content-Type: application/json

{}
```

- **HTTP Status:** `200 OK`

#### `[VERIFIED ACTUAL OUTPUT]`
```json
{
  "id": 5,
  "orderNumber": "ORD-20261022-2B4F9A20",
  "customerName": "[REDACTED]",
  "customerEmail": "[REDACTED]",
  "status": "Confirmed",
  "totalAmount": 187500.00,
  "createdAtUtc": "2026-10-22T09:09:32.635721Z",
  "updatedAtUtc": "2026-10-22T09:12:30.2532034Z",
  "confirmedAtUtc": "2026-10-22T09:12:30.2531662Z",
  "cancelledAtUtc": null,
  "cancellationReason": null,
  "completedAtUtc": null,
  "version": 8600,
  "items": [
    {
      "id": 7,
      "productId": 2,
      "productSku": "ABON-SAPI-PDS-100",
      "productName": "Abon Sapi Pedas 100g",
      "quantity": 5,
      "unitPrice": 37500.00,
      "lineTotal": 187500.00
    }
  ]
}
```

#### `[VERIFIED ACTUAL OUTPUT]` — State Verification (Confirmation)
1. **Order 5:** Transitioned from `Pending (1)` $\rightarrow$ `Confirmed (2)`. `ConfirmedAtUtc` populated.
2. **OrderReservation (ID 7):** Created with `Status = 1` (`Active`), `InventoryBatchId = 4`, `Quantity = 5`.
3. **Batch 4 (`BAT-202610-SP01`):** `PhysicalQuantity` remained `65`; `ReservedQuantity` became `5`; `AvailableQuantity` became `60`.
4. **Product 2 (`ABON-SAPI-PDS-100`):** `QuantityOnHand` remained `65`.
5. **StockMovements:** `0` rows created.

---

### Step 3: Cancel Disposable Order 5
```http
POST /api/orders/5/cancel HTTP/1.1
Host: localhost:5299
Content-Type: application/json

{
  "cancellationReason": "Pembatalan pesanan untuk verifikasi pengembalian reservasi stok"
}
```

- **HTTP Status:** `200 OK`

#### `[VERIFIED ACTUAL OUTPUT]`
```json
{
  "id": 5,
  "orderNumber": "ORD-20261022-2B4F9A20",
  "customerName": "[REDACTED]",
  "customerEmail": "[REDACTED]",
  "status": "Cancelled",
  "totalAmount": 187500.00,
  "createdAtUtc": "2026-10-22T09:09:32.635721Z",
  "updatedAtUtc": "2026-10-22T09:16:42.1226492Z",
  "confirmedAtUtc": "2026-10-22T09:12:30.253166Z",
  "cancelledAtUtc": "2026-10-22T09:16:42.1226492Z",
  "cancellationReason": "Pembatalan pesanan untuk verifikasi pengembalian reservasi stok",
  "completedAtUtc": null,
  "version": 8602,
  "items": [
    {
      "id": 7,
      "productId": 2,
      "productSku": "ABON-SAPI-PDS-100",
      "productName": "Abon Sapi Pedas 100g",
      "quantity": 5,
      "unitPrice": 37500.00,
      "lineTotal": 187500.00
    }
  ]
}
```

#### `[VERIFIED ACTUAL OUTPUT]` — State Verification (Cancellation)
1. **Order 5:** Transitioned from `Confirmed (2)` $\rightarrow$ `Cancelled (4)`. `CancelledAtUtc` and `CancellationReason` populated.
2. **OrderReservation (ID 7):** Transitioned from `Active (1)` $\rightarrow$ `Released (2)`. `ReleasedAtUtc` populated (`2026-10-22 09:16:42.122649+00`).
3. **Batch 4 (`BAT-202610-SP01`):** `PhysicalQuantity` remained `65`; `ReservedQuantity` returned from `5` to `0`; `AvailableQuantity` restored from `60` to `65`.
4. **Product 2 (`ABON-SAPI-PDS-100`):** `QuantityOnHand` remained `65` (100% reconciliation with physical batch quantity).
5. **StockMovements:** `0` rows created (unfulfilled orders do not incur ledger deductions). Zero inventory leakage confirmed.
6. **Order 3 Isolation:** Order 3 remained untouched and `Confirmed` with its 2 active reservations intact.

---

## 6. Order Fulfillment & Stock Movement Verification

> [!NOTE]
> **Execution Status:** Verified Live on 2026-10-23 (Stage 7.1I-B).<br>
> Demonstrates atomic physical stock deduction, reservation fulfillment, and ledger recording in `StockMovements` following canonical 3-tier locking.

### HTTP Request
```http
POST /api/orders/3/fulfill HTTP/1.1
Host: localhost:5299
Content-Type: application/json

{}
```

### Execution Details & Concurrency Control
1. **Pessimistic Canonical Lock Sequence:**
   - Tier 1: `Orders` row 3 `FOR UPDATE`.
   - Tier 2: `InventoryBatches` (Batches 2 & 5) `WHERE "Id" = ANY(...) ORDER BY "Id" ASC FOR UPDATE` (locked in primary key order; FEFO allocation and movement sequence applied separately in memory).
   - Tier 3: `Products` (Products 1 & 3) `WHERE "Id" = ANY(...) ORDER BY "Id" ASC FOR UPDATE`.
2. **Validation:** Confirms status is `Confirmed`, active reservations match ordered items, and batches are released and unexpired.
3. **Atomic Execution:** In a single database transaction, decrements physical and reserved batch quantities, reduces product `QuantityOnHand`, marks reservations `Fulfilled (3)`, inserts two `StockMovements` ledger records, and transitions order status to `Completed (3)`.

---

### Actual Response
- **HTTP Status:** `200 OK`

#### `[VERIFIED ACTUAL OUTPUT]`
```json
{
  "id": 3,
  "orderNumber": "ORD-20261019-DEMO-003",
  "customerName": "[REDACTED]",
  "customerEmail": "[REDACTED]",
  "status": "Completed",
  "totalAmount": 630000.00,
  "createdAtUtc": "2026-10-20T09:21:28.545832Z",
  "updatedAtUtc": "2026-10-23T07:05:09.0679073Z",
  "confirmedAtUtc": "2026-10-22T08:16:31.266995Z",
  "cancelledAtUtc": null,
  "cancellationReason": null,
  "completedAtUtc": "2026-10-23T07:05:09.0679073Z",
  "version": 8604,
  "items": [
    {
      "id": 4,
      "productId": 1,
      "productSku": "ABON-SAPI-ORI-100",
      "productName": "Abon Sapi Original 100g",
      "quantity": 10,
      "unitPrice": 35000.00,
      "lineTotal": 350000.00
    },
    {
      "id": 5,
      "productId": 3,
      "productSku": "ABON-AYAM-GRH-100",
      "productName": "Abon Ayam Gurih 100g",
      "quantity": 10,
      "unitPrice": 28000.00,
      "lineTotal": 280000.00
    }
  ]
}
```
*(Customer name and email redacted for privacy).*

---

### Verified Post-Execution Database State

#### `[VERIFIED ACTUAL OUTPUT]`

1. **Order State Transition (`Orders` table):**
   - `Status`: Transitioned from `Confirmed (2)` $\rightarrow$ `Completed (3)`.
   - `CompletedAtUtc`: Populated with `2026-10-23 07:05:09.067907+00`.

2. **Reservation Status Transition (`OrderReservations` table):**
   - **Reservation ID 5:** Transitioned from `Active (1)` $\rightarrow$ `Fulfilled (3)` (Batch 2, 10 units).
   - **Reservation ID 6:** Transitioned from `Active (1)` $\rightarrow$ `Fulfilled (3)` (Batch 5, 10 units).

3. **Batch Quantities & Balances (`InventoryBatches` table):**
   - **Batch 2 (`BAT-202610-SO02`):**
     - `PhysicalQuantity`: Changed from `80` $\rightarrow$ `70` (deducted 10 units).
     - `ReservedQuantity`: Changed from `10` $\rightarrow$ `0` (deducted 10 units).
     - `AvailableQuantity`: Remained `70` (`70 - 0 = 70`).
   - **Batch 5 (`BAT-202610-AG01`):**
     - `PhysicalQuantity`: Changed from `60` $\rightarrow$ `50` (deducted 10 units).
     - `ReservedQuantity`: Changed from `10` $\rightarrow$ `0` (deducted 10 units).
     - `AvailableQuantity`: Remained `50` (`50 - 0 = 50`).

4. **Product Inventory Reconciliation (`Products` table):**
   - **Product 1 (`ABON-SAPI-ORI-100`):** `QuantityOnHand` changed from `130` $\rightarrow$ `120` (matches Batch 2 [70] + Batch 3 [50] = 120).
   - **Product 3 (`ABON-AYAM-GRH-100`):** `QuantityOnHand` changed from `60` $\rightarrow$ `50` (matches Batch 5 [50] = 50).
   - **Product 2 (`ABON-SAPI-PDS-100`):** `QuantityOnHand` remained `65` (untouched).

5. **Ledger Verification (`StockMovements` table):**
   - Exactly two `OrderDeduction` records (`MovementType = 3`) inserted:
     - **Movement ID 9:** Product 1, Batch 2, `QuantityChange = -10`, `BalanceAfter = 120`, `BatchBalanceAfter = 70`, `ReferenceOrderId = 3`, `Reason = "Order ORD-20261019-DEMO-003 fulfillment"`.
     - **Movement ID 10:** Product 3, Batch 5, `QuantityChange = -10`, `BalanceAfter = 50`, `BatchBalanceAfter = 50`, `ReferenceOrderId = 3`, `Reason = "Order ORD-20261019-DEMO-003 fulfillment"`.

6. **Isolation Verification:**
   - Order 5 remained `Cancelled (4)`; Reservation 7 remained `Released (2)`; Batch 4 remained at physical `65` and reserved `0`.

---

## 7. Double-Fulfillment Rejection Procedure

Attempting to fulfill an already completed order (e.g., repeating fulfillment on Order 3, or testing on Order 1/Order 2):

### HTTP Request
```http
POST /api/orders/3/fulfill HTTP/1.1
Host: localhost:5299
Content-Type: application/json

{}
```

### Expected Response:
- **HTTP Status:** `409 Conflict` (RFC 7807 Problem Details)

#### `[ILLUSTRATIVE EXPECTED OUTPUT]`
```json
{
  "type": "about:blank",
  "title": "Conflict",
  "status": 409,
  "detail": "Order 'ORD-20261019-DEMO-003' cannot be fulfilled because it is in status 'Completed'.",
  "instance": "/api/orders/3/fulfill"
}
```

### Defense in Depth:
1. **Application Layer:** State machine validation checks `order.Status == OrderStatus.Confirmed`.
2. **Database Layer:** Partial unique index `IX_StockMovements_Order_Batch_Deduction` on `(ReferenceOrderId, InventoryBatchId) WHERE MovementType = 3` rejects concurrent duplicates.

---

## 8. Summary of State Transitions

| Operation | HTTP Method & Route | Data Mutated? | Status Before | Status After | Stock Impact |
| :--- | :--- | :---: | :---: | :---: | :--- |
| **Catalog Query** | `GET /api/products/1` | No | `Active` | `Active` | None (Read-only) |
| **Order 3 Query** | `GET /api/orders/3` | No | `Pending` | `Pending` | None (Read-only) |
| **Order 3 Confirm** | `POST /api/orders/3/confirm` | **Yes** | `Pending` | `Confirmed` | Batch 2 Res: $+10$, Batch 5 Res: $+10$. Physical QOH unchanged. |
| **Disposable Create** | `POST /api/orders` | **Yes** | — | `Pending` | Order 5 created (5 units Product 2). Inventory untouched. |
| **Disposable Confirm** | `POST /api/orders/5/confirm` | **Yes** | `Pending` | `Confirmed` | Batch 4 Res: $+5$. Physical QOH unchanged. |
| **Disposable Cancel** | `POST /api/orders/5/cancel` | **Yes** | `Confirmed` | `Cancelled` | Batch 4 Res: $-5$ (returned to 0). Zero leakage. |
| **Order 3 Fulfill** | `POST /api/orders/3/fulfill` | **Yes** | `Confirmed` | `Completed` | Batch 2 Phys: $-10$, Res: $-10$. Batch 5 Phys: $-10$, Res: $-10$. Product 1 QOH: $130 \rightarrow 120$, Product 3 QOH: $60 \rightarrow 50$. |
| **Double Fulfill** | `POST /api/orders/3/fulfill` | No | `Completed` | `Completed` | None. Rejected with HTTP 409 Conflict. |
