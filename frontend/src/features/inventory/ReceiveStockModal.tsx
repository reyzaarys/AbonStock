import React, { useState, useEffect } from 'react';
import { Loader2, AlertCircle, PackagePlus } from 'lucide-react';
import { Modal } from '../../components/common';
import { inventoryService, productsService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type { ProductResponse, StockReceiptRequest, StockReceiptResponse } from '../../types';

interface ReceiveStockModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (receipt: StockReceiptResponse) => void;
}

export const ReceiveStockModal: React.FC<ReceiveStockModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [products, setProducts] = useState<ProductResponse[]>([]);
  const [loadingProducts, setLoadingProducts] = useState(false);

  // Form fields
  const [productId, setProductId] = useState<string>('');
  const [batchNo, setBatchNo] = useState('');
  const [productionDate, setProductionDate] = useState(() => {
    return new Date().toISOString().split('T')[0];
  });
  const [expiryDate, setExpiryDate] = useState('');
  const [locationId, setLocationId] = useState('LOC_KDR_PROD');
  const [quantity, setQuantity] = useState<string>('50');
  const [reason, setReason] = useState('');

  const [submitting, setSubmitting] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);

  // Fetch active products when modal opens
  useEffect(() => {
    if (isOpen) {
      setLoadingProducts(true);
      productsService
        .list({ includeInactive: false, pageSize: 100 })
        .then((res) => {
          setProducts(res.items);
          if (res.items.length > 0 && !productId) {
            setProductId(String(res.items[0].id));
          }
        })
        .catch(() => {
          // If fetching products fails, user will see empty dropdown
        })
        .finally(() => {
          setLoadingProducts(false);
        });
    }
  }, [isOpen]);

  const resetForm = () => {
    setBatchNo('');
    setProductionDate(new Date().toISOString().split('T')[0]);
    setExpiryDate('');
    setLocationId('LOC_KDR_PROD');
    setQuantity('50');
    setReason('');
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
    const parsedQty = parseInt(quantity, 10);
    const parsedProdId = parseInt(productId, 10);

    if (!parsedProdId || isNaN(parsedProdId)) {
      errors.productId = 'Product is required.';
    }

    if (!batchNo.trim()) {
      errors.batchNo = 'Batch number is required.';
    } else if (batchNo.trim().length > 100) {
      errors.batchNo = 'Batch number cannot exceed 100 characters.';
    }

    if (!productionDate) {
      errors.productionDate = 'Production date is required.';
    }

    if (expiryDate && productionDate && expiryDate < productionDate) {
      errors.expiryDate = 'Expiry date cannot be earlier than production date.';
    }

    if (!quantity || isNaN(parsedQty) || parsedQty <= 0) {
      errors.quantity = 'Quantity must be a positive integer.';
    }

    if (reason && reason.length > 500) {
      errors.reason = 'Reason cannot exceed 500 characters.';
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

    const payload: StockReceiptRequest = {
      productId: parseInt(productId, 10),
      batchNo: batchNo.trim().toUpperCase(),
      productionDate,
      expiryDate: expiryDate ? expiryDate : null,
      locationId: locationId.trim() || 'LOC_KDR_PROD',
      quantity: parseInt(quantity, 10),
      reason: reason.trim() ? reason.trim() : null,
    };

    try {
      const result = await inventoryService.receiveStock(payload);
      resetForm();
      onSuccess(result);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (err.problem?.errors) {
          const mapped: Record<string, string> = {};
          for (const [key, msgs] of Object.entries(err.problem.errors)) {
            const lower = key.toLowerCase();
            if (lower === 'productid') mapped.productId = msgs[0];
            else if (lower === 'batchno') mapped.batchNo = msgs[0];
            else if (lower === 'productiondate') mapped.productionDate = msgs[0];
            else if (lower === 'expirydate') mapped.expiryDate = msgs[0];
            else if (lower === 'quantity') mapped.quantity = msgs[0];
            else if (lower === 'locationid') mapped.locationId = msgs[0];
            else if (lower === 'reason') mapped.reason = msgs[0];
          }
          setFieldErrors(mapped);
        }
        setGeneralError(err.message);
      } else if (err instanceof Error) {
        setGeneralError(err.message);
      } else {
        setGeneralError('An unexpected error occurred while receiving stock.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title="Receive Incoming Stock"
      maxWidth="520px"
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
            disabled={submitting || loadingProducts}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Receiving Stock...</span>
              </>
            ) : (
              <>
                <PackagePlus size={16} />
                <span>Confirm Stock Receipt</span>
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
          <label className="form-label" htmlFor="receipt-product">
            <span>
              Target Product SKU<span className="required-star">*</span>
            </span>
            {loadingProducts && <span className="text-muted" style={{ fontSize: '0.75rem' }}>Loading catalog...</span>}
          </label>
          <select
            id="receipt-product"
            className={`form-input ${fieldErrors.productId ? 'input-error' : ''}`}
            value={productId}
            onChange={(e) => setProductId(e.target.value)}
            disabled={submitting || loadingProducts}
            required
          >
            {products.length === 0 ? (
              <option value="">No active products available</option>
            ) : (
              products.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name} ({p.sku})
                </option>
              ))
            )}
          </select>
          {fieldErrors.productId && <p className="form-error">{fieldErrors.productId}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="receipt-batch-no">
            <span>
              Batch Number<span className="required-star">*</span>
            </span>
            <span className="mono text-muted" style={{ fontSize: '0.75rem' }}>
              Auto-capitalized
            </span>
          </label>
          <input
            id="receipt-batch-no"
            type="text"
            className={`form-input mono ${fieldErrors.batchNo ? 'input-error' : ''}`}
            placeholder="e.g. BAT-202610-01"
            value={batchNo}
            onChange={(e) => setBatchNo(e.target.value.toUpperCase())}
            disabled={submitting}
            maxLength={100}
            required
          />
          {fieldErrors.batchNo && <p className="form-error">{fieldErrors.batchNo}</p>}
          <p className="form-hint">Unique identifier for this production run per product.</p>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
          <div className="form-group">
            <label className="form-label" htmlFor="receipt-prod-date">
              <span>
                Production Date<span className="required-star">*</span>
              </span>
            </label>
            <input
              id="receipt-prod-date"
              type="date"
              className={`form-input ${fieldErrors.productionDate ? 'input-error' : ''}`}
              value={productionDate}
              onChange={(e) => setProductionDate(e.target.value)}
              disabled={submitting}
              required
            />
            {fieldErrors.productionDate && (
              <p className="form-error">{fieldErrors.productionDate}</p>
            )}
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="receipt-exp-date">
              <span>Expiry Date</span>
            </label>
            <input
              id="receipt-exp-date"
              type="date"
              className={`form-input ${fieldErrors.expiryDate ? 'input-error' : ''}`}
              value={expiryDate}
              onChange={(e) => setExpiryDate(e.target.value)}
              disabled={submitting}
            />
            {fieldErrors.expiryDate && <p className="form-error">{fieldErrors.expiryDate}</p>}
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
          <div className="form-group">
            <label className="form-label" htmlFor="receipt-qty">
              <span>
                Quantity Received (Units)<span className="required-star">*</span>
              </span>
            </label>
            <input
              id="receipt-qty"
              type="number"
              className={`form-input currency ${fieldErrors.quantity ? 'input-error' : ''}`}
              min="1"
              step="1"
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
              disabled={submitting}
              required
            />
            {fieldErrors.quantity && <p className="form-error">{fieldErrors.quantity}</p>}
          </div>

          <div className="form-group">
            <label className="form-label" htmlFor="receipt-location">
              <span>Storage Location</span>
              <span className="badge badge-neutral" style={{ fontSize: '0.6875rem' }}>Facility M1</span>
            </label>
            <input
              id="receipt-location"
              type="text"
              className="form-input mono"
              value={locationId}
              onChange={(e) => setLocationId(e.target.value)}
              disabled
            />
          </div>
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label" htmlFor="receipt-reason">
            <span>Receipt Notes / Reason</span>
            <span className="text-muted" style={{ fontSize: '0.75rem' }}>{reason.length}/500</span>
          </label>
          <textarea
            id="receipt-reason"
            className={`form-textarea ${fieldErrors.reason ? 'input-error' : ''}`}
            placeholder="e.g. Initial production run received from packaging facility"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            disabled={submitting}
            maxLength={500}
          />
          {fieldErrors.reason && <p className="form-error">{fieldErrors.reason}</p>}
        </div>
      </form>
    </Modal>
  );
};
