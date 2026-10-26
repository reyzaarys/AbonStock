import { test, describe, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { authService } from './authService.ts';
import { ApiError } from '../lib/apiClient.ts';
import type { LoginRequest, LoginResponse, AuthUser } from '../types/auth.ts';

describe('authService unit tests', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  test('authService.login sends POST to /api/auth/login with credentials', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    const mockResponse: LoginResponse = {
      token: 'mock-jwt-token-value',
      expiresIn: 28800,
      user: {
        id: 'usr_admin',
        username: 'admin',
        displayName: 'Administrator',
        role: 'Admin',
      },
    };

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? 'GET';
      capturedBody = String(init?.body);

      return new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const credentials: LoginRequest = {
      username: 'admin',
      password: 'demo-password',
    };

    const result = await authService.login(credentials);

    assert.ok(capturedUrl.endsWith('/api/auth/login'));
    assert.equal(capturedMethod, 'POST');
    assert.equal(capturedBody, JSON.stringify(credentials));
    assert.equal(result.token, 'mock-jwt-token-value');
    assert.equal(result.user.username, 'admin');
    assert.equal(result.user.role, 'Admin');
  });

  test('authService.login propagates 401 ApiError on invalid credentials', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          status: 401,
          title: 'Unauthorized',
          detail: 'Invalid username or password.',
        }),
        {
          status: 401,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await authService.login({ username: 'baduser', password: 'wrong' });
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 401);
        assert.equal(err.message, 'Invalid username or password.');
        return true;
      }
    );
  });

  test('authService.getCurrentUser sends GET to /api/auth/me and returns profile', async () => {
    let capturedUrl = '';
    let capturedMethod = '';

    const mockProfile: AuthUser = {
      id: 'usr_staff',
      username: 'staff',
      displayName: 'Warehouse Staff',
      role: 'Staff',
    };

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? 'GET';

      return new Response(JSON.stringify(mockProfile), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const result = await authService.getCurrentUser();

    assert.ok(capturedUrl.endsWith('/api/auth/me'));
    assert.equal(capturedMethod, 'GET');
    assert.equal(result.username, 'staff');
    assert.equal(result.role, 'Staff');
  });

  test('authService.getCurrentUser propagates 401 when token is expired or invalid', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          status: 401,
          title: 'Unauthorized',
          detail: 'User profile not found.',
        }),
        {
          status: 401,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await authService.getCurrentUser();
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 401);
        return true;
      }
    );
  });
});
