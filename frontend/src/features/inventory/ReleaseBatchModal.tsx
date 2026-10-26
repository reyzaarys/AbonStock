import React, { useState, useEffect } from 'react';
import { Loader2, ShieldCheck, AlertCircle, AlertTriangle, RefreshCw } from 'lucide-react';
import { Modal } from '../../components/common';
import { inventoryService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { BatchResponse, ReleaseBatchRequest } from '../../types';
import { formatDate } from '../../lib';

interface ReleaseBatchModalProps {
  batch: BatchResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (released: BatchResponse) => void;
}

export const ReleaseBatchModal: React.FC<ReleaseBatchModalProps> = ({
  batch,
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [visualCheck, setVisualCheck] = useState(false);
  const [aromaCheck, setAromaCheck] = useState(false);
  const [packagingCheck, setPackagingCheck] = useState(false);
  const [notes, setNotes] = useState('');
  const [rowVersion, setRowVersion] = useState<number>(0);

  const [submitting, setSubmitting] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [conflictDetected, setConflictDetected] = useState(false);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);
  const [generalError, setGeneralError] = useState<string | null>(null);

  useEffect(() => {
    if (batch) {
      setVisualCheck(false);
      setAromaCheck(false);
      setPackagingCheck(false);
      setNotes('');
      setRowVersion(batch.version);
      setConflictDetected(false);
      setConflictMessage(null);
      setGeneralError(null);
    }
  }, [batch, isOpen]);

  if (!batch) return null;

  const handleClose = () => {
    if (!submitting && !reloading) {
      onClose();
    }
  };

  const allChecksPassed = visualCheck && aromaCheck && packagingCheck;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setGeneralError(null);
    setConflictDetected(false);

    if (!allChecksPassed) {
      setGeneralError('All quality checks (Visual, Aroma, Packaging) must pass to release the batch.');
      return;
    }

    setSubmitting(true);

    const payload: ReleaseBatchRequest = {
      visualCheckPassed: visualCheck,
      aromaCheckPassed: aromaCheck,
      packagingCheckPassed: packagingCheck,
      notes: notes.trim() ? notes.trim() : null,
      rowVersion,
    };

    try {
      const released = await inventoryService.releaseBatch(batch.id, payload);
      onSuccess(released);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (
          err.status === 409 &&
          (err.problem?.title?.includes('Concurrency') ||
            err.message.toLowerCase().includes('modified by another operation'))
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
        setGeneralError('An unexpected error occurred while releasing the batch.');
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
      title="Quality Control (QC) Release Gate"
      maxWidth="520px"
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
            className="btn btn-primary"
            onClick={handleSubmit}
            disabled={submitting || reloading || !allChecksPassed}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Releasing Batch...</span>
              </>
            ) : (
              <>
                <ShieldCheck size={16} />
                <span>Pass QC &amp; Release Batch</span>
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
            <span className="detail-banner-label">Product SKU</span>
            <span className="detail-banner-val mono">{batch.productSku || '—'}</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Physical Qty</span>
            <span className="detail-banner-val">{batch.physicalQuantity} units</span>
          </div>
          <div className="detail-banner-item">
            <span className="detail-banner-label">Production Date</span>
            <span className="detail-banner-val">{formatDate(batch.productionDate)}</span>
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
                <span>{reloading ? 'Reloading...' : 'Reload Latest Batch Version'}</span>
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

        {/* 3-Point QC Checklist */}
        <div style={{ marginBottom: '1rem' }}>
          <label className="form-label">
            <span>Mandatory 3-Point QC Verification</span>
            <span className="text-muted" style={{ fontSize: '0.75rem' }}>
              All 3 required by API
            </span>
          </label>

          <div className="qc-checklist">
            <label className={`qc-item ${visualCheck ? 'checked' : ''}`}>
              <input
                type="checkbox"
                className="qc-checkbox"
                checked={visualCheck}
                onChange={(e) => setVisualCheck(e.target.checked)}
                disabled={submitting}
              />
              <div className="qc-content">
                <div className="qc-title">1. Visual &amp; Color Inspection</div>
                <div className="qc-desc">
                  Uniform golden-brown shredding, no clumping, absence of burnt flecks or foreign matter.
                </div>
              </div>
            </label>

            <label className={`qc-item ${aromaCheck ? 'checked' : ''}`}>
              <input
                type="checkbox"
                className="qc-checkbox"
                checked={aromaCheck}
                onChange={(e) => setAromaCheck(e.target.checked)}
                disabled={submitting}
              />
              <div className="qc-content">
                <div className="qc-title">2. Aroma &amp; Scent Inspection</div>
                <div className="qc-desc">
                  Characteristic fragrant aroma of fried shallots and spiced beef/chicken; zero rancidity.
                </div>
              </div>
            </label>

            <label className={`qc-item ${packagingCheck ? 'checked' : ''}`}>
              <input
                type="checkbox"
                className="qc-checkbox"
                checked={packagingCheck}
                onChange={(e) => setPackagingCheck(e.target.checked)}
                disabled={submitting}
              />
              <div className="qc-content">
                <div className="qc-title">3. Packaging &amp; Seal Integrity</div>
                <div className="qc-desc">
                  Hermetic pouch seal intact, nitrogen cushion preserved, batch code and expiration clearly printed.
                </div>
              </div>
            </label>
          </div>
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="release-notes">
            <span>QC Inspector Notes</span>
            <span className="text-muted" style={{ fontSize: '0.75rem' }}>
              {notes.length}/500
            </span>
          </label>
          <textarea
            id="release-notes"
            className="form-textarea"
            placeholder="Optional inspection remarks or sign-off notes..."
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            disabled={submitting}
            maxLength={500}
          />
        </div>
      </form>
    </Modal>
  );
};
