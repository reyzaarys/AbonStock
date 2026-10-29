import { test, describe, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { inventoryService } from './inventoryService.ts';
import { ApiError } from '../lib/apiClient.ts';
import type {
  QuarantineBatchRequest,
  ReleaseBatchRequest,
  StockAdjustmentRequest,
  StockReceiptRequest,
  WriteOffBatchRequest,
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

  test('inventoryService.quarantineBatch sends POST to /api/inventory/batches/:id/quarantine with payload', async () => {
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
          productName: 'Abon Sapi Original',
          batchNo: 'BAT-202610-01',
          productionDate: '2026-10-24',
          expiryDate: '2027-04-24',
          locationId: 'LOC_KDR_PROD',
          initialQuantity: 100,
          physicalQuantity: 100,
          reservedQuantity: 0,
          availableQuantity: 100,
          status: 'Quarantined',
          createdAtUtc: '2026-10-24T10:00:00Z',
          updatedAtUtc: '2026-10-24T11:00:00Z',
          version: 2,
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: QuarantineBatchRequest = {
      reason: 'Sensory aroma defect detected',
      notes: 'Sent sample to QA laboratory',
      rowVersion: 1,
    };

    const result = await inventoryService.quarantineBatch(101, request);

    assert.ok(capturedUrl.endsWith('/api/inventory/batches/101/quarantine'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.id, 101);
    assert.equal(result.status, 'Quarantined');
    assert.equal(result.version, 2);
  });

  test('inventoryService.quarantineBatch propagates 409 conflict when batch has active reservations or conflict', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          status: 409,
          title: 'Conflict',
          detail: 'Cannot quarantine batch because it has 15 active reserved units.',
        }),
        {
          status: 409,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await inventoryService.quarantineBatch(101, {
          reason: 'Defect detected',
          rowVersion: 1,
        });
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 409);
        assert.ok(err.message.includes('15 active reserved units'));
        return true;
      }
    );
  });

  test('inventoryService.writeOffBatch sends POST to /api/inventory/batches/:id/write-off with payload', async () => {
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
          writtenOffQuantity: 25,
          previousPhysicalQuantity: 100,
          newPhysicalQuantity: 75,
          previousProductQuantityOnHand: 200,
          newProductQuantityOnHand: 175,
          status: 'Quarantined',
          movementId: 88,
          batchVersion: 3,
          productVersion: 4,
          timestampUtc: '2026-10-24T12:00:00Z',
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const request: WriteOffBatchRequest = {
      quantity: 25,
      reason: 'Physical leak and severe packaging damage',
      batchRowVersion: 2,
      disposalDocumentNumber: 'BA-DISP-2026/01',
    };

    const result = await inventoryService.writeOffBatch(101, request);

    assert.ok(capturedUrl.endsWith('/api/inventory/batches/101/write-off'));
    assert.equal(capturedMethod, 'POST');
    assert.deepEqual(JSON.parse(capturedBody), request);
    assert.equal(result.batchId, 101);
    assert.equal(result.writtenOffQuantity, 25);
    assert.equal(result.newPhysicalQuantity, 75);
    assert.equal(result.newProductQuantityOnHand, 175);
    assert.equal(result.batchVersion, 3);
  });

  test('inventoryService.writeOffBatch propagates 409 conflict on concurrency conflict or insufficient unreserved quantity', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          status: 409,
          title: 'Concurrency Conflict',
          detail: 'Batch with ID 101 has been modified by another operation. Please reload and retry.',
        }),
        {
          status: 409,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await inventoryService.writeOffBatch(101, {
          quantity: 10,
          reason: 'Disposal',
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

  test('inventoryService.writeOffBatch partial write-off from Released preserves Released status', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          batchId: 202,
          productId: 2,
          batchNo: 'BAT-202610-REL01',
          writtenOffQuantity: 10,
          previousPhysicalQuantity: 50,
          newPhysicalQuantity: 40,
          previousProductQuantityOnHand: 100,
          newProductQuantityOnHand: 90,
          status: 'Released',
          movementId: 89,
          batchVersion: 5,
          productVersion: 6,
          timestampUtc: '2026-10-24T12:30:00Z',
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const result = await inventoryService.writeOffBatch(202, {
      quantity: 10,
      reason: 'Partial spoilage in open warehouse pack',
      batchRowVersion: 4,
    });

    assert.equal(result.batchId, 202);
    assert.equal(result.writtenOffQuantity, 10);
    assert.equal(result.newPhysicalQuantity, 40);
    assert.equal(result.status, 'Released');
    assert.equal(result.batchVersion, 5);
  });

  test('inventoryService.writeOffBatch full write-off transitions status to Depleted', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          batchId: 303,
          productId: 3,
          batchNo: 'BAT-202610-QRN01',
          writtenOffQuantity: 40,
          previousPhysicalQuantity: 40,
          newPhysicalQuantity: 0,
          previousProductQuantityOnHand: 80,
          newProductQuantityOnHand: 40,
          status: 'Depleted',
          movementId: 90,
          batchVersion: 7,
          productVersion: 8,
          timestampUtc: '2026-10-24T13:00:00Z',
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const result = await inventoryService.writeOffBatch(303, {
      quantity: 40,
      reason: 'Full batch incineration after total microbial quarantine',
      batchRowVersion: 6,
      disposalDocumentNumber: 'BA-TOTAL-DISP-001',
    });

    assert.equal(result.batchId, 303);
    assert.equal(result.writtenOffQuantity, 40);
    assert.equal(result.newPhysicalQuantity, 0);
    assert.equal(result.status, 'Depleted');
    assert.equal(result.batchVersion, 7);
  });

  test('inventoryService.getMovements sends GET to /api/inventory/movements with query parameters', async () => {
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
              productId: 10,
              productSku: 'ABON-SAPI-100',
              productName: 'Abon Sapi Gurih 100g',
              inventoryBatchId: 25,
              batchNo: 'BAT-202610-01',
              movementType: 'Receipt',
              quantityChange: 50,
              balanceAfter: 150,
              batchBalanceAfter: 50,
              referenceOrderId: null,
              reason: 'Initial production receipt',
              createdAtUtc: '2026-10-24T10:00:00Z',
            },
          ],
          page: 2,
          pageSize: 10,
          totalCount: 25,
          totalPages: 3,
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const result = await inventoryService.getMovements({
      page: 2,
      pageSize: 10,
      movementType: 'Receipt',
      productId: 10,
      batchId: 25,
    });

    assert.equal(capturedMethod, 'GET');
    const url = new URL(capturedUrl);
    assert.equal(url.pathname, '/api/inventory/movements');
    assert.equal(url.searchParams.get('page'), '2');
    assert.equal(url.searchParams.get('pageSize'), '10');
    assert.equal(url.searchParams.get('movementType'), 'Receipt');
    assert.equal(url.searchParams.get('productId'), '10');
    assert.equal(url.searchParams.get('batchId'), '25');

    assert.equal(result.items.length, 1);
    assert.equal(result.page, 2);
    assert.equal(result.pageSize, 10);
    assert.equal(result.totalCount, 25);
    assert.equal(result.totalPages, 3);
    assert.equal(result.items[0].movementType, 'Receipt');
    assert.equal(result.items[0].quantityChange, 50);
    assert.equal(result.items[0].balanceAfter, 150);
    assert.equal(result.items[0].batchBalanceAfter, 50);
    assert.equal(result.items[0].inventoryBatchId, 25);
    assert.equal(result.items[0].referenceOrderId, null);
  });

  test('inventoryService.getMovements with empty query sends default GET', async () => {
    let capturedUrl = '';

    globalThis.fetch = async (input: RequestInfo | URL): Promise<Response> => {
      capturedUrl = String(input);
      return new Response(
        JSON.stringify({
          items: [],
          page: 1,
          pageSize: 20,
          totalCount: 0,
          totalPages: 0,
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const result = await inventoryService.getMovements();
    const url = new URL(capturedUrl);
    assert.equal(url.pathname, '/api/inventory/movements');
    assert.equal(url.searchParams.get('page'), null);
    assert.equal(result.totalCount, 0);
  });

  test('inventoryService.getMovements propagates AbortSignal', async () => {
    const controller = new AbortController();
    let capturedSignal: AbortSignal | null | undefined = null;

    globalThis.fetch = async (_input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedSignal = init?.signal;
      return new Response(
        JSON.stringify({
          items: [],
          page: 1,
          pageSize: 20,
          totalCount: 0,
          totalPages: 0,
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    await inventoryService.getMovements(undefined, controller.signal);
    assert.equal(capturedSignal, controller.signal);
  });

  test('inventoryService.auditInventory sends GET to /api/inventory/audit without params when productId omitted', async () => {
    let capturedUrl = '';
    let capturedMethod = '';

    globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? 'GET';

      return new Response(
        JSON.stringify({
          auditTimestampUtc: '2026-10-24T15:00:00Z',
          totalProductsChecked: 5,
          consistentProductsCount: 5,
          inconsistentProductsCount: 0,
          isSystemConsistent: true,
          discrepancies: [],
          invariantViolations: [],
          ledgerAudit: {
            status: 'Valid',
            totalMovementsChecked: 48,
            issues: [],
            limitations: [],
            summary: 'Ledger chain is valid and mathematically reconciled across all products and batches.',
          },
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const result = await inventoryService.auditInventory();

    assert.equal(capturedMethod, 'GET');
    const url = new URL(capturedUrl);
    assert.equal(url.pathname, '/api/inventory/audit');
    assert.equal(url.searchParams.get('productId'), null);

    assert.equal(result.isSystemConsistent, true);
    assert.equal(result.totalProductsChecked, 5);
    assert.equal(result.consistentProductsCount, 5);
    assert.equal(result.inconsistentProductsCount, 0);
    assert.equal(result.discrepancies.length, 0);
    assert.equal(result.invariantViolations.length, 0);
    assert.equal(result.ledgerAudit.status, 'Valid');
    assert.equal(result.ledgerAudit.totalMovementsChecked, 48);
  });

  test('inventoryService.auditInventory sends GET to /api/inventory/audit?productId=7 when productId given', async () => {
    let capturedUrl = '';

    globalThis.fetch = async (input: RequestInfo | URL): Promise<Response> => {
      capturedUrl = String(input);

      return new Response(
        JSON.stringify({
          auditTimestampUtc: '2026-10-24T15:05:00Z',
          totalProductsChecked: 1,
          consistentProductsCount: 0,
          inconsistentProductsCount: 1,
          isSystemConsistent: false,
          discrepancies: [
            {
              productId: 7,
              sku: 'ABON-PEDAS-250',
              productName: 'Abon Sapi Pedas 250g',
              expectedQuantityOnHand: 50,
              actualQuantityOnHand: 60,
              discrepancy: 10,
              batchCount: 3,
              isConsistent: false,
            },
          ],
          invariantViolations: [
            {
              batchId: 14,
              productId: 7,
              productSku: 'ABON-PEDAS-250',
              batchNo: 'BAT-202610-09',
              status: 'Released',
              physicalQuantity: 10,
              reservedQuantity: 15,
              violationType: 'ReservedExceedsPhysical',
              description: 'ReservedQuantity (15) cannot exceed PhysicalQuantity (10)',
            },
          ],
          ledgerAudit: {
            status: 'Compromised',
            totalMovementsChecked: 10,
            issues: [
              'Product quantity divergence at movement 33',
            ],
            limitations: [],
            summary: 'Ledger is compromised',
          },
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    const result = await inventoryService.auditInventory(7);

    const url = new URL(capturedUrl);
    assert.equal(url.pathname, '/api/inventory/audit');
    assert.equal(url.searchParams.get('productId'), '7');

    assert.equal(result.isSystemConsistent, false);
    assert.equal(result.totalProductsChecked, 1);
    assert.equal(result.consistentProductsCount, 0);
    assert.equal(result.inconsistentProductsCount, 1);
    assert.equal(result.discrepancies.length, 1);
    assert.equal(result.discrepancies[0].productId, 7);
    assert.equal(result.discrepancies[0].sku, 'ABON-PEDAS-250');
    assert.equal(result.discrepancies[0].discrepancy, 10);
    assert.equal(result.discrepancies[0].isConsistent, false);
    assert.equal(result.invariantViolations.length, 1);
    assert.equal(result.invariantViolations[0].batchNo, 'BAT-202610-09');
    assert.equal(result.invariantViolations[0].violationType, 'ReservedExceedsPhysical');
    assert.equal(result.ledgerAudit.status, 'Compromised');
    assert.equal(result.ledgerAudit.issues.length, 1);
  });

  test('inventoryService.auditInventory propagates 403 Forbidden when not authorized', async () => {
    globalThis.fetch = async (): Promise<Response> => {
      return new Response(
        JSON.stringify({
          status: 403,
          title: 'Forbidden',
          detail: 'User does not have Admin role required for audit operations.',
        }),
        {
          status: 403,
          headers: { 'Content-Type': 'application/problem+json' },
        }
      );
    };

    await assert.rejects(
      async () => {
        await inventoryService.auditInventory();
      },
      (err: unknown) => {
        assert.ok(err instanceof ApiError);
        assert.equal(err.status, 403);
        assert.equal(err.problem?.title, 'Forbidden');
        return true;
      }
    );
  });

  test('inventoryService.auditInventory propagates AbortSignal', async () => {
    const controller = new AbortController();
    let capturedSignal: AbortSignal | null | undefined = null;

    globalThis.fetch = async (_input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      capturedSignal = init?.signal;
      return new Response(
        JSON.stringify({
          auditTimestampUtc: '2026-10-24T15:00:00Z',
          totalProductsChecked: 0,
          consistentProductsCount: 0,
          inconsistentProductsCount: 0,
          isSystemConsistent: true,
          discrepancies: [],
          invariantViolations: [],
          ledgerAudit: {
            status: 'Valid',
            totalMovementsChecked: 0,
            issues: [],
            limitations: [],
            summary: 'Ledger chain is valid',
          },
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }
      );
    };

    await inventoryService.auditInventory(undefined, controller.signal);
    assert.equal(capturedSignal, controller.signal);
  });
});
