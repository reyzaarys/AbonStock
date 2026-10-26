import React, { useState, useEffect } from 'react';
import {
  Loader2,
  AlertCircle,
  Plus,
  Trash2,
  ShoppingBag,
  Info,
} from 'lucide-react';
import { Modal } from '../../components/common';
import { ordersService, productsService } from '../../services';
import { ApiError } from '../../lib/apiClient';
import type {
  CreateOrderRequest,
  OrderResponse,
  ProductResponse,
} from '../../types';
import { formatCurrency } from '../../lib';

interface CreateOrderModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (order: OrderResponse) => void;
}

interface LineItemState {
  id: string; // unique local ID for React keys
  productId: number;
  quantity: string;
}

export const CreateOrderModal: React.FC<CreateOrderModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
}) => {
  const [products, setProducts] = useState<ProductResponse[]>([]);
  const [loadingProducts, setLoadingProducts] = useState<boolean>(false);

  // Form state
  const [customerName, setCustomerName] = useState<string>('');
  const [customerEmail, setCustomerEmail] = useState<string>('');
  const [items, setItems] = useState<LineItemState[]>([]);

  const [submitting, setSubmitting] = useState<boolean>(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);

  // Fetch active product catalog whenever modal opens
  useEffect(() => {
    if (isOpen) {
      setLoadingProducts(true);
      productsService
        .list({ includeInactive: false, pageSize: 100 })
        .then((res) => {
          setProducts(res.items);
          if (res.items.length > 0 && items.length === 0) {
            setItems([
              {
                id: crypto.randomUUID ? crypto.randomUUID() : String(Date.now()),
                productId: res.items[0].id,
                quantity: '1',
              },
            ]);
          }
        })
        .catch(() => {
          // If fetching products fails, show error state
        })
        .finally(() => {
          setLoadingProducts(false);
        });
    }
  }, [isOpen]);

  const resetForm = () => {
    setCustomerName('');
    setCustomerEmail('');
    setItems([]);
    setFieldErrors({});
    setGeneralError(null);
  };

  const handleClose = () => {
    if (!submitting) {
      resetForm();
      onClose();
    }
  };

  const handleAddItem = () => {
    // Pick the first product not already in the items list, or fallback to first
    const usedIds = new Set(items.map((it) => it.productId));
    const nextAvailableProduct = products.find((p) => !usedIds.has(p.id)) || products[0];

    if (!nextAvailableProduct) return;

    setItems((prev) => [
      ...prev,
      {
        id: crypto.randomUUID ? crypto.randomUUID() : String(Date.now() + Math.random()),
        productId: nextAvailableProduct.id,
        quantity: '1',
      },
    ]);
  };

  const handleRemoveItem = (idToRemove: string) => {
    if (items.length <= 1) return; // Keep at least 1 item
    setItems((prev) => prev.filter((it) => it.id !== idToRemove));
  };

  const handleProductChange = (index: number, newProductId: number) => {
    setItems((prev) => {
      const next = [...prev];
      next[index] = { ...next[index], productId: newProductId };
      return next;
    });
  };

  const handleQuantityChange = (index: number, newQuantity: string) => {
    setItems((prev) => {
      const next = [...prev];
      next[index] = { ...next[index], quantity: newQuantity };
      return next;
    });
  };

  // Helper product lookups
  const getProduct = (productId: number) => products.find((p) => p.id === productId);

  // Calculate live client preview
  let totalPreviewAmount = 0;
  let totalQuantityUnits = 0;

  for (const it of items) {
    const p = getProduct(it.productId);
    const qty = parseInt(it.quantity, 10);
    if (p && !isNaN(qty) && qty > 0) {
      totalPreviewAmount += p.unitPrice * qty;
      totalQuantityUnits += qty;
    }
  }

  // Duplicate product check
  const duplicateProductIds = items
    .map((it) => it.productId)
    .filter((id, idx, arr) => arr.indexOf(id) !== idx && id > 0);

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    const trimmedName = customerName.trim();
    const trimmedEmail = customerEmail.trim();

    if (!trimmedName) {
      errors.customerName = 'Customer Name is required.';
    } else if (trimmedName.length > 200) {
      errors.customerName = 'Customer Name cannot exceed 200 characters.';
    }

    if (trimmedEmail) {
      if (trimmedEmail.length > 200) {
        errors.customerEmail = 'Customer Email cannot exceed 200 characters.';
      } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
        errors.customerEmail = 'Customer Email must be a valid email address.';
      }
    }

    if (items.length === 0) {
      errors.items = 'Order must contain at least one line item.';
    } else {
      if (duplicateProductIds.length > 0) {
        errors.items =
          'Order cannot contain duplicate products. Please combine quantities into a single line item.';
      }

      for (let i = 0; i < items.length; i++) {
        const it = items[i];
        const parsedQty = parseInt(it.quantity, 10);
        if (!it.productId || it.productId <= 0) {
          errors[`item_${i}`] = 'Please select a valid product.';
        } else if (!it.quantity || isNaN(parsedQty) || parsedQty < 1 || parsedQty > 100000) {
          errors[`item_${i}`] = 'Quantity must be an integer between 1 and 100,000.';
        }
      }
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setGeneralError(null);

    if (!validate()) return;

    setSubmitting(true);

    const payload: CreateOrderRequest = {
      customerName: customerName.trim(),
      customerEmail: customerEmail.trim() ? customerEmail.trim() : null,
      items: items.map((it) => ({
        productId: it.productId,
        quantity: parseInt(it.quantity, 10),
      })),
    };

    try {
      const created = await ordersService.create(payload);
      resetForm();
      onSuccess(created);
      onClose();
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        if (err.problem?.errors) {
          const mappedErrors: Record<string, string> = {};
          for (const [key, messages] of Object.entries(err.problem.errors)) {
            const lowerKey = key.toLowerCase();
            if (lowerKey === 'customername') mappedErrors.customerName = messages[0];
            else if (lowerKey === 'customeremail') mappedErrors.customerEmail = messages[0];
            else if (lowerKey === 'items') mappedErrors.items = messages[0];
            else mappedErrors[key] = messages[0];
          }
          setFieldErrors(mappedErrors);
        }
        setGeneralError(err.message);
      } else if (err instanceof Error) {
        setGeneralError(err.message);
      } else {
        setGeneralError('An unexpected error occurred while creating the order.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title="Create New Order"
      maxWidth="720px"
    >
      <form onSubmit={handleSubmit} noValidate>
        {generalError && (
          <div className="alert-toast alert-toast-error" style={{ marginBottom: '1.25rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <AlertCircle size={18} />
              <span>{generalError}</span>
            </div>
          </div>
        )}

        <div className="form-group">
          <label className="form-label" htmlFor="order-customer-name">
            <span>Customer Name <span className="required-star">*</span></span>
          </label>
          <input
            id="order-customer-name"
            type="text"
            className={`form-input ${fieldErrors.customerName ? 'input-error' : ''}`}
            placeholder="e.g. Toko Berkah Mandiri / Ibu Siti"
            value={customerName}
            onChange={(e) => setCustomerName(e.target.value)}
            disabled={submitting}
            maxLength={200}
            required
          />
          {fieldErrors.customerName && (
            <div className="form-error">{fieldErrors.customerName}</div>
          )}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="order-customer-email">
            <span>Customer Email <span className="text-muted">(Optional)</span></span>
          </label>
          <input
            id="order-customer-email"
            type="email"
            className={`form-input ${fieldErrors.customerEmail ? 'input-error' : ''}`}
            placeholder="e.g. orders@berkahmandiri.com"
            value={customerEmail}
            onChange={(e) => setCustomerEmail(e.target.value)}
            disabled={submitting}
            maxLength={200}
          />
          {fieldErrors.customerEmail && (
            <div className="form-error">{fieldErrors.customerEmail}</div>
          )}
        </div>

        {/* Dynamic Items Builder */}
        <div className="form-group">
          <div className="form-label">
            <span>Order Line Items <span className="required-star">*</span></span>
            <span className="text-muted" style={{ fontSize: '0.75rem', fontWeight: 'normal' }}>
              Min 1 item &bull; No duplicate products
            </span>
          </div>

          {loadingProducts ? (
            <div style={{ padding: '1.5rem', textAlign: 'center', color: 'var(--color-text-muted)' }}>
              <Loader2 size={20} className="spinner" style={{ margin: '0 auto 0.5rem' }} />
              <div>Loading active product catalog...</div>
            </div>
          ) : products.length === 0 ? (
            <div className="action-notice-box action-notice-warning">
              <AlertCircle size={18} className="action-notice-icon" />
              <div>
                <strong>No active products available.</strong> Please create and activate products in the Products tab before placing an order.
              </div>
            </div>
          ) : (
            <div className="order-items-builder">
              <div className="order-items-header">
                <div>Product</div>
                <div>Quantity</div>
                <div>Unit Price</div>
                <div>Line Total</div>
                <div></div>
              </div>

              {items.map((it, idx) => {
                const prod = getProduct(it.productId);
                const qty = parseInt(it.quantity, 10);
                const lineTotal = prod && !isNaN(qty) && qty > 0 ? prod.unitPrice * qty : 0;
                const isDuplicate = duplicateProductIds.includes(it.productId);
                const itemError = fieldErrors[`item_${idx}`];

                return (
                  <div key={it.id}>
                    <div className="order-item-row">
                      {/* Product dropdown */}
                      <div>
                        <select
                          className={`form-input ${isDuplicate || itemError ? 'input-error' : ''}`}
                          value={it.productId}
                          onChange={(e) =>
                            handleProductChange(idx, parseInt(e.target.value, 10))
                          }
                          disabled={submitting}
                          style={{ padding: '0.375rem 0.5rem', fontSize: '0.8125rem' }}
                        >
                          {products.map((p) => (
                            <option key={p.id} value={p.id}>
                              [{p.sku}] {p.name}
                            </option>
                          ))}
                        </select>
                      </div>

                      {/* Quantity input */}
                      <div>
                        <input
                          type="number"
                          min="1"
                          max="100000"
                          step="1"
                          className={`form-input mono ${itemError ? 'input-error' : ''}`}
                          value={it.quantity}
                          onChange={(e) => handleQuantityChange(idx, e.target.value)}
                          disabled={submitting}
                          style={{ padding: '0.375rem 0.5rem', fontSize: '0.8125rem', textAlign: 'right' }}
                        />
                      </div>

                      {/* Unit Price preview */}
                      <div className="mono text-muted" style={{ fontSize: '0.8125rem', textAlign: 'right' }}>
                        {prod ? formatCurrency(prod.unitPrice) : '—'}
                      </div>

                      {/* Line Total preview */}
                      <div className="mono font-semibold" style={{ fontSize: '0.8125rem', textAlign: 'right' }}>
                        {formatCurrency(lineTotal)}
                      </div>

                      {/* Remove item button */}
                      <div style={{ textAlign: 'center' }}>
                        <button
                          type="button"
                          className="btn btn-ghost-danger"
                          onClick={() => handleRemoveItem(it.id)}
                          disabled={submitting || items.length <= 1}
                          title={items.length <= 1 ? 'An order must contain at least 1 item' : 'Remove item'}
                          style={{ padding: '0.375rem', borderRadius: '0.25rem' }}
                        >
                          <Trash2 size={16} />
                        </button>
                      </div>
                    </div>

                    {itemError && (
                      <div className="form-error" style={{ marginBottom: '0.5rem' }}>
                        {itemError}
                      </div>
                    )}
                  </div>
                );
              })}

              <div style={{ marginTop: '0.5rem', display: 'flex', justifyContent: 'flex-start' }}>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={handleAddItem}
                  disabled={submitting || products.length === 0}
                >
                  <Plus size={14} />
                  <span>Add Line Item</span>
                </button>
              </div>
            </div>
          )}

          {fieldErrors.items && (
            <div className="form-error" style={{ marginTop: '-0.75rem', marginBottom: '1rem' }}>
              {fieldErrors.items}
            </div>
          )}
        </div>

        {/* Order Summary & Authoritative Pricing Callout */}
        <div className="order-summary-card">
          <div className="order-summary-row">
            <span>Total Line Items:</span>
            <span className="font-semibold">{items.length}</span>
          </div>
          <div className="order-summary-row">
            <span>Total Units to Order:</span>
            <span className="font-semibold">{totalQuantityUnits} units</span>
          </div>
          <div className="order-summary-row total">
            <span>Estimated Total:</span>
            <span className="currency font-bold" style={{ color: 'var(--color-primary)' }}>
              {formatCurrency(totalPreviewAmount)}
            </span>
          </div>
          <div className="pricing-authoritative-badge">
            <Info size={14} />
            <span>
              Order pricing is authoritative: unit prices are looked up from the product catalog and calculated on the server.
            </span>
          </div>
        </div>

        {/* Modal Actions */}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '1.5rem' }}>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={handleClose}
            disabled={submitting}
          >
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={submitting || loadingProducts || products.length === 0}
          >
            {submitting ? (
              <>
                <Loader2 size={16} className="spinner" />
                <span>Creating Order...</span>
              </>
            ) : (
              <>
                <ShoppingBag size={16} />
                <span>Create Order</span>
              </>
            )}
          </button>
        </div>
      </form>
    </Modal>
  );
};
