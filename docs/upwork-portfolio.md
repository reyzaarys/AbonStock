# AbonStock — Portfolio Case Study: Inventory & Order Fulfillment Engine

> **Project Classification:** Full-stack Reference Implementation & Technical Portfolio  
> **Target Audience:** Engineering Leads, Technical Recruiters, and Upwork Enterprise Clients evaluating Senior .NET / Backend Engineering capabilities.

---

## 1. Executive Summary

**AbonStock** is a production-grade backend service engineered with **ASP.NET Core 8**, **Entity Framework Core 8**, and **PostgreSQL 16**. It solves complex inventory management, batch-level quality control, and transactional order fulfillment for artisanal food manufacturing businesses (specifically Indonesian shredded meat / *Abon* producers).

The core objective of this project was to implement a rock-solid, concurrent-safe inventory reservation and fulfillment engine that defends against data corruption, overselling, double deductions, and database deadlocks under high concurrency.

---

## 2. Business Challenge & Technical Solution

### The Business Challenge
In artisanal food manufacturing:
1. **Perishability & Batch Recalls:** Products are produced in physical batches with distinct production and expiration dates. A single quality defect requires knowing exactly which customer orders received items from that specific batch.
2. **Race Conditions & Overselling:** Multiple sales reps or digital channels placing orders simultaneously can easily over-commit limited batch stock if isolation levels are weak.
3. **Double Inventory Deductions:** Accidental network retries or concurrent clicks during order fulfillment frequently lead to duplicate inventory deductions.
4. **Deadlocks in Multi-Item Orders:** When concurrent transactions lock inventory batches and order rows in inconsistent sequences, database deadlocks occur.

### The Engineered Solution
- **Strict FIFO Batch Allocation:** Orders deterministically reserve inventory from the oldest available production batches using `ORDER BY "ProductionDate" ASC, "Id" ASC`.
- **Three-Tier Canonical Lock Hierarchy:** A global locking sequence (`Orders` → `InventoryBatches` → `Products`) prevents circular-wait deadlocks.
- **Two-Layer Double-Deduction Prevention:** Application state validation coupled with a PostgreSQL partial unique index on `(ReferenceOrderId, InventoryBatchId)` for order deduction movements.
- **Optimistic Concurrency Control:** System-level tracking using PostgreSQL's native `xmin` row transaction tokens mapped to EF Core's `IsRowVersion()`.
- **Audit-Compliant Stock Movement Ledger:** Continuous before/after balance tracking for both the individual production batch and aggregate product on-hand inventory.

---

## 3. Technology Stack

- **Runtime & Language:** .NET 8 (C# 12)
- **Framework:** ASP.NET Core Web API with RFC 7807 Problem Details
- **Data Access:** Entity Framework Core 8.0.8, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.8
- **Database Engine:** PostgreSQL 16
- **Containerization:** Docker Compose
- **API Documentation:** OpenAPI / Swagger (Swashbuckle 6.6.2)
- **Testing Frameworks:** xUnit 2.5.3, `Microsoft.AspNetCore.Mvc.Testing` 8.0.8

---

## 4. Key Verifiable Technical Contributions

### 1. Zero-Deadlock Canonical Lock Ordering
Rather than relying on generic ORM-level locking, `OrderService` explicitly orchestrates row-level locks using PostgreSQL's `SELECT ... FOR UPDATE` in a deterministic order:
1. Lock Order row: `SELECT "Id" FROM "Orders" WHERE "Id" = {id} FOR UPDATE`
2. Lock candidate Batches: `SELECT ... FROM "InventoryBatches" ... ORDER BY "ProductionDate" ASC, "Id" ASC FOR UPDATE`
3. Lock associated Products: `SELECT ... FROM "Products" ... ORDER BY "Id" ASC FOR UPDATE`

This guarantees that whether an order is being confirmed, cancelled, or fulfilled, all concurrent operations acquire locks in the exact same direction, eliminating deadlocks.

### 2. High-Performance Optimistic Concurrency with PostgreSQL `xmin`
Instead of adding extra timestamp columns and triggers, the schema maps `uint Version` directly to PostgreSQL’s internal 32-bit `xmin` system column. EF Core automatically enforces version verification during updates (`WHERE "xmin" = @originalVersion`), catching stale reads without additional storage overhead.

### 3. Database-Enforced Double-Fulfillment Defense
To ensure idempotency even under network retry anomalies, a partial unique index was designed:
```sql
CREATE UNIQUE INDEX "IX_StockMovements_Order_Batch_Deduction" 
ON "StockMovements" ("ReferenceOrderId", "InventoryBatchId") 
WHERE "ReferenceOrderId" IS NOT NULL 
  AND "InventoryBatchId" IS NOT NULL 
  AND "MovementType" = 3; -- OrderDeduction
```
This guarantees at the storage engine level that an order cannot deduct inventory from the same batch twice.

### 4. Non-Blocking Advisory Locks for Seeding
To support seamless local developer onboarding without race conditions when spinning up multiple API replicas, `AbonStockDataSeeder` utilizes PostgreSQL application advisory transaction locks:
```sql
SELECT pg_try_advisory_xact_lock(0x41626F6E53746F63);
```
This allows exactly one instance to seed demo data while other startup instances gracefully bypass the process without waiting or crashing.

---

## 5. Automated Test Evidence

The repository maintains **100% test pass rate** across unit and integration test suites:

```text
Test run for AbonStock.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Passed!  - Failed: 0, Passed: 113, Skipped: 0, Total: 113, Duration: 3 s
```

### Breakdown of 113 Automated Tests:
- **40 Unit Tests:** Covers business validation rules, price calculations, order line integrity, negative price rejection, and QC release criteria.
- **73 PostgreSQL Integration Tests:** Runs against real PostgreSQL test instances verifying:
  - Atomic multi-batch fulfillment.
  - Concurrency conflict handling under stale `xmin` tokens.
  - Transaction rollbacks upon invariant violations.
  - Ledger continuity and running balance accuracy.
  - Seeder idempotency across repeated execution.

---

## 6. Project Scope & Realism Disclaimer

> [!NOTE]
> This repository is a technical demonstration developed to exhibit architectural mastery of ASP.NET Core, EF Core, and PostgreSQL concurrency patterns. While the business domain (Abon manufacturing) is modeled on real artisanal food operations in East Java, Indonesia, this project was developed as a clean-slate portfolio implementation and has not yet been deployed to commercial live production.

---

## 7. Ready-to-Use Upwork Proposal & Portfolio Snippets

### Short Project Summary (For Upwork Portfolio Item)
> **Title:** Production-Grade Inventory & Order Fulfillment Web API (.NET 8 & PostgreSQL)  
> **Description:** Engineered an artisanal food manufacturing inventory and order fulfillment engine using ASP.NET Core 8, EF Core 8, and PostgreSQL 16. Designed strict FIFO batch allocation, deterministic row locking (`FOR UPDATE`) to prevent deadlocks, optimistic concurrency with PostgreSQL native `xmin` tokens, and partial unique indexes to guarantee zero double-deductions. Verified with 113 automated unit and integration tests.

### Proposal Pitch Snippet (Applying for .NET / EF Core / Concurrency Roles)
> *"I specialize in designing resilient backend systems in .NET 8 where transaction integrity and concurrency cannot be compromised. In my recent project, **AbonStock**, I engineered a multi-batch inventory fulfillment system with PostgreSQL row-level locking, strict FIFO allocation, and partial unique index constraints that physically prevent duplicate stock deductions. The entire architecture is thoroughly verified by a 113-test automated suite (including multi-threaded concurrency integration tests). I would love to bring this level of rigor and clean architecture to your project."*
