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

  test('ordersService.getById loads complete order with line items and item totals', async () => {
    let capturedUrl = '';

    globalThis.fetch = async (input: RequestInfo | URL): Promise<Response> => {
      capturedUrl = String(input);

      return new Response(
        JSON.stringify({
          id: 10,
          orderNumber: 'ORD-20261025-0010',
          customerName: 'Resto Padang Sederhana',
          customerEmail: 'order@sederhana.com',
          status: 'Confirmed',
          totalAmount: 250000,
          createdAtUtc: '2026-10-25T08:00:00Z',
          updatedAtUtc: '2026-10-25T08:15:00Z',
          confirmedAtUtc: '2026-10-25T08:15:00Z',
          cancelledAtUtc: null,
          cancellationReason: null,
          completedAtUtc: null,
          version: 2,
          items: [
            {
              id: 201,
              productId: 1,
              productSku: 'ABON-SAPI-250G',
              productName: 'Abon Sapi Premium 250g',
              quantity: 2,
              unitPrice: 65000,
              lineTotal: 130000,
            },
            {
              id: 202,
              productId: 2,
              productSku: 'ABON-AYAM-250G',
              productName: 'Abon Ayam Spesial 250g',
              quantity: 3,
              unitPrice: 40000,
              lineTotal: 120000,
            },
          ],
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const order = await ordersService.getById(10);

    assert.ok(capturedUrl.endsWith('/api/orders/10'));
    assert.equal(order.id, 10);
    assert.equal(order.orderNumber, 'ORD-20261025-0010');
    assert.equal(order.customerName, 'Resto Padang Sederhana');
    assert.equal(order.customerEmail, 'order@sederhana.com');
    assert.equal(order.items.length, 2);
    assert.equal(order.items[0].productSku, 'ABON-SAPI-250G');
    assert.equal(order.items[0].quantity, 2);
    assert.equal(order.items[0].lineTotal, 130000);
    assert.equal(order.items[1].productSku, 'ABON-AYAM-250G');
    assert.equal(order.items[1].quantity, 3);
    assert.equal(order.items[1].lineTotal, 120000);
    const sumLines = order.items.reduce((s, it) => s + it.lineTotal, 0);
    assert.equal(sumLines, order.totalAmount);
  });

  test('ordersService.getById handles Completed order with completion timestamp', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          id: 11,
          orderNumber: 'ORD-20261025-0011',
          customerName: 'Catering Berkah',
          customerEmail: 'catering@berkah.com',
          status: 'Completed',
          totalAmount: 180000,
          createdAtUtc: '2026-10-25T07:00:00Z',
          updatedAtUtc: '2026-10-25T07:30:00Z',
          confirmedAtUtc: '2026-10-25T07:10:00Z',
          cancelledAtUtc: null,
          cancellationReason: null,
          completedAtUtc: '2026-10-25T07:30:00Z',
          version: 3,
          items: [
            {
              id: 205,
              productId: 3,
              productSku: 'ABON-PEDAS-100G',
              productName: 'Abon Pedas Manis 100g',
              quantity: 10,
              unitPrice: 18000,
              lineTotal: 180000,
            },
          ],
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const order = await ordersService.getById(11);
    assert.equal(order.status, 'Completed');
    assert.equal(order.completedAtUtc, '2026-10-25T07:30:00Z');
    assert.equal(order.confirmedAtUtc, '2026-10-25T07:10:00Z');
    assert.equal(order.cancelledAtUtc, null);
    assert.equal(order.cancellationReason, null);
  });

  test('ordersService.getById handles Cancelled order with reason and cancellation timestamp', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          id: 12,
          orderNumber: 'ORD-20261025-0012',
          customerName: 'Pak Budi',
          customerEmail: null,
          status: 'Cancelled',
          totalAmount: 50000,
          createdAtUtc: '2026-10-25T06:00:00Z',
          updatedAtUtc: '2026-10-25T06:20:00Z',
          confirmedAtUtc: '2026-10-25T06:05:00Z',
          cancelledAtUtc: '2026-10-25T06:20:00Z',
          cancellationReason: 'Customer requested cancellation prior to dispatch',
          completedAtUtc: null,
          version: 3,
          items: [
            {
              id: 208,
              productId: 2,
              productSku: 'ABON-SAPI-100G',
              productName: 'Abon Sapi Gurih 100g',
              quantity: 2,
              unitPrice: 25000,
              lineTotal: 50000,
            },
          ],
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const order = await ordersService.getById(12);
    assert.equal(order.status, 'Cancelled');
    assert.equal(order.cancelledAtUtc, '2026-10-25T06:20:00Z');
    assert.equal(order.cancellationReason, 'Customer requested cancellation prior to dispatch');
    assert.equal(order.completedAtUtc, null);
  });

  test('ordersService.getById handles null optional fields gracefully', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          id: 13,
          orderNumber: 'ORD-20261025-0013',
          customerName: 'Walk-in Customer',
          customerEmail: null,
          status: 'Pending',
          totalAmount: 25000,
          createdAtUtc: '2026-10-25T09:00:00Z',
          updatedAtUtc: '2026-10-25T09:00:00Z',
          confirmedAtUtc: null,
          cancelledAtUtc: null,
          cancellationReason: null,
          completedAtUtc: null,
          version: 1,
          items: [],
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const order = await ordersService.getById(13);
    assert.equal(order.customerEmail, null);
    assert.equal(order.confirmedAtUtc, null);
    assert.equal(order.completedAtUtc, null);
    assert.equal(order.cancelledAtUtc, null);
    assert.equal(order.cancellationReason, null);
    assert.equal(order.items.length, 0);
  });

  test('ordersService.getById propagates 404 ApiError when order not found', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
          title: 'Not Found',
          status: 404,
          detail: 'Order with ID 9999 was not found.',
        }),
        {
          status: 404,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await ordersService.getById(9999);
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 404);
        assert.ok(err.message.includes('Order with ID 9999 was not found'));
        return true;
      }
    );
  });

  test('ordersService.getById propagates 500 ApiError on server error', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
          title: 'Internal Server Error',
          status: 500,
          detail: 'Database connection failed unexpectedly.',
        }),
        {
          status: 500,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await ordersService.getById(10);
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 500);
        return true;
      }
    );
  });

  test('ordersService.getById respects AbortSignal', async () => {
    const controller = new AbortController();
    controller.abort();

    globalThis.fetch = async (_input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      if (init?.signal?.aborted) {
        throw new DOMException('The operation was aborted.', 'AbortError');
      }
      return new Response('{}', { status: 200 });
    };

    await assert.rejects(
      async () => {
        await ordersService.getById(10, controller.signal);
      },
      (err: unknown) => {
        assert.ok(err instanceof DOMException);
        assert.equal(err.name, 'AbortError');
        return true;
      }
    );
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
