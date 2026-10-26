import React, { useState, useEffect } from 'react';
import {
  Loader2,
  PackageCheck,
  AlertCircle,
  AlertTriangle,
  RefreshCw,
} from 'lucide-react';
import { Modal } from '../../components/common';
import { ordersService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { OrderResponse } from '../../types';
import { formatCurrency } from '../../lib';

interface FulfillOrderModalProps {
  order: OrderResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (updated: OrderResponse) => void;
  onReloadRequested?: (orderId: number) => void;
}

export const FulfillOrderModal: React.FC<FulfillOrderModalProps> = ({
  order,
  isOpen,
  onClose,
  onSuccess,
  onReloadRequested,
}) => {
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [reloading, setReloading] = useState<boolean>(false);
  const [conflictDetected, setConflictDetected] = useState<boolean>(false);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);
  const [generalError, setGeneralError] = useState<string | null>(null);
  const [currentRowVersion, setCurrentRowVersion] = useState<number>(0);

  useEffect(() => {
    if (order) {
      setCurrentRowVersion(order.version);
      setConflictDetected(false);
      setConflictMessage(null);
      setGeneralError(null);
    }
  }, [order, isOpen]);

  if (!order) return null;

  const handleClose = () => {
    if (!submitting && !reloading) {
      onClose();
    }
  };

  const handleReload = async () => {
    setReloading(true);
    try {
      const refreshed = await ordersService.getById(order.id);
      setCurrentRowVersion(refreshed.version);
      setConflictDetected(false);
      setConflictMessage(null);
      setGeneralError(null);
      if (onReloadRequested) {
        onReloadRequested(order.id);
      }
    } catch {
      setGeneralError('Failed to reload order data from backend.');
    } finally {
      setReloading(false);
    }
  };

  const handleFulfill = async () => {
    setSubmitting(true);
    setConflictDetected(false);
    setConflictMessage(null);
    setGeneralError(null);

    try {
      const updated = await ordersService.fulfill(order.id, {
        rowVersion: currentRowVersion,
      });
      onSuccess(updated);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (err.status === 409) {
          setConflictDetected(true);
          setConflictMessage(
            err.message ||
              'This order record has been modified by another transaction. Please reload before retrying.'
          );
        } else {
          setGeneralError(err.message);
        }
      } else if (err instanceof Error) {
        setGeneralError(err.message);
      } else {
        setGeneralError('An unexpected error occurred while fulfilling the order.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const totalQuantityUnits = order.items.reduce((sum, it) => sum + it.quantity, 0);

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={`Fulfill Order — ${order.orderNumber}`}
      maxWidth="620px"
    >
      <div>
        {/* Concurrency Conflict Banner */}
        {conflictDetected && (
          <div className="conflict-banner">
            <div className="conflict-banner-header">
              <AlertTriangle size={20} className="conflict-banner-icon" />
              <div>
                <div className="conflict-banner-title">Concurrency Conflict Detected</div>
                <div className="conflict-banner-desc">
                  {conflictMessage ||
                    'This order record has been updated by another transaction. Reload the latest version to retry.'}
                </div>
              </div>
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '0.25rem' }}>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={handleReload}
                disabled={reloading}
              >
                <RefreshCw size={14} className={reloading ? 'spinner' : ''} />
                <span>Reload Order (v{currentRowVersion})</span>
              </button>
            </div>
          </div>
        )}

        {generalError && (
          <div className="alert-toast alert-toast-error" style={{ marginBottom: '1.25rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <AlertCircle size={18} />
              <span>{generalError}</span>
            </div>
          </div>
        )}

        {/* Order Details Banner */}
        <div className="detail-banner">
          <div className="detail-banner-item">
            <span className="detail-banner-label">Customer</span>
            <span className="detail-banner-val">{order.customerName}</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Status</span>
            <span className="detail-banner-val">{order.status}</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Total Amount</span>
            <span className="detail-banner-val currency">{formatCurrency(order.totalAmount)}</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Version</span>
            <span className="detail-banner-val mono">v{currentRowVersion}</span>
          </div>
        </div>

        {/* Items Summary Table */}
        <div style={{ marginBottom: '1.25rem' }}>
          <div style={{ fontSize: '0.8125rem', fontWeight: 600, marginBottom: '0.5rem' }}>
            Items to Fulfill ({order.items.length} lines, {totalQuantityUnits} units):
          </div>
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
                  <th>Product</th>
                  <th className="text-right" style={{ width: '90px' }}>Quantity</th>
                  <th className="text-right" style={{ width: '130px' }}>Line Total</th>
                </tr>
              </thead>
              <tbody>
                {order.items.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <span className="font-semibold">{item.productName}</span>{' '}
                      <span className="text-muted mono" style={{ fontSize: '0.75rem' }}>
                        ({item.productSku})
                      </span>
                    </td>
                    <td className="text-right mono font-semibold">{item.quantity}</td>
                    <td className="text-right mono font-semibold">{formatCurrency(item.lineTotal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Permanent Physical Deduction Notice */}
        <div className="action-notice-box action-notice-warning">
          <PackageCheck size={20} className="action-notice-icon" />
          <div>
            <div className="action-notice-title">Permanent Physical Stock Deduction &amp; Ledger Posting</div>
            <div>
              Fulfilling this order commits physical inventory permanently:
            </div>
            <ul style={{ margin: '0.375rem 0 0 1.25rem', padding: 0, fontSize: '0.75rem' }}>
              <li>
                Batch reservations are consumed and physical quantities deducted (<code>PhysicalQuantity</code> and <code>ReservedQuantity</code> reduced).
              </li>
              <li>
                Permanent stock ledger movements (<code>OrderDeduction</code>) are recorded for all allocated batches.
              </li>
              <li>
                Product <code>QuantityOnHand</code> is decremented. Batches reduced to 0 are set to <code>Depleted</code>.
              </li>
              <li>
                The order status advances to <strong>Completed</strong> (terminal state).
              </li>
            </ul>
          </div>
        </div>

        {/* Actions */}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '1.5rem' }}>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={handleClose}
            disabled={submitting || reloading}
          >
            Cancel
          </button>
          <button
            type="button"
            className="btn btn-success"
            onClick={handleFulfill}
            disabled={submitting || reloading || conflictDetected}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Fulfilling Order...</span>
              </>
            ) : (
              <>
                <PackageCheck size={16} />
                <span>Complete Fulfillment</span>
              </>
            )}
          </button>
        </div>
      </div>
    </Modal>
  );
};
