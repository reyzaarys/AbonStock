# AbonStock — Interactive Demo Walkthrough & Verification Runbook

This guide provides step-by-step instructions for demonstrating the **AbonStock** platform via **Swagger UI** and the **React Web Frontend**.

---

## 1. Prerequisites & Environment Startup

### Required Configuration
AbonStock enforces a **fail-fast startup defense**: the backend API will immediately refuse to start (`InvalidOperationException`) if placeholder keys or connection strings are detected.

Ensure the following configuration is supplied via environment variables before starting the backend:
- `ConnectionStrings__DefaultConnection`: Target PostgreSQL 16 connection string.
- `Jwt__SecretKey`: Cryptographically secure signing key (minimum 32 characters / 256 bits for HS256).
- `Auth__DefaultPassword`: Password assigned to pre-configured demo accounts (`admin` and `staff`).

---

### Step 1: Start PostgreSQL Container & Verify Test Database
1. Start the local PostgreSQL 16 Alpine container via Docker Compose:
   ```bash
   docker compose up -d
   ```
2. Verify the container is healthy:
   ```bash
   docker ps --filter "name=abonstock-postgres"
   ```
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
   *(Docker Compose initializes `abonstock_db` on boot; this step ensures the isolated `abonstock_test_db` is available before running walkthrough migrations or tests).*

---

### Step 2: Set Environment Variables & Start the Web API
In **Terminal 1**:

> [!WARNING]
> **Database Safety:** Always target an isolated database (such as `abonstock_test_db`) during walkthroughs and testing. Never run migrations, seeding, or automated test suites against `abonstock_db` if it holds existing operational or demo baseline records.

> [!IMPORTANT]
> **Terminal Session Scope:** Environment variables set via `$env:...` (PowerShell) or `export ...` (Bash) exist in process memory only for that specific terminal session. Opening a new terminal window creates an isolated session where these variables are not set. Any new terminal executing EF Core migrations or running the API must have these variables configured.

> [!IMPORTANT]
> **Schema & Demo Data Prerequisites for Walkthrough:**
> A freshly created `abonstock_test_db` contains no schema tables or initial records. The walkthrough steps in this runbook rely on baseline catalog items (e.g. `ABON-SAPI-ORI-100`) and demo batches.
> Before starting the API for the walkthrough, ensure `abonstock_test_db` is initialized:
> 1. **Apply Migrations:** Run EF Core database update to create tables in `abonstock_test_db`: `dotnet ef database update --project src/AbonStock.Api`.
> 2. **Seed Demo Records:** Enable the built-in idempotent seeder (`AbonStockDataSeeder`) by setting `Seeding__AutoSeed="true"` during initial startup.
> Never assume that creating `abonstock_test_db` via `createdb` automatically generates schema tables or demo data.

**PowerShell:**
```powershell
# 1. Supply your local PostgreSQL connection string (targeting isolated test database)
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"

# 2. Generate a dynamic 256-bit cryptographically secure JWT signing key for this session
$env:Jwt__SecretKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# 3. Configure a chosen local password for demo users (admin, staff) in this session
$env:Auth__DefaultPassword = "<choose_a_strong_local_password>"

# 4. Enable idempotent demo data seeder on initial run with empty database
$env:Seeding__AutoSeed = "true"

# 5. Apply EF Core schema migrations (if not yet applied to abonstock_test_db)
dotnet ef database update --project src/AbonStock.Api

# 6. Start the Web API
dotnet run --project src/AbonStock.Api --launch-profile http
```

**Bash:**
```bash
# 1. Supply your local PostgreSQL connection string (targeting isolated test database)
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"

# 2. Generate a dynamic 256-bit secure JWT signing key
export Jwt__SecretKey=$(openssl rand -base64 32)

# 3. Configure a chosen local password for demo users
export Auth__DefaultPassword="<choose_a_strong_local_password>"

# 4. Enable idempotent demo data seeder on initial run with empty database
export Seeding__AutoSeed="true"

# 5. Apply EF Core schema migrations (if not yet applied to abonstock_test_db)
dotnet ef database update --project src/AbonStock.Api

# 6. Start the Web API
dotnet run --project src/AbonStock.Api --launch-profile http
```

> [!TIP]
> For complete onboarding instructions, database isolation safety gates, and volume caveats, refer to the [Safe Local Development Guide](local-development.md).

The Web API starts listening at `http://localhost:5299`.
- Confirm Swagger UI is accessible: open `http://localhost:5299/swagger`.

> [!NOTE]
> **Database Migration & Seeding Safety:**
> - `Seeding:AutoSeed` is disabled (`false`) by default. Keep automatic seeding disabled unless intentionally setting up a disposable, isolated database. Never run migration or seeding commands against `abonstock_db` if it contains baseline or demo data.
> - The API `appsettings.json` defaults to `Database=abonstock_db`. Migrations must explicitly target your isolated database (`abonstock_test_db`) by setting the connection string in the active terminal session before running EF Core CLI:
>
>   **PowerShell:**
>   ```powershell
>   # Set connection string targeting your isolated test database
>   $env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"
>   dotnet ef database update --project src/AbonStock.Api
>   ```
>
>   **Bash:**
>   ```bash
>   # Set connection string targeting your isolated test database
>   export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"
>   dotnet ef database update --project src/AbonStock.Api
>   ```
>   *(Replace `YOUR_LOCAL_PG_PASSWORD` with your local PostgreSQL password; never paste real credentials into public documentation. Always verify the resolved database name before applying migrations).*

---

### Step 3: Start the React Frontend Application
In **Terminal 2**:
```bash
cd frontend

# Ensure .env is present (points VITE_API_BASE_URL to http://localhost:5299)
cp .env.example .env

# Install dependencies and launch Vite development server
npm install   # or pnpm install
npm run dev   # or pnpm dev
```

The React frontend starts at `http://localhost:5173`.
- Confirm the application is accessible by opening `http://localhost:5173` in your browser.

---

## 2. Swagger UI JWT Authentication

All business endpoints in AbonStock require an authenticated JWT Bearer token via ASP.NET Core `FallbackPolicy`. Calling any business endpoint without authenticating first will return `401 Unauthorized`.

```text
┌────────────────────────┐      ┌───────────────────────────┐      ┌─────────────────────────┐
│ POST /api/auth/login   │ ───> │ Extract token from JSON   │ ───> │ Click "Authorize" lock  │
│ (Submit credentials)   │      │ response                  │      │ Enter "Bearer <token>"  │
└────────────────────────┘      └───────────────────────────┘      └─────────────────────────┘
```

### 1. Authenticate via `POST /api/auth/login`
1. Navigate to `http://localhost:5299/swagger`.
2. Expand the `Auth` section and click `POST /api/auth/login`.
3. Click **Try it out**.
4. In the request body, provide your locally configured credentials:
   ```json
   {
     "username": "admin",
     "password": "<your_configured_password>"
   }
   ```
   *(For Staff testing, replace `"admin"` with `"staff"`).*
5. Click **Execute**.
6. The response will return `200 OK` with a signed JWT token:
   ```json
   {
     "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
     "expiresIn": 28800,
     "user": {
       "id": "usr_admin",
       "username": "admin",
       "displayName": "Administrator",
       "role": "Admin"
     }
   }
   ```
7. Copy the value of the `token` string (without quotes).

---

### 2. Configure Swagger Bearer Authorization
The Swagger security definition is configured as an API Key header:
```csharp
Name = "Authorization", In = ParameterLocation.Header
```
Therefore, Swagger UI does **not** automatically prepend the word `Bearer`. You must provide the scheme manually:

1. Scroll to the top of Swagger UI and click the green **Authorize** (lock icon) button.
2. In the `Value:` input field, enter:
   ```text
   Bearer <paste_your_token_here>
   ```
   *(Example: `Bearer eyJhbGciOiJIUzI1Ni...` — ensure there is a space after `Bearer`).*
3. Click **Authorize**, then click **Close**.

---

### 3. Verify Authentication via `GET /api/auth/me`
1. Expand `GET /api/auth/me`.
2. Click **Try it out** $\rightarrow$ **Execute**.
3. **Response:** `200 OK` returning your identity claims:
   ```json
   {
     "id": "usr_admin",
     "username": "admin",
     "displayName": "Administrator",
     "role": "Admin"
   }
   ```

---

## 3. Administrator Demonstration Scenario

This scenario demonstrates operations exclusive to the **`Admin`** role:

### 1. Create a New Product (Admin Only)
- **Endpoint:** `POST /api/products`
- **Request Body:**
  ```json
  {
    "sku": "ABON-SAPI-SPCL-100",
    "name": "Abon Sapi Spesial 100g",
    "description": "Premium shredded beef with caramelized shallots",
    "unitPrice": 42000.00
  }
  ```
- **Click:** **Execute**.
- **Expected Response:** `201 Created` with initialized `version` token and `isActive: true`.

---

### 2. Update Product & Concurrency Token (Admin Only)
- **Endpoint:** `PUT /api/products/{id}`
- **Request Body:**
  ```json
  {
    "name": "Abon Sapi Spesial 100g (Updated)",
    "description": "Premium shredded beef with shallots and garlic",
    "unitPrice": 45000.00,
    "rowVersion": 1
  }
  ```
- **Expected Response:** `200 OK` (or `409 Conflict` if the supplied `rowVersion` does not match PostgreSQL's current `xmin`).

---

### 3. Perform Stock Adjustment (Admin Only)
Physical stock corrections (such as shrinkage, damage, or audit variances) are restricted to Administrators:
- **Endpoint:** `POST /api/inventory/adjustments`
- **Request Body:**
  ```json
  {
    "batchId": 2,
    "quantityChange": -2,
    "reason": "Damaged container during warehouse inspection",
    "batchRowVersion": 1
  }
  ```
- **Expected Response:** `200 OK` detailing `previousPhysicalQuantity` and `newPhysicalQuantity`, and generating an audit entry in the stock ledger.

---

### 4. Deactivate Product (Admin Only)
- **Endpoint:** `PATCH /api/products/{id}/deactivate`
- **Expected Response:** `200 OK` with `isActive: false`.

---

## 4. Warehouse Staff Demonstration Scenario

This scenario demonstrates permitted operational workflows and proves that administrative actions are strictly rejected with `403 Forbidden`:

### 1. Re-Authenticate as Staff
1. Execute `POST /api/auth/login` with:
   ```json
   {
     "username": "staff",
     "password": "<your_configured_password>"
   }
   ```
2. Copy the token from response.
3. In Swagger Authorize, click **Logout**, then enter:
   ```text
   Bearer <paste_staff_token_here>
   ```
4. Call `GET /api/auth/me` to confirm identity: `role: "Staff"`.

---

### 2. Permitted Operational Workflows (Staff Allowed)
Staff can perform all day-to-day warehouse receipt, inspection, and order fulfillment tasks:

#### A. Receive Production Stock
- **Endpoint:** `POST /api/inventory/receipts`
- **Request Body:**
  ```json
  {
    "productId": 1,
    "batchNo": "BAT-202610-DEMO",
    "productionDate": "2026-10-24",
    "quantity": 50,
    "reason": "Morning artisan production batch"
  }
  ```
- **Expected Response:** `201 Created` (Batch initialized in `PendingRelease` status).

#### B. Perform 3-Point Quality Gate & Release
- **Endpoint:** `POST /api/inventory/batches/{id}/release`
- **Request Body:**
  ```json
  {
    "visualCheckPassed": true,
    "aromaCheckPassed": true,
    "packagingCheckPassed": true,
    "rowVersion": 1
  }
  ```
- **Expected Response:** `200 OK` (Batch transitions to `Released` and becomes eligible for FEFO order allocation).

#### C. Order Confirmation & FEFO Allocation
- **Endpoint:** `POST /api/orders/{id}/confirm`
- **Expected Response:** `200 OK` (Status transitions to `Confirmed`, reserving stock from oldest available released batches).

---

### 3. Role-Based Access Enforcement (Staff Rejected)
To prove that security is enforced by the backend and not merely hidden in the UI:

#### Attempt 1: Staff calls Create Product
- **Endpoint:** `POST /api/products`
- **Request Body:** `{ "sku": "STAFF-PROD", "name": "Illegal Product", "unitPrice": 10000 }`
- **Expected Response:** `403 Forbidden`
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
    "title": "Forbidden",
    "status": 403
  }
  ```

#### Attempt 2: Staff calls Stock Adjustment
- **Endpoint:** `POST /api/inventory/adjustments`
- **Request Body:** `{ "batchId": 2, "quantityChange": -1, "reason": "Staff attempt", "batchRowVersion": 1 }`
- **Expected Response:** `403 Forbidden`

#### Attempt 3: Staff calls Batch Write-Off
- **Endpoint:** `POST /api/inventory/batches/2/write-off`
- **Request Body:** `{ "quantity": 5, "reason": "Spoilage", "batchRowVersion": 1 }`
- **Expected Response:** `403 Forbidden`

#### Attempt 4: Staff calls Inventory Invariant Audit
- **Endpoint:** `GET /api/inventory/audit`
- **Expected Response:** `403 Forbidden`

> **Key Takeaway:** The user's active session is **not** terminated on `403 Forbidden`. The user remains authenticated, but the specific operation is blocked.

---

## 5. HTTP Response Status Code Reference

| Status Code | Reason / Meaning | Example Trigger |
| :---: | :--- | :--- |
| `200 OK` | Request succeeded. | `GET /api/products`, `POST /api/orders/3/confirm` |
| `201 Created` | New resource created. | `POST /api/products` (Admin), `POST /api/orders` |
| `400 Bad Request` | Validation failure (RFC 7807). | Missing required fields, negative unit price, invalid QC checklist |
| `401 Unauthorized` | Missing, invalid, or expired JWT token. | Calling business endpoint without Bearer token, wrong credentials |
| `403 Forbidden` | Authenticated user lacks required role. | Staff calling `POST /api/products` or `POST /api/inventory/adjustments` |
| `404 Not Found` | Target entity does not exist. | `GET /api/orders/99999` |
| `409 Conflict` | Optimistic concurrency or business invariant conflict. | Stale `xmin` token, insufficient available stock, duplicate order fulfillment |

---

## 6. Frontend Browser Walkthrough (React 18 + Vite)

The React web client demonstrates how role-based authorization translates into a clean, modern user experience:

```text
Browser Flow:
[/inventory (Anon)] ──> Redirect ──> [/login] ──> Enter Credentials ──> [/inventory (Restored)]
                                                                               │
                                                       ┌───────────────────────┴───────────────────────┐
                                                       ▼                                               ▼
                                                [ Admin Role ]                                  [ Staff Role ]
                                         (Add/Edit/Adjust Buttons)                       (View Only / Hidden Controls)
```

### 1. Anonymous Access & Protected Routes
1. Open your browser in an incognito window and navigate directly to:
   ```text
   http://localhost:5173/inventory
   ```
2. **Observed Behavior:** The application displays `Verifying user session...` while checking local storage, detects no active session, and immediately navigates to `http://localhost:5173/login`.
3. The destination URL (`/inventory`) is preserved in React Router navigation state.

---

### 2. Administrator Login & UI Experience
1. On the login card, enter your configured Admin credentials (`admin` / password).
2. Click **Sign In**.
3. **Observed Behavior:**
   - The button shows a spinner and disables double-clicking during transit.
   - Upon successful response, user is redirected to `/inventory`.
   - The **Navbar** displays the user identity: `Administrator` with a blue `ADMIN` badge.
   - On the **Products Page (`/products`)**:
     - The **+ Add Product** button is visible and active in the header.
     - Each product row has active **Edit** and **Deactivate** buttons.
   - On the **Inventory Page (`/inventory`)**:
     - The **+ Receive Stock** button is active.
     - Batches with `Released` status have an active **Adjust** button.

---

### 3. Session Restoration on Page Reload
1. While logged in as Admin, press **F5** or click browser **Refresh**.
2. **Observed Behavior:**
   - The application briefly mounts `ProtectedRoute` with a verifying state.
   - `AuthContext` retrieves the stored token from `sessionStorage` and validates it against `GET /api/auth/me`.
   - The user remains logged in without being redirected to `/login`.

---

### 4. Staff Login & Role-Aware UI Comparison
1. In the Navbar, click **Logout**.
   - React state and `sessionStorage` are purged; browser navigates to `/login`.
2. Login using Staff credentials (`staff` / password).
3. **Observed Behavior:**
   - The Navbar displays `Warehouse Staff` with a neutral `STAFF` badge.
   - On the **Products Page (`/products`)**:
     - The **+ Add Product** button is **hidden**.
     - Table rows display a subtle **View only** label instead of Edit/Deactivate action buttons.
   - On the **Inventory Page (`/inventory`)**:
     - The **Adjust** button on Released batches is **hidden** and replaced by a neutral status label.
     - Operational buttons (**Receive Stock** and **QC Release**) remain **fully active and accessible**.
   - On the **Orders Page (`/orders`)**:
     - Staff retains full operational capability to create, confirm, cancel, and fulfill orders.

---

### 5. Inventory Operations: Quarantine, Write-Off & Movements Ledger
1. Navigate to the **Inventory Page (`/inventory`)**:
   - The top tab navigation includes **Batches**, **Stock Movements**, and (for Admin) **Invariant Audit**.
2. **Batch Quarantine Workflow:**
   - On a batch with `PendingRelease` or `Released` status, click the **Quarantine** button.
   - The `QuarantineBatchModal` opens: enter a reason ($\ge 5$ characters, e.g., `"Package seam inspection required"`) and optional notes.
   - Click **Confirm Quarantine**: the batch status updates to `Quarantined`, locking it from order allocation while preserving physical inventory.
3. **Batch Write-Off Workflow (Admin Only):**
   - As an Administrator, click **Write-Off** on an eligible batch.
   - The `WriteOffBatchModal` prompts for write-off quantity ($\le$ physical quantity), reason, and disposal document number.
   - Submit: physical batch balance and product `QuantityOnHand` decrease synchronously, transitioning to `Depleted` if quantity reaches zero.
4. **Stock Movements Ledger (`/inventory?tab=movements`):**
   - Click the **Stock Movements** tab.
   - View the immutable audit trail displaying Date, Movement Type (Receipt, Adjustment, WriteOff, OrderDeduction, OrderCancellation), Product SKU, Batch No, Reference Order badge, Quantity Change, and resultant Batch / Product balances.
   - Test filters: filter by Movement Type or specific Product.

---

### 6. Administrative Invariant Audit Engine (`/inventory?tab=audit`)
1. Logged in as Administrator, click the **Invariant Audit** tab.
2. The UI calls `GET /api/inventory/audit`:
   - Summary statistics display Total Products Scanned, Total Batches Audited, and Discrepancy Count.
   - When all balances are mathematically aligned ($\sum \text{Batches} = \text{Product QuantityOnHand}$ and $\text{Reserved} \le \text{Physical}$), a green **All Invariants Intact** badge is displayed.
   - Click **Run Audit Reconciliation** to trigger an on-demand re-scan.
3. If logged in as Staff, the Invariant Audit tab is completely hidden from the UI, and direct API access is rejected with `403 Forbidden`.

---

### 7. Order Lifecycle, Deep Inspection & Ledger Navigation
1. Navigate to **Orders Page (`/orders`)**:
   - All orders (Pending, Confirmed, Completed, Cancelled) display an active **Details** button.
2. **Order Detail Inspection (`OrderDetailModal`):**
   - Click **Details** on any order to open the read-only modal.
   - View customer name, email, status badge, itemized product lines (SKU, quantity, unit price in IDR, line totals), and aggregate order total.
   - Timestamps (`createdAt`, `confirmedAt`, `completedAt`, `cancelledAt`) and cancellation reasons are displayed conditionally based on actual status.
3. **Contextual Ledger Navigation:**
   - Open a `Completed` (fulfilled) order.
   - Notice the green notification banner: *"Order fulfillment has updated inventory balances."*
   - Click the **Go to Stock Movements** button: the modal closes and the browser navigates smoothly to `/inventory?tab=movements` with the movements ledger opened.

---

### 8. Logout & Access Revocation
1. Click **Logout** from the Navbar.
2. Manually enter `http://localhost:5173/orders` in the browser address bar.
3. **Observed Behavior:** Access is rejected; the browser immediately redirects to `/login`.

---

## 7. Troubleshooting Common Startup & Runtime Issues

| Symptom / Error | Root Cause | Solution |
| :--- | :--- | :--- |
| **Backend crash:** `InvalidOperationException: Jwt:SecretKey must be configured...` | Missing or placeholder JWT signing key in configuration. | Set `$env:Jwt__SecretKey` in PowerShell or `export Jwt__SecretKey` in Bash with at least 32 characters. |
| **Backend crash:** `Npgsql.NpgsqlException: Connection refused` | PostgreSQL container is stopped or port `5432` is not reachable. | Run `docker compose up -d` and check `docker ps` to verify the container is healthy. |
| **Login returns `401 Unauthorized`:** `"Invalid username or password."` | Password for demo users (`admin`, `staff`) has not been supplied to the backend. | Set `$env:Auth__DefaultPassword` before starting the backend API. |
| **Frontend displays network error toast:** `Unable to connect to backend at http://localhost:5299` | Backend Web API is not running or running on an unexpected port. | Confirm backend is running via `dotnet run` on port `5299` and accessible via curl or browser. |
| **CORS error in browser console:** `Origin http://localhost:5173 is not allowed by Access-Control-Allow-Origin` | Frontend URL does not match backend allowed origins. | Check `appsettings.Development.json` to ensure `http://localhost:5173` is listed under `Cors:AllowedOrigins`. |
| **HTTP `401 Unauthorized` on all Swagger endpoints** | Token was not supplied to Swagger Authorize dialog. | Execute `POST /api/auth/login`, copy the token, click Authorize in Swagger, and enter `Bearer <token>`. |
| **HTTP `404 Not Found` on database tables** | EF Core migrations have not been applied to the isolated database. | Ensure `$env:ConnectionStrings__DefaultConnection` is set to target `abonstock_test_db` in your active terminal, verify the database name, and run `dotnet ef database update --project src/AbonStock.Api`. |
