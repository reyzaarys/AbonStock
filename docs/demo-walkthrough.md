# AbonStock — Interactive Swagger Demo Walkthrough

This guide provides step-by-step instructions for testing and demonstrating the **AbonStock API** via Swagger UI or curl.

---

## 1. Starting the Application & Accessing Swagger UI

### Step 1: Ensure PostgreSQL Container is Running
```bash
docker compose up -d
```

### Step 2: Run the API (Development Profile)
```bash
dotnet run --project src/AbonStock.Api --launch-profile http
```
The application will start listening on `http://localhost:5299`.

### Step 3: Open Swagger UI
Open your web browser and navigate to:
```
http://localhost:5299/swagger
```

> [!NOTE]
> The demo data seeder runs automatically upon startup only when `ASPNETCORE_ENVIRONMENT=Development`. In production or testing environments, the seeder does not execute.

---

## 2. Pre-Seeded Demo Dataset Status

Upon initial launch in Development mode, the database contains:

### Products
| ID | SKU | Name | Unit Price | Initial Stock |
| :---: | :--- | :--- | :---: | :---: |
| **1** | `ABON-SAPI-ORI-100` | Abon Sapi Original 100g | Rp 35.000 | 150 |
| **2** | `ABON-SAPI-PDS-100` | Abon Sapi Pedas 100g | Rp 37.500 | 80 |
| **3** | `ABON-AYAM-GRH-100` | Abon Ayam Gurih 100g | Rp 28.000 | 60 |

### Inventory Batches
| ID | Batch No | Product | Status | Physical Qty | Reserved Qty | Expiry |
| :---: | :--- | :--- | :---: | :---: | :---: | :---: |
| **1** | `BAT-202609-SO01` | Sapi Original | `Depleted` | 0 | 0 | Expired |
| **2** | `BAT-202610-SO02` | Sapi Original | `Released` | 100 | 20 | +6 Mos |
| **3** | `BAT-202610-SO03` | Sapi Original | `Released` | 50 | 0 | None (Non-expiring) |
| **4** | `BAT-202610-SP01` | Sapi Pedas | `Released` | 80 | 15 | +6 Mos |
| **5** | `BAT-202610-AG01` | Ayam Gurih | `Released` | 60 | 0 | +4 Mos |

### Pre-Seeded Orders & Statuses
| ID | Order Number | Customer | Status | Details |
| :---: | :--- | :--- | :---: | :--- |
| **1** | `ORD-20260915-DEMO-001` | Hendra Wijaya (Toko Berkah) | `Completed` | Historical fulfillment of Batch 1. |
| **2** | `ORD-20261018-DEMO-002` | Dewi Lestari (Resto Nusantara) | `Completed`* | Fulfilled during development verification. |
| **3** | `ORD-20261019-DEMO-003` | Siti Rahayu (Catering Berkah) | `Pending` | **Primary order for testing the full lifecycle!** |
| **4** | `ORD-20261017-DEMO-004` | Bambang Pratama | `Cancelled` | Audit trail of cancelled order with released reservation. |

*\* Note: Once an order reaches `Completed`, it cannot be re-fulfilled. Use Order 3 (or create a new order) to test confirmation and fulfillment.*

---

## 3. Scenario A: Complete Lifecycle (Pending → Confirmed → Completed)

In this scenario, you will take Order 3 (`ORD-20261019-DEMO-003`) through confirmation (FIFO batch reservation) and final fulfillment (stock deduction and ledger entry).

### Step 1: Inspect the Pending Order
- **Endpoint:** `GET /api/orders/3`
- **Click:** "Try it out" → `id: 3` → "Execute".
- **Response:** `200 OK`
```json
{
  "id": 3,
  "orderNumber": "ORD-20261019-DEMO-003",
  "customerName": "Siti Rahayu (Catering Berkah)",
  "status": "Pending",
  "totalAmount": 630000.00,
  "completedAtUtc": null,
  "items": [
    { "id": 4, "productSku": "ABON-SAPI-ORI-100", "quantity": 10, "unitPrice": 35000.00, "lineTotal": 350000.00 },
    { "id": 5, "productSku": "ABON-AYAM-GRH-100", "quantity": 10, "unitPrice": 28000.00, "lineTotal": 280000.00 }
  ]
}
```
*Note that no inventory is reserved yet.*

---

### Step 2: Confirm Order & Reserve Stock (FIFO Allocation)
- **Endpoint:** `POST /api/orders/3/confirm`
- **Request Body:** `{}` (or leave default empty JSON object)
- **Click:** "Execute".
- **Response:** `200 OK`
```json
{
  "id": 3,
  "orderNumber": "ORD-20261019-DEMO-003",
  "status": "Confirmed",
  "confirmedAtUtc": "2026-10-22T...",
  "completedAtUtc": null
}
```

#### What happened behind the scenes?
1. The system evaluated eligible released batches using FIFO: `ProductionDate ASC, Id ASC`.
2. For Sapi Original (10 units), `BAT-202610-SO02` was selected. `ReservedQuantity` increased from 0 to 10.
3. For Ayam Gurih (10 units), `BAT-202610-AG01` was selected. `ReservedQuantity` increased from 0 to 10.
4. Two active `OrderReservation` records were persisted.
5. Physical stock (`QuantityOnHand`) was **not** decremented yet.

You can verify the updated reservation status on Batch 5:
- **Endpoint:** `GET /api/inventory/batches/5`
- Result: `physicalQuantity: 60`, `reservedQuantity: 10`.

---

### Step 3: Fulfill Order (Atomic Stock Deduction & Ledger Entry)
- **Endpoint:** `POST /api/orders/3/fulfill`
- **Request Body:** `{}`
- **Click:** "Execute".
- **Response:** `200 OK`
```json
{
  "id": 3,
  "orderNumber": "ORD-20261019-DEMO-003",
  "status": "Completed",
  "confirmedAtUtc": "2026-10-22T...",
  "completedAtUtc": "2026-10-22T..."
}
```

#### What happened behind the scenes?
1. **Row Locks:** PostgreSQL row locks were acquired following the canonical sequence: `Order` → `Batches` → `Products`.
2. **Stock Decrements:**
   - `BAT-202610-SO02`: PhysicalQuantity decremented by 10; ReservedQuantity decremented by 10.
   - `BAT-202610-AG01`: PhysicalQuantity decremented by 10 (now 50); ReservedQuantity decremented by 10 (now 0).
3. **Product Stock:** Product 1 and Product 3 `QuantityOnHand` decremented by 10.
4. **Reservations:** OrderReservations transitioned from `Active` to `Fulfilled`.
5. **Ledger Records:** Two `OrderDeduction` records (`MovementType = 3`) were written to `StockMovements`.

---

### Step 4: Verify the Stock Movement Ledger
- **Endpoint:** `GET /api/inventory/movements?orderId=3`
- **Click:** "Execute".
- **Response:** `200 OK`
```json
{
  "items": [
    {
      "productId": 1,
      "inventoryBatchId": 2,
      "referenceOrderId": 3,
      "movementType": "OrderDeduction",
      "quantityChange": -10,
      "reason": "Order ORD-20261019-DEMO-003 fulfillment"
    },
    {
      "productId": 3,
      "inventoryBatchId": 5,
      "referenceOrderId": 3,
      "movementType": "OrderDeduction",
      "quantityChange": -10,
      "reason": "Order ORD-20261019-DEMO-003 fulfillment"
    }
  ],
  "totalCount": 2
}
```

---

## 4. Scenario B: Order Cancellation & Reservation Release

Demonstrates how cancelling a `Confirmed` order safely restores reserved inventory without stock leakage.

### Step 1: Create a New Order
- **Endpoint:** `POST /api/orders`
- **Request Body:**
```json
{
  "customerName": "Toko Oleh-Oleh Mandiri",
  "customerEmail": "mandiri@example.com",
  "items": [
    {
      "productId": 2,
      "quantity": 5
    }
  ]
}
```
- **Response:** `201 Created` with a new Order ID (e.g., ID `5`).

### Step 2: Confirm the Order
- **Endpoint:** `POST /api/orders/5/confirm`
- **Response:** `200 OK` (Status: `Confirmed`).
- Batch `BAT-202610-SP01` has its `ReservedQuantity` increased by 5.

### Step 3: Cancel the Order
- **Endpoint:** `POST /api/orders/5/cancel`
- **Request Body:**
```json
{
  "reason": "Customer cancelled due to delivery address change"
}
```
- **Response:** `200 OK`
```json
{
  "id": 5,
  "status": "Cancelled",
  "cancelledAtUtc": "2026-10-22T...",
  "cancellationReason": "Customer cancelled due to delivery address change"
}
```
- Active reservations are transitioned to `Released`, and batch `ReservedQuantity` is decremented by 5.

---

## 5. Scenario C: Quality Control & Batch Release

Demonstrates the 3-point QC gate before a newly received batch can be sold.

### Step 1: Receive a New Production Batch
- **Endpoint:** `POST /api/inventory/receipts`
- **Request Body:**
```json
{
  "productId": 1,
  "batchNo": "BAT-202610-SO99",
  "productionDate": "2026-10-22",
  "expiryDate": "2027-04-22",
  "quantity": 50,
  "reason": "Daily morning production run"
}
```
- **Response:** `201 Created` (Batch Status: `PendingRelease`, Version: e.g. `6510`).

### Step 2: Attempt Allocation Before Release (Expect Failure)
A batch in `PendingRelease` cannot be reserved by any order. Only `Released` batches are selected by the FIFO query.

### Step 3: Release Batch via Quality Control
- **Endpoint:** `POST /api/inventory/batches/{newBatchId}/release`
- **Request Body:**
```json
{
  "visualCheckPassed": true,
  "aromaCheckPassed": true,
  "packagingCheckPassed": true,
  "rowVersion": 6510
}
```
- **Response:** `200 OK` (Batch Status: `Released`). The batch is now ready for order allocation.

---

## 6. Scenario D: Testing Validations & Conflict Responses

Test how the API handles invalid operations, invariant violations, and concurrency conflicts:

### 1. Attempting Double Fulfillment (HTTP 409 Conflict)
- Try to fulfill an already `Completed` order (e.g., Order 1 or Order 3 after completing Scenario A).
- **Endpoint:** `POST /api/orders/1/fulfill`
- **Response:** `409 Conflict`
```json
{
  "type": "about:blank",
  "title": "Conflict",
  "status": 409,
  "detail": "Order 'ORD-20260915-DEMO-001' cannot be fulfilled because it is in status 'Completed'.",
  "instance": "/api/orders/1/fulfill"
}
```

---

### 2. Attempting to Fulfill a Pending Order (HTTP 409 Conflict)
- Create a new order (status `Pending`), then immediately attempt to call `fulfill`.
- **Response:** `409 Conflict` (Orders must be `Confirmed` first).

---

### 3. Exceeding Available Stock During Confirmation (HTTP 409 Conflict)
- Create an order for a quantity exceeding available physical stock:
```json
{
  "customerName": "Bulk Buyer",
  "items": [{ "productId": 3, "quantity": 9999 }]
}
```
- Call `POST /api/orders/{id}/confirm`.
- **Response:** `409 Conflict`
```json
{
  "title": "Conflict",
  "status": 409,
  "detail": "Insufficient available stock for product 'ABON-AYAM-GRH-100'. Requested: 9999, Available: 50."
}
```

---

### 4. Stale Optimistic Concurrency Token (HTTP 409 Concurrency Conflict)
- Call `PUT /api/products/1` with an outdated `rowVersion` (e.g., `1`):
```json
{
  "name": "Abon Sapi Original Stale",
  "unitPrice": 36000.00,
  "rowVersion": 1
}
```
- **Response:** `409 Conflict`
```json
{
  "title": "Concurrency Conflict",
  "status": 409,
  "detail": "The resource was modified by another operation. Please reload and retry."
}
```

---

## 7. Summary of Expected State Transitions

| Action | Order Status | Batch Physical Qty | Batch Reserved Qty | Product QuantityOnHand | StockMovement |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Create Order** | `Pending` | Unchanged | Unchanged | Unchanged | None |
| **Confirm Order** | `Confirmed` | Unchanged | $+N$ | Unchanged | None |
| **Fulfill Order** | `Completed` | $-N$ | $-N$ | $-N$ | `OrderDeduction (-N)` |
| **Cancel Confirmed Order** | `Cancelled` | Unchanged | $-N$ | Unchanged | None |
| **Receive Batch** | — | $+N$ (Pending) | $0$ | $+N$ | `Receipt (+N)` |
| **Release Batch** | — | Unchanged (Released) | $0$ | Unchanged | None |
