import React, { useState, useEffect } from 'react';
import { Loader2, ShieldAlert, AlertCircle, AlertTriangle, RefreshCw } from 'lucide-react';
import { Modal, StatusBadge } from '../../components/common';
import { inventoryService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { BatchResponse, QuarantineBatchRequest } from '../../types';
import { formatDate } from '../../lib';

interface QuarantineBatchModalProps {
  batch: BatchResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (quarantined: BatchResponse) => void;
}

export const QuarantineBatchModal: React.FC<QuarantineBatchModalProps> = ({
  batch,
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [reason, setReason] = useState('');
  const [notes, setNotes] = useState('');
  const [rowVersion, setRowVersion] = useState<number>(0);

  const [submitting, setSubmitting] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [conflictDetected, setConflictDetected] = useState(false);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);

  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);

  useEffect(() => {
    if (batch) {
      setReason('');
      setNotes('');
      setRowVersion(batch.version);
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

  const hasActiveReservations = batch.reservedQuantity > 0;

  const validate = (): boolean => {
    const errors: Record<string, string> = {};

    const trimmedReason = reason.trim();
    if (!trimmedReason) {
      errors.reason = 'Reason is required for batch quarantine audit trail.';
    } else if (trimmedReason.length > 500) {
      errors.reason = 'Reason cannot exceed 500 characters.';
    }

    if (notes && notes.length > 500) {
      errors.notes = 'Notes cannot exceed 500 characters.';
    }

    if (hasActiveReservations) {
      errors.general = `Cannot quarantine batch '${batch.batchNo}' because it has ${batch.reservedQuantity} active reserved units.`;
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

    const payload: QuarantineBatchRequest = {
      reason: reason.trim(),
      notes: notes.trim() ? notes.trim() : null,
      rowVersion,
    };

    try {
      const quarantined = await inventoryService.quarantineBatch(batch.id, payload);
      onSuccess(quarantined);
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
        setGeneralError('An unexpected error occurred while quarantining the batch.');
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
      setRowVersion(latest.version);
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
      title="Quarantine Production Batch"
      maxWidth="540px"
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
            className="btn btn-warning"
            onClick={handleSubmit}
            disabled={submitting || reloading || hasActiveReservations}
          >
            {submitting ? (
              <>
                <Loader2 size={15} className="animate-spin" />
                <span>Quarantining...</span>
              </>
            ) : (
              <>
                <ShieldAlert size={15} />
                <span>Confirm Quarantine</span>
              </>
            )}
          </button>
        </>
      }
    >
      <form onSubmit={handleSubmit} className="workflow-form">
        {/* Batch Metadata Header */}
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
              <span className="text-muted">Current Status: </span>
              <StatusBadge status={batch.status} />
            </div>
            <div>
              <span className="text-muted">Production Date: </span>
              <span>{formatDate(batch.productionDate)}</span>
            </div>
          </div>
        </div>

        {/* Warning if Batch Has Active Reservations */}
        {hasActiveReservations && (
          <div className="alert alert-danger" style={{ marginBottom: '1rem', display: 'flex', gap: '0.5rem', alignItems: 'flex-start' }}>
            <AlertTriangle size={18} style={{ flexShrink: 0, marginTop: '2px' }} />
            <div style={{ fontSize: '0.8125rem' }}>
              <strong>Active Reservations Detected:</strong> This batch currently has <strong>{batch.reservedQuantity}</strong> reserved units for confirmed customer orders. Under safety policies, batches with active reservations cannot be quarantined until orders are fulfilled or cancelled.
            </div>
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

        {/* Quarantine Reason */}
        <div className="form-group">
          <label className="form-label" htmlFor="quarantine-reason">
            Quarantine Reason <span className="text-danger">*</span>
          </label>
          <textarea
            id="quarantine-reason"
            className={`form-textarea ${fieldErrors.reason ? 'input-error' : ''}`}
            placeholder="Document the exact quality failure, sensory defect, aroma flaw, or packaging contamination..."
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
            disabled={submitting || reloading || hasActiveReservations}
            maxLength={500}
          />
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', marginTop: '0.25rem' }}>
            {fieldErrors.reason ? (
              <span className="text-danger">{fieldErrors.reason}</span>
            ) : (
              <span className="text-muted">Required for QC compliance and traceability</span>
            )}
            <span className="text-muted">{reason.length}/500</span>
          </div>
        </div>

        {/* Optional Notes */}
        <div className="form-group">
          <label className="form-label" htmlFor="quarantine-notes">
            Internal Inspection Notes (Optional)
          </label>
          <textarea
            id="quarantine-notes"
            className={`form-textarea ${fieldErrors.notes ? 'input-error' : ''}`}
            placeholder="Additional context from laboratory analysis or supplier batch dispute..."
            rows={2}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            disabled={submitting || reloading || hasActiveReservations}
            maxLength={500}
          />
          <div style={{ display: 'flex', justifyContent: 'flex-end', fontSize: '0.75rem', marginTop: '0.25rem' }}>
            <span className="text-muted">{notes.length}/500</span>
          </div>
        </div>

        <div style={{ fontSize: '0.75rem', color: 'var(--text-muted, #64748b)', marginTop: '0.5rem' }}>
          <em>Note: Quarantining preserves physical inventory balance while removing the batch from FEFO sales allocation.</em>
        </div>
      </form>
    </Modal>
  );
};
