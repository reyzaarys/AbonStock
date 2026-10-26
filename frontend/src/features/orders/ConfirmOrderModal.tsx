import React, { useState, useEffect } from 'react';
import {
  Loader2,
  CheckCircle,
  AlertCircle,
  AlertTriangle,
  RefreshCw,
  Layers,
} from 'lucide-react';
import { Modal } from '../../components/common';
import { ordersService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { OrderResponse } from '../../types';
import { formatCurrency } from '../../lib';

interface ConfirmOrderModalProps {
  order: OrderResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (updated: OrderResponse) => void;
  onReloadRequested?: (orderId: number) => void;
}

export const ConfirmOrderModal: React.FC<ConfirmOrderModalProps> = ({
  order,
  isOpen,
  onClose,
  onSuccess,
  onReloadRequested,
}) => {
  const [submitting, setSubmitting] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [conflictDetected, setConflictDetected] = useState(false);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);
  const [stockShortageError, setStockShortageError] = useState<string | null>(null);
  const [generalError, setGeneralError] = useState<string | null>(null);
  const [currentRowVersion, setCurrentRowVersion] = useState<number>(0);

  useEffect(() => {
    if (order) {
      setCurrentRowVersion(order.version);
      setConflictDetected(false);
      setConflictMessage(null);
      setStockShortageError(null);
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
      setStockShortageError(null);
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

  const handleConfirm = async () => {
    setSubmitting(true);
    setConflictDetected(false);
    setConflictMessage(null);
    setStockShortageError(null);
    setGeneralError(null);

    try {
      const updated = await ordersService.confirm(order.id, {
        rowVersion: currentRowVersion,
      });
      onSuccess(updated);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (err.status === 409) {
          // Check whether conflict is insufficient stock or concurrency collision
          const msg = err.message || '';
          if (msg.toLowerCase().includes('insufficient') || msg.toLowerCase().includes('stock')) {
            setStockShortageError(msg);
          } else {
            setConflictDetected(true);
            setConflictMessage(
              msg || 'The order has been modified by another process. Please reload to see the latest version before confirming.'
            );
          }
        } else {
          setGeneralError(err.message);
        }
      } else if (err instanceof Error) {
        setGeneralError(err.message);
      } else {
        setGeneralError('An unexpected error occurred while confirming the order.');
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
      title={`Confirm Order — ${order.orderNumber}`}
      maxWidth="650px"
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

        {/* Insufficient Stock Shortage Alert */}
        {stockShortageError && (
          <div className="action-notice-box action-notice-danger">
            <AlertCircle size={20} className="action-notice-icon" />
            <div>
              <div className="action-notice-title">Insufficient Available Stock</div>
              <div>{stockShortageError}</div>
              <div style={{ marginTop: '0.5rem', fontSize: '0.75rem', opacity: 0.9 }}>
                Only released batches with non-expired shelf life contribute to available inventory. Please receive or release stock in Quality Control before confirming this order.
              </div>
            </div>
          </div>
        )}

        {/* General Error */}
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
            <span className="detail-banner-label">Order Total</span>
            <span className="detail-banner-val currency" style={{ color: 'var(--color-primary)' }}>
              {formatCurrency(order.totalAmount)}
            </span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Concurrency Token</span>
            <span className="detail-banner-val mono">v{currentRowVersion}</span>
          </div>
        </div>

        {/* Line Items Table */}
        <div style={{ marginBottom: '1.25rem' }}>
          <div style={{ fontSize: '0.8125rem', fontWeight: 600, marginBottom: '0.5rem' }}>
            Items to Reserve ({order.items.length} product lines, {totalQuantityUnits} units):
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
                  <th className="text-right" style={{ width: '80px' }}>Quantity</th>
                  <th className="text-right" style={{ width: '110px' }}>Unit Price</th>
                  <th className="text-right" style={{ width: '120px' }}>Line Total</th>
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
                    <td className="text-right mono">{formatCurrency(item.unitPrice)}</td>
                    <td className="text-right mono font-semibold">{formatCurrency(item.lineTotal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* FEFO Reservation Explanation Notice */}
        <div className="action-notice-box action-notice-info">
          <Layers size={20} className="action-notice-icon" />
          <div>
            <div className="action-notice-title">Server-Side FEFO Batch Reservation</div>
            <div>
              Confirming this order locks eligible inventory batches and reserves physical stock using the <strong>First-Expired, First-Out (FEFO)</strong> allocation strategy.
            </div>
            <ul style={{ margin: '0.375rem 0 0 1.25rem', padding: 0, fontSize: '0.75rem' }}>
              <li>Only batches with <code>Released</code> QC status and active shelf life are allocated.</li>
              <li>Batch <code>ReservedQuantity</code> will be incremented server-side; physical stock remains until fulfillment.</li>
              <li>Order state will transition to <strong>Confirmed</strong>.</li>
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
            Close
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleConfirm}
            disabled={submitting || reloading || conflictDetected}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Confirming &amp; Reserving...</span>
              </>
            ) : (
              <>
                <CheckCircle size={16} />
                <span>Confirm Order</span>
              </>
            )}
          </button>
        </div>
      </div>
    </Modal>
  );
};
