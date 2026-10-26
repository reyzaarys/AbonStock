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
| **PostgreSQL 16** | `5432` | TCP / `abonstock_db` | Docker Container (`abonstock-postgres`) |
| **ASP.NET Core API** | `5299` | HTTP / `http://localhost:5299` | .NET Kestrel Server (`launch-profile http`) |
| **Swagger UI** | `5299` | HTTP / `http://localhost:5299/swagger` | OpenAPI Interface |
| **React Frontend** | `5173` | HTTP / `http://localhost:5173` | Vite Dev Server (`localhost:5173`) |

---

## 2. Secrets & Configuration Architecture

To comply with enterprise security standards, AbonStock separates base configuration from runtime secrets:
- Base configuration files (`appsettings.json`, `appsettings.Development.json`) contain **safe placeholders only**.
- Secrets are supplied at runtime via environment variables or User Secrets, ensuring credentials are never committed to version control.
- `Program.cs` implements a **fail-fast startup check**: the API refuses to start (`InvalidOperationException`) if `Jwt:SecretKey` contains `"PLACEHOLDER"`, `"YOUR_JWT"`, or is under 32 bytes (256 bits for HS256).

### A. Dynamic Secret Generation (PowerShell)
Instead of reusing fixed strings, generate a fresh cryptographically secure random signing key in your active terminal session using .NET's built-in `RandomNumberGenerator`:

```powershell
# Generate 32 cryptographically random bytes encoded as Base64 (44 characters / 256 bits)
$jwtKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# Assign to active PowerShell session (held in process memory only)
$env:Jwt__SecretKey = $jwtKey
```

> [!NOTE]
> Environment variables set via `$env:NAME = ...` persist only for the lifetime of that specific terminal session. Restarting or opening a new terminal requires re-running these commands.

### B. Demo-User Password Configuration
The backend authenticates demo users (`admin` and `staff`) against salted PBKDF2 password hashes. In `AuthService.cs`, configuration lookup adheres to the following precedence:
1. `Auth:Users:{index}:Password` (e.g. `Auth__Users__0__Password`)
2. `Auth:DefaultPassword` (e.g. `Auth__DefaultPassword`)

Set a chosen local password for your terminal session:
```powershell
$env:Auth__DefaultPassword = "<choose_a_strong_local_password>"
```
*(If no password is provided in configuration or environment variables, `AuthService` sets an empty hash and rejects all login attempts with `401 Unauthorized` as a fail-safe).*

### C. Database Connection String Configuration
Supply the connection string matching your local PostgreSQL container instance:
```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=abonstock_db;Username=abonstock;Password=<your_local_pg_password>"
```

---

## 3. Database Credentials & Persistent Volume Warning

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
│ 2. Confirm database name is dedicated to development (abonstock_db).   │
│ 3. Confirm the database contains NO production or irreplaceable data.  │
│ 4. If target isolation cannot be established: STOP IMMEDIATELY.        │
└────────────────────────────────────────────────────────────────────────┘
```

These operations represent distinct lifecycle phases:
- **Starting the PostgreSQL Container:** Starts the database engine daemon only.
- **Applying EF Core Migrations:** Creates or updates schema tables and constraints (`dotnet ef database update`).
- **Seeding Demo Data:** Populates sample products, batches, and orders (explicit opt-in via `Seeding__AutoSeed="true"`).
- **Starting the Web API:** Boots the HTTP pipeline and begins listening for requests.
- **Starting the Frontend:** Compiles and serves the React client SPA.

---

## 5. Step-by-Step Local Startup Procedure

### Terminal 1: Database & Backend Web API

#### Step 1: Open PowerShell and Launch PostgreSQL
```powershell
# 1. Navigate to repository root
cd C:\Users\trans\Downloads\Upwork

# 2. Start PostgreSQL container in background
docker compose up -d

# 3. Confirm container is healthy and listening on port 5432
docker ps --filter "name=abonstock-postgres"
```

#### Step 2: Configure Session Secrets
```powershell
# Generate dynamic 256-bit JWT signing key
$env:Jwt__SecretKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# Configure demo user password for this session
$env:Auth__DefaultPassword = "<choose_a_strong_local_password>"

# Configure local development connection string
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=abonstock_db;Username=abonstock;Password=abonstock_dev_pass_2026"
```

#### Step 3: Apply Database Migrations (After Safety Gate)
Confirm the target database is your dedicated local `abonstock_db`, then apply EF Core migrations:
```powershell
dotnet ef database update --project src/AbonStock.Api
```

#### Step 4: (Optional) Opt-in to Demo Data Seeding
`Seeding:AutoSeed` is disabled (`false`) by default. If starting with an empty database and you wish to populate demo products and batches:
```powershell
$env:Seeding__AutoSeed = "true"
```
*(The seeder is idempotent and acquires PostgreSQL transaction advisory locks to guarantee safety).*

#### Step 5: Start the Web API
```powershell
dotnet run --project src/AbonStock.Api --launch-profile http
```
The API is now running at `http://localhost:5299`.
- Verify Swagger UI: open `http://localhost:5299/swagger` in your browser.

---

### Terminal 2: React Frontend Application

#### Step 1: Open Terminal 2 and Configure Environment
```powershell
cd C:\Users\trans\Downloads\Upwork\frontend

# Ensure local environment file is created
cp .env.example .env
```
Confirm `.env` contains:
```env
VITE_API_BASE_URL=http://localhost:5299
```

#### Step 2: Install Dependencies & Launch Vite
```powershell
# Install packages
pnpm install   # or npm install

# Launch Vite development server
pnpm dev       # or npm run dev
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
4. **Automated Unit Tests:**
   - Backend: `dotnet test tests/AbonStock.Tests/AbonStock.Tests.csproj --filter FullyQualifiedName~Unit` (96 passed, 0 database calls).
   - Frontend: `cd frontend; pnpm test` (41 passed, mock fetch).

---

## 7. Troubleshooting Common Onboarding Issues

| Symptom / Error | Root Cause | Verified Resolution |
| :--- | :--- | :--- |
| **Startup Crash:** `InvalidOperationException: Jwt:SecretKey must be configured...` | Missing, placeholder, or short JWT signing key. | Generate a key using `[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))` and assign to `$env:Jwt__SecretKey`. |
| **Startup Crash:** `Npgsql.NpgsqlException: Connection refused` | PostgreSQL container is stopped or port 5432 is not exposed. | Run `docker compose up -d` and check `docker ps` to ensure the container is running and healthy. |
| **Database Error:** `28P01: password authentication failed for user "abonstock"` | Mismatch between connection string password and the password set when the persistent volume was created. | Check `.env.example` defaults or verify the password configured during initial volume creation. Do not delete volumes containing needed data. |
| **Login returns `401 Unauthorized`:** `"Invalid username or password."` | `Auth__DefaultPassword` was not set in the terminal before running the API. | Set `$env:Auth__DefaultPassword` to your chosen password and restart the backend. |
| **Frontend displays network error:** `Unable to connect to backend at http://localhost:5299` | Backend API is not running or listening on a different port. | Verify Terminal 1 is running `dotnet run --project src/AbonStock.Api --launch-profile http` and reachable at `http://localhost:5299`. |
| **CORS console error:** `Access-Control-Allow-Origin blocked` | Frontend launched on a non-standard origin port. | Ensure Vite runs on port 5173 (standard dev port allowed in `appsettings.Development.json`). |
| **HTTP `401 Unauthorized` on all Swagger endpoints** | Token was omitted or not prefixed with `Bearer `. | Enter `Bearer <token>` (with space) into the Swagger Authorize dialog. |
| **Database table not found error:** `relation "Products" does not exist` | EF Core migrations have not yet been applied to the database. | Run `dotnet ef database update --project src/AbonStock.Api` after confirming database isolation. |
