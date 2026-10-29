import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  RefreshCw,
  AlertCircle,
  AlertTriangle,
  ArrowRight,
  Layers,
  Calendar,
  User,
  Mail,
  Hash,
} from 'lucide-react';
import { Modal, StatusBadge } from '../../components/common';
import { ordersService } from '../../services';
import type { OrderResponse } from '../../types';
import { formatCurrency, formatDateTime } from '../../lib';

interface OrderDetailModalProps {
  order: OrderResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onOrderUpdated?: (updated: OrderResponse) => void;
}

export const OrderDetailModal: React.FC<OrderDetailModalProps> = ({
  order,
  isOpen,
  onClose,
  onOrderUpdated,
}) => {
  const navigate = useNavigate();
  const [currentOrder, setCurrentOrder] = useState<OrderResponse | null>(order);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setCurrentOrder(order);
    setError(null);
  }, [order, isOpen]);

  if (!currentOrder) return null;

  const handleRefresh = async () => {
    setLoading(true);
    setError(null);
    try {
      const refreshed = await ordersService.getById(currentOrder.id);
      setCurrentOrder(refreshed);
      if (onOrderUpdated) {
        onOrderUpdated(refreshed);
      }
    } catch {
      setError('Failed to refresh order details from server.');
    } finally {
      setLoading(false);
    }
  };

  const handleNavigateToMovements = () => {
    onClose();
    navigate('/inventory?tab=movements');
  };

  const totalQuantity = currentOrder.items.reduce(
    (sum, item) => sum + item.quantity,
    0
  );

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={`Order Details — ${currentOrder.orderNumber}`}
      maxWidth="720px"
      footer={
        <div style={{ display: 'flex', justifyContent: 'space-between', width: '100%', alignItems: 'center' }}>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={handleRefresh}
            disabled={loading}
            title="Reload latest order details"
          >
            <RefreshCw size={14} className={loading ? 'spinner' : ''} />
            <span>Refresh</span>
          </button>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Close
          </button>
        </div>
      }
    >
      <div>
        {/* Error Alert if Refresh Fails */}
        {error && (
          <div className="alert-toast alert-toast-error" style={{ marginBottom: '1.25rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <AlertCircle size={18} />
              <span>{error}</span>
            </div>
          </div>
        )}

        {/* Primary Meta Banner */}
        <div className="detail-banner">
          <div className="detail-banner-item">
            <span className="detail-banner-label">
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.25rem' }}>
                <User size={12} />
                <span>Customer</span>
              </span>
            </span>
            <span className="detail-banner-val">{currentOrder.customerName}</span>
          </div>

          <div className="detail-banner-item">
            <span className="detail-banner-label">
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.25rem' }}>
                <Mail size={12} />
                <span>Email</span>
              </span>
            </span>
            <span
              className="detail-banner-val"
              style={{
                fontSize: '0.8125rem',
                fontWeight: currentOrder.customerEmail ? 500 : 400,
                color: currentOrder.customerEmail ? 'inherit' : 'var(--color-text-muted)',
              }}
            >
              {currentOrder.customerEmail || '—'}
            </span>
          </div>

          <div className="detail-banner-item">
            <span className="detail-banner-label">Status</span>
            <div>
              <StatusBadge status={currentOrder.status} />
            </div>
          </div>

          <div className="detail-banner-item">
            <span className="detail-banner-label">Order Total</span>
            <span
              className="detail-banner-val currency"
              style={{ color: 'var(--color-primary)', fontSize: '1rem' }}
            >
              {formatCurrency(currentOrder.totalAmount)}
            </span>
          </div>

          <div className="detail-banner-item">
            <span className="detail-banner-label">
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.25rem' }}>
                <Hash size={12} />
                <span>Version</span>
              </span>
            </span>
            <span className="detail-banner-val mono" style={{ fontSize: '0.8125rem' }}>
              v{currentOrder.version} (ID: #{currentOrder.id})
            </span>
          </div>
        </div>

        {/* Lifecycle Timestamps Banner */}
        <div
          style={{
            background: 'var(--color-bg)',
            border: '1px solid var(--color-border)',
            borderRadius: '0.5rem',
            padding: '0.75rem 1rem',
            marginBottom: '1.25rem',
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))',
            gap: '0.625rem',
            fontSize: '0.75rem',
          }}
        >
          <div>
            <div className="text-muted" style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
              <Calendar size={12} />
              <span>Created</span>
            </div>
            <div className="font-semibold" style={{ marginTop: '0.125rem' }}>
              {formatDateTime(currentOrder.createdAtUtc)}
            </div>
          </div>

          <div>
            <div className="text-muted" style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
              <Calendar size={12} />
              <span>Updated</span>
            </div>
            <div className="font-semibold" style={{ marginTop: '0.125rem' }}>
              {formatDateTime(currentOrder.updatedAtUtc)}
            </div>
          </div>

          {currentOrder.confirmedAtUtc && (
            <div>
              <div className="text-muted" style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
                <Calendar size={12} />
                <span>Confirmed</span>
              </div>
              <div className="font-semibold" style={{ marginTop: '0.125rem', color: '#2563eb' }}>
                {formatDateTime(currentOrder.confirmedAtUtc)}
              </div>
            </div>
          )}

          {currentOrder.completedAtUtc && (
            <div>
              <div className="text-muted" style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
                <Calendar size={12} />
                <span>Fulfilled</span>
              </div>
              <div className="font-semibold" style={{ marginTop: '0.125rem', color: '#16a34a' }}>
                {formatDateTime(currentOrder.completedAtUtc)}
              </div>
            </div>
          )}

          {currentOrder.cancelledAtUtc && (
            <div>
              <div className="text-muted" style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
                <Calendar size={12} />
                <span>Cancelled</span>
              </div>
              <div className="font-semibold" style={{ marginTop: '0.125rem', color: '#dc2626' }}>
                {formatDateTime(currentOrder.cancelledAtUtc)}
              </div>
            </div>
          )}
        </div>

        {/* Cancellation Reason Notice (Cancelled Status) */}
        {currentOrder.cancellationReason && (
          <div className="action-notice-box action-notice-danger" style={{ marginBottom: '1.25rem' }}>
            <AlertTriangle size={20} className="action-notice-icon" />
            <div>
              <div className="action-notice-title">Cancellation Reason</div>
              <div>{currentOrder.cancellationReason}</div>
              <div style={{ marginTop: '0.375rem', fontSize: '0.75rem', opacity: 0.9 }}>
                Stock reservations previously held for this order were released back to available inventory.
              </div>
            </div>
          </div>
        )}

        {/* Stock Ledger Audit Navigation Banner (Completed Status) */}
        {currentOrder.status === 'Completed' && (
          <div className="action-notice-box action-notice-info" style={{ marginBottom: '1.25rem' }}>
            <Layers size={20} className="action-notice-icon" />
            <div style={{ flex: 1 }}>
              <div className="action-notice-title">Inventory Ledger Audit Trail</div>
              <div style={{ fontSize: '0.8125rem', marginBottom: '0.375rem' }}>
                Physical stock for this order was deducted from inventory upon fulfillment. Ledger records were created with reference Order #{currentOrder.id} under movement type <strong>Order Deduction</strong>.
              </div>
              <div className="text-muted" style={{ fontSize: '0.75rem', marginBottom: '0.625rem' }}>
                Note: The Stock Movements ledger supports filtering by product ID and batch ID. In the ledger, look for reference #{currentOrder.id} in the Order Ref column.
              </div>
              <div>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={handleNavigateToMovements}
                  title="Navigate to Stock Movements ledger"
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '0.375rem',
                    background: '#ffffff',
                    color: '#1d4ed8',
                    borderColor: '#bfdbfe',
                  }}
                >
                  <Layers size={13} />
                  <span>View Stock Movements Ledger</span>
                  <ArrowRight size={13} />
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Order Items Table */}
        <div style={{ marginBottom: '0.5rem' }}>
          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              marginBottom: '0.5rem',
            }}
          >
            <div style={{ fontSize: '0.875rem', fontWeight: 600 }}>
              Ordered Items ({currentOrder.items.length} line{currentOrder.items.length !== 1 ? 's' : ''}, {totalQuantity} units)
            </div>
          </div>

          {currentOrder.items.length === 0 ? (
            <div
              style={{
                padding: '2rem',
                textAlign: 'center',
                background: 'var(--color-bg)',
                border: '1px dashed var(--color-border)',
                borderRadius: '0.5rem',
                color: 'var(--color-text-muted)',
                fontSize: '0.875rem',
              }}
            >
              No item details recorded for this order.
            </div>
          ) : (
            <div
              style={{
                border: '1px solid var(--color-border)',
                borderRadius: '0.5rem',
                overflow: 'hidden',
              }}
            >
              <table className="data-table" style={{ fontSize: '0.8125rem' }}>
                <thead>
                  <tr>
                    <th style={{ width: '40px' }} className="text-center">#</th>
                    <th>Product</th>
                    <th className="text-right" style={{ width: '90px' }}>Quantity</th>
                    <th className="text-right" style={{ width: '130px' }}>Unit Price</th>
                    <th className="text-right" style={{ width: '130px' }}>Line Total</th>
                  </tr>
                </thead>
                <tbody>
                  {currentOrder.items.map((item, idx) => (
                    <tr key={item.id ?? idx} className="table-row-hover">
                      <td className="text-center text-muted" style={{ fontSize: '0.75rem' }}>
                        {idx + 1}
                      </td>
                      <td>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.125rem' }}>
                          <span className="sku-pill" style={{ alignSelf: 'flex-start' }}>
                            {item.productSku}
                          </span>
                          <span className="font-semibold" style={{ fontSize: '0.8125rem' }}>
                            {item.productName}
                          </span>
                        </div>
                      </td>
                      <td className="text-right mono font-semibold">
                        {item.quantity}
                      </td>
                      <td className="text-right mono text-muted">
                        {formatCurrency(item.unitPrice)}
                      </td>
                      <td className="text-right mono font-semibold currency">
                        {formatCurrency(item.lineTotal)}
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr style={{ background: 'var(--color-bg)', borderTop: '2px solid var(--color-border)' }}>
                    <td colSpan={2} style={{ fontWeight: 600, padding: '0.75rem 1rem' }}>
                      Total
                    </td>
                    <td className="text-right mono font-semibold" style={{ padding: '0.75rem 1rem' }}>
                      {totalQuantity}
                    </td>
                    <td></td>
                    <td
                      className="text-right mono font-semibold currency"
                      style={{
                        padding: '0.75rem 1rem',
                        fontSize: '0.9375rem',
                        color: 'var(--color-primary)',
                      }}
                    >
                      {formatCurrency(currentOrder.totalAmount)}
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
};
