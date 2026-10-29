import React, { useState, useEffect, useCallback } from 'react';
import { RefreshCw, Filter } from 'lucide-react';
import { inventoryService } from '../../services';
import type {
  PagedResult,
  StockMovementListQuery,
  StockMovementResponse,
  StockMovementType,
} from '../../types';
import {
  LoadingState,
  ErrorState,
  EmptyState,
  Pagination,
} from '../../components/common';
import { formatDateTime } from '../../lib';

export const StockMovementsTab: React.FC = () => {
  const [data, setData] = useState<PagedResult<StockMovementResponse> | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<unknown | null>(null);

  // Filters state
  const [page, setPage] = useState<number>(1);
  const [pageSize] = useState<number>(20);
  const [movementTypeFilter, setMovementTypeFilter] = useState<string>('');
  const [productIdFilter, setProductIdFilter] = useState<string>('');
  const [batchIdFilter, setBatchIdFilter] = useState<string>('');

  const fetchMovements = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);

      const parsedProductId = productIdFilter.trim() ? parseInt(productIdFilter.trim(), 10) : undefined;
      const parsedBatchId = batchIdFilter.trim() ? parseInt(batchIdFilter.trim(), 10) : undefined;

      const query: StockMovementListQuery = {
        page,
        pageSize,
        movementType: (movementTypeFilter as StockMovementType) || undefined,
        productId: !isNaN(parsedProductId ?? NaN) ? parsedProductId : undefined,
        batchId: !isNaN(parsedBatchId ?? NaN) ? parsedBatchId : undefined,
      };

      inventoryService
        .getMovements(query, signal)
        .then((result) => {
          setData(result);
          setError(null);
        })
        .catch((err: unknown) => {
          if (err instanceof DOMException && err.name === 'AbortError') {
            return;
          }
          setError(err);
        })
        .finally(() => {
          setLoading(false);
        });
    },
    [page, pageSize, movementTypeFilter, productIdFilter, batchIdFilter]
  );

  useEffect(() => {
    const controller = new AbortController();
    fetchMovements(controller.signal);

    return () => {
      controller.abort();
    };
  }, [fetchMovements]);

  const handleResetFilters = () => {
    setMovementTypeFilter('');
    setProductIdFilter('');
    setBatchIdFilter('');
    setPage(1);
  };

  const renderMovementTypeBadge = (type: StockMovementType | string) => {
    switch (type) {
      case 'Receipt':
        return <span className="badge badge-success">Receipt</span>;
      case 'Adjustment':
        return <span className="badge badge-warning">Adjustment</span>;
      case 'OrderDeduction':
        return <span className="badge badge-info">Order Deduction</span>;
      case 'OrderCancellation':
        return <span className="badge badge-warning">Order Cancelled</span>;
      case 'WriteOff':
        return <span className="badge badge-danger">Write-Off</span>;
      default:
        return <span className="badge badge-neutral">{type}</span>;
    }
  };

  return (
    <div>
      {/* Filters Toolbar */}
      <div className="card" style={{ marginBottom: '1.25rem', padding: '1rem' }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem', alignItems: 'center', justifyContent: 'space-between' }}>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem', alignItems: 'center' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <Filter size={15} className="text-muted" />
              <span style={{ fontSize: '0.8125rem', fontWeight: 600 }}>Filter Ledger:</span>
            </div>

            {/* Movement Type Dropdown */}
            <select
              className="form-input"
              value={movementTypeFilter}
              onChange={(e) => {
                setMovementTypeFilter(e.target.value);
                setPage(1);
              }}
              style={{ width: 'auto', minWidth: '160px', padding: '0.375rem 0.625rem', fontSize: '0.8125rem' }}
            >
              <option value="">All Movement Types</option>
              <option value="Receipt">Receipt (Stok Masuk)</option>
              <option value="Adjustment">Adjustment (Koreksi)</option>
              <option value="OrderDeduction">Order Deduction (Penjualan)</option>
              <option value="OrderCancellation">Order Cancel (Batal)</option>
              <option value="WriteOff">Write-Off (Pemusnahan)</option>
            </select>

            {/* Product ID Filter */}
            <input
              type="number"
              className="form-input"
              placeholder="Product ID"
              value={productIdFilter}
              onChange={(e) => {
                setProductIdFilter(e.target.value);
                setPage(1);
              }}
              style={{ width: '120px', padding: '0.375rem 0.625rem', fontSize: '0.8125rem' }}
              min={1}
            />

            {/* Batch ID Filter */}
            <input
              type="number"
              className="form-input"
              placeholder="Batch ID"
              value={batchIdFilter}
              onChange={(e) => {
                setBatchIdFilter(e.target.value);
                setPage(1);
              }}
              style={{ width: '120px', padding: '0.375rem 0.625rem', fontSize: '0.8125rem' }}
              min={1}
            />

            {(movementTypeFilter || productIdFilter || batchIdFilter) && (
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={handleResetFilters}
              >
                Reset Filters
              </button>
            )}
          </div>

          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => fetchMovements()}
            disabled={loading}
          >
            <RefreshCw size={13} className={loading ? 'animate-spin' : ''} />
            <span>Refresh Ledger</span>
          </button>
        </div>
      </div>

      {/* Main Content Area */}
      {loading && !data && <LoadingState message="Loading immutable stock ledger..." />}

      {!!error && !loading && (
        <ErrorState
          error={error}
          onRetry={() => fetchMovements()}
        />
      )}

      {!loading && !error && data && data.items.length === 0 && (
        <EmptyState
          title="No Stock Movements Recorded"
          description={
            movementTypeFilter || productIdFilter || batchIdFilter
              ? 'No ledger movements match your current filter criteria. Try clearing filters.'
              : 'The stock movement ledger is currently empty. Physical stock deductions, receipts, and write-offs will appear here.'
          }
          action={
            (movementTypeFilter || productIdFilter || batchIdFilter) ? (
              <button type="button" className="btn btn-secondary btn-sm" onClick={handleResetFilters}>
                Clear All Filters
              </button>
            ) : undefined
          }
        />
      )}

      {!error && data && data.items.length > 0 && (
        <div className="table-card">
          <div className="table-container">
            <table className="data-table">
              <thead>
                <tr>
                  <th style={{ width: '140px' }}>Timestamp</th>
                  <th style={{ width: '130px' }} className="text-center">Movement Type</th>
                  <th>Product</th>
                  <th style={{ width: '130px' }}>Batch No</th>
                  <th style={{ width: '110px' }} className="text-right">Change</th>
                  <th style={{ width: '130px' }} className="text-right">Product Balance</th>
                  <th style={{ width: '130px' }} className="text-right">Batch Balance</th>
                  <th style={{ width: '100px' }} className="text-center">Order Ref</th>
                  <th>Reason & Audit Trail</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((m) => (
                  <tr key={m.id} className="table-row-hover">
                    <td className="mono" style={{ fontSize: '0.75rem', whiteSpace: 'nowrap' }}>
                      {formatDateTime(m.createdAtUtc)}
                    </td>
                    <td className="text-center">
                      {renderMovementTypeBadge(m.movementType)}
                    </td>
                    <td>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                        <span className="sku-pill" style={{ alignSelf: 'flex-start' }}>
                          {m.productSku ?? `PID #${m.productId}`}
                        </span>
                        <span style={{ fontSize: '0.8125rem', color: 'var(--color-text-main, #0f172a)' }}>
                          {m.productName ?? '—'}
                        </span>
                      </div>
                    </td>
                    <td>
                      {m.batchNo ? (
                        <span className="mono" style={{ fontWeight: 600 }}>{m.batchNo}</span>
                      ) : (
                        <span className="text-muted">—</span>
                      )}
                    </td>
                    <td className="text-right mono">
                      {m.quantityChange > 0 ? (
                        <strong style={{ color: '#10b981' }}>+{m.quantityChange}</strong>
                      ) : m.quantityChange < 0 ? (
                        <strong style={{ color: '#ef4444' }}>{m.quantityChange}</strong>
                      ) : (
                        <span>0</span>
                      )}
                    </td>
                    <td className="text-right mono">
                      <span style={{ fontWeight: 600 }}>{m.balanceAfter}</span>
                      <span className="text-muted" style={{ fontSize: '0.6875rem', marginLeft: '3px' }}>units</span>
                    </td>
                    <td className="text-right mono">
                      {m.batchBalanceAfter !== null ? (
                        <>
                          <span style={{ fontWeight: 500 }}>{m.batchBalanceAfter}</span>
                          <span className="text-muted" style={{ fontSize: '0.6875rem', marginLeft: '3px' }}>units</span>
                        </>
                      ) : (
                        <span className="text-muted">—</span>
                      )}
                    </td>
                    <td className="text-center">
                      {m.referenceOrderId ? (
                        <span className="mono badge badge-neutral" style={{ fontSize: '0.6875rem' }}>
                          #{m.referenceOrderId}
                        </span>
                      ) : (
                        <span className="text-muted">—</span>
                      )}
                    </td>
                    <td>
                      <div style={{ fontSize: '0.8125rem', color: 'var(--color-text-muted, #64748b)', maxWidth: '340px', wordBreak: 'break-word' }}>
                        {m.reason}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            totalPages={data.totalPages}
            hasNextPage={data.hasNextPage}
            hasPreviousPage={data.hasPreviousPage}
            onPageChange={(newPage) => setPage(newPage)}
            disabled={loading}
          />
        </div>
      )}
    </div>
  );
};
