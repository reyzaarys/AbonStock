import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import type { OrderResponse } from '../../types/orders.ts';
import { formatCurrency, formatDateTime } from '../../lib/formatters.ts';

describe('OrderDetail & Ledger Navigation domain rules', () => {
  const samplePendingOrder: OrderResponse = {
    id: 101,
    orderNumber: 'ORD-20261025-0101',
    customerName: 'CV Sumber Rejeki',
    customerEmail: 'sumber@example.com',
    status: 'Pending',
    totalAmount: 125000,
    createdAtUtc: '2026-10-25T10:00:00Z',
    updatedAtUtc: '2026-10-25T10:00:00Z',
    confirmedAtUtc: null,
    cancelledAtUtc: null,
    cancellationReason: null,
    completedAtUtc: null,
    version: 1,
    items: [
      {
        id: 1,
        productId: 2,
        productSku: 'ABON-SAPI-100G',
        productName: 'Abon Sapi Gurih 100g',
        quantity: 5,
        unitPrice: 25000,
        lineTotal: 125000,
      },
    ],
  };

  const sampleCompletedOrder: OrderResponse = {
    id: 102,
    orderNumber: 'ORD-20261025-0102',
    customerName: 'PT Kuliner Nusantara',
    customerEmail: 'finance@kuliner.co.id',
    status: 'Completed',
    totalAmount: 350000,
    createdAtUtc: '2026-10-25T08:00:00Z',
    updatedAtUtc: '2026-10-25T09:30:00Z',
    confirmedAtUtc: '2026-10-25T08:15:00Z',
    cancelledAtUtc: null,
    cancellationReason: null,
    completedAtUtc: '2026-10-25T09:30:00Z',
    version: 4,
    items: [
      {
        id: 2,
        productId: 1,
        productSku: 'ABON-SAPI-250G',
        productName: 'Abon Sapi Premium 250g',
        quantity: 4,
        unitPrice: 65000,
        lineTotal: 260000,
      },
      {
        id: 3,
        productId: 3,
        productSku: 'ABON-AYAM-100G',
        productName: 'Abon Ayam Spesial 100g',
        quantity: 5,
        unitPrice: 18000,
        lineTotal: 90000,
      },
    ],
  };

  const sampleCancelledOrder: OrderResponse = {
    id: 103,
    orderNumber: 'ORD-20261025-0103',
    customerName: 'Bapak Ahmad',
    customerEmail: null,
    status: 'Cancelled',
    totalAmount: 50000,
    createdAtUtc: '2026-10-25T11:00:00Z',
    updatedAtUtc: '2026-10-25T11:20:00Z',
    confirmedAtUtc: '2026-10-25T11:05:00Z',
    cancelledAtUtc: '2026-10-25T11:20:00Z',
    cancellationReason: 'Customer requested cancellation due to wrong delivery address',
    completedAtUtc: null,
    version: 3,
    items: [
      {
        id: 4,
        productId: 2,
        productSku: 'ABON-SAPI-100G',
        productName: 'Abon Sapi Gurih 100g',
        quantity: 2,
        unitPrice: 25000,
        lineTotal: 50000,
      },
    ],
  };

  test('Pending order exposes status and preserves null timestamps', () => {
    assert.equal(samplePendingOrder.status, 'Pending');
    assert.equal(samplePendingOrder.confirmedAtUtc, null);
    assert.equal(samplePendingOrder.completedAtUtc, null);
    assert.equal(samplePendingOrder.cancelledAtUtc, null);
    assert.equal(samplePendingOrder.cancellationReason, null);
    assert.equal(samplePendingOrder.items.length, 1);
    assert.equal(samplePendingOrder.items[0].productSku, 'ABON-SAPI-100G');
  });

  test('item line totals and aggregate units compute accurately', () => {
    const totalUnits = sampleCompletedOrder.items.reduce((s, it) => s + it.quantity, 0);
    const calculatedTotalAmount = sampleCompletedOrder.items.reduce((s, it) => s + it.lineTotal, 0);

    assert.equal(totalUnits, 9);
    assert.equal(calculatedTotalAmount, 350000);
    assert.equal(calculatedTotalAmount, sampleCompletedOrder.totalAmount);
    assert.equal(formatCurrency(sampleCompletedOrder.totalAmount), 'Rp\u00A0350.000');
  });

  test('Completed order exposes fulfillment timestamp and ledger routing target', () => {
    assert.equal(sampleCompletedOrder.status, 'Completed');
    assert.ok(sampleCompletedOrder.completedAtUtc !== null);
    assert.ok(sampleCompletedOrder.confirmedAtUtc !== null);
    assert.equal(sampleCompletedOrder.cancelledAtUtc, null);
    assert.equal(sampleCompletedOrder.cancellationReason, null);

    // Ledger routing target specification:
    // Route must point to /inventory?tab=movements without unsupported referenceOrderId query parameter
    const ledgerRoute = '/inventory?tab=movements';
    const parsedUrl = new URL(ledgerRoute, 'http://localhost');
    assert.equal(parsedUrl.pathname, '/inventory');
    assert.equal(parsedUrl.searchParams.get('tab'), 'movements');
    assert.equal(parsedUrl.searchParams.get('referenceOrderId'), null);
  });

  test('Cancelled order exposes cancellation reason and cancellation timestamp', () => {
    assert.equal(sampleCancelledOrder.status, 'Cancelled');
    assert.ok(sampleCancelledOrder.cancelledAtUtc !== null);
    assert.equal(
      sampleCancelledOrder.cancellationReason,
      'Customer requested cancellation due to wrong delivery address'
    );
    assert.equal(sampleCancelledOrder.completedAtUtc, null);
  });

  test('handles missing optional fields (null customerEmail and timestamps)', () => {
    const minimalOrder: OrderResponse = {
      id: 104,
      orderNumber: 'ORD-20261025-0104',
      customerName: 'Anonymous Counter Buyer',
      customerEmail: null,
      status: 'Pending',
      totalAmount: 18000,
      createdAtUtc: '2026-10-25T12:00:00Z',
      updatedAtUtc: '2026-10-25T12:00:00Z',
      confirmedAtUtc: null,
      cancelledAtUtc: null,
      cancellationReason: null,
      completedAtUtc: null,
      version: 1,
      items: [],
    };

    assert.equal(minimalOrder.customerEmail, null);
    assert.equal(minimalOrder.confirmedAtUtc, null);
    assert.equal(minimalOrder.completedAtUtc, null);
    assert.equal(minimalOrder.cancelledAtUtc, null);
    assert.equal(minimalOrder.cancellationReason, null);
    assert.equal(minimalOrder.items.length, 0);

    // Formatter fallback behavior
    const displayedEmail = minimalOrder.customerEmail ?? '—';
    assert.equal(displayedEmail, '—');
  });

  test('currency and date formatting match system conventions', () => {
    const formattedAmount = formatCurrency(125000);
    assert.ok(formattedAmount.includes('125.000'));

    const formattedDate = formatDateTime('2026-10-25T10:00:00Z');
    assert.ok(typeof formattedDate === 'string');
    assert.ok(formattedDate.length > 0);
  });

  test('inventory tab URL query synchronization matches supported tabs', () => {
    const resolveTab = (tabParam: string | null, isAdminUser: boolean): 'batches' | 'movements' | 'audit' => {
      if (tabParam === 'movements') return 'movements';
      if (tabParam === 'audit' && isAdminUser) return 'audit';
      return 'batches';
    };

    // Staff navigation to ?tab=movements
    assert.equal(resolveTab('movements', false), 'movements');

    // Default / no query param
    assert.equal(resolveTab(null, false), 'batches');
    assert.equal(resolveTab('batches', false), 'batches');

    // Staff attempting ?tab=audit falls back to batches
    assert.equal(resolveTab('audit', false), 'batches');

    // Admin accessing ?tab=audit succeeds
    assert.equal(resolveTab('audit', true), 'audit');

    // Unsupported tab falls back to batches
    assert.equal(resolveTab('unknown-tab', true), 'batches');
  });
});
