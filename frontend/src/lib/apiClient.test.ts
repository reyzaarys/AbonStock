import { test, describe, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiError,
  apiClient,
  buildQueryString,
  getBaseUrl,
} from './apiClient.ts';

describe('apiClient unit tests', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  test('getBaseUrl returns trimmed base URL without trailing slash', () => {
    const url = getBaseUrl();
    assert.ok(url.startsWith('http'));
    assert.ok(!url.endsWith('/'));
  });

  test('buildQueryString serializes params correctly and skips empty values', () => {
    const query = buildQueryString({
      page: 1,
      pageSize: 20,
      search: 'abon',
      includeInactive: false,
      emptyStr: '',
      nullVal: null,
      undefVal: undefined,
    });

    assert.equal(query, '?page=1&pageSize=20&search=abon&includeInactive=false');
  });

  test('buildQueryString returns empty string when params is empty or undefined', () => {
    assert.equal(buildQueryString(), '');
    assert.equal(buildQueryString({}), '');
  });

  test('apiClient.get makes GET request without Content-Type header', async () => {
    let capturedUrl = '';
    let capturedOptions: RequestInit | undefined;

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedOptions = init;
      return new Response(JSON.stringify({ success: true }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const data = await apiClient.get<{ success: boolean }>('/api/test', {
      params: { page: 2 },
    });

    assert.deepEqual(data, { success: true });
    assert.ok(capturedUrl.endsWith('/api/test?page=2'));
    assert.equal(capturedOptions?.method, 'GET');
    const headers = capturedOptions?.headers as Headers;
    assert.equal(headers?.get('Content-Type'), null);
  });

  test('apiClient.post sends JSON body and Content-Type header', async () => {
    let capturedBody = '';
    let capturedContentType = '';

    globalThis.fetch = async (_input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedBody = String(init?.body);
      const headers = init?.headers as Headers;
      capturedContentType = headers.get('Content-Type') ?? '';
      return new Response(JSON.stringify({ id: 1 }), {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const payload = { sku: 'ABON-01', name: 'Abon Sapi' };
    const res = await apiClient.post<{ id: number }>('/api/products', payload);

    assert.equal(res.id, 1);
    assert.equal(capturedContentType, 'application/json');
    assert.equal(capturedBody, JSON.stringify(payload));
  });

  test('apiClient parses ProblemDetails on HTTP 400/404/409', async () => {
    const problem = {
      status: 409,
      title: 'Conflict',
      detail: 'A product with this SKU already exists.',
      instance: '/api/products',
    };

    globalThis.fetch = async (): Promise<Response> => {
      return new Response(JSON.stringify(problem), {
        status: 409,
        headers: { 'Content-Type': 'application/problem+json' },
      });
    };

    await assert.rejects(
      async () => {
        await apiClient.post('/api/products', { sku: 'ABON-EXISTING' });
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 409);
        assert.equal(err.message, 'A product with this SKU already exists.');
        assert.equal(err.isNetworkError, false);
        assert.deepEqual(err.problem, problem);
        return true;
      }
    );
  });

  test('apiClient parses validation errors dictionary from ValidationProblemDetails', async () => {
    const validationProblem = {
      status: 400,
      title: 'Validation Failed',
      detail: 'One or more validation errors occurred.',
      errors: {
        Quantity: ['Quantity must be greater than 0.'],
        BatchNo: ['BatchNo is required.'],
      },
    };

    globalThis.fetch = async (): Promise<Response> => {
      return new Response(JSON.stringify(validationProblem), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    await assert.rejects(
      async () => {
        await apiClient.post('/api/inventory/receipts', {});
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 400);
        // detail takes priority or errors are captured in err.problem
        assert.equal(err.problem?.errors?.Quantity?.[0], 'Quantity must be greater than 0.');
        return true;
      }
    );
  });

  test('apiClient handles network failure gracefully', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      throw new TypeError('Failed to fetch');
    };

    await assert.rejects(
      async () => {
        await apiClient.get('/api/products');
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 0);
        assert.equal(err.isNetworkError, true);
        assert.ok(err.message.includes('Network error: Unable to connect to backend'));
        return true;
      }
    );
  });

  test('apiClient respects AbortSignal', async () => {
    const controller = new AbortController();
    controller.abort();

    globalThis.fetch = async (_input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      if (init?.signal?.aborted) {
        throw new DOMException('The operation was aborted.', 'AbortError');
      }
      return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    await assert.rejects(
      async () => {
        await apiClient.get('/api/products', { signal: controller.signal });
      },
      (err: unknown) => {
        assert.ok(err instanceof DOMException);
        assert.equal(err.name, 'AbortError');
        return true;
      }
    );
  });

  test('apiClient attaches Authorization Bearer token when tokenProvider is set', async () => {
    let capturedAuthHeader: string | null = null;
    apiClient.setTokenProvider(() => 'test-jwt-token-xyz');

    globalThis.fetch = async (_input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      const headers = init?.headers as Headers;
      capturedAuthHeader = headers?.get('Authorization') ?? null;
      return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    try {
      await apiClient.get('/api/products');
      assert.equal(capturedAuthHeader, 'Bearer test-jwt-token-xyz');
    } finally {
      apiClient.clearTokenProvider();
    }
  });

  test('apiClient does not attach Authorization header if tokenProvider returns null', async () => {
    let capturedAuthHeader: string | null = null;
    apiClient.setTokenProvider(() => null);

    globalThis.fetch = async (_input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      const headers = init?.headers as Headers;
      capturedAuthHeader = headers?.get('Authorization') ?? null;
      return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    try {
      await apiClient.get('/api/products');
      assert.equal(capturedAuthHeader, null);
    } finally {
      apiClient.clearTokenProvider();
    }
  });

  test('apiClient triggers unauthorizedHandler on HTTP 401 for protected endpoints', async () => {
    let handlerCalled = false;
    apiClient.setUnauthorizedHandler(() => {
      handlerCalled = true;
    });

    globalThis.fetch = async (): Promise<Response> => {
      return new Response(JSON.stringify({ title: 'Unauthorized' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    try {
      await assert.rejects(async () => {
        await apiClient.get('/api/products');
      });
      assert.equal(handlerCalled, true);
    } finally {
      apiClient.clearUnauthorizedHandler();
    }
  });

  test('apiClient does NOT trigger unauthorizedHandler on HTTP 401 for /api/auth/login', async () => {
    let handlerCalled = false;
    apiClient.setUnauthorizedHandler(() => {
      handlerCalled = true;
    });

    globalThis.fetch = async (): Promise<Response> => {
      return new Response(JSON.stringify({ title: 'Unauthorized', detail: 'Invalid credentials' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    try {
      await assert.rejects(async () => {
        await apiClient.post('/api/auth/login', { username: 'admin', password: 'bad' });
      });
      assert.equal(handlerCalled, false);
    } finally {
      apiClient.clearUnauthorizedHandler();
    }
  });

  test('apiClient does NOT trigger unauthorizedHandler on HTTP 403 Forbidden', async () => {
    let handlerCalled = false;
    apiClient.setUnauthorizedHandler(() => {
      handlerCalled = true;
    });

    globalThis.fetch = async (): Promise<Response> => {
      return new Response(JSON.stringify({ title: 'Forbidden' }), {
        status: 403,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    try {
      await assert.rejects(async () => {
        await apiClient.post('/api/inventory/adjustments', {});
      });
      assert.equal(handlerCalled, false);
    } finally {
      apiClient.clearUnauthorizedHandler();
    }
  });
});
