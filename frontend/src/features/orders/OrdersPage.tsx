import React, { useState, useEffect, useCallback } from 'react';
import {
  Search,
  RefreshCw,
  ShoppingBag,
  Plus,
  CheckCircle2,
  PackageCheck,
  XCircle,
  X,
  AlertCircle,
  Info,
} from 'lucide-react';
import { ordersService } from '../../services';
import type { OrderListQuery, OrderResponse, OrderStatus, PagedResult } from '../../types';
import {
  PageHeader,
  LoadingState,
  ErrorState,
  EmptyState,
  Pagination,
  StatusBadge,
} from '../../components/common';
import { formatCurrency, formatDateTime } from '../../lib';
import { CreateOrderModal } from './CreateOrderModal';
import { ConfirmOrderModal } from './ConfirmOrderModal';
import { CancelOrderModal } from './CancelOrderModal';
import { FulfillOrderModal } from './FulfillOrderModal';

interface ToastNotification {
  type: 'success' | 'error';
  message: string;
}

export const OrdersPage: React.FC = () => {
  const [data, setData] = useState<PagedResult<OrderResponse> | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<unknown | null>(null);

  // Filters & pagination state
  const [page, setPage] = useState<number>(1);
  const [pageSize] = useState<number>(10);
  const [searchInput, setSearchInput] = useState<string>('');
  const [customerSearch, setCustomerSearch] = useState<string>('');
  const [statusFilter, setStatusFilter] = useState<string>('');

  // Workflow modals state
  const [createModalOpen, setCreateModalOpen] = useState<boolean>(false);
  const [confirmingOrder, setConfirmingOrder] = useState<OrderResponse | null>(null);
  const [cancellingOrder, setCancellingOrder] = useState<OrderResponse | null>(null);
  const [fulfillingOrder, setFulfillingOrder] = useState<OrderResponse | null>(null);

  // Feedback notifications
  const [toast, setToast] = useState<ToastNotification | null>(null);

  useEffect(() => {
    if (toast) {
      const timer = setTimeout(() => setToast(null), 5000);
      return () => clearTimeout(timer);
    }
  }, [toast]);

  const fetchOrders = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);

      const query: OrderListQuery = {
        page,
        pageSize,
        customerSearch: customerSearch || undefined,
        status: (statusFilter as OrderStatus) || undefined,
      };

      ordersService
        .list(query, signal)
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
    [page, pageSize, customerSearch, statusFilter]
  );

  useEffect(() => {
    const controller = new AbortController();
    fetchOrders(controller.signal);

    return () => {
      controller.abort();
    };
  }, [fetchOrders]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    setCustomerSearch(searchInput.trim());
  };

  const handleStatusChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setPage(1);
    setStatusFilter(e.target.value);
  };

  const handleResetFilters = () => {
    setSearchInput('');
    setCustomerSearch('');
    setStatusFilter('');
    setPage(1);
  };

  return (
    <div>
      <PageHeader
        title="Orders Management"
        description="Fulfillment lifecycle tracking: Pending &rarr; Confirmed (FEFO batch reservations) &rarr; Fulfilled (ledger stock deduction) or Cancelled."
        action={
          <div style={{ display: 'flex', gap: '0.625rem' }}>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => fetchOrders()}
              disabled={loading}
              title="Refresh orders list"
            >
              <RefreshCw size={16} className={loading ? 'spinner' : ''} />
              <span>Refresh</span>
            </button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => setCreateModalOpen(true)}
            >
              <Plus size={16} />
              <span>Create Order</span>
            </button>
          </div>
        }
      />

      {/* Toast Feedback Notification */}
      {toast && (
        <div
          className={`alert-toast ${
            toast.type === 'success' ? 'alert-toast-success' : 'alert-toast-error'
          }`}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            {toast.type === 'success' ? (
              <CheckCircle2 size={18} />
            ) : (
              <AlertCircle size={18} />
            )}
            <span>{toast.message}</span>
          </div>
          <button
            type="button"
            className="alert-toast-close"
            onClick={() => setToast(null)}
          >
            <X size={16} />
          </button>
        </div>
      )}

      <div className="filters-bar">
        <form onSubmit={handleSearchSubmit} className="search-input-wrap">
          <Search size={16} className="search-icon" />
          <input
            type="text"
            className="search-input"
            placeholder="Search by customer name..."
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
          />
        </form>

        <div className="filter-group">
          <select
            className="filter-select"
            value={statusFilter}
            onChange={handleStatusChange}
          >
            <option value="">All Statuses</option>
            <option value="Pending">Pending</option>
            <option value="Confirmed">Confirmed</option>
            <option value="Completed">Completed</option>
            <option value="Cancelled">Cancelled</option>
          </select>
        </div>
      </div>

      {error ? (
        <ErrorState
          error={error}
          onRetry={() => fetchOrders()}
          title="Could not load orders"
        />
      ) : loading && !data ? (
        <LoadingState message="Fetching orders from backend..." />
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          icon={<ShoppingBag size={36} className="empty-default-icon" />}
          title="No orders found"
          description={
            customerSearch || statusFilter
              ? 'No orders matched your current customer or status filter.'
              : 'There are currently no customer orders placed in the system.'
          }
          action={
            customerSearch || statusFilter ? (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={handleResetFilters}
              >
                Clear Filters
              </button>
            ) : undefined
          }
        />
      ) : (
        <div className="table-card">
          <div className="table-container">
            <table className="data-table">
              <thead>
                <tr>
                  <th style={{ width: '130px' }}>Order No</th>
                  <th>Customer</th>
                  <th>Items Ordered</th>
                  <th className="text-right" style={{ width: '120px' }}>
                    Total Amount
                  </th>
                  <th className="text-center" style={{ width: '110px' }}>
                    Status
                  </th>
                  <th style={{ width: '140px' }}>Created At</th>
                  <th className="text-center" style={{ width: '70px' }}>
                    Version
                  </th>
                  <th className="text-center" style={{ width: '180px' }}>
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((order) => {
                  const itemCount = order.items.reduce(
                    (sum, it) => sum + it.quantity,
                    0
                  );

                  return (
                    <tr key={order.id} className="table-row-hover">
                      <td>
                        <span className="mono font-semibold">
                          {order.orderNumber}
                        </span>
                      </td>
                      <td>
                        <div className="font-semibold">
                          {order.customerName}
                        </div>
                        {order.customerEmail && (
                          <div
                            className="text-muted"
                            style={{ fontSize: '0.75rem' }}
                          >
                            {order.customerEmail}
                          </div>
                        )}
                      </td>
                      <td>
                        <div style={{ fontSize: '0.8125rem' }}>
                          <span className="font-semibold">
                            {itemCount} item{itemCount !== 1 ? 's' : ''}
                          </span>{' '}
                          <span className="text-muted">
                            ({order.items.length} line{order.items.length !== 1 ? 's' : ''})
                          </span>
                        </div>
                        <div
                          className="text-muted"
                          style={{
                            fontSize: '0.75rem',
                            marginTop: '0.125rem',
                            maxWidth: '300px',
                            whiteSpace: 'nowrap',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                          }}
                        >
                          {order.items
                            .map((it) => `${it.quantity}x ${it.productName}`)
                            .join(', ')}
                        </div>
                      </td>
                      <td className="text-right">
                        <span className="currency font-semibold">
                          {formatCurrency(order.totalAmount)}
                        </span>
                      </td>
                      <td className="text-center">
                        <StatusBadge status={order.status} />
                      </td>
                      <td style={{ fontSize: '0.8125rem' }}>
                        {formatDateTime(order.createdAtUtc)}
                      </td>
                      <td className="text-center mono text-muted">
                        v{order.version}
                      </td>
                      <td className="text-center">
                        <div
                          className="table-actions"
                          style={{ justifyContent: 'center' }}
                        >
                          {order.status === 'Pending' && (
                            <>
                              <button
                                type="button"
                                className="btn btn-primary btn-sm"
                                onClick={() => setConfirmingOrder(order)}
                                title="Confirm order and reserve inventory batches via FEFO"
                              >
                                <CheckCircle2 size={13} />
                                <span>Confirm</span>
                              </button>
                              <button
                                type="button"
                                className="btn btn-outline-danger btn-sm"
                                onClick={() => setCancellingOrder(order)}
                                title="Cancel pending order"
                              >
                                <XCircle size={13} />
                                <span>Cancel</span>
                              </button>
                            </>
                          )}

                          {order.status === 'Confirmed' && (
                            <>
                              <button
                                type="button"
                                className="btn btn-success btn-sm"
                                onClick={() => setFulfillingOrder(order)}
                                title="Fulfill confirmed order and deduct physical stock"
                              >
                                <PackageCheck size={13} />
                                <span>Fulfill</span>
                              </button>
                              <button
                                type="button"
                                className="btn btn-outline-danger btn-sm"
                                onClick={() => setCancellingOrder(order)}
                                title="Cancel confirmed order and release held reservations"
                              >
                                <XCircle size={13} />
                                <span>Cancel</span>
                              </button>
                            </>
                          )}

                          {order.status === 'Completed' && (
                            <span
                              className="text-muted"
                              style={{
                                fontSize: '0.75rem',
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '0.25rem',
                              }}
                            >
                              <CheckCircle2 size={14} style={{ color: '#16a34a' }} />
                              <span>Fulfilled</span>
                            </span>
                          )}

                          {order.status === 'Cancelled' && (
                            <span
                              className="text-muted"
                              style={{
                                fontSize: '0.75rem',
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '0.25rem',
                                cursor: order.cancellationReason ? 'help' : 'default',
                              }}
                              title={
                                order.cancellationReason
                                  ? `Reason: ${order.cancellationReason}`
                                  : 'Order cancelled'
                              }
                            >
                              <XCircle size={14} style={{ color: '#dc2626' }} />
                              <span>Cancelled</span>
                              {order.cancellationReason && (
                                <Info size={12} style={{ color: '#94a3b8' }} />
                              )}
                            </span>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
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

      {/* Create Order Modal */}
      <CreateOrderModal
        isOpen={createModalOpen}
        onClose={() => setCreateModalOpen(false)}
        onSuccess={(created) => {
          setToast({
            type: 'success',
            message: `Order ${created.orderNumber} created successfully in Pending status.`,
          });
          fetchOrders();
        }}
      />

      {/* Confirm Order Modal */}
      <ConfirmOrderModal
        order={confirmingOrder}
        isOpen={!!confirmingOrder}
        onClose={() => setConfirmingOrder(null)}
        onSuccess={(updated) => {
          setToast({
            type: 'success',
            message: `Order ${updated.orderNumber} confirmed successfully. Batch reservations allocated via FEFO.`,
          });
          fetchOrders();
        }}
        onReloadRequested={(orderId) => {
          ordersService.getById(orderId).then((latest) => {
            setConfirmingOrder(latest);
            fetchOrders();
          });
        }}
      />

      {/* Cancel Order Modal */}
      <CancelOrderModal
        order={cancellingOrder}
        isOpen={!!cancellingOrder}
        onClose={() => setCancellingOrder(null)}
        onSuccess={(updated) => {
          setToast({
            type: 'success',
            message: `Order ${updated.orderNumber} cancelled successfully.`,
          });
          fetchOrders();
        }}
        onReloadRequested={(orderId) => {
          ordersService.getById(orderId).then((latest) => {
            setCancellingOrder(latest);
            fetchOrders();
          });
        }}
      />

      {/* Fulfill Order Modal */}
      <FulfillOrderModal
        order={fulfillingOrder}
        isOpen={!!fulfillingOrder}
        onClose={() => setFulfillingOrder(null)}
        onSuccess={(updated) => {
          setToast({
            type: 'success',
            message: `Order ${updated.orderNumber} fulfilled successfully. Physical stock deducted and ledger recorded.`,
          });
          fetchOrders();
        }}
        onReloadRequested={(orderId) => {
          ordersService.getById(orderId).then((latest) => {
            setFulfillingOrder(latest);
            fetchOrders();
          });
        }}
      />
    </div>
  );
};
