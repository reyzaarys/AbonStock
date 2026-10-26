import React, { useState } from 'react';
import { Loader2, Plus, AlertCircle } from 'lucide-react';
import { Modal } from '../../components/common';
import { productsService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { CreateProductRequest, ProductResponse } from '../../types';
import { formatCurrency } from '../../lib';

interface CreateProductModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (created: ProductResponse) => void;
}

export const CreateProductModal: React.FC<CreateProductModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [sku, setSku] = useState('');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [unitPrice, setUnitPrice] = useState<string>('0');

  const [submitting, setSubmitting] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);

  const resetForm = () => {
    setSku('');
    setName('');
    setDescription('');
    setUnitPrice('0');
    setFieldErrors({});
    setGeneralError(null);
  };

  const handleClose = () => {
    if (!submitting) {
      resetForm();
      onClose();
    }
  };

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    const trimmedSku = sku.trim();
    const trimmedName = name.trim();
    const numPrice = Number(unitPrice);

    if (!trimmedSku) {
      errors.sku = 'SKU is required.';
    } else if (trimmedSku.length > 50) {
      errors.sku = 'SKU cannot exceed 50 characters.';
    }

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

    if (!validate()) {
      return;
    }

    setSubmitting(true);

    const payload: CreateProductRequest = {
      sku: sku.trim().toUpperCase(),
      name: name.trim(),
      description: description.trim() ? description.trim() : null,
      unitPrice: Number(unitPrice),
    };

    try {
      const created = await productsService.create(payload);
      resetForm();
      onSuccess(created);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (err.problem?.errors) {
          const mappedErrors: Record<string, string> = {};
          for (const [key, messages] of Object.entries(err.problem.errors)) {
            const lowerKey = key.toLowerCase();
            if (lowerKey === 'sku') mappedErrors.sku = messages[0];
            else if (lowerKey === 'name') mappedErrors.name = messages[0];
            else if (lowerKey === 'unitprice') mappedErrors.unitPrice = messages[0];
            else if (lowerKey === 'description') mappedErrors.description = messages[0];
          }
          setFieldErrors(mappedErrors);
        }
        setGeneralError(err.message);
      } else if (err instanceof Error) {
        setGeneralError(err.message);
      } else {
        setGeneralError('An unexpected error occurred while creating the product.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const priceNumber = Number(unitPrice);
  const formattedPreview =
    !isNaN(priceNumber) && priceNumber >= 0 ? formatCurrency(priceNumber) : null;

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title="Create New Product"
      maxWidth="500px"
      footer={
        <>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={handleClose}
            disabled={submitting}
          >
            Cancel
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleSubmit}
            disabled={submitting}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Creating...</span>
              </>
            ) : (
              <>
                <Plus size={16} />
                <span>Create Product</span>
              </>
            )}
          </button>
        </>
      }
    >
      <form onSubmit={handleSubmit} noValidate>
        {generalError && (
          <div className="alert-toast alert-toast-error" style={{ marginBottom: '1rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <AlertCircle size={18} />
              <span>{generalError}</span>
            </div>
          </div>
        )}

        <div className="form-group">
          <label className="form-label" htmlFor="create-product-sku">
            <span>
              SKU (Stock Keeping Unit)<span className="required-star">*</span>
            </span>
            <span className="mono text-muted" style={{ fontSize: '0.75rem' }}>
              Auto-capitalized
            </span>
          </label>
          <input
            id="create-product-sku"
            type="text"
            className={`form-input mono ${fieldErrors.sku ? 'input-error' : ''}`}
            placeholder="e.g. ABON-SAPI-100G"
            value={sku}
            onChange={(e) => setSku(e.target.value.toUpperCase())}
            disabled={submitting}
            maxLength={50}
            required
          />
          {fieldErrors.sku && <p className="form-error">{fieldErrors.sku}</p>}
          <p className="form-hint">Must be unique across the catalog (1-50 characters).</p>
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="create-product-name">
            <span>
              Product Name<span className="required-star">*</span>
            </span>
          </label>
          <input
            id="create-product-name"
            type="text"
            className={`form-input ${fieldErrors.name ? 'input-error' : ''}`}
            placeholder="e.g. Abon Sapi Original 100g"
            value={name}
            onChange={(e) => setName(e.target.value)}
            disabled={submitting}
            maxLength={200}
            required
          />
          {fieldErrors.name && <p className="form-error">{fieldErrors.name}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="create-product-price">
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
            id="create-product-price"
            type="number"
            className={`form-input currency ${fieldErrors.unitPrice ? 'input-error' : ''}`}
            placeholder="0"
            min="0"
            step="100"
            value={unitPrice}
            onChange={(e) => setUnitPrice(e.target.value)}
            disabled={submitting}
            required
          />
          {fieldErrors.unitPrice && <p className="form-error">{fieldErrors.unitPrice}</p>}
          <p className="form-hint">Authoritative unit price in Indonesian Rupiah (non-negative).</p>
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="create-product-desc">
            <span>Description</span>
            <span className="text-muted" style={{ fontSize: '0.75rem' }}>
              {description.length}/1000
            </span>
          </label>
          <textarea
            id="create-product-desc"
            className={`form-textarea ${fieldErrors.description ? 'input-error' : ''}`}
            placeholder="Optional product notes, packaging details, or shelf life info..."
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            disabled={submitting}
            maxLength={1000}
          />
          {fieldErrors.description && <p className="form-error">{fieldErrors.description}</p>}
        </div>
      </form>
    </Modal>
  );
};
