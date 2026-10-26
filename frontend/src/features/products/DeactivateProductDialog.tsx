import React, { useState } from 'react';
import { Loader2, AlertTriangle, AlertCircle } from 'lucide-react';
import { Modal } from '../../components/common';
import { productsService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { ProductResponse } from '../../types';

interface DeactivateProductDialogProps {
  product: ProductResponse | null;
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (deactivated: ProductResponse) => void;
}

export const DeactivateProductDialog: React.FC<DeactivateProductDialogProps> = ({
  product,
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!product) return null;

  const handleClose = () => {
    if (!submitting) {
      setError(null);
      onClose();
    }
  };

  const handleDeactivate = async () => {
    setError(null);
    setSubmitting(true);

    try {
      const deactivated = await productsService.deactivate(product.id);
      onSuccess(deactivated);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Failed to deactivate the product. Please try again.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title="Deactivate Product"
      maxWidth="460px"
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
            className="btn btn-danger"
            onClick={handleDeactivate}
            disabled={submitting}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Deactivating...</span>
              </>
            ) : (
              <span>Deactivate Product</span>
            )}
          </button>
        </>
      }
    >
      <div>
        <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-start', marginBottom: '1rem' }}>
          <div
            style={{
              padding: '0.5rem',
              borderRadius: '50%',
              background: '#fef2f2',
              color: '#dc2626',
              flexShrink: 0,
            }}
          >
            <AlertTriangle size={24} />
          </div>
          <div>
            <p style={{ fontSize: '0.925rem', color: '#1e293b', marginBottom: '0.5rem' }}>
              Are you sure you want to deactivate{' '}
              <strong>{product.name}</strong> (<span className="mono">{product.sku}</span>)?
            </p>
            <p style={{ fontSize: '0.8125rem', color: '#64748b', lineHeight: 1.5 }}>
              Deactivated products will no longer appear in active catalog searches and cannot be added
              to new customer orders. Existing inventory batches and historical order line items are
              strictly preserved.
            </p>
          </div>
        </div>

        {error && (
          <div className="alert-toast alert-toast-error" style={{ marginTop: '0.5rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <AlertCircle size={18} />
              <span>{error}</span>
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
};
