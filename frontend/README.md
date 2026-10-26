# AbonStock — Frontend Application

Modern React 18 + TypeScript + Vite Single Page Application (SPA) client for the **AbonStock** inventory tracking and order fulfillment platform.

---

## 1. Technology Stack

- **UI Framework:** React 18.3 (`react`, `react-dom`)
- **Language:** TypeScript 5.5 (strict mode enabled)
- **Bundler & Dev Server:** Vite 5.4 with `@vitejs/plugin-react`
- **Routing:** React Router DOM v6 (`react-router-dom` v6.26)
- **Icons:** Lucide React (`lucide-react` v0.441)
- **Automated Testing:** Node.js native test runner (`node --experimental-strip-types --test`)
- **State & Storage:** React Context API (`AuthContext`) paired with browser `sessionStorage`

---

## 2. Getting Started & Installation

> [!TIP]
> For comprehensive full-stack setup, dynamic secret generation via PowerShell, database isolation safety gates, and end-to-end startup procedures, refer to the [Safe Local Development & Startup Guide](../docs/local-development.md).

### Prerequisites
- **Node.js:** v18.0.0 or later (v20+ recommended)
- **Package Manager:** `pnpm` (recommended) or `npm`
- **Backend Web API:** Running at `http://localhost:5299`

---

### Step 1: Install Dependencies
```bash
cd frontend
pnpm install
# or
npm install
```

---

### Step 2: Configure Environment Variables
Copy the example environment configuration:
```bash
cp .env.example .env
```

The default API base URL is configured to point to the local ASP.NET Core 8 Web API:
```env
VITE_API_BASE_URL=http://localhost:5299
```
*(The API client trims trailing slashes and falls back to `http://localhost:5299` if the environment variable is not defined).*

---

### Step 3: Start Vite Development Server
```bash
pnpm dev
# or
npm run dev
```

The web application starts at:
```text
http://localhost:5173
```
*(Port `5173` matches the `AllowFrontend` CORS policy configured on the backend).*

---

### Step 4: Build for Production
To typecheck and compile the optimized production bundle to `dist/`:
```bash
pnpm build
# or
npm run build
```

---

## 3. Application Routes & Navigation

The routing hierarchy is defined in [`src/app/router.tsx`](src/app/router.tsx):

| Route Path | Access Type | Page Component | Description |
| :--- | :---: | :--- | :--- |
| `/login` | Public | [`LoginPage`](src/features/auth/LoginPage.tsx) | Clean credentials login form with validation, spinner feedback, and error alerts. Redirects to previous destination upon successful login. |
| `/` | Protected | Redirect | Automatically redirects to `/inventory` if authenticated, or `/login` if unauthenticated. |
| `/inventory` | Protected | [`InventoryPage`](src/features/inventory/InventoryPage.tsx) | Batch inventory tracking, QC inspection gate, stock receipts, and stock adjustments. |
| `/products` | Protected | [`ProductsPage`](src/features/products/ProductsPage.tsx) | Product catalog management, unit pricing, aggregate on-hand stock, and active status toggles. |
| `/orders` | Protected | [`OrdersPage`](src/features/orders/OrdersPage.tsx) | Order lifecycle management: draft creation, FEFO stock reservation, fulfillment, and cancellation. |
| `*` | Catch-All | Redirect | Redirects unknown routes to `/inventory`. |

---

## 4. Authentication & Session Lifecycle

Authentication is coordinated centrally via [`AuthContext`](src/context/AuthContext.tsx) and enforced via [`ProtectedRoute`](src/components/auth/ProtectedRoute.tsx):

```text
Browser Navigation Flow:
Unauthenticated Request ──> ProtectedRoute ──> Navigate to /login (Preserves location.state.from)
                                                         │
                                                  Submit Credentials
                                                         │
                                                         ▼
                                               POST /api/auth/login
                                                         │
                                        ┌────────────────┴────────────────┐
                                        ▼                                 ▼
                                  200 Success                       401 Failure
                           Store token in sessionStorage       Display error banner
                           Navigate to location.state.from     (No redirect loop)
```

### Key Architectural Mechanisms:
1. **Dynamic Bearer Injection (`src/lib/apiClient.ts`):** The HTTP client registers a token provider callback with `AuthContext`. Every outgoing request to protected endpoints automatically attaches `Authorization: Bearer <token>`.
2. **Session Restoration on Page Reload:** When a user refreshes the browser tab, `AuthContext` retrieves the stored token from `sessionStorage` and immediately validates it against `GET /api/auth/me`. If the token has expired or is revoked, the session is purged and the user is redirected to `/login`.
3. **Flash-Free Route Guards (`src/components/auth/ProtectedRoute.tsx`):** While session validation is in progress, the route guard renders a verifying loading indicator (`LoadingState`), preventing any brief flash of private business data.
4. **Isolated HTTP 401/403 Handling:**
   - **401 Unauthorized:** If a protected endpoint returns 401, the API client automatically clears local session storage and triggers navigation to `/login`. Calling `/api/auth/login` itself does **not** trigger session clearing or redirect loops.
   - **403 Forbidden:** If an authenticated user lacks the required role, the API client throws an `ApiError` with status 403, and the user's active session is preserved.
5. **Session Termination (Logout):** The Logout button in the Navbar clears React state and purges `sessionStorage`, immediately redirecting the browser to `/login`.

---

## 5. Role-Aware UI Permissions (Admin vs. Staff)

While the backend Web API is the authoritative security boundary, the React UI adapts based on the authenticated user's role:

| Feature / Action | Administrator (`Admin`) Experience | Warehouse Staff (`Staff`) Experience |
| :--- | :--- | :--- |
| **Navbar Badge** | Blue `ADMIN` badge | Neutral `STAFF` badge |
| **Product Creation** | **+ Add Product** button is visible and active | Button is **hidden** |
| **Product Modification** | **Edit** and **Deactivate** buttons are active | Replaced with subtle **"View only"** label |
| **Stock Adjustments** | **Adjust** button active on Released batches | Replaced with neutral status indicator |
| **Stock Receipts** | Fully accessible | Fully accessible |
| **QC Release Gate** | Fully accessible | Fully accessible |
| **Order Lifecycle** | Full capability (Create, Confirm, Cancel, Fulfill) | Full capability (Create, Confirm, Cancel, Fulfill) |

---

## 6. Running Automated Tests & Code Quality

The frontend uses Node.js native test runner (`node --experimental-strip-types`) with mock Fetch API handlers, executing fast and without external test runner bloat:

### 1. Run Unit Tests (41 Tests)
```bash
pnpm test
# or
npm test
```
- **Test Suites (6 Suites, 41 Tests):**
  - `authSession & role authorization state unit tests`: Session persistence, token recovery, role checks.
  - `apiClient unit tests`: Query string formatting, Bearer header injection, 401/403 handling, ProblemDetails parsing.
  - `authService unit tests`: Login and profile fetching.
  - `inventoryService unit tests`: Receipt, QC release, adjustment, concurrency conflict handling.
  - `ordersService unit tests`: Order list, creation, confirmation, cancellation, and fulfillment.
  - `productsService unit tests`: Creation, updates, deactivation, and concurrency checks.

### 2. Strict TypeScript Type Check
```bash
pnpm exec tsc -b
```

### 3. Production Build
```bash
pnpm run build
```

---

## 7. Known Architectural Limitations

- **Browser Storage (`sessionStorage`):** JWT tokens and cached user profiles are stored in browser `sessionStorage`. This provides isolation per tab and automatic purging when the browser tab closes, but tokens could be read if an XSS vulnerability existed. The codebase defends against XSS by strictly avoiding `dangerouslySetInnerHTML` and `eval()`.
- **No Refresh Token Flow:** Tokens have an 8-hour lifetime. If a token expires during an active session, the next API request returns 401, requiring the user to log in again.
- **Backend Dependency:** The frontend requires an active ASP.NET Core 8 backend instance running at `http://localhost:5299`.
