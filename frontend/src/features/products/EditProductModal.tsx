import React, { useState, useEffect } from 'react';
import { Loader2, Save, AlertTriangle, RefreshCw, AlertCircle } from 'lucide-react';
import { Modal } from '../../components/common';
import { productsService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { ProductResponse, UpdateProductRequest } from '../../types';
import { formatCurrency } from '../../lib';

interface EditProductModalProps {
  product: ProductResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (updated: ProductResponse) => void;
}

export const EditProductModal: React.FC<EditProductModalProps> = ({
  product,
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [unitPrice, setUnitPrice] = useState<string>('0');
  const [rowVersion, setRowVersion] = useState<number>(0);

  const [submitting, setSubmitting] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [conflictDetected, setConflictDetected] = useState(false);
  const [conflictMessage, setConflictMessage] = useState<string | null>(null);

  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);

  // Sync state when product changes
  useEffect(() => {
    if (product) {
      setName(product.name);
      setDescription(product.description || '');
      setUnitPrice(String(product.unitPrice));
      setRowVersion(product.version);
      setConflictDetected(false);
      setConflictMessage(null);
      setFieldErrors({});
      setGeneralError(null);
    }
  }, [product, isOpen]);

  if (!product) return null;

  const handleClose = () => {
    if (!submitting && !reloading) {
      onClose();
    }
  };

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    const trimmedName = name.trim();
    const numPrice = Number(unitPrice);

    if (!trimmedName) {
      errors.name = 'Product name is required.';
    } else if (trimmedName.length > 200) {
      errors.name = 'Product name cannot exceed 200 characters.';
    }

    if (description && description.length > 1000) {
      errors.description = 'Description cannot exceed 1000 characters.';
    }

    if (unitPrice === '' || isNaN(numPrice)) {
      errors.unitPrice = 'Unit price must be a valid number.';
    } else if (numPrice < 0) {
      errors.unitPrice = 'Unit price must be non-negative.';
    } else if (numPrice > 100_000_000) {
      errors.unitPrice = 'Unit price cannot exceed 100,000,000.';
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setGeneralError(null);
    setConflictDetected(false);
    setConflictMessage(null);

    if (!validate()) {
      return;
    }

    setSubmitting(true);

    const payload: UpdateProductRequest = {
      name: name.trim(),
      description: description.trim() ? description.trim() : null,
      unitPrice: Number(unitPrice),
      rowVersion,
    };

    try {
      const updated = await productsService.update(product.id, payload);
      onSuccess(updated);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        // Concurrency conflict detection (HTTP 409 or title "Concurrency Conflict")
        if (
          err.status === 409 &&
          (err.problem?.title?.includes('Concurrency') ||
            err.message.toLowerCase().includes('modified by another operation'))
        ) {
          setConflictDetected(true);
          setConflictMessage(
            err.message ||
              'This product has been modified by another operation. Your changes cannot be saved over the newer version without reviewing latest values.'
          );
        } else if (err.problem?.errors) {
          const mappedErrors: Record<string, string> = {};
          for (const [key, messages] of Object.entries(err.problem.errors)) {
            const lowerKey = key.toLowerCase();
            if (lowerKey === 'name') mappedErrors.name = messages[0];
            else if (lowerKey === 'unitprice') mappedErrors.unitPrice = messages[0];
            else if (lowerKey === 'description') mappedErrors.description = messages[0];
            else if (lowerKey === 'rowversion') mappedErrors.rowVersion = messages[0];
          }
          setFieldErrors(mappedErrors);
          setGeneralError(err.message);
        } else {
          setGeneralError(err.message);
        }
      } else if (err instanceof Error) {
        setGeneralError(err.message);
      } else {
        setGeneralError('An unexpected error occurred while saving product updates.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleReloadLatest = async () => {
    setReloading(true);
    setGeneralError(null);

    try {
      const latest = await productsService.getById(product.id);
      setName(latest.name);
      setDescription(latest.description || '');
      setUnitPrice(String(latest.unitPrice));
      setRowVersion(latest.version);
      setConflictDetected(false);
      setConflictMessage(null);
      setFieldErrors({});
    } catch (err: unknown) {
      if (err instanceof Error) {
        setGeneralError(`Failed to reload latest product: ${err.message}`);
      }
    } finally {
      setReloading(false);
    }
  };

  const priceNumber = Number(unitPrice);
  const formattedPreview =
    !isNaN(priceNumber) && priceNumber >= 0 ? formatCurrency(priceNumber) : null;

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={`Edit Product: ${product.sku}`}
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
            className="btn btn-primary"
            onClick={handleSubmit}
            disabled={submitting || reloading}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Saving...</span>
              </>
            ) : (
              <>
                <Save size={16} />
                <span>Save Changes</span>
              </>
            )}
          </button>
        </>
      }
    >
      <form onSubmit={handleSubmit} noValidate>
        {conflictDetected && (
          <div className="conflict-banner">
            <div className="conflict-banner-header">
              <AlertTriangle size={20} className="conflict-banner-icon" />
              <div>
                <h4 className="conflict-banner-title">Concurrency Conflict (HTTP 409)</h4>
                <p className="conflict-banner-desc">
                  {conflictMessage ||
                    'Another user or process updated this product while you were editing. To prevent data corruption, your changes were not applied.'}
                </p>
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

        <div className="form-group">
          <label className="form-label" htmlFor="edit-product-sku">
            <span>SKU</span>
            <span className="badge badge-neutral" style={{ fontSize: '0.7rem' }}>
              Immutable
            </span>
          </label>
          <input
            id="edit-product-sku"
            type="text"
            className="form-input mono"
            value={product.sku}
            disabled
          />
          <p className="form-hint">SKU is the master unique key and cannot be modified.</p>
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="edit-product-name">
            <span>
              Product Name<span className="required-star">*</span>
            </span>
          </label>
          <input
            id="edit-product-name"
            type="text"
            className={`form-input ${fieldErrors.name ? 'input-error' : ''}`}
            value={name}
            onChange={(e) => setName(e.target.value)}
            disabled={submitting || reloading}
            maxLength={200}
            required
          />
          {fieldErrors.name && <p className="form-error">{fieldErrors.name}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="edit-product-price">
            <span>
              Unit Price (IDR)<span className="required-star">*</span>
            </span>
            {formattedPreview && (
              <span className="currency" style={{ fontSize: '0.8125rem', color: '#16a34a' }}>
                {formattedPreview}
              </span>
            )}
          </label>
          <input
            id="edit-product-price"
            type="number"
            className={`form-input currency ${fieldErrors.unitPrice ? 'input-error' : ''}`}
            min="0"
            step="100"
            value={unitPrice}
            onChange={(e) => setUnitPrice(e.target.value)}
            disabled={submitting || reloading}
            required
          />
          {fieldErrors.unitPrice && <p className="form-error">{fieldErrors.unitPrice}</p>}
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="edit-product-desc">
            <span>Description</span>
            <span className="text-muted" style={{ fontSize: '0.75rem' }}>
              {description.length}/1000
            </span>
          </label>
          <textarea
            id="edit-product-desc"
            className={`form-textarea ${fieldErrors.description ? 'input-error' : ''}`}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            disabled={submitting || reloading}
            maxLength={1000}
          />
          {fieldErrors.description && <p className="form-error">{fieldErrors.description}</p>}
        </div>
      </form>
    </Modal>
  );
};
