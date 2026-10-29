import React, { useState, useEffect } from 'react';
import { Loader2, Trash2, AlertCircle, AlertTriangle, RefreshCw } from 'lucide-react';
import { Modal, StatusBadge } from '../../components/common';
import { inventoryService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { BatchResponse, WriteOffBatchRequest, WriteOffBatchResponse } from '../../types';

interface WriteOffBatchModalProps {
  batch: BatchResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (response: WriteOffBatchResponse) => void;
}

export const WriteOffBatchModal: React.FC<WriteOffBatchModalProps> = ({
  batch,
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [quantityInput, setQuantityInput] = useState<string>('1');
  const [reason, setReason] = useState<string>('');
  const [disposalDoc, setDisposalDoc] = useState<string>('');
  const [batchRowVersion, setBatchRowVersion] = useState<number>(0);

  const [submitting, setSubmitting] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [conflictDetected, setConflictDetected] = useState(false);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);

  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);

  useEffect(() => {
    if (batch) {
      // Default quantity to entire unreserved stock if quarantined, else 1
      const maxAvailable = Math.max(0, batch.physicalQuantity - batch.reservedQuantity);
      setQuantityInput(maxAvailable > 0 ? (batch.status === 'Quarantined' ? String(maxAvailable) : '1') : '0');
      setReason('');
      setDisposalDoc('');
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

  const maxUnreserved = Math.max(0, batch.physicalQuantity - batch.reservedQuantity);
  const parsedQuantity = parseInt(quantityInput, 10);
  const validQuantity = !isNaN(parsedQuantity) && parsedQuantity > 0 ? parsedQuantity : 0;
  const expectedNewPhysical = Math.max(0, batch.physicalQuantity - validQuantity);
  const expectedNewStatus = expectedNewPhysical === 0 ? 'Depleted' : batch.status;

  const validate = (): boolean => {
    const errors: Record<string, string> = {};

    if (!quantityInput || isNaN(parsedQuantity) || parsedQuantity <= 0) {
      errors.quantity = 'Write-off quantity must be a positive integer greater than zero.';
    } else if (parsedQuantity > maxUnreserved) {
      errors.quantity = `Quantity cannot exceed maximum unreserved stock (${maxUnreserved} units).`;
    }

    const trimmedReason = reason.trim();
    if (!trimmedReason) {
      errors.reason = 'Disposal/write-off reason is required for financial and audit compliance.';
    } else if (trimmedReason.length > 500) {
      errors.reason = 'Reason cannot exceed 500 characters.';
    }

    if (disposalDoc && disposalDoc.trim().length > 100) {
      errors.disposalDoc = 'Disposal document reference cannot exceed 100 characters.';
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

    const payload: WriteOffBatchRequest = {
      quantity: parsedQuantity,
      reason: reason.trim(),
      batchRowVersion,
      disposalDocumentNumber: disposalDoc.trim() ? disposalDoc.trim() : null,
    };

    try {
      const response = await inventoryService.writeOffBatch(batch.id, payload);
      onSuccess(response);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (
          err.status === 409 &&
          (err.problem?.title?.includes('Concurrency') ||
            err.message.toLowerCase().includes('modified by another operation') ||
            err.message.toLowerCase().includes('concurrency'))
        ) {
          setConflictDetected(true);
          setConflictMessage(
            err.message ||
              'This batch has been modified by another concurrent operation. Please reload the latest record.'
          );
        } else {
          setGeneralError(err.message);
        }
      } else if (err instanceof Error) {
        setGeneralError(err.message);
      } else {
        setGeneralError('An unexpected error occurred while writing off the batch.');
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
      title="Write-Off & Disposal of Stock"
      maxWidth="560px"
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
            className="btn btn-danger"
            onClick={handleSubmit}
            disabled={submitting || reloading || maxUnreserved <= 0}
          >
            {submitting ? (
              <>
                <Loader2 size={15} className="animate-spin" />
                <span>Writing off...</span>
              </>
            ) : (
              <>
                <Trash2 size={15} />
                <span>Authorize Write-Off</span>
              </>
            )}
          </button>
        </>
      }
    >
      <form onSubmit={handleSubmit} className="workflow-form">
        {/* Batch Summary */}
        <div className="card" style={{ padding: '0.875rem', marginBottom: '1.25rem', backgroundColor: 'var(--bg-subtle, #f8fafc)' }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '0.5rem', fontSize: '0.8125rem' }}>
            <div>
              <span className="text-muted">Product: </span>
              <strong>{batch.productName ?? batch.productSku ?? `Product #${batch.productId}`}</strong>
            </div>
            <div>
              <span className="text-muted">Batch No: </span>
              <strong className="mono">{batch.batchNo}</strong>
            </div>
            <div>
              <span className="text-muted">Physical Qty: </span>
              <strong>{batch.physicalQuantity} units</strong>
            </div>
            <div>
              <span className="text-muted">Reserved Qty: </span>
              <strong style={{ color: batch.reservedQuantity > 0 ? '#ef4444' : 'inherit' }}>
                {batch.reservedQuantity} units
              </strong>
            </div>
            <div>
              <span className="text-muted">Max Write-Off: </span>
              <strong style={{ color: maxUnreserved > 0 ? '#059669' : '#dc2626' }}>
                {maxUnreserved} units
              </strong>
            </div>
            <div>
              <span className="text-muted">Current Status: </span>
              <StatusBadge status={batch.status} />
            </div>
          </div>
        </div>

        {/* Warning if no stock available for write off */}
        {maxUnreserved <= 0 && (
          <div className="alert alert-danger" style={{ marginBottom: '1rem', display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
            <AlertTriangle size={18} />
            <span style={{ fontSize: '0.8125rem' }}>
              No unreserved stock is available for write-off in this batch (all units are reserved or depleted).
            </span>
          </div>
        )}

        {/* Concurrency Conflict Alert */}
        {conflictDetected && (
          <div className="alert alert-warning" style={{ marginBottom: '1rem' }}>
            <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'flex-start' }}>
              <AlertTriangle size={18} style={{ flexShrink: 0, marginTop: '2px' }} />
              <div style={{ flex: 1, fontSize: '0.8125rem' }}>
                <div style={{ fontWeight: 600, marginBottom: '0.25rem' }}>Concurrency Conflict Detected</div>
                <div>{conflictMessage}</div>
              </div>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={handleReloadLatest}
                disabled={reloading}
                style={{ marginLeft: '0.5rem', whiteSpace: 'nowrap' }}
              >
                {reloading ? (
                  <Loader2 size={13} className="animate-spin" />
                ) : (
                  <>
                    <RefreshCw size={13} />
                    <span>Reload Record</span>
                  </>
                )}
              </button>
            </div>
          </div>
        )}

        {/* General Error Banner */}
        {generalError && (
          <div className="alert alert-danger" style={{ marginBottom: '1rem', display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
            <AlertCircle size={16} />
            <span style={{ fontSize: '0.8125rem' }}>{generalError}</span>
          </div>
        )}

        {/* Quantity Field */}
        <div className="form-group">
          <label className="form-label" htmlFor="writeoff-quantity">
            Quantity to Write Off <span className="text-danger">*</span>
          </label>
          <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
            <input
              id="writeoff-quantity"
              type="number"
              className={`form-input ${fieldErrors.quantity ? 'input-error' : ''}`}
              min={1}
              max={maxUnreserved}
              value={quantityInput}
              onChange={(e) => {
                setQuantityInput(e.target.value);
                if (fieldErrors.quantity) {
                  setFieldErrors((prev) => {
                    const updated = { ...prev };
                    delete updated.quantity;
                    return updated;
                  });
                }
              }}
              disabled={submitting || reloading || maxUnreserved <= 0}
              style={{ maxWidth: '180px' }}
            />
            {maxUnreserved > 0 && (
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => setQuantityInput(String(maxUnreserved))}
                disabled={submitting || reloading}
              >
                Entire Available ({maxUnreserved})
              </button>
            )}
          </div>
          {fieldErrors.quantity && (
            <div className="text-danger" style={{ fontSize: '0.75rem', marginTop: '0.25rem' }}>
              {fieldErrors.quantity}
            </div>
          )}
        </div>

        {/* Write-off Impact Simulation */}
        {validQuantity > 0 && (
          <div style={{ padding: '0.625rem 0.875rem', marginBottom: '1rem', borderRadius: '4px', border: '1px solid #e2e8f0', backgroundColor: '#f1f5f9', fontSize: '0.8125rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '0.25rem' }}>
              <span>Deduction from Batch:</span>
              <strong style={{ color: '#dc2626' }}>-{validQuantity} units</strong>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '0.25rem' }}>
              <span>Remaining Physical Quantity:</span>
              <strong>{expectedNewPhysical} units</strong>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span>Batch Status After Deduction:</span>
              <StatusBadge status={expectedNewStatus} />
            </div>
          </div>
        )}

        {/* Reason Field */}
        <div className="form-group">
          <label className="form-label" htmlFor="writeoff-reason">
            Disposal Reason <span className="text-danger">*</span>
          </label>
          <textarea
            id="writeoff-reason"
            className={`form-textarea ${fieldErrors.reason ? 'input-error' : ''}`}
            placeholder="Official justification (e.g. Expired batch destruction, microbial contamination, packaging leak)..."
            rows={3}
            value={reason}
            onChange={(e) => {
              setReason(e.target.value);
              if (fieldErrors.reason) {
                setFieldErrors((prev) => {
                  const updated = { ...prev };
                  delete updated.reason;
                  return updated;
                });
              }
            }}
            disabled={submitting || reloading || maxUnreserved <= 0}
            maxLength={500}
          />
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', marginTop: '0.25rem' }}>
            {fieldErrors.reason ? (
              <span className="text-danger">{fieldErrors.reason}</span>
            ) : (
              <span className="text-muted">Recorded into the permanent immutable stock ledger</span>
            )}
            <span className="text-muted">{reason.length}/500</span>
          </div>
        </div>

        {/* Optional Disposal Document Number */}
        <div className="form-group">
          <label className="form-label" htmlFor="writeoff-doc">
            Disposal Reference / Berita Acara No. (Optional)
          </label>
          <div style={{ position: 'relative' }}>
            <input
              id="writeoff-doc"
              type="text"
              className={`form-input ${fieldErrors.disposalDoc ? 'input-error' : ''}`}
              placeholder="e.g. BA-PEMUSNAHAN-2026-10-001"
              value={disposalDoc}
              onChange={(e) => setDisposalDoc(e.target.value)}
              disabled={submitting || reloading || maxUnreserved <= 0}
              maxLength={100}
            />
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', marginTop: '0.25rem' }}>
            {fieldErrors.disposalDoc ? (
              <span className="text-danger">{fieldErrors.disposalDoc}</span>
            ) : (
              <span className="text-muted">Optional internal memorandum or incinerator certificate</span>
            )}
            <span className="text-muted">{disposalDoc.length}/100</span>
          </div>
        </div>

        <div style={{ fontSize: '0.75rem', color: '#dc2626', marginTop: '0.5rem' }}>
          <em>Warning: Stock write-off permanently removes physical units from inventory and is non-reversible.</em>
        </div>
      </form>
    </Modal>
  );
};
