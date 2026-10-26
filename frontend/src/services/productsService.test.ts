import { test, describe, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { productsService } from './productsService.ts';
import { ApiError } from '../lib/apiClient.ts';
import type { CreateProductRequest, UpdateProductRequest } from '../types';

describe('productsService unit tests', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  test('productsService.create sends POST to /api/products with JSON payload', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');

      return new Response(
        JSON.stringify({
          id: 10,
          sku: 'ABON-SAPI-50G',
          name: 'Abon Sapi 50g',
          description: 'Kemasan mini',
          unitPrice: 18000,
          quantityOnHand: 0,
          isActive: true,
          createdAtUtc: '2026-10-24T10:00:00Z',
          updatedAtUtc: '2026-10-24T10:00:00Z',
          version: 1,
        }),
        {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: CreateProductRequest = {
      sku: 'ABON-SAPI-50G',
      name: 'Abon Sapi 50g',
      description: 'Kemasan mini',
      unitPrice: 18000,
    };

    const result = await productsService.create(request);

    assert.ok(capturedUrl.endsWith('/api/products'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.id, 10);
    assert.equal(result.sku, 'ABON-SAPI-50G');
    assert.equal(result.version, 1);
  });

  test('productsService.update sends PUT to /api/products/:id with rowVersion token', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');

      return new Response(
        JSON.stringify({
          id: 10,
          sku: 'ABON-SAPI-50G',
          name: 'Abon Sapi 50g Updated',
          description: 'Updated desc',
          unitPrice: 20000,
          quantityOnHand: 0,
          isActive: true,
          createdAtUtc: '2026-10-24T10:00:00Z',
          updatedAtUtc: '2026-10-24T10:05:00Z',
          version: 2,
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: UpdateProductRequest = {
      name: 'Abon Sapi 50g Updated',
      description: 'Updated desc',
      unitPrice: 20000,
      rowVersion: 1,
    };

    const result = await productsService.update(10, request);

    assert.ok(capturedUrl.endsWith('/api/products/10'));
    assert.equal(capturedMethod, 'PUT');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.version, 2);
  });

  test('productsService.update propagates 409 Concurrency Conflict', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          status: 409,
          title: 'Concurrency Conflict',
          detail: 'Product with ID 10 has been modified by another operation. Please reload and retry.',
        }),
        {
          status: 409,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await productsService.update(10, {
          name: 'Stale update',
          unitPrice: 22000,
          rowVersion: 1,
        });
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 409);
        assert.equal(err.problem?.title, 'Concurrency Conflict');
        assert.ok(err.message.includes('modified by another operation'));
        return true;
      }
    );
  });

  test('productsService.deactivate sends PATCH to /api/products/:id/deactivate', async () => {
    let capturedUrl = '';
    let capturedMethod = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';

      return new Response(
        JSON.stringify({
          id: 10,
          sku: 'ABON-SAPI-50G',
          name: 'Abon Sapi 50g',
          unitPrice: 18000,
          quantityOnHand: 0,
          isActive: false,
          createdAtUtc: '2026-10-24T10:00:00Z',
          updatedAtUtc: '2026-10-24T10:10:00Z',
          version: 3,
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const result = await productsService.deactivate(10);

    assert.ok(capturedUrl.endsWith('/api/products/10/deactivate'));
    assert.equal(capturedMethod, 'PATCH');
    assert.equal(result.isActive, false);
  });
});
