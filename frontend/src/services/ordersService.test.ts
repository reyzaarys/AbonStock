import { test, describe, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { ordersService } from './ordersService.ts';
import { ApiError } from '../lib/apiClient.ts';
import type {
  CancelOrderRequest,
  ConfirmOrderRequest,
  CreateOrderRequest,
  FulfillOrderRequest,
} from '../types';

describe('ordersService unit tests', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  test('ordersService.list sends GET to /api/orders with serialized query params', async () => {
    let capturedUrl = '';
    let capturedMethod = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? 'GET';

      return new Response(
        JSON.stringify({
          items: [
            {
              id: 1,
              orderNumber: 'ORD-20261024-0001',
              customerName: 'Toko Berkah',
              customerEmail: 'berkah@example.com',
              status: 'Pending',
              totalAmount: 125000,
              createdAtUtc: '2026-10-24T10:00:00Z',
              updatedAtUtc: '2026-10-24T10:00:00Z',
              confirmedAtUtc: null,
              cancelledAtUtc: null,
              cancellationReason: null,
              completedAtUtc: null,
              version: 1,
              items: [
                {
                  id: 101,
                  productId: 2,
                  productSku: 'ABON-SAPI-100G',
                  productName: 'Abon Sapi Gurih 100g',
                  quantity: 5,
                  unitPrice: 25000,
                  lineTotal: 125000,
                },
              ],
            },
          ],
          page: 1,
          pageSize: 10,
          totalCount: 1,
          totalPages: 1,
          hasNextPage: false,
          hasPreviousPage: false,
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const result = await ordersService.list({
      page: 1,
      pageSize: 10,
      customerSearch: 'Berkah',
      status: 'Pending',
    });

    assert.equal(capturedMethod, 'GET');
    assert.ok(capturedUrl.includes('/api/orders'));
    assert.ok(capturedUrl.includes('customerSearch=Berkah'));
    assert.ok(capturedUrl.includes('status=Pending'));
    assert.equal(result.items.length, 1);
    assert.equal(result.items[0].orderNumber, 'ORD-20261024-0001');
    assert.equal(result.items[0].status, 'Pending');
  });

  test('ordersService.getById sends GET to /api/orders/:id and returns order', async () => {
    let capturedUrl = '';

    globalThis.fetch = async (input: RequestInfo | URL): Promise<Response> => {
      capturedUrl = String(input);

      return new Response(
        JSON.stringify({
          id: 42,
          orderNumber: 'ORD-20261024-0042',
          customerName: 'Ibu Siti',
          customerEmail: null,
          status: 'Confirmed',
          totalAmount: 75000,
          createdAtUtc: '2026-10-24T09:00:00Z',
          updatedAtUtc: '2026-10-24T09:30:00Z',
          confirmedAtUtc: '2026-10-24T09:30:00Z',
          cancelledAtUtc: null,
          cancellationReason: null,
          completedAtUtc: null,
          version: 2,
          items: [],
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const order = await ordersService.getById(42);

    assert.ok(capturedUrl.endsWith('/api/orders/42'));
    assert.equal(order.id, 42);
    assert.equal(order.orderNumber, 'ORD-20261024-0042');
    assert.equal(order.status, 'Confirmed');
    assert.equal(order.version, 2);
  });

  test('ordersService.create sends POST to /api/orders with request body', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');

      return new Response(
        JSON.stringify({
          id: 5,
          orderNumber: 'ORD-20261024-0005',
          customerName: 'Warung Madura',
          customerEmail: 'madura@example.com',
          status: 'Pending',
          totalAmount: 90000,
          createdAtUtc: '2026-10-24T10:15:00Z',
          updatedAtUtc: '2026-10-24T10:15:00Z',
          confirmedAtUtc: null,
          cancelledAtUtc: null,
          cancellationReason: null,
          completedAtUtc: null,
          version: 1,
          items: [
            {
              id: 12,
              productId: 3,
              productSku: 'ABON-AYAM-100G',
              productName: 'Abon Ayam Spesial 100g',
              quantity: 5,
              unitPrice: 18000,
              lineTotal: 90000,
            },
          ],
        }),
        {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: CreateOrderRequest = {
      customerName: 'Warung Madura',
      customerEmail: 'madura@example.com',
      items: [{ productId: 3, quantity: 5 }],
    };

    const result = await ordersService.create(request);

    assert.ok(capturedUrl.endsWith('/api/orders'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.id, 5);
    assert.equal(result.status, 'Pending');
    assert.equal(result.version, 1);
  });

  test('ordersService.confirm sends POST to /api/orders/:id/confirm with rowVersion', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');

      return new Response(
        JSON.stringify({
          id: 5,
          orderNumber: 'ORD-20261024-0005',
          customerName: 'Warung Madura',
          customerEmail: 'madura@example.com',
          status: 'Confirmed',
          totalAmount: 90000,
          createdAtUtc: '2026-10-24T10:15:00Z',
          updatedAtUtc: '2026-10-24T10:16:00Z',
          confirmedAtUtc: '2026-10-24T10:16:00Z',
          cancelledAtUtc: null,
          cancellationReason: null,
          completedAtUtc: null,
          version: 2,
          items: [],
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: ConfirmOrderRequest = { rowVersion: 1 };
    const result = await ordersService.confirm(5, request);

    assert.ok(capturedUrl.endsWith('/api/orders/5/confirm'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.status, 'Confirmed');
    assert.equal(result.version, 2);
  });

  test('ordersService.confirm handles 409 Conflict (insufficient stock / concurrency)', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          type: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
          title: 'Conflict',
          status: 409,
          detail: 'Insufficient available stock for product ABON-AYAM-100G. Requested: 10, Available: 4.',
        }),
        {
          status: 409,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await ordersService.confirm(5, { rowVersion: 1 });
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 409);
        assert.ok(err.message.includes('Insufficient available stock'));
        return true;
      }
    );
  });

  test('ordersService.cancel sends POST to /api/orders/:id/cancel with reason and rowVersion', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');

      return new Response(
        JSON.stringify({
          id: 5,
          orderNumber: 'ORD-20261024-0005',
          customerName: 'Warung Madura',
          customerEmail: 'madura@example.com',
          status: 'Cancelled',
          totalAmount: 90000,
          createdAtUtc: '2026-10-24T10:15:00Z',
          updatedAtUtc: '2026-10-24T10:18:00Z',
          confirmedAtUtc: null,
          cancelledAtUtc: '2026-10-24T10:18:00Z',
          cancellationReason: 'Customer requested change of address',
          completedAtUtc: null,
          version: 3,
          items: [],
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: CancelOrderRequest = {
      cancellationReason: 'Customer requested change of address',
      rowVersion: 2,
    };

    const result = await ordersService.cancel(5, request);

    assert.ok(capturedUrl.endsWith('/api/orders/5/cancel'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.status, 'Cancelled');
    assert.equal(result.cancellationReason, 'Customer requested change of address');
    assert.equal(result.version, 3);
  });

  test('ordersService.fulfill sends POST to /api/orders/:id/fulfill with rowVersion', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');

      return new Response(
        JSON.stringify({
          id: 5,
          orderNumber: 'ORD-20261024-0005',
          customerName: 'Warung Madura',
          customerEmail: 'madura@example.com',
          status: 'Completed',
          totalAmount: 90000,
          createdAtUtc: '2026-10-24T10:15:00Z',
          updatedAtUtc: '2026-10-24T10:20:00Z',
          confirmedAtUtc: '2026-10-24T10:16:00Z',
          cancelledAtUtc: null,
          cancellationReason: null,
          completedAtUtc: '2026-10-24T10:20:00Z',
          version: 4,
          items: [],
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: FulfillOrderRequest = { rowVersion: 3 };
    const result = await ordersService.fulfill(5, request);

    assert.ok(capturedUrl.endsWith('/api/orders/5/fulfill'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.status, 'Completed');
    assert.equal(result.version, 4);
  });

  test('ordersService.fulfill handles 409 Concurrency Conflict', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          type: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
          title: 'Conflict',
          status: 409,
          detail: 'Order 5 has been modified concurrently. Please reload.',
        }),
        {
          status: 409,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await ordersService.fulfill(5, { rowVersion: 2 });
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 409);
        assert.ok(err.message.includes('modified concurrently'));
        return true;
      }
    );
  });
});
