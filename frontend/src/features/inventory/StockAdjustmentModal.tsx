import React, { useState, useEffect } from 'react';
import {
  Loader2,
  Sliders,
  AlertCircle,
  AlertTriangle,
  RefreshCw,
  MinusCircle,
  PlusCircle,
} from 'lucide-react';
import { Modal } from '../../components/common';
import { inventoryService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { BatchResponse, StockAdjustmentRequest, StockAdjustmentResponse } from '../../types';

interface StockAdjustmentModalProps {
  batch: BatchResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (response: StockAdjustmentResponse) => void;
}

export const StockAdjustmentModal: React.FC<StockAdjustmentModalProps> = ({
  batch,
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [adjustmentMode, setAdjustmentMode] = useState<'deduct' | 'add'>('deduct');
  const [amountInput, setAmountInput] = useState<string>('1');
  const [reason, setReason] = useState<string>('');
  const [batchRowVersion, setBatchRowVersion] = useState<number>(0);

  const [submitting, setSubmitting] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [conflictDetected, setConflictDetected] = useState(false);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);

  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);

  useEffect(() => {
    if (batch) {
      setAdjustmentMode('deduct');
      setAmountInput('1');
      setReason('');
      setBatchRowVersion(batch.version);
      setConflictDetected(false);
      setConflictMessage(null);
      setFieldErrors({});
      setGeneralError(null);
    }
  }, [batch, isOpen]);

  if (!batch) return null;

  const handleClose = () => {
    if (!submitting && !reloading) {
      onClose();
    }
  };

  const parsedAmount = parseInt(amountInput, 10);
  const validAmount = !isNaN(parsedAmount) && parsedAmount > 0 ? parsedAmount : 0;
  const quantityDelta = adjustmentMode === 'deduct' ? -validAmount : validAmount;

  const calculatedNewPhysical = batch.physicalQuantity + quantityDelta;
  const calculatedNewAvailable = calculatedNewPhysical - batch.reservedQuantity;

  const isBelowZero = calculatedNewPhysical < 0;
  const isBelowReserved = calculatedNewPhysical < batch.reservedQuantity;

  const validate = (): boolean => {
    const errors: Record<string, string> = {};

    if (!amountInput || isNaN(parsedAmount) || parsedAmount <= 0) {
      errors.amount = 'Quantity must be a positive integer greater than zero.';
    }

    if (isBelowZero) {
      errors.amount = `Adjustment would result in negative physical quantity (${calculatedNewPhysical}).`;
    } else if (isBelowReserved) {
      errors.amount = `Cannot reduce physical quantity below reserved quantity (${batch.reservedQuantity}).`;
    }

    if (!reason.trim()) {
      errors.reason = 'Audit reason is required by backend.';
    } else if (reason.trim().length > 500) {
      errors.reason = 'Reason cannot exceed 500 characters.';
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setGeneralError(null);
    setConflictDetected(false);

    if (!validate()) {
      return;
    }

    setSubmitting(true);

    const payload: StockAdjustmentRequest = {
      batchId: batch.id,
      quantityChange: quantityDelta,
      reason: reason.trim(),
      batchRowVersion,
    };

    try {
      const response = await inventoryService.adjustStock(payload);
      onSuccess(response);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (
          err.status === 409 &&
          (err.problem?.title?.includes('Concurrency') ||
            err.message.toLowerCase().includes('modified by another'))
        ) {
          setConflictDetected(true);
          setConflictMessage(
            err.message ||
              'Inventory has been modified by another concurrent operation. Please reload latest data.'
          );
        } else if (err.problem?.errors) {
          const mapped: Record<string, string> = {};
          for (const [key, msgs] of Object.entries(err.problem.errors)) {
            const lower = key.toLowerCase();
            if (lower === 'quantitychange') mapped.amount = msgs[0];
            else if (lower === 'reason') mapped.reason = msgs[0];
          }
          setFieldErrors(mapped);
          setGeneralError(err.message);
        } else {
          setGeneralError(err.message);
        }
      } else if (err instanceof Error) {
        setGeneralError(err.message);
      } else {
        setGeneralError('An unexpected error occurred while adjusting stock.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleReloadLatest = async () => {
    setReloading(true);
    setGeneralError(null);

    try {
      const latest = await inventoryService.getBatchById(batch.id);
      setBatchRowVersion(latest.version);
      setConflictDetected(false);
      setConflictMessage(null);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setGeneralError(`Failed to reload batch: ${err.message}`);
      }
    } finally {
      setReloading(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title="Stock Quantity Adjustment"
      maxWidth="500px"
      footer={
        <>
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
            className={`btn ${adjustmentMode === 'deduct' ? 'btn-danger' : 'btn-primary'}`}
            onClick={handleSubmit}
            disabled={submitting || reloading || isBelowZero || isBelowReserved}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Applying Adjustment...</span>
              </>
            ) : (
              <>
                <Sliders size={16} />
                <span>Confirm {adjustmentMode === 'deduct' ? 'Deduction' : 'Addition'}</span>
              </>
            )}
          </button>
        </>
      }
    >
      <form onSubmit={handleSubmit} noValidate>
        {/* Batch Info Summary */}
        <div className="detail-banner">
          <div className="detail-banner-item">
            <span className="detail-banner-label">Batch No</span>
            <span className="detail-banner-val mono">{batch.batchNo}</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Physical Qty</span>
            <span className="detail-banner-val">{batch.physicalQuantity} units</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Reserved Qty</span>
            <span className="detail-banner-val text-muted">{batch.reservedQuantity} units</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Available Qty</span>
            <span className="detail-banner-val" style={{ color: '#16a34a' }}>
              {batch.availableQuantity} units
            </span>
          </div>
        </div>

        {conflictDetected && (
          <div className="conflict-banner">
            <div className="conflict-banner-header">
              <AlertTriangle size={20} className="conflict-banner-icon" />
              <div>
                <h4 className="conflict-banner-title">Concurrency Conflict (HTTP 409)</h4>
                <p className="conflict-banner-desc">{conflictMessage}</p>
              </div>
            </div>
            <div>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={handleReloadLatest}
                disabled={reloading}
              >
                <RefreshCw size={14} className={reloading ? 'spinner' : ''} />
                <span>{reloading ? 'Reloading...' : 'Reload Latest Record & Token'}</span>
              </button>
            </div>
          </div>
        )}

        {generalError && !conflictDetected && (
          <div className="alert-toast alert-toast-error" style={{ marginBottom: '1rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <AlertCircle size={18} />
              <span>{generalError}</span>
            </div>
          </div>
        )}

        {/* Mode Selector */}
        <div className="segmented-control">
          <button
            type="button"
            className={`segmented-btn ${adjustmentMode === 'deduct' ? 'active-deduct' : ''}`}
            onClick={() => setAdjustmentMode('deduct')}
          >
            <MinusCircle size={15} />
            <span>Deduct Stock (Spoilage / Damage)</span>
          </button>
          <button
            type="button"
            className={`segmented-btn ${adjustmentMode === 'add' ? 'active-add' : ''}`}
            onClick={() => setAdjustmentMode('add')}
          >
            <PlusCircle size={15} />
            <span>Add Stock (Surplus / Correction)</span>
          </button>
        </div>

        {/* Quantity Delta Input */}
        <div className="form-group">
          <label className="form-label" htmlFor="adj-amount">
            <span>
              {adjustmentMode === 'deduct' ? 'Quantity to Deduct' : 'Quantity to Add'} (Delta)
              <span className="required-star">*</span>
            </span>
            <span
              className="mono"
              style={{
                fontSize: '0.8125rem',
                fontWeight: 600,
                color: adjustmentMode === 'deduct' ? '#dc2626' : '#16a34a',
              }}
            >
              Delta: {quantityDelta > 0 ? `+${quantityDelta}` : quantityDelta}
            </span>
          </label>
          <input
            id="adj-amount"
            type="number"
            className={`form-input ${fieldErrors.amount ? 'input-error' : ''}`}
            min="1"
            step="1"
            value={amountInput}
            onChange={(e) => setAmountInput(e.target.value)}
            disabled={submitting}
            required
          />
          {fieldErrors.amount && <p className="form-error">{fieldErrors.amount}</p>}
        </div>

        {/* Live Calculation Preview */}
        <div className="adjustment-preview-card">
          <div className="adjustment-preview-title">Post-Adjustment Projection</div>
          <div className="adjustment-preview-grid">
            <div className="preview-item">
              <div className="preview-label">New Physical</div>
              <div
                className="preview-val"
                style={{ color: isBelowZero ? '#dc2626' : '#1e293b' }}
              >
                {calculatedNewPhysical}
              </div>
            </div>
            <div className="preview-item">
              <div className="preview-label">Active Reserved</div>
              <div className="preview-val text-muted">{batch.reservedQuantity}</div>
            </div>
            <div className="preview-item">
              <div className="preview-label">New Available</div>
              <div
                className="preview-val"
                style={{
                  color: isBelowReserved ? '#dc2626' : calculatedNewAvailable > 0 ? '#16a34a' : '#64748b',
                }}
              >
                {calculatedNewAvailable}
              </div>
            </div>
          </div>
        </div>

        {/* Reason */}
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="adj-reason">
            <span>
              Audit Reason<span className="required-star">*</span>
            </span>
            <span className="text-muted" style={{ fontSize: '0.75rem' }}>
              {reason.length}/500
            </span>
          </label>
          <textarea
            id="adj-reason"
            className={`form-textarea ${fieldErrors.reason ? 'input-error' : ''}`}
            placeholder="e.g. Expired pouches discarded during inspection, or cycle count recount variance"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            disabled={submitting}
            maxLength={500}
            required
          />
          {fieldErrors.reason && <p className="form-error">{fieldErrors.reason}</p>}
          <p className="form-hint">
            Mandatory audit reason recorded permanently in stock movements ledger.
          </p>
        </div>
      </form>
    </Modal>
  );
};
