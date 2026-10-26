import React, { useState, useEffect } from 'react';
import {
  Loader2,
  XCircle,
  AlertCircle,
  AlertTriangle,
  RefreshCw,
  RotateCcw,
} from 'lucide-react';
import { Modal } from '../../components/common';
import { ordersService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { OrderResponse } from '../../types';
import { formatCurrency } from '../../lib';

interface CancelOrderModalProps {
  order: OrderResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (updated: OrderResponse) => void;
  onReloadRequested?: (orderId: number) => void;
}

export const CancelOrderModal: React.FC<CancelOrderModalProps> = ({
  order,
  isOpen,
  onClose,
  onSuccess,
  onReloadRequested,
}) => {
  const [reason, setReason] = useState<string>('');
  const [currentRowVersion, setCurrentRowVersion] = useState<number>(0);
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [reloading, setReloading] = useState<boolean>(false);
  const [conflictDetected, setConflictDetected] = useState<boolean>(false);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);

  useEffect(() => {
    if (order) {
      setReason('');
      setCurrentRowVersion(order.version);
      setConflictDetected(false);
      setConflictMessage(null);
      setFieldErrors({});
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

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    const trimmed = reason.trim();

    if (!trimmed) {
      errors.reason = 'Cancellation reason is required.';
    } else if (trimmed.length > 500) {
      errors.reason = 'Cancellation reason cannot exceed 500 characters.';
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleCancel = async (e: React.FormEvent) => {
    e.preventDefault();
    setGeneralError(null);

    if (!validate()) return;

    setSubmitting(true);
    setConflictDetected(false);
    setConflictMessage(null);

    try {
      const updated = await ordersService.cancel(order.id, {
        cancellationReason: reason.trim(),
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
              'This order was modified by another transaction. Please reload before retrying.'
          );
        } else if (err.problem?.errors?.CancellationReason) {
          setFieldErrors({ reason: err.problem.errors.CancellationReason[0] });
        } else {
          setGeneralError(err.message);
        }
      } else if (err instanceof Error) {
        setGeneralError(err.message);
      } else {
        setGeneralError('An unexpected error occurred while cancelling the order.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const isConfirmed = order.status === 'Confirmed';

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={`Cancel Order — ${order.orderNumber}`}
      maxWidth="580px"
    >
      <form onSubmit={handleCancel} noValidate>
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
            <span className="detail-banner-label">Current Status</span>
            <span className="detail-banner-val">{order.status}</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Order Total</span>
            <span className="detail-banner-val currency">{formatCurrency(order.totalAmount)}</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Version</span>
            <span className="detail-banner-val mono">v{currentRowVersion}</span>
          </div>
        </div>

        {/* Impact Warning Notice */}
        {isConfirmed ? (
          <div className="action-notice-box action-notice-warning">
            <RotateCcw size={20} className="action-notice-icon" />
            <div>
              <div className="action-notice-title">Active Reservation Release</div>
              <div>
                This order is currently <strong>Confirmed</strong>. Cancelling will automatically release all held FEFO batch reservations back to available stock.
              </div>
              <div style={{ marginTop: '0.25rem', fontSize: '0.75rem', opacity: 0.9 }}>
                No physical stock will be deducted, and no stock ledger movements will be written.
              </div>
            </div>
          </div>
        ) : (
          <div className="action-notice-box action-notice-info">
            <AlertCircle size={20} className="action-notice-icon" />
            <div>
              <div className="action-notice-title">Pending Order Cancellation</div>
              <div>
                This order is in <strong>Pending</strong> status and has not reserved any inventory batches. It will transition to <strong>Cancelled</strong> without affecting stock.
              </div>
            </div>
          </div>
        )}

        {/* Cancellation Reason Input */}
        <div className="form-group">
          <label className="form-label" htmlFor="order-cancellation-reason">
            <span>Cancellation Reason <span className="required-star">*</span></span>
            <span className="text-muted" style={{ fontSize: '0.75rem' }}>
              {reason.length} / 500
            </span>
          </label>
          <textarea
            id="order-cancellation-reason"
            className={`form-textarea ${fieldErrors.reason ? 'input-error' : ''}`}
            placeholder="e.g. Customer requested cancellation due to revised production plan or address change."
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            disabled={submitting}
            maxLength={500}
            rows={3}
            required
          />
          {fieldErrors.reason && (
            <div className="form-error">{fieldErrors.reason}</div>
          )}
          <div className="form-hint">
            The cancellation reason is stored permanently in the audit record for this order.
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
            Keep Order
          </button>
          <button
            type="submit"
            className="btn btn-danger"
            disabled={submitting || reloading || conflictDetected}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Cancelling Order...</span>
              </>
            ) : (
              <>
                <XCircle size={16} />
                <span>Confirm Cancellation</span>
              </>
            )}
          </button>
        </div>
      </form>
    </Modal>
  );
};
