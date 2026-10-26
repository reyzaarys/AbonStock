import { test, describe, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { inventoryService } from './inventoryService.ts';
import { ApiError } from '../lib/apiClient.ts';
import type {
  ReleaseBatchRequest,
  StockAdjustmentRequest,
  StockReceiptRequest,
} from '../types';

describe('inventoryService unit tests', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  test('inventoryService.receiveStock sends POST to /api/inventory/receipts', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');

      return new Response(
        JSON.stringify({
          batchId: 101,
          productId: 1,
          batchNo: 'BAT-202610-01',
          productionDate: '2026-10-24',
          expiryDate: '2027-04-24',
          locationId: 'LOC_KDR_PROD',
          initialQuantity: 100,
          physicalQuantity: 100,
          status: 'PendingRelease',
          totalProductQuantityOnHand: 100,
          batchVersion: 1,
          productVersion: 1,
          createdAtUtc: '2026-10-24T10:00:00Z',
        }),
        {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: StockReceiptRequest = {
      productId: 1,
      batchNo: 'BAT-202610-01',
      productionDate: '2026-10-24',
      expiryDate: '2027-04-24',
      locationId: 'LOC_KDR_PROD',
      quantity: 100,
      reason: 'Initial production receipt',
    };

    const result = await inventoryService.receiveStock(request);

    assert.ok(capturedUrl.endsWith('/api/inventory/receipts'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.batchId, 101);
    assert.equal(result.status, 'PendingRelease');
    assert.equal(result.physicalQuantity, 100);
  });

  test('inventoryService.releaseBatch sends POST to /api/inventory/batches/:id/release with 3-point QC checklist', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');

      return new Response(
        JSON.stringify({
          id: 101,
          productId: 1,
          productSku: 'ABON-01',
          productName: 'Abon Sapi',
          batchNo: 'BAT-202610-01',
          productionDate: '2026-10-24',
          expiryDate: '2027-04-24',
          locationId: 'LOC_KDR_PROD',
          initialQuantity: 100,
          physicalQuantity: 100,
          reservedQuantity: 0,
          availableQuantity: 100,
          status: 'Released',
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

    const request: ReleaseBatchRequest = {
      visualCheckPassed: true,
      aromaCheckPassed: true,
      packagingCheckPassed: true,
      notes: 'QC passed',
      rowVersion: 1,
    };

    const result = await inventoryService.releaseBatch(101, request);

    assert.ok(capturedUrl.endsWith('/api/inventory/batches/101/release'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.status, 'Released');
    assert.equal(result.version, 2);
  });

  test('inventoryService.releaseBatch propagates 400 when QC checks fail', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          status: 400,
          title: 'Validation Failed',
          detail: 'All quality checks (Visual, Aroma, Packaging) must pass to release the batch.',
        }),
        {
          status: 400,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await inventoryService.releaseBatch(101, {
          visualCheckPassed: true,
          aromaCheckPassed: false,
          packagingCheckPassed: true,
          rowVersion: 1,
        });
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 400);
        assert.ok(err.message.includes('All quality checks'));
        return true;
      }
    );
  });

  test('inventoryService.adjustStock sends POST to /api/inventory/adjustments with delta', async () => {
    let capturedUrl = '';
    let capturedMethod = '';
    let capturedBody = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? '';
      capturedBody = String(init?.body ?? '');

      return new Response(
        JSON.stringify({
          batchId: 101,
          productId: 1,
          previousPhysicalQuantity: 100,
          newPhysicalQuantity: 95,
          previousProductQuantityOnHand: 100,
          newProductQuantityOnHand: 95,
          status: 'Released',
          batchVersion: 3,
          productVersion: 2,
          timestampUtc: '2026-10-24T10:10:00Z',
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: StockAdjustmentRequest = {
      batchId: 101,
      quantityChange: -5,
      reason: 'Damage during handling',
      batchRowVersion: 2,
    };

    const result = await inventoryService.adjustStock(request);

    assert.ok(capturedUrl.endsWith('/api/inventory/adjustments'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.newPhysicalQuantity, 95);
    assert.equal(result.batchVersion, 3);
  });

  test('inventoryService.adjustStock propagates 409 concurrency conflict', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          status: 409,
          title: 'Concurrency Conflict',
          detail: 'Inventory has been modified by another concurrent operation. Please reload and retry.',
        }),
        {
          status: 409,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await inventoryService.adjustStock({
          batchId: 101,
          quantityChange: -10,
          reason: 'Spoilage',
          batchRowVersion: 1,
        });
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 409);
        assert.equal(err.problem?.title, 'Concurrency Conflict');
        return true;
      }
    );
  });
});
