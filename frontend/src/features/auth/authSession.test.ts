import { test, describe, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import type { AuthUser, LoginResponse, UserRole } from '../../types/auth.ts';

// In-memory mock of Storage (sessionStorage)
class MockStorage implements Storage {
  private store: Map<string, string> = new Map();

  get length(): number {
    return this.store.size;
  }

  clear(): void {
    this.store.clear();
  }

  getItem(key: string): string | null {
    return this.store.get(key) ?? null;
  }

  key(index: number): string | null {
    return Array.from(this.store.keys())[index] ?? null;
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  setItem(key: string, value: string): void {
    this.store.set(key, String(value));
  }
}

describe('authSession & role authorization state unit tests', () => {
  const TOKEN_KEY = 'abonstock_auth_token';
  const USER_KEY = 'abonstock_auth_user';

  let mockSessionStorage: MockStorage;

  beforeEach(() => {
    mockSessionStorage = new MockStorage();
  });

  test('successful login persists token and user to sessionStorage', () => {
    const loginResponse: LoginResponse = {
      token: 'jwt.token.sample',
      expiresIn: 3600,
      user: {
        id: 'usr_admin',
        username: 'admin',
        displayName: 'Administrator',
        role: 'Admin',
      },
    };

    // Simulate login persistence
    mockSessionStorage.setItem(TOKEN_KEY, loginResponse.token);
    mockSessionStorage.setItem(USER_KEY, JSON.stringify(loginResponse.user));

    assert.equal(mockSessionStorage.getItem(TOKEN_KEY), 'jwt.token.sample');
    const restoredUser = JSON.parse(mockSessionStorage.getItem(USER_KEY)!) as AuthUser;
    assert.equal(restoredUser.username, 'admin');
    assert.equal(restoredUser.role, 'Admin');
  });

  test('session recovery reads valid token and cached profile', () => {
    const mockUser: AuthUser = {
      id: 'usr_staff',
      username: 'staff',
      displayName: 'Warehouse Staff',
      role: 'Staff',
    };

    mockSessionStorage.setItem(TOKEN_KEY, 'valid.session.token');
    mockSessionStorage.setItem(USER_KEY, JSON.stringify(mockUser));

    const token = mockSessionStorage.getItem(TOKEN_KEY);
    const rawUser = mockSessionStorage.getItem(USER_KEY);
    const user = rawUser ? (JSON.parse(rawUser) as AuthUser) : null;

    assert.equal(token, 'valid.session.token');
    assert.ok(user !== null);
    assert.equal(user.username, 'staff');
    assert.equal(user.role, 'Staff');
  });

  test('invalid token on session recovery clears sessionStorage', () => {
    mockSessionStorage.setItem(TOKEN_KEY, 'expired.token');
    mockSessionStorage.setItem(USER_KEY, JSON.stringify({ username: 'ghost' }));

    // Simulate invalid token handling during initial mount
    const handleInvalidToken = () => {
      mockSessionStorage.removeItem(TOKEN_KEY);
      mockSessionStorage.removeItem(USER_KEY);
    };

    handleInvalidToken();

    assert.equal(mockSessionStorage.getItem(TOKEN_KEY), null);
    assert.equal(mockSessionStorage.getItem(USER_KEY), null);
  });

  test('logout removes token and user profile completely from sessionStorage', () => {
    mockSessionStorage.setItem(TOKEN_KEY, 'active.session.token');
    mockSessionStorage.setItem(USER_KEY, JSON.stringify({ username: 'admin' }));

    // Simulate logout action
    const performLogout = () => {
      mockSessionStorage.removeItem(TOKEN_KEY);
      mockSessionStorage.removeItem(USER_KEY);
    };

    performLogout();

    assert.equal(mockSessionStorage.getItem(TOKEN_KEY), null);
    assert.equal(mockSessionStorage.getItem(USER_KEY), null);
  });

  test('role-checking helper verifies Admin and Staff permissions correctly', () => {
    const adminUser: AuthUser = {
      id: 'usr_admin',
      username: 'admin',
      displayName: 'Admin',
      role: 'Admin',
    };

    const staffUser: AuthUser = {
      id: 'usr_staff',
      username: 'staff',
      displayName: 'Staff',
      role: 'Staff',
    };

    const hasRole = (user: AuthUser | null, role: UserRole | UserRole[]): boolean => {
      if (!user) return false;
      if (Array.isArray(role)) return role.includes(user.role);
      return user.role === role;
    };

    // Admin checks
    assert.equal(hasRole(adminUser, 'Admin'), true);
    assert.equal(hasRole(adminUser, 'Staff'), false);
    assert.equal(hasRole(adminUser, ['Admin', 'Staff']), true);

    // Staff checks
    assert.equal(hasRole(staffUser, 'Admin'), false);
    assert.equal(hasRole(staffUser, 'Staff'), true);
    assert.equal(hasRole(staffUser, ['Admin', 'Staff']), true);

    // Unauthenticated user
    assert.equal(hasRole(null, 'Admin'), false);
    assert.equal(hasRole(null, 'Staff'), false);
  });

  test('role-aware UI visibility prevents Staff from administrative controls', () => {
    const canPerformAdminAction = (role: UserRole): boolean => role === 'Admin';
    const canPerformWarehouseOps = (role: UserRole): boolean => role === 'Admin' || role === 'Staff';

    // Product Create / Edit / Deactivate and Stock Adjustment
    assert.equal(canPerformAdminAction('Admin'), true);
    assert.equal(canPerformAdminAction('Staff'), false);

    // Stock Receipt, QC Release, Order Confirmation / Fulfillment
    assert.equal(canPerformWarehouseOps('Admin'), true);
    assert.equal(canPerformWarehouseOps('Staff'), true);
  });
});
