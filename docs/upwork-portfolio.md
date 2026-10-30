# AbonStock — Portfolio Case Study: Full-Stack Inventory & Order Fulfillment Engine

> **Project Classification:** Full-Stack Reference Implementation & Technical Portfolio
> **Target Audience:** Engineering Leads, Technical Recruiters, and Enterprise Clients evaluating Senior .NET / Backend Engineering capabilities.

---

## 1. Executive Summary

**AbonStock** is an architectural reference implementation and full-stack portfolio platform demonstrating inventory tracking and order fulfillment with **ASP.NET Core 8**, **Entity Framework Core 8**, **PostgreSQL 16**, and **React 18 + TypeScript + Vite**.

It addresses the operational challenges of artisanal food manufacturing (specifically Indonesian shredded meat / *Abon* production), where physical batch perishability, quality assurance gates, strict FEFO allocation, and concurrent order races demand absolute transactional consistency.

The system features:
- Canonical row-level locking (`SELECT ... FOR UPDATE`) in PostgreSQL to mitigate circular-wait deadlocks.
- Optimistic concurrency control using PostgreSQL's native `xmin` system column.
- Database-enforced duplicate-deduction prevention via partial unique indexes on order-batch deductions.
- Enterprise JWT Bearer authentication with role-based access control (Admin and Staff).
- A modern, responsive React 18 SPA with session restoration, protected routing, and role-aware UI.
- Comprehensive automated testing: 320 automated tests verified passed across backend (252) and frontend (68).

---

## 2. Business Challenge & Operational Context

In artisanal food production:
1. **Perishability & Recall Risks:** Food batches have distinct production and expiration dates. A single quality issue requires pinpointing the exact batch numbers that fulfilled specific customer shipments.
2. **Race Conditions & Overselling:** Multiple sales channels placing concurrent orders for limited artisan stock can easily cause overselling if transaction isolation is insufficient.
3. **Double Inventory Deductions:** Accidental network retries or concurrent clicks during order fulfillment frequently result in duplicate stock deductions.
4. **Deadlocks in Multi-Item Orders:** Inconsistent locking orders across multiple products and batches during concurrent fulfillment lead to cyclic-dependency database deadlocks.

---

## 3. Solution & Full-Stack Architecture

AbonStock is architected with a decoupled frontend and backend adhering to clean architecture principles:

```text
┌────────────────────────────────────────────────────────────────────────┐
│                        React 18 + Vite SPA Client                      │
│     (TypeScript, React Router v6, Lucide Icons, sessionStorage JWT)    │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ HTTP / JSON + Authorization: Bearer <token>
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                      ASP.NET Core 8 Web API Host                       │
│  ├── Middleware Pipeline: CORS, Authentication, Authorization, RFC7807 │
│  ├── Controllers: AuthController, Products, Inventory, Orders          │
│  └── Services: AuthService, ProductService, InventoryService, Orders   │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ EF Core 8 / Npgsql
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                          PostgreSQL 16 Engine                          │
│  ├── Canonical Lock Hierarchy (SELECT ... FOR UPDATE)                  │
│  ├── Optimistic Concurrency via PostgreSQL system column `xmin`        │
│  └── Idempotency Partial Unique Index ("IX_StockMovements_Order_Batch")│
└────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Key Backend Engineering Decisions

### 1. Deadlock-Resistant Canonical Lock Ordering
Generic ORM updates under concurrent multi-line orders easily cause deadlocks. To mitigate circular-wait deadlock conditions across concurrent fulfillment transactions, `OrderService` acquires row-level locks using PostgreSQL's `SELECT ... FOR UPDATE` in a strictly defined canonical order:
1. Lock Order record: `SELECT "Id" FROM "Orders" WHERE "Id" = {id} FOR UPDATE`
2. Lock candidate Batches: `SELECT ... FROM "InventoryBatches" ... ORDER BY "Id" ASC FOR UPDATE`
3. Lock associated Products: `SELECT ... FROM "Products" ... WHERE "Id" = ANY({productIds}) ORDER BY "Id" ASC FOR UPDATE`

Database row locks are acquired strictly in deterministic primary-key order (`ORDER BY "Id" ASC`), separating database locking mechanics from business allocation logic. Once row locks are secured, FEFO allocation ordering is applied separately in memory through `FefoAllocationHelper.SelectAndOrderEligibleBatches` (`ExpiryDate ASC, ProductionDate ASC, Id ASC`).

`OrderService` uses a consistent resource-locking hierarchy and deterministic primary-key ordering for batch and product locks to mitigate circular-wait deadlock risk across its coordinated transaction paths.

### 2. High-Performance Optimistic Concurrency via PostgreSQL `xmin`
Rather than adding extra timestamp columns, application triggers, or custom stored procedures, the schema maps an unsigned integer property (`uint Version`) directly to PostgreSQL’s native 32-bit `xmin` system column via EF Core's `IsRowVersion()`:
```csharp
builder.Entity<InventoryBatch>()
    .Property(b => b.Version)
    .IsRowVersion();
```
EF Core automatically appends `WHERE "xmin" = @pOriginalVersion` to update queries. Any concurrent update immediately throws `DbUpdateConcurrencyException`, translated by the API into `409 Conflict` (RFC 7807 `ProblemDetails`).

### 3. Database-Enforced Double-Fulfillment Defense
To guarantee idempotency against network retries or duplicate client clicks, a database-level partial unique index was designed:
```sql
CREATE UNIQUE INDEX "IX_StockMovements_Order_Batch_Deduction" 
ON "StockMovements" ("ReferenceOrderId", "InventoryBatchId") 
WHERE "ReferenceOrderId" IS NOT NULL 
  AND "InventoryBatchId" IS NOT NULL 
  AND "MovementType" = 3; -- OrderDeduction
```
This guarantees at the database storage engine layer that an order cannot deduct inventory from the same batch more than once.

### 4. Non-Blocking Advisory Locks for Seeder Safety
To support local development without race conditions when launching multiple API replicas, `AbonStockDataSeeder` uses PostgreSQL transaction-scoped advisory locks:
```sql
SELECT pg_try_advisory_xact_lock(0x41626F6E53746F63);
```
Only one startup instance acquires the lock to perform seeding, while other instances gracefully skip without waiting or crashing.

---

## 5. Inventory & Order Fulfillment Workflows

```text
[ Production ] ──> Receipt (PendingRelease) ──> QC Pass ──> Released
                                                                │
[ Sales ] ──> Create Order (Pending)                            ▼
                    │                                   FEFO Allocation
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

1. **3-Point Quality Assurance Gate:** Production batches initialize in `PendingRelease`. Only batches passing visual, aroma, and packaging checks transition to `Released`.
2. **Deterministic FEFO Reservation:** On order confirmation, available `Released` batches are allocated strictly by earliest production date (`ProductionDate ASC, Id ASC`).
3. **Quarantine & Write-Off Auditing:** Suspect batches can be quarantined to halt allocation while preserving audit trails. Damaged/spoiled batches are formally written off by Administrators, updating physical balances and writing immutable `WriteOff` movement ledger records.
4. **Real-Time Invariant Reconciliation:** An automated administrative audit engine validates that aggregate product stock precisely equals the sum of batch physical quantities, verifying zero negative inventory and zero unbacked reservations.
5. **Order Detail Inspection & Ledger Navigation:** Full operational visibility into order lifecycle states, itemized prices/subtotals, conditional timestamps, and seamless one-click routing from fulfilled orders directly to corresponding Stock Movement ledger entries.
6. **Immutable Audit Ledger:** Every stock adjustment, receipt, write-off, or fulfillment produces an immutable `StockMovement` audit record with before and after balances for both the batch and aggregate product inventory.

---

## 6. Enterprise Authentication & Role-Based Authorization

The application enforces an API-wide `FallbackPolicy` requiring JWT Bearer authentication on all endpoints unless explicitly decorated with `[AllowAnonymous]`.

### Security Implementation Highlights
- **Password Hashing:** `AuthService` hashes passwords using ASP.NET Core's salted `PasswordHasher<DemoUser>` (PBKDF2 with SHA-256). No plaintext passwords exist in configuration or database.
- **Fail-Fast Defense:** The API validates that `Jwt:SecretKey` is non-placeholder and at least 32 bytes (256 bits) on startup, halting boot if an insecure key is detected.
- **Anti-Enumeration Protection:** `POST /api/auth/login` returns an identical RFC 7807 `ProblemDetails` message (`"Invalid username or password."`) regardless of whether the username or password was incorrect.
- **Role Permissions Boundary:**
  - **Admin Only:** Product creation (`POST /api/products`), product editing (`PUT /api/products/{id}`), product deactivation (`PATCH /api/products/{id}/deactivate`), and stock adjustments (`POST /api/inventory/adjustments`).
  - **Staff & Admin:** Stock receipt, QC release inspection, batch catalog browsing, stock ledger queries, and all order operations (Create, Confirm, Cancel, Fulfill).

---

## 7. Frontend Implementation (React 18 + TypeScript + Vite)

The frontend client is engineered with modern React patterns:
- **Centralized Session Lifecycle (`AuthContext.tsx`):** Manages user state, token storage via `sessionStorage`, and authoritative profile verification (`GET /api/auth/me`) on page reload.
- **Protected Routing (`ProtectedRoute.tsx`):** Prevents flash of unauthenticated content by displaying a verifying loader until session validity is established, redirecting unauthorized users to `/login`.
- **Bearer Token Interceptor (`apiClient.ts`):** Dynamically injects `Authorization: Bearer <token>` into outgoing requests. Handles `401 Unauthorized` by clearing the session without causing login redirect loops, while preserving active sessions on `403 Forbidden`.
- **Role-Aware UI:** Administratively restricted actions (Add Product, Edit, Deactivate, Stock Adjust) are hidden or marked as "View only" for `Staff` users.

---

## 8. Testing Evidence & Verification Limits

The solution maintains extensive automated test suites across both backend and frontend layers:

| Suite | Runner / Framework | Tests | Status | Scope |
| :--- | :--- | :---: | :---: | :--- |
| **Backend Automated Tests** | xUnit 2.5.3 + .NET 8 (Unit & Integration) | **252** | **252 Passed (100%)** | 140 in-memory unit tests (FEFO, domain invariants, RBAC) & 112 PostgreSQL integration tests (concurrency, reservations, double-deduction prevention) |
| **Frontend Unit & Domain Tests** | Node.js Test Runner (`--experimental-strip-types`) | **68** | **68 Passed (100%)** | API client error handling, Bearer injection, session restoration, role-aware rendering, order itemization & ledger navigation, inventory audit & quarantine services |
| **Total Full-Stack Verification** | **xUnit + Node.js Runner** | **320** | **320 Passed (100%)** | **Zero test failures across entire full-stack solution** |

### Verification Boundaries
- **Automated Regression:** 320 automated tests (252 backend xUnit tests: 140 unit + 112 PostgreSQL integration; 68 frontend Node.js unit and domain tests) pass with zero failures.
- **Runtime Smoke Verification:** Host runtime verification confirmed API startup, Swagger UI availability (`/swagger`), JWT authentication issuance (`POST /api/auth/login`), and claims enforcement (`GET /api/auth/me`).
- **Browser Verification Scope:** Interactive browser workflows (order fulfillment, quarantine, ledger navigation) were verified via manual developer walkthroughs on the host environment; automated headless browser E2E test suites (e.g. Playwright/Cypress) are not part of the repository CI suite.

### Documented Architectural Limitations
To maintain technical credibility with senior interviewers and enterprise clients:
- **Authentication Scope:** Uses single-lifetime JWT tokens (8 hours) in browser `sessionStorage` without refresh token rotation. Demo accounts authenticate against in-memory salted PBKDF2 hashes (`Auth:Users`) rather than an external ASP.NET Core Identity database.
- **Facility Scope:** Designed for single-facility operations (`LOC_KDR_PROD`); multi-warehouse routing is out of scope for this milestone.
- **Date Handling:** Expiration policies evaluate UTC dates directly without local Indonesian (WIB) timezone conversion.
- **Browser Automation:** Interactive workflows were verified manually on host browsers; automated headless browser E2E suites (Playwright/Cypress) are not included in CI.

---

## 9. Core Technical Competencies Demonstrated

- **ASP.NET Core 8 & C# 12:** Minimal API design, RFC 7807 global exception handling, dependency injection, and clean architecture.
- **PostgreSQL & Concurrency Engineering:** Row-level locking (`FOR UPDATE`), deadlock mitigation via canonical ordering, advisory locks, partial unique constraints, and system column `xmin` optimistic concurrency tokens.
- **Inventory Domain Integrity:** Strict FEFO allocation, batch quarantine/write-off auditing, real-time invariant reconciliation, and immutable ledger movements.
- **Enterprise Security:** JWT Bearer authentication, PBKDF2 password hashing, fallback authorization policies, and role-based access control.
- **Frontend Development:** React 18, TypeScript (strict mode), Vite bundler, React Router v6, custom state hooks, and resilient HTTP client wrappers.
- **Automated Testing:** Test-driven development using xUnit, `WebApplicationFactory`, and native Node.js test runners (320 tests verified).

---

## 10. Ready-to-Use Upwork Portfolio Snippets

### Project Title & Overview (For Upwork Portfolio Item)
> **Title:** Full-Stack Inventory & Order Fulfillment Platform (.NET 8 & React 18)
> **Summary:** Engineered a concurrent-safe inventory tracking and order fulfillment system for artisanal food manufacturing using ASP.NET Core 8, EF Core 8, PostgreSQL 16, and React 18 + TypeScript. Designed deterministic FEFO batch allocation, deadlock-resistant canonical row locking (`SELECT ... FOR UPDATE`), optimistic concurrency with native PostgreSQL `xmin` tokens, and a partial unique index on order deductions to prevent duplicate batch decrements. Features real-time invariant audits, batch quarantine/write-off workflows, JWT Bearer authentication, and Admin/Staff role-based access control. Fully verified across 320 automated tests.

### Proposal Pitch Snippet (Applying for Senior .NET / Full-Stack Roles)
> *"I specialize in designing resilient backend architectures and full-stack systems in .NET 8 where transaction integrity and concurrency are critical design priorities. In my recent reference project, **AbonStock**, I engineered a multi-batch inventory fulfillment system with deadlock-resistant PostgreSQL row locking, strict FEFO allocation, and database partial unique indexes that prevent duplicate stock deductions against the same batch. The platform includes real-time invariant auditing, batch quarantine/write-off workflows, JWT authentication, Admin/Staff role-based authorization, and a modern React 18 TypeScript frontend. The entire codebase is verified by 320 automated tests (252 backend, 68 frontend). I would love to bring this level of architectural rigor and code quality to your project."*
