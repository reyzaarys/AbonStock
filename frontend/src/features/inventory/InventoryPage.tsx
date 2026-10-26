import React, { useState, useEffect, useCallback } from 'react';
import {
  Search,
  RefreshCw,
  Layers,
  ArrowUpDown,
  Plus,
  ShieldCheck,
  Sliders,
  CheckCircle2,
  X,
} from 'lucide-react';
import { inventoryService } from '../../services';
import type {
  BatchListQuery,
  BatchResponse,
  BatchSortBy,
  BatchStatus,
  PagedResult,
  StockAdjustmentResponse,
  StockReceiptResponse,
} from '../../types';
import {
  PageHeader,
  LoadingState,
  ErrorState,
  EmptyState,
  Pagination,
  StatusBadge,
} from '../../components/common';
import { formatDate } from '../../lib';
import { ReceiveStockModal } from './ReceiveStockModal';
import { ReleaseBatchModal } from './ReleaseBatchModal';
import { StockAdjustmentModal } from './StockAdjustmentModal';
import { useAuth } from '../../context/AuthContext';

interface ToastNotification {
  type: 'success' | 'error';
  message: string;
}

export const InventoryPage: React.FC = () => {
  const { isAdmin } = useAuth();
  const [data, setData] = useState<PagedResult<BatchResponse> | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<unknown | null>(null);

  // Filters & sorting state
  const [page, setPage] = useState<number>(1);
  const [pageSize] = useState<number>(10);
  const [searchInput, setSearchInput] = useState<string>('');
  const [searchTerm, setSearchTerm] = useState<string>('');
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [sortBy, setSortBy] = useState<BatchSortBy>('productiondate');
  const [sortDescending, setSortDescending] = useState<boolean>(false);

  // Workflow modals state
  const [receiveModalOpen, setReceiveModalOpen] = useState(false);
  const [releasingBatch, setReleasingBatch] = useState<BatchResponse | null>(null);
  const [adjustingBatch, setAdjustingBatch] = useState<BatchResponse | null>(null);

  // Toast feedback
  const [toast, setToast] = useState<ToastNotification | null>(null);

  useEffect(() => {
    if (!toast) return;
    const timer = setTimeout(() => {
      setToast(null);
    }, 6000);
    return () => clearTimeout(timer);
  }, [toast]);

  const fetchBatches = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);

      const query: BatchListQuery = {
        page,
        pageSize,
        search: searchTerm || undefined,
        status: (statusFilter as BatchStatus) || undefined,
        sortBy,
        sortDescending,
      };

      inventoryService
        .getBatches(query, signal)
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
    [page, pageSize, searchTerm, statusFilter, sortBy, sortDescending]
  );

  useEffect(() => {
    const controller = new AbortController();
    fetchBatches(controller.signal);

    return () => {
      controller.abort();
    };
  }, [fetchBatches]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    setSearchTerm(searchInput.trim());
  };

  const handleStatusChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setPage(1);
    setStatusFilter(e.target.value);
  };

  const handleSortByChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setSortBy(e.target.value as BatchSortBy);
  };

  const toggleSortDirection = () => {
    setSortDescending((prev) => !prev);
  };

  const handleResetFilters = () => {
    setSearchInput('');
    setSearchTerm('');
    setStatusFilter('');
    setSortBy('productiondate');
    setSortDescending(false);
    setPage(1);
  };

  const handleReceiveSuccess = (receipt: StockReceiptResponse) => {
    setToast({
      type: 'success',
      message: `Stock received: Batch "${receipt.batchNo}" initialized with ${receipt.initialQuantity} units.`,
    });
    fetchBatches();
  };

  const handleReleaseSuccess = (released: BatchResponse) => {
    setToast({
      type: 'success',
      message: `Batch "${released.batchNo}" successfully passed QC gate and is now Released.`,
    });
    fetchBatches();
  };

  const handleAdjustmentSuccess = (adj: StockAdjustmentResponse) => {
    const changeSign = adj.newPhysicalQuantity >= adj.previousPhysicalQuantity ? '+' : '';
    const diff = adj.newPhysicalQuantity - adj.previousPhysicalQuantity;
    setToast({
      type: 'success',
      message: `Stock adjusted (${changeSign}${diff} units). New physical quantity: ${adj.newPhysicalQuantity}.`,
    });
    fetchBatches();
  };

  return (
    <div>
      <PageHeader
        title="Inventory Batches"
        description="Physical production batch tracking with deterministic FIFO/FEFO allocation, quality inspection statuses, and real-time available stock."
        action={
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => fetchBatches()}
              disabled={loading}
              title="Refresh batch list"
            >
              <RefreshCw size={16} className={loading ? 'spinner' : ''} />
              <span>Refresh</span>
            </button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => setReceiveModalOpen(true)}
            >
              <Plus size={16} />
              <span>Receive Stock</span>
            </button>
          </div>
        }
      />

      {toast && (
        <div
          className={`alert-toast ${
            toast.type === 'success' ? 'alert-toast-success' : 'alert-toast-error'
          }`}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <CheckCircle2 size={18} />
            <span>{toast.message}</span>
          </div>
          <button
            type="button"
            className="alert-toast-close"
            onClick={() => setToast(null)}
            aria-label="Dismiss notification"
          >
            <X size={16} />
          </button>
        </div>
      )}

      <div className="filters-bar">
        <div className="filter-group">
          <form onSubmit={handleSearchSubmit} className="search-input-wrap">
            <Search size={16} className="search-icon" />
            <input
              type="text"
              className="search-input"
              placeholder="Search batch or SKU..."
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
            />
          </form>

          <select
            className="filter-select"
            value={statusFilter}
            onChange={handleStatusChange}
          >
            <option value="">All Statuses</option>
            <option value="PendingRelease">Pending Release</option>
            <option value="Released">Released</option>
            <option value="Quarantined">Quarantined</option>
            <option value="Depleted">Depleted</option>
          </select>
        </div>

        <div className="filter-group">
          <select
            className="filter-select"
            value={sortBy}
            onChange={handleSortByChange}
          >
            <option value="productiondate">Sort: Production Date</option>
            <option value="expirydate">Sort: Expiry Date</option>
            <option value="batchno">Sort: Batch No</option>
            <option value="createdat">Sort: Created Date</option>
          </select>

          <button
            type="button"
            className="btn btn-secondary"
            onClick={toggleSortDirection}
            title={sortDescending ? 'Descending order' : 'Ascending order'}
          >
            <ArrowUpDown size={15} />
            <span>{sortDescending ? 'Desc' : 'Asc'}</span>
          </button>
        </div>
      </div>

      {error ? (
        <ErrorState
          error={error}
          onRetry={() => fetchBatches()}
          title="Could not load inventory batches"
        />
      ) : loading && !data ? (
        <LoadingState message="Fetching inventory batches from backend..." />
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          icon={<Layers size={36} className="empty-default-icon" />}
          title="No inventory batches found"
          description={
            searchTerm || statusFilter
              ? 'No batches matched your current filter criteria.'
              : 'There are currently no inventory batches recorded.'
          }
          action={
            searchTerm || statusFilter ? (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={handleResetFilters}
              >
                Clear Filters
              </button>
            ) : (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => setReceiveModalOpen(true)}
              >
                <Plus size={16} />
                <span>Receive First Batch</span>
              </button>
            )
          }
        />
      ) : (
        <div className="table-card">
          <div className="table-container">
            <table className="data-table">
              <thead>
                <tr>
                  <th style={{ width: '120px' }}>Batch No</th>
                  <th>Product</th>
                  <th style={{ width: '110px' }}>Production</th>
                  <th style={{ width: '110px' }}>Expiry</th>
                  <th style={{ width: '80px' }}>Location</th>
                  <th className="text-right" style={{ width: '70px' }}>
                    Init
                  </th>
                  <th className="text-right" style={{ width: '70px' }}>
                    Phys
                  </th>
                  <th className="text-right" style={{ width: '70px' }}>
                    Rsrv
                  </th>
                  <th className="text-right" style={{ width: '80px' }}>
                    Avail
                  </th>
                  <th className="text-center" style={{ width: '120px' }}>
                    Status
                  </th>
                  <th className="text-center" style={{ width: '60px' }}>
                    Ver.
                  </th>
                  <th className="text-center" style={{ width: '130px' }}>
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((batch) => (
                  <tr key={batch.id} className="table-row-hover">
                    <td>
                      <span className="mono font-semibold">
                        {batch.batchNo}
                      </span>
                    </td>
                    <td>
                      <div className="font-semibold">
                        {batch.productName || '—'}
                      </div>
                      <div className="text-muted mono" style={{ fontSize: '0.75rem' }}>
                        SKU: {batch.productSku || '—'}
                      </div>
                    </td>
                    <td>{formatDate(batch.productionDate)}</td>
                    <td>{formatDate(batch.expiryDate)}</td>
                    <td>
                      <span className="mono">{batch.locationId}</span>
                    </td>
                    <td className="text-right text-muted">
                      {batch.initialQuantity}
                    </td>
                    <td className="text-right font-semibold">
                      {batch.physicalQuantity}
                    </td>
                    <td className="text-right text-muted">
                      {batch.reservedQuantity}
                    </td>
                    <td className="text-right">
                      <span
                        className="font-semibold"
                        style={{
                          color:
                            batch.availableQuantity > 0 ? '#16a34a' : '#64748b',
                        }}
                      >
                        {batch.availableQuantity}
                      </span>
                    </td>
                    <td className="text-center">
                      <StatusBadge status={batch.status} />
                    </td>
                    <td className="text-center mono text-muted" title={`xmin concurrency token: ${batch.version}`}>
                      v{batch.version}
                    </td>
                    <td className="text-center">
                      <div className="table-actions">
                        {batch.status === 'PendingRelease' && (
                          <button
                            type="button"
                            className="btn btn-primary btn-sm"
                            onClick={() => setReleasingBatch(batch)}
                            title="Perform 3-point QC inspection and release"
                          >
                            <ShieldCheck size={13} />
                            <span>QC Release</span>
                          </button>
                        )}

                        {batch.status === 'Released' && (
                          isAdmin ? (
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm"
                              onClick={() => setAdjustingBatch(batch)}
                              title="Adjust physical stock quantity"
                            >
                              <Sliders size={13} />
                              <span>Adjust</span>
                            </button>
                          ) : (
                            <span
                              className="badge badge-neutral"
                              style={{ fontSize: '0.6875rem' }}
                              title="Inventory adjustment requires Administrator privileges"
                            >
                              Released
                            </span>
                          )
                        )}

                        {batch.status === 'Depleted' && (
                          <span
                            className="badge badge-neutral"
                            style={{ fontSize: '0.6875rem' }}
                          >
                            Depleted
                          </span>
                        )}

                        {batch.status === 'Quarantined' && (
                          <span
                            className="badge badge-danger"
                            style={{ fontSize: '0.6875rem' }}
                          >
                            Quarantined
                          </span>
                        )}
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

      {/* Inventory Workflow Modals */}
      <ReceiveStockModal
        isOpen={receiveModalOpen}
        onClose={() => setReceiveModalOpen(false)}
        onSuccess={handleReceiveSuccess}
      />

      <ReleaseBatchModal
        batch={releasingBatch}
        isOpen={!!releasingBatch}
        onClose={() => setReleasingBatch(null)}
        onSuccess={handleReleaseSuccess}
      />

      <StockAdjustmentModal
        batch={adjustingBatch}
        isOpen={!!adjustingBatch}
        onClose={() => setAdjustingBatch(null)}
        onSuccess={handleAdjustmentSuccess}
      />
    </div>
  );
};
