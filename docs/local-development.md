# AbonStock — Safe Local Development & Onboarding Guide

This guide provides a reproducible, secure procedure for configuring and launching **AbonStock** locally. It explains how to set up the environment using session-scoped secrets without hardcoding passwords into Git-tracked files, and outlines the database isolation precautions required before executing migrations or seeding.

---

## 1. System Topology & Port Allocations

A complete local AbonStock deployment runs three decoupled services:

```text
┌──────────────────────────────────────┐        ┌──────────────────────────────────────┐
│      React 18 + Vite Frontend        │        │      ASP.NET Core 8 Web API          │
│      URL: http://localhost:5173      │ ─────> │      URL: http://localhost:5299      │
│      Process: Node / Vite Dev Server │        │      Process: dotnet run (PID)       │
└──────────────────────────────────────┘        └──────────────────┬───────────────────┘
                                                                   │
                                                                   ▼
                                                ┌──────────────────────────────────────┐
                                                │      PostgreSQL 16 Engine            │
                                                │      Port: 5432 (Localhost)          │
                                                │      Process: Docker Container       │
                                                └──────────────────────────────────────┘
```

| Service | Host Port | Protocol / Path | Process Type |
| :--- | :---: | :--- | :--- |
| **PostgreSQL 16** | `5432` | TCP (`abonstock_db`, `abonstock_test_db`) | Docker Container (`abonstock-postgres`) |
| **ASP.NET Core API** | `5299` | HTTP / `http://localhost:5299` | .NET Kestrel Server (`launch-profile http`) |
| **Swagger UI** | `5299` | HTTP / `http://localhost:5299/swagger` | OpenAPI Interface |
| **React Frontend** | `5173` | HTTP / `http://localhost:5173` | Vite Dev Server (`localhost:5173`) |

> [!NOTE]
> **Database Role Distinction:**
> - **`abonstock_db` (Baseline / Demo Environment):** The default database created automatically by Docker Compose (`POSTGRES_DB`), containing verified baseline demo data (products, batches, and orders). Never execute migrations, seed operations, or test runs against this database.
> - **`abonstock_test_db` (Dedicated Isolated Database):** The isolated database used for automated tests, migration verification, and safe onboarding experiments. All onboarding examples and integration test suites strictly target this database.

---

## 2. Secrets & Configuration Architecture

To comply with enterprise security standards, AbonStock separates base configuration from runtime secrets:
- Base configuration files (`appsettings.json`, `appsettings.Development.json`) contain **safe placeholders only**.
- Secrets are supplied at runtime via environment variables or User Secrets, ensuring credentials are never committed to version control.
- `Program.cs` implements a **fail-fast startup check**: the API refuses to start (`InvalidOperationException`) if `Jwt:SecretKey` contains `"PLACEHOLDER"`, `"YOUR_JWT"`, or is under 32 bytes (256 bits for HS256).

### A. Dynamic Secret Generation
Instead of reusing fixed strings, generate a fresh cryptographically secure random signing key in your active terminal session:

**PowerShell:**
```powershell
# Generate 32 cryptographically random bytes encoded as Base64 (44 characters / 256 bits)
$env:Jwt__SecretKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

**Bash:**
```bash
# Generate 32 cryptographically random bytes encoded as Base64
export Jwt__SecretKey=$(openssl rand -base64 32)
```

> [!IMPORTANT]
> **Terminal Session Persistence:**
> Environment variables set via `$env:NAME = ...` (PowerShell) or `export NAME=...` (Bash) exist in process memory only for the lifetime of that specific terminal session. Opening a new terminal window creates an isolated session where these variables are not set. Any new terminal executing EF Core migrations or running the API must have these variables configured.

### B. Demo-User Password Configuration
The backend authenticates demo users (`admin` and `staff`) against salted PBKDF2 password hashes. In `AuthService.cs`, configuration lookup adheres to the following precedence:
1. `Auth:Users:{index}:Password` (e.g. `Auth__Users__0__Password`)
2. `Auth:DefaultPassword` (e.g. `Auth__DefaultPassword`)

Set a chosen local password for your terminal session:

**PowerShell:**
```powershell
$env:Auth__DefaultPassword = "<choose_a_strong_local_password>"
```

**Bash:**
```bash
export Auth__DefaultPassword="<choose_a_strong_local_password>"
```

*(The `<choose_a_strong_local_password>` placeholder must be replaced with your chosen local password before running. If omitted, `AuthService` sets an empty hash and rejects all login attempts with `401 Unauthorized` as a fail-safe).*

### C. Database Connection String Configuration
Configure the connection string in your active terminal session to target the dedicated test database (`abonstock_test_db`). Replace `YOUR_LOCAL_PG_PASSWORD` with your local PostgreSQL password:

**PowerShell:**
```powershell
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"
```

**Bash:**
```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"
```

> [!WARNING]
> **Database Isolation Guard:**
> The API `appsettings.json` defaults to `Database=abonstock_db` with a placeholder password. If you omit `$env:ConnectionStrings__DefaultConnection`, EF Core CLI or the API will fall back to `abonstock_db`. Always set the connection string to `abonstock_test_db` in your active terminal session to protect the baseline demo database.

---

## 3. Database Credentials, Multi-Database Topology & Persistent Volume Warning

Reviewing `docker-compose.yml`:
```yaml
services:
  postgres:
    image: postgres:16-alpine
    container_name: abonstock-postgres
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-abonstock}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-abonstock_dev_pass_2026}
      POSTGRES_DB: ${POSTGRES_DB:-abonstock_db}
    volumes:
      - abonstock_pgdata:/var/lib/postgresql/data
```

### PostgreSQL Multi-Database Architecture:
1. **`abonstock_db` (Default Development/Demo Baseline):**
   The official PostgreSQL container initializes the database defined in `POSTGRES_DB` (`abonstock_db`) on first startup. This database serves as the baseline demo environment. Never run migrations, seeding, or automated test suites against `abonstock_db`.
2. **`abonstock_test_db` (Dedicated Isolated Database):**
   Because PostgreSQL only creates one database at initial boot, verify and create `abonstock_test_db` on the running container before running isolated migrations:
   ```bash
   # Check whether abonstock_test_db exists (returns 1 if exists, empty if absent):
   docker exec abonstock-postgres psql -U abonstock -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'abonstock_test_db';"
   ```
   - If output is `1`: The database is already present. Do not run `createdb`.
   - If output is empty: The database does not yet exist; create it via:
     ```bash
     docker exec -i abonstock-postgres createdb -U abonstock abonstock_test_db
     ```
   - If the check command fails: Stop and inspect container and PostgreSQL status (`docker ps`). Do not attempt to drop or recreate any database.

> [!WARNING]
> **PostgreSQL Volume Initialization Rule:**
> The official PostgreSQL Docker image evaluates `POSTGRES_PASSWORD` and `POSTGRES_USER` **only once** when the named volume (`abonstock_pgdata`) is created. Modifying `.env` or `docker-compose.yml` subsequently does **not** update the password in an existing data volume. If you encounter password authentication errors, ensure your connection string uses the password that was active when the volume was first initialized. Never delete database volumes on environments where you have data you wish to preserve.

---

## 4. Database Isolation Safety Gate

Before running any mutating database command (applying migrations or enabling the seeder), you must explicitly pass this safety gate:

```text
┌────────────────────────────────────────────────────────────────────────┐
│                   DATABASE ISOLATION SAFETY GATE                       │
├────────────────────────────────────────────────────────────────────────┤
│ 1. Confirm target host is strictly localhost (127.0.0.1:5432).         │
│ 2. Confirm database name is dedicated to test/isolation                │
│    (abonstock_test_db), NOT operational/demo baseline (abonstock_db).  │
│ 3. Confirm you never run migrations, seeding, or test writes on        │
│    the baseline database (abonstock_db).                               │
│ 4. If target isolation cannot be established: STOP IMMEDIATELY.        │
└────────────────────────────────────────────────────────────────────────┘
```

These operations represent distinct lifecycle phases:
- **Starting the PostgreSQL Container:** Starts the database engine daemon only.
- **Ensuring Test Database Exists:** Creates `abonstock_test_db` via `createdb` if absent.
- **Applying EF Core Migrations:** Creates or updates schema tables and constraints on `abonstock_test_db` (`dotnet ef database update`).
- **Seeding Demo Data:** Populates sample products, batches, and orders (explicit opt-in via `Seeding__AutoSeed="true"`, disposable test DB only).
- **Starting the Web API:** Boots the HTTP pipeline and begins listening for requests.
- **Starting the Frontend:** Compiles and serves the React client SPA.

---

## 5. Step-by-Step Local Startup Procedure

### Terminal 1: Database & Backend Web API

#### Step 1: Launch PostgreSQL & Ensure Test Database Exists
```bash
# 1. Start PostgreSQL container in background
docker compose up -d

# 2. Confirm container is healthy and listening on port 5432
docker ps --filter "name=abonstock-postgres"

# 3. Check if isolated test database exists before createdb:
docker exec abonstock-postgres psql -U abonstock -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'abonstock_test_db';"
# If output is 1: database is ready (do not run createdb).
# If output is empty: create via:
# docker exec -i abonstock-postgres createdb -U abonstock abonstock_test_db
# If check fails: stop and verify container status. Never drop/recreate databases.
```

#### Step 2: Configure Session Secrets
Session environment variables must be configured in the active terminal session.

> [!IMPORTANT]
> **Terminal Session Scope:** Environment variables exist in process memory only for that specific terminal session. Opening a new terminal window creates an isolated session where these variables are not set. If you open a separate terminal for EF Core CLI or running the API, you must set these variables again in that new terminal session.

**PowerShell:**
```powershell
# 1. Supply connection string targeting the isolated test database
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"

# 2. Generate dynamic 256-bit cryptographically secure JWT signing key for this session
$env:Jwt__SecretKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# 3. Configure demo user password for this session
$env:Auth__DefaultPassword = "<choose_a_strong_local_password>"
```

**Bash:**
```bash
# 1. Supply connection string targeting the isolated test database
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=abonstock_test_db;Username=abonstock;Password=YOUR_LOCAL_PG_PASSWORD"

# 2. Generate dynamic 256-bit secure JWT signing key
export Jwt__SecretKey=$(openssl rand -base64 32)

# 3. Configure demo user password for this session
export Auth__DefaultPassword="<choose_a_strong_local_password>"
```

#### Step 3: Apply Database Migrations (After Safety Gate)
Confirm the target database is your isolated `abonstock_test_db`, then apply EF Core migrations:
```bash
dotnet ef database update --project src/AbonStock.Api
```

#### Step 4: (Optional) Opt-in to Demo Data Seeding (Disposable Test DB Only)
`Seeding:AutoSeed` is disabled (`false`) by default. If starting with an empty test database and you wish to populate demo products and batches:

**PowerShell:**
```powershell
$env:Seeding__AutoSeed = "true"
```

**Bash:**
```bash
export Seeding__AutoSeed="true"
```
*(The seeder is idempotent and acquires PostgreSQL transaction advisory locks to guarantee safety).*

#### Step 5: Start the Web API
```bash
dotnet run --project src/AbonStock.Api --launch-profile http
```
The API is now running at `http://localhost:5299`.
- Verify Swagger UI: open `http://localhost:5299/swagger` in your browser.

---

### Terminal 2: React Frontend Application

#### Step 1: Open Terminal 2 and Configure Environment
```bash
cd frontend

# Ensure local environment file is created
cp .env.example .env
```
Confirm `.env` contains:
```env
VITE_API_BASE_URL=http://localhost:5299
```

#### Step 2: Install Dependencies & Launch Vite
```bash
# Install packages
npm install   # or pnpm install

# Launch Vite development server
npm run dev   # or pnpm dev
```
The frontend is now accessible at `http://localhost:5173`.
- Open `http://localhost:5173` in your browser.
- Login using username `admin` or `staff` with the password configured in Terminal 1.

---

## 6. Verifying API & Frontend Health (Non-Mutating)

To verify all components without modifying any business records:

1. **API Swagger Accessibility:** Navigate to `http://localhost:5299/swagger`. Confirm endpoints for Auth, Products, Inventory, and Orders appear.
2. **Authentication Verification:**
   - Execute `POST /api/auth/login` using your configured credentials. Confirm a signed JWT token is returned.
   - Execute `GET /api/auth/me` with `Bearer <token>` in the Authorize dialog. Confirm your profile claims are returned.
3. **Frontend UI Health:** Navigate to `http://localhost:5173`. Confirm unauthenticated requests redirect cleanly to `/login`, and logging in reveals the Navbar with your display name and role badge.
4. **Automated Unit & Integration Tests:**
   - Backend Unit Subset: `dotnet test tests/AbonStock.Tests/AbonStock.Tests.csproj --filter FullyQualifiedName~Unit` (140 passed via in-memory runner, 0 database calls).
   - Backend Integration Subset: `dotnet test tests/AbonStock.Tests/AbonStock.Tests.csproj --filter FullyQualifiedName~Integration` (112 passed against `abonstock_test_db`).
   - Frontend Unit Tests: `cd frontend; npm test` (68 passed via native Node.js test runner).
   - Full Solution Verification: 320 passed across separate backend (252) and frontend (68) suites.

---

## 7. Troubleshooting Common Onboarding Issues

| Symptom / Error | Root Cause | Verified Resolution |
| :--- | :--- | :--- |
| **Startup Crash:** `InvalidOperationException: Jwt:SecretKey must be configured...` | Missing, placeholder, or short JWT signing key. | Generate a key using `[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))` and assign to `$env:Jwt__SecretKey` or `export Jwt__SecretKey`. |
| **Startup Crash:** `Npgsql.NpgsqlException: Connection refused` | PostgreSQL container is stopped or port 5432 is not exposed. | Run `docker compose up -d` and check `docker ps` to ensure the container is running and healthy. |
| **Database Error:** `28P01: password authentication failed for user "abonstock"` | Mismatch between connection string password and the password set when the persistent volume was created. | Check `.env.example` defaults or verify the password configured during initial volume creation (`POSTGRES_PASSWORD`). Replace `YOUR_LOCAL_PG_PASSWORD` with that exact value. Do not delete volumes containing needed data. |
| **Login returns `401 Unauthorized`:** `"Invalid username or password."` | `Auth__DefaultPassword` was not set in the terminal before running the API. | Set `$env:Auth__DefaultPassword` (or `export Auth__DefaultPassword`) to your chosen password and restart the backend. |
| **Frontend displays network error:** `Unable to connect to backend at http://localhost:5299` | Backend API is not running or listening on a different port. | Verify Terminal 1 is running `dotnet run --project src/AbonStock.Api --launch-profile http` and reachable at `http://localhost:5299`. |
| **CORS console error:** `Access-Control-Allow-Origin blocked` | Frontend launched on a non-standard origin port. | Ensure Vite runs on port 5173 (standard dev port allowed in `appsettings.Development.json`). |
| **HTTP `401 Unauthorized` on all Swagger endpoints** | Token was omitted or not prefixed with `Bearer `. | Enter `Bearer <token>` (with space) into the Swagger Authorize dialog. |
| **Database table not found error:** `relation "Products" does not exist` | EF Core migrations have not yet been applied to the isolated database. | Ensure `$env:ConnectionStrings__DefaultConnection` targets `abonstock_test_db` in your active terminal, then run `dotnet ef database update --project src/AbonStock.Api`. |
