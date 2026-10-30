# AbonStock — Full-Stack Inventory & Order Fulfillment System

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-18.3-61dafb.svg)](https://react.dev/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.5-blue.svg)](https://www.typescriptlang.org/)
[![Vite](https://img.shields.io/badge/Vite-5.4-646cff.svg)](https://vitejs.dev/)
[![Entity Framework Core](https://img.shields.io/badge/EF%20Core-8.0-purple.svg)](https://learn.microsoft.com/en-us/ef/core/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue.svg)](https://www.postgresql.org/)
[![Automated Tests](https://img.shields.io/badge/Automated%20Tests-320%20Passed-brightgreen.svg)]()
[![License](https://img.shields.io/badge/License-MIT-green.svg)]()

A full-stack, concurrent-safe inventory tracking and order fulfillment platform engineered for artisanal food manufacturers (specifically Indonesian shredded meat / *Abon* producers). Built with an **ASP.NET Core 8 Web API**, **Entity Framework Core 8**, **PostgreSQL 16**, and a modern **React 18 + TypeScript + Vite** Single Page Application.

> 📍 **Quick Navigation:**
> - [1. Project Overview & Business Domain](#1-project-overview--business-domain)
> - [2. Full-Stack Architecture](#2-full-stack-architecture)
> - [3. Verified Features & Business Workflows](#3-verified-features--business-workflows)
> - [4. Authentication & Role-Based Authorization](#4-authentication--role-based-authorization)
> - [5. Prerequisites & Local Installation](#5-prerequisites--local-installation)
> - [6. Running Automated Tests (320 Passed)](#6-running-automated-tests)
> - [7. Verified Test Metrics](#7-verified-test-metrics-full-verification)
> - [8. Technical & Security Limitations](#8-technical--security-limitations)
> - [9. Documentation Directory & Portfolio Case Study](#9-documentation-directory)

---

## 1. Project Overview & Business Domain

Artisanal food manufacturing involves short shelf-life batches, strict quality control requirements, and high transaction volumes during distribution peaks. Small-to-medium producers face severe operational risks:
- **Overselling & Race Conditions:** Concurrent orders competing for the same limited batch stock leading to negative inventory or over-commitment.
- **Traceability & Recall Inability:** Inability to track which production batch fulfilled which customer order, creating critical compliance and food safety recall risks.
- **Duplicate Stock Deductions:** Network retries or accidental double-clicks during fulfillment leading to duplicate physical deductions.
- **Inventory Expiration Waste:** Non-deterministic batch selection causing older batches to expire in the warehouse while newer inventory is shipped.

**AbonStock** resolves these operational challenges through a concurrent-safe, auditable architecture:
- **Strict FEFO Allocation:** Customer orders reserve inventory deterministically from eligible batches ordered by `ExpiryDate ASC, ProductionDate ASC, Id ASC`.
- **Pessimistic & Optimistic Concurrency:** Canonical PostgreSQL row-level locks (`SELECT ... FOR UPDATE`) mitigate circular-wait deadlocks, combined with PostgreSQL system column `xmin` concurrency tokens for stale read detection.
- **Database-Level Double-Deduction Prevention:** Application-level order state guards reinforced by a PostgreSQL partial unique index on `StockMovements`.
- **Immutable Stock Ledger:** Every physical adjustment or fulfillment writes an immutable movement record with before/after balances for both the individual batch and aggregate product inventory.
- **Role-Based Security:** Enterprise JWT Bearer authentication enforcing distinct operational permissions for **Administrator** and **Warehouse Staff** roles across both backend endpoints and frontend interfaces.

---

## 2. Full-Stack Architecture

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

### Directory Structure

```text
AbonStock/
├── .env.example                     # Environment template for local Docker PostgreSQL
├── .gitignore                       # Repository-wide ignore rules (.NET, VS, secrets, logs)
├── AbonStock.sln                    # Visual Studio solution file
├── docker-compose.yml               # PostgreSQL 16 container definition
├── docs/                            # Architectural guides & portfolio runbooks
│   ├── architecture.md              # In-depth concurrency, locking & relational schema
│   ├── demo-evidence.md             # Baseline operational runbook & endpoint evidence
│   ├── demo-walkthrough.md          # Step-by-step Swagger UI verification guide
│   └── upwork-portfolio.md          # Upwork portfolio case study & technical breakdown
├── src/
│   └── AbonStock.Api/               # ASP.NET Core 8 Web API project
│       ├── Common/                  # Exceptions (NotFound, Conflict, Validation), PagedResult
│       ├── Controllers/             # Products, Inventory, Orders, Auth controllers
│       ├── Data/                    # AbonStockDbContext, AbonStockDataSeeder, Migrations
│       ├── Dtos/                    # Strongly typed request/response records
│       ├── Entities/                # Relational domain models
│       ├── Middleware/              # GlobalExceptionHandler (RFC 7807 ProblemDetails)
│       ├── Services/                # Business logic, lock ordering, JWT token generation
│       ├── appsettings.json         # Base configuration (with safe placeholders)
│       ├── appsettings.Development.json # Development CORS policy (localhost:5173, localhost:3000)
│       └── Program.cs               # Host configuration, JWT authentication, and DI pipeline
├── tests/
│   └── AbonStock.Tests/             # Automated test suite (xUnit)
│       ├── Common/                  # Test fixtures & ordering helpers
│       ├── Integration/             # PostgreSQL database integration test suite
│       └── Unit/                    # In-memory unit and domain test suite (Auth, RBAC, CORS, Domain, FEFO)
└── frontend/                        # React 18 + TypeScript + Vite Single Page Application
    ├── .env.example                 # Frontend environment template (VITE_API_BASE_URL)
    ├── .gitignore                   # Ignore rules for node_modules, dist, and local .env
    ├── package.json                 # Dependencies and npm/pnpm scripts
    ├── tsconfig.json                # Strict TypeScript configuration
    ├── vite.config.ts               # Vite configuration and path aliases
    └── src/
        ├── app/                     # App.tsx (AuthProvider wrapper), router.tsx
        ├── components/              # AppLayout, Navbar, ProtectedRoute, common modal/table UI
        ├── context/                 # AuthContext.tsx (sessionStorage token lifecycle & RBAC)
        ├── features/                # auth (LoginPage), inventory, orders, products
        ├── lib/                     # apiClient.ts (Bearer token injection, 401/403 handlers)
        ├── services/                # authService, productsService, inventoryService, ordersService
        ├── styles/                  # index.css (modular responsive design system)
        └── types/                   # TypeScript interfaces (auth, common, inventory, orders, products)
```

---

## 3. Verified Features & Business Workflows

### A. Product Catalog Management
- SKU uniqueness enforcement and normalization.
- Active/Inactive status toggles and server-authoritative pricing.
- Real-time aggregate inventory tracking (`QuantityOnHand`).
- Product creation, modification, and deactivation restricted strictly to **Admin** role.

### B. Batch Tracking & 3-Point Quality Gate
- Physical production batches initialized in `PendingRelease` status.
- Mandatory 3-point QC gate (`VisualCheckPassed`, `AromaCheckPassed`, `PackagingCheckPassed`) before transitioning to `Released`.
- Per-product batch number uniqueness (`IX_InventoryBatches_ProductId_BatchNo`).
- QC inspection and stock receipt accessible to both **Staff** and **Admin**.

### C. Stock Adjustments, Quarantine & Write-Off Operations
- **Physical Adjustments:** Correct stock counts requiring documented audit reasons; guarded so physical quantity cannot fall below active reserved quantity.
- **Batch Quarantine:** Transition suspect or damaged batches to `Quarantined` status (`POST /api/inventory/batches/{id}/quarantine`). Prevents allocation while preserving physical count and audit history.
- **Batch Write-Off:** Formally write off damaged, expired, or spoiled units (`POST /api/inventory/batches/{id}/write-off`, **Admin Only**). Deducts physical stock, synchronizes product `QuantityOnHand`, transitions to `Depleted` if zero, and logs an immutable `WriteOff` movement.

### D. Real-Time Inventory Invariant Audit Engine
- **Automated Ledger Auditing:** Real-time reconciliation engine (`GET /api/inventory/audit`, **Admin Only**) scanning the entire catalog or specific products.
- **Invariant Guarantees Verified:**
  1. $\sum \text{Batch Physical Quantities} \equiv \text{Product QuantityOnHand}$
  2. $\text{Batch ReservedQuantity} \le \text{Batch PhysicalQuantity}$
  3. $\text{Product QuantityOnHand} \ge 0$ (non-negative guarantee)
- Instant discrepancy reporting detailing expected vs. actual balances and batch counts.

### E. Order Lifecycle, Deterministic FEFO Allocation & Deep Inspection
- **Creation (`Pending`):** Draft orders calculate line items and totals server-side; no stock is locked or reserved at draft creation.
- **Confirmation (`Confirmed`):** Acquires pessimistic row locks on candidate `Released` batches in primary-key order (`ORDER BY "Id" ASC FOR UPDATE`), deterministically allocates reservations in memory using strict FEFO (`FefoAllocationHelper`), and creates `OrderReservation` records.
- **Cancellation (`Cancelled`):** Atomically rolls back reservations without losing audit continuity.
- **Fulfillment (`Completed`):** Atomically deducts physical stock, clears reservations, transitions status to `Completed`, and writes immutable `OrderDeduction` entries to `StockMovements`.
- **Order Detail Inspection & Ledger Navigation:** Full modal transparency for any order status with SKU itemization, unit price, quantity, line total, order total, conditional completion/cancellation timestamps, and contextual one-click navigation to the Stock Movements ledger (`/inventory?tab=movements`).

### F. Concurrency & Idempotency Safeguards
- **PostgreSQL `xmin` Optimistic Concurrency:** System column `xmin` mapped to entity versions (`uint Version`), detecting concurrent stale updates with `409 Conflict`.
- **Three-Tier Canonical Lock Hierarchy:** `OrderService` uses a consistent resource-locking hierarchy (`Orders` $\rightarrow$ `InventoryBatches` $\rightarrow$ `Products`) and deterministic primary-key ordering (`ORDER BY "Id" ASC`) for batch and product locks to mitigate circular-wait deadlock risk across its coordinated transaction paths. FEFO allocation ordering is applied separately in memory through `FefoAllocationHelper.SelectAndOrderEligibleBatches`.
- **Database-Level Partial Unique Index:**
  ```sql
  CREATE UNIQUE INDEX "IX_StockMovements_Order_Batch_Deduction"
  ON "StockMovements" ("ReferenceOrderId", "InventoryBatchId")
  WHERE "ReferenceOrderId" IS NOT NULL AND "InventoryBatchId" IS NOT NULL AND "MovementType" = 3;
  ```
  Prevents duplicate fulfillment deductions at the database storage engine layer.

---

## 4. Authentication & Role-Based Authorization

The application uses ASP.NET Core JWT Bearer authentication with an enforced `FallbackPolicy` (all endpoints require authentication unless explicitly marked `[AllowAnonymous]`).

### Authentication Endpoints
- `POST /api/auth/login` (`[AllowAnonymous]`): Validates credentials using `PasswordHasher<DemoUser>` (PBKDF2 with unique salts), returning an HS256-signed JWT token (8-hour expiry). Returns a generic error (`"Invalid username or password."`) on failure to prevent username enumeration.
- `GET /api/auth/me`: Validates the caller's JWT token and returns the authenticated user's ID, username, display name, and role.

### Endpoint Role Matrix

| Endpoint | Method | Operation Description | Access Permission | Unauthenticated / Forbidden |
| :--- | :---: | :--- | :---: | :---: |
| `/api/auth/login` | `POST` | Authenticate and obtain JWT token | Public (Anonymous) | `400 Bad Request` / `401 Unauthorized` |
| `/api/auth/me` | `GET` | Get current user identity & role | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/products` | `GET` | Search and list products | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/products/{id}` | `GET` | View product details | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/products` | `POST` | Create new product | **`Admin` Only** | `401 Unauthorized` / **`403 Forbidden`** |
| `/api/products/{id}` | `PUT` | Update product details | **`Admin` Only** | `401 Unauthorized` / **`403 Forbidden`** |
| `/api/products/{id}/deactivate` | `PATCH` | Deactivate product | **`Admin` Only** | `401 Unauthorized` / **`403 Forbidden`** |
| `/api/inventory/batches` | `GET` | List inventory batches | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/inventory/batches/{id}` | `GET` | View batch details | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/inventory/movements` | `GET` | View stock ledger audit trail | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/inventory/receipts` | `POST` | Receive new stock from factory | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/inventory/batches/{id}/release` | `POST` | Perform QC inspection & release | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/inventory/batches/{id}/quarantine` | `POST` | Quarantine suspect batch | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/inventory/batches/{id}/write-off` | `POST` | Write off damaged or expired stock | **`Admin` Only** | `401 Unauthorized` / **`403 Forbidden`** |
| `/api/inventory/adjustments` | `POST` | Adjust physical batch stock | **`Admin` Only** | `401 Unauthorized` / **`403 Forbidden`** |
| `/api/inventory/audit` | `GET` | Reconcile inventory invariants & discrepancies | **`Admin` Only** | `401 Unauthorized` / **`403 Forbidden`** |
| `/api/orders` | `GET` | List and filter orders | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/orders/{id}` | `GET` | View order details | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/orders` | `POST` | Create new draft order | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/orders/{id}/confirm` | `POST` | Confirm order & reserve FEFO stock | `Staff`, `Admin` | `401 Unauthorized` |
| `/api/orders/{id}/cancel` | `POST` | Cancel order & release reservations| `Staff`, `Admin` | `401 Unauthorized` |
| `/api/orders/{id}/fulfill` | `POST` | Fulfill order & deduct stock | `Staff`, `Admin` | `401 Unauthorized` |

> [!IMPORTANT]
> **Authoritative Security Boundary:** The backend API strictly enforces permissions via `[Authorize(Roles = "Admin")]`. Hiding action buttons in the React UI for `Staff` is purely a user experience enhancement; direct API requests from unauthorized accounts are strictly rejected with `403 Forbidden`.

---

## 5. Prerequisites & Local Installation

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (v8.0.x or higher)
- [Node.js](https://nodejs.org/) (v18.0.0 or higher; v20+ recommended)
- Package Manager: `npm` (bundled with Node.js) or `pnpm`
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) or Docker Engine
- PowerShell 7+ or Bash terminal

---

### Step 1: Start PostgreSQL Container & Verify Test Database
1. Start the local PostgreSQL 16 Alpine container:
   ```bash
   docker compose up -d
   ```
2. Verify the container is healthy:
   ```bash
   docker ps --filter "name=abonstock-postgres"
   ```
   The container exposes port `5432` with username `abonstock` and initializes the primary database `abonstock_db` (see `.env.example`).

3. Ensure the isolated test database exists in the container:
   Check whether `abonstock_test_db` already exists before running creation:
   ```bash
   docker exec abonstock-postgres psql -U abonstock -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'abonstock_test_db';"
   ```
   - If output is `1`: The database is already present. Do not run `createdb`.
   - If output is empty: The database does not yet exist; create it via:
     ```bash
     docker exec -i abonstock-postgres createdb -U abonstock abonstock_test_db
     ```
   - If the check command fails: Stop and inspect container and PostgreSQL status (`docker ps`). Do not attempt to drop or recreate any database.

   > [!NOTE]
   > **Database Roles & Isolation:**
   > - **`abonstock_db` (Baseline / Demo Environment):** Created automatically by Docker Compose (`POSTGRES_DB`), holding verified demo baseline data. Never run migrations, seeding, or test suites against this database.
   > - **`abonstock_test_db` (Dedicated Isolated Database):** The isolated database for automated tests, migration testing, and safe local onboarding.

---

### Step 2: Configure Backend Environment Variables
To defend against insecure deployments, `src/AbonStock.Api` implements a **fail-fast startup check**: it refuses to start if placeholder keys or connection strings are detected.

Environment variables must be configured in the active terminal session used to launch the API or execute migrations. These session-scoped environment variables exist in process memory only and are never committed to version control.

> [!IMPORTANT]
> **Terminal Session Scope:** Environment variables set via `$env:...` (PowerShell) or `export ...` (Bash) exist in process memory only for that specific terminal session. Opening a new terminal window creates an isolated session where these variables are not set. Any new terminal executing EF Core migrations or running the API must have these variables configured.

> [!TIP]
> For comprehensive full-stack setup, dynamic secret generation, database isolation safety gates, and end-to-end troubleshooting, refer to the [Safe Local Development Guide](docs/local-development.md).

> [!CAUTION]
> **Database Isolation Guard:**
> Never execute migrations, seed operations, or test runs against the operational database `abonstock_db`. For local development and test execution, configure your connection string to target the dedicated test database (`abonstock_test_db`).

**PowerShell:**
```powershell
# 1. Set database connection string targeting the isolated test database
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"

# 2. Generate a dynamic 256-bit cryptographically secure JWT signing key for this session
$env:Jwt__SecretKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# 3. Configure demo user password for this session (must choose a strong, unique password before executing)
$env:Auth__DefaultPassword = "<choose_a_strong_local_password>"
```

**Bash:**
```bash
# 1. Set database connection string targeting the isolated test database
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"

# 2. Generate a dynamic 256-bit secure JWT signing key
export Jwt__SecretKey=$(openssl rand -base64 32)

# 3. Configure demo user password for this session (must choose a strong, unique password before executing)
export Auth__DefaultPassword="<choose_a_strong_local_password>"
```

> [!NOTE]
> The `<choose_a_strong_local_password>` and `YOUR_LOCAL_PG_PASSWORD` placeholders must be replaced with your actual chosen passwords before execution; they are not working credentials. Replace `YOUR_LOCAL_PG_PASSWORD` with your local PostgreSQL password. If `$env:Auth__DefaultPassword` is omitted, the API will start safely but all login attempts will return HTTP 401 Unauthorized as a security safeguard. Never commit real secrets or production connection strings to version control.

---

### Step 3: Apply Database Migrations & Seed Demo Data

> [!CAUTION]
> **Database Migration Safety Rules:**
> 1. **Target Verification:** Always verify the target database name in your connection string before applying migrations. Migrations must explicitly target an isolated database such as `abonstock_test_db`.
> 2. **Protect Baseline Data:** Never run migration or seeding commands against `abonstock_db` if it contains baseline or demo data.
> 3. **Credential Safety:** Replace the connection string placeholder locally in your active terminal; never commit or paste real credentials into public repositories or documentation.
> 4. **Disable Seeding by Default:** Keep `Seeding:AutoSeed=false` unless intentionally populating a fresh, disposable test database.

1. **Apply Migrations to the Isolated Test Database:**
   Migrations are never executed automatically at startup. The API `appsettings.json` defaults to `Database=abonstock_db`, so you must explicitly set the connection string in your active terminal session to target your isolated database (`abonstock_test_db`) before running EF Core CLI:

   **PowerShell:**
   ```powershell
   # 1. Set connection string targeting your isolated test database
   $env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"

   # 2. Apply EF Core migrations to the isolated database
   dotnet ef database update --project src/AbonStock.Api
   ```

   **Bash:**
   ```bash
   # 1. Set connection string targeting your isolated test database
   export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"

   # 2. Apply EF Core migrations to the isolated database
   dotnet ef database update --project src/AbonStock.Api
   ```

2. **Database Seeding (Explicit Opt-In for Disposable Test DB Only):**
   `Seeding:AutoSeed` is set to `false` by default in `appsettings.json`. If you wish to populate demo products, batches, and orders on initial launch on your dedicated, disposable test database, opt in explicitly in that session:
   ```powershell
   $env:Seeding__AutoSeed = "true"
   ```
   *(The seeder is idempotent and acquires PostgreSQL transaction advisory locks to prevent duplicate writes).*

---

### Step 4: Run the Backend Web API
In Terminal 1:
```bash
dotnet run --project src/AbonStock.Api --launch-profile http
```
The API starts listening at `http://localhost:5299`.
- **Interactive Swagger UI:** Navigate to `http://localhost:5299/swagger`.

---

### Step 5: Run the Frontend React Application
In Terminal 2:
```bash
cd frontend

# 1. Configure environment file
cp .env.example .env

# 2. Install dependencies
npm install   # or pnpm install

# 3. Start development server
npm run dev   # or pnpm dev
```
The web application is accessible at `http://localhost:5173`.
- Authenticate using the configured demo accounts (`admin` or `staff` with your configured password).

---

## 6. Running Automated Tests

The repository contains automated unit and integration test suites:

### 1. Run Complete Backend Test Suite
Executes the complete backend test suite, combining both in-memory unit tests and PostgreSQL integration tests:
```bash
dotnet test tests/AbonStock.Tests/AbonStock.Tests.csproj
```
*(Requires active PostgreSQL container since integration tests are included; `PostgresTestFixture` automatically validates that integration tests target `abonstock_test_db`).*

### 2. Run Backend Unit Tests (In-Memory Subset)
Runs only the in-memory unit test subset (isolated from database dependencies), covering authentication, RBAC, domain invariants, CORS configuration, FEFO allocation helpers, and startup safety guards:
```bash
dotnet test tests/AbonStock.Tests/AbonStock.Tests.csproj --filter FullyQualifiedName~Unit
```

### 3. Run PostgreSQL Integration Tests (Integration Subset)
Runs only the PostgreSQL database integration test subset, verifying live database transactions, row-level concurrency locking, reservation lifecycles, and double-deduction prevention against the dedicated test database (`abonstock_test_db`):
```bash
dotnet test tests/AbonStock.Tests/AbonStock.Tests.csproj --filter FullyQualifiedName~Integration
```
*(Requires active PostgreSQL container on port 5432).*

### 4. Run Frontend Unit Tests (Node.js Native Test Runner)
Executes frontend unit and domain tests covering authentication, order details & totals calculation, ledger routing, inventory quarantine/write-off/audit services, and API client error mapping:
```bash
cd frontend
npm test     # or pnpm test
```

### 5. Run Frontend Type Check & Production Build
```bash
cd frontend
npm run build   # (executes tsc -b && vite build)
```

---

## 7. Verified Test Metrics (Full Verification)

The following test counts are actively maintained and verified across the test suites:

| Test Suite | Framework / Runner | Total Tests | Status | Scope |
| :--- | :--- | :---: | :---: | :--- |
| **Backend Automated Tests** | xUnit + `WebApplicationFactory` + PostgreSQL Fixture | **252** | **252 Passed (100%)** | 140 in-memory unit tests (FEFO, domain invariants, RBAC, DTO validation) & 112 PostgreSQL integration tests (full transactions, reservation lifecycle, double-deduction prevention) |
| **Frontend Unit & Domain Tests** | Node.js Test Runner (`--experimental-strip-types`) | **68** | **68 Passed (100%)** | Bearer token injection, session restoration, role-aware UI logic, API client error mapping, order itemization & ledger routing, inventory audit & quarantine services |
| **Total Full-Stack Verification** | **xUnit + Node.js Runner** | **320** | **320 Passed (100%)** | **Zero test failures across entire full-stack solution** |

---

## 8. Technical & Security Limitations

To maintain transparency for technical reviewers:
- **Token Storage:** JWT tokens are stored in browser `sessionStorage`. While this ensures session isolation per tab and automatic purging when the tab closes, tokens remain accessible to JavaScript in the same origin if an XSS vulnerability were introduced.
- **Refresh Token Mechanism:** The current implementation uses single-lifetime tokens (8 hours). No silent background refresh-token rotation is implemented.
- **Single Warehouse Facility:** Inventory logic currently operates within a single facility (`LOC_KDR_PROD`). Multi-warehouse inter-facility routing is slated for future milestones.
- **Timezone Handling:** Expiry validation compares UTC dates directly against PostgreSQL `date` columns without local WIB timezone offset conversions.
- **Automated Browser E2E:** Automated unit and domain test coverage is extensive (320 automated tests); however, headless browser end-to-end integration (e.g. Playwright / Cypress) has not yet been introduced into CI.

---

## 9. Documentation Directory

For deeper architectural breakdowns and runbooks:
- [Safe Local Development Guide](docs/local-development.md) — Safe local onboarding, dynamic secret generation, database isolation safety gates, and troubleshooting.
- [Architecture & Concurrency Guide](docs/architecture.md) — Detailed lock hierarchies, PostgreSQL `xmin` mechanics, and ER diagrams.
- [Upwork Portfolio Case Study](docs/upwork-portfolio.md) — Executive summary, technical decisions, and interview pitch snippets.
- [Interactive Swagger Demo Walkthrough](docs/demo-walkthrough.md) — Step-by-step Swagger UI verification guide.
- [Verified API Demonstration Evidence](docs/demo-evidence.md) — Operational runbook and verified JSON responses.
