import React, { useState, useEffect, useCallback } from 'react';
import { Search, RefreshCw, Package, Plus, Edit2, Ban, CheckCircle2, X } from 'lucide-react';
import { productsService } from '../../services';
import type { PagedResult, ProductResponse } from '../../types';
import {
  PageHeader,
  LoadingState,
  ErrorState,
  EmptyState,
  Pagination,
  StatusBadge,
} from '../../components/common';
import { formatCurrency } from '../../lib';
import { CreateProductModal } from './CreateProductModal';
import { EditProductModal } from './EditProductModal';
import { DeactivateProductDialog } from './DeactivateProductDialog';
import { useAuth } from '../../context/AuthContext';

interface ToastNotification {
  type: 'success' | 'error';
  message: string;
}

export const ProductsPage: React.FC = () => {
  const { isAdmin } = useAuth();
  const [data, setData] = useState<PagedResult<ProductResponse> | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<unknown | null>(null);

  // Filter and pagination state
  const [page, setPage] = useState<number>(1);
  const [pageSize] = useState<number>(10);
  const [searchInput, setSearchInput] = useState<string>('');
  const [searchTerm, setSearchTerm] = useState<string>('');
  const [includeInactive, setIncludeInactive] = useState<boolean>(false);

  // Workflow modals state
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [editingProduct, setEditingProduct] = useState<ProductResponse | null>(null);
  const [deactivatingProduct, setDeactivatingProduct] = useState<ProductResponse | null>(null);

  // Toast feedback
  const [toast, setToast] = useState<ToastNotification | null>(null);

  // Auto-dismiss toast after 6 seconds
  useEffect(() => {
    if (!toast) return;
    const timer = setTimeout(() => {
      setToast(null);
    }, 6000);
    return () => clearTimeout(timer);
  }, [toast]);

  const fetchProducts = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);

      productsService
        .list(
          {
            page,
            pageSize,
            search: searchTerm || undefined,
            includeInactive,
          },
          signal
        )
        .then((result) => {
          setData(result);
          setError(null);
        })
        .catch((err: unknown) => {
          if (err instanceof DOMException && err.name === 'AbortError') {
            return;
          }
          setError(err);
        })
        .finally(() => {
          setLoading(false);
        });
    },
    [page, pageSize, searchTerm, includeInactive]
  );

  useEffect(() => {
    const controller = new AbortController();
    fetchProducts(controller.signal);

    return () => {
      controller.abort();
    };
  }, [fetchProducts]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    setSearchTerm(searchInput.trim());
  };

  const handleIncludeInactiveChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setPage(1);
    setIncludeInactive(e.target.checked);
  };

  const handleResetFilters = () => {
    setSearchInput('');
    setSearchTerm('');
    setIncludeInactive(false);
    setPage(1);
  };

  const handleCreateSuccess = (created: ProductResponse) => {
    setToast({
      type: 'success',
      message: `Product "${created.name}" (${created.sku}) created successfully.`,
    });
    fetchProducts();
  };

  const handleEditSuccess = (updated: ProductResponse) => {
    setToast({
      type: 'success',
      message: `Product "${updated.name}" (${updated.sku}) updated successfully.`,
    });
    fetchProducts();
  };

  const handleDeactivateSuccess = (deactivated: ProductResponse) => {
    setToast({
      type: 'success',
      message: `Product "${deactivated.name}" (${deactivated.sku}) has been deactivated.`,
    });
    fetchProducts();
  };

  return (
    <div>
      <PageHeader
        title="Product Catalog"
        description="Master catalog of shredded meat products (Abon Sapi, Abon Ayam) with server-authoritative pricing and inventory balances."
        action={
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => fetchProducts()}
              disabled={loading}
              title="Refresh list"
            >
              <RefreshCw size={16} className={loading ? 'spinner' : ''} />
              <span>Refresh</span>
            </button>
            {isAdmin && (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => setCreateModalOpen(true)}
              >
                <Plus size={16} />
                <span>Add Product</span>
              </button>
            )}
          </div>
        }
      />

      {toast && (
        <div
          className={`alert-toast ${
            toast.type === 'success' ? 'alert-toast-success' : 'alert-toast-error'
          }`}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <CheckCircle2 size={18} />
            <span>{toast.message}</span>
          </div>
          <button
            type="button"
            className="alert-toast-close"
            onClick={() => setToast(null)}
            aria-label="Dismiss notification"
          >
            <X size={16} />
          </button>
        </div>
      )}

      <div className="filters-bar">
        <form onSubmit={handleSearchSubmit} className="search-input-wrap">
          <Search size={16} className="search-icon" />
          <input
            type="text"
            className="search-input"
            placeholder="Search by SKU or name..."
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
          />
        </form>

        <div className="filter-group">
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={includeInactive}
              onChange={handleIncludeInactiveChange}
            />
            <span>Include Inactive</span>
          </label>
        </div>
      </div>

      {error ? (
        <ErrorState
          error={error}
          onRetry={() => fetchProducts()}
          title="Could not load products"
        />
      ) : loading && !data ? (
        <LoadingState message="Fetching products from backend..." />
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          icon={<Package size={36} className="empty-default-icon" />}
          title="No products found"
          description={
            searchTerm || includeInactive
              ? 'No products matched your search or filter criteria.'
              : 'There are currently no products registered in the database.'
          }
          action={
            searchTerm || includeInactive ? (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={handleResetFilters}
              >
                Clear Filters
              </button>
            ) : isAdmin ? (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => setCreateModalOpen(true)}
              >
                <Plus size={16} />
                <span>Create First Product</span>
              </button>
            ) : undefined
          }
        />
      ) : (
        <div className="table-card">
          <div className="table-container">
            <table className="data-table">
              <thead>
                <tr>
                  <th style={{ width: '120px' }}>SKU</th>
                  <th>Product Name</th>
                  <th>Description</th>
                  <th className="text-right" style={{ width: '130px' }}>
                    Unit Price
                  </th>
                  <th className="text-right" style={{ width: '100px' }}>
                    On Hand
                  </th>
                  <th className="text-center" style={{ width: '90px' }}>
                    Status
                  </th>
                  <th className="text-center" style={{ width: '70px' }}>
                    Ver.
                  </th>
                  <th className="text-center" style={{ width: '150px' }}>
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((product) => (
                  <tr key={product.id} className="table-row-hover">
                    <td>
                      <span className="sku-pill">{product.sku}</span>
                    </td>
                    <td>
                      <div className="font-semibold">{product.name}</div>
                    </td>
                    <td>
                      <span className="text-muted">
                        {product.description || '—'}
                      </span>
                    </td>
                    <td className="text-right">
                      <span className="currency">
                        {formatCurrency(product.unitPrice)}
                      </span>
                    </td>
                    <td className="text-right">
                      <span className="font-semibold">
                        {product.quantityOnHand.toLocaleString()}
                      </span>
                    </td>
                    <td className="text-center">
                      <StatusBadge status={product.isActive} />
                    </td>
                    <td className="text-center mono text-muted" title={`xmin concurrency token: ${product.version}`}>
                      v{product.version}
                    </td>
                    <td className="text-center">
                      {isAdmin ? (
                        <div className="table-actions">
                          <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            onClick={() => setEditingProduct(product)}
                            title={`Edit ${product.sku}`}
                          >
                            <Edit2 size={13} />
                            <span>Edit</span>
                          </button>
                          {product.isActive ? (
                            <button
                              type="button"
                              className="btn btn-ghost-danger btn-sm"
                              onClick={() => setDeactivatingProduct(product)}
                              title={`Deactivate ${product.sku}`}
                            >
                              <Ban size={13} />
                              <span>Deactivate</span>
                            </button>
                          ) : (
                            <span
                              className="badge badge-neutral"
                              style={{ fontSize: '0.7rem', padding: '0.2rem 0.4rem' }}
                            >
                              Deactivated
                            </span>
                          )}
                        </div>
                      ) : (
                        <span className="text-muted" style={{ fontSize: '0.75rem' }}>
                          View only
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            totalPages={data.totalPages}
            hasNextPage={data.hasNextPage}
            hasPreviousPage={data.hasPreviousPage}
            onPageChange={(newPage) => setPage(newPage)}
            disabled={loading}
          />
        </div>
      )}

      {/* Workflow Modals */}
      <CreateProductModal
        isOpen={createModalOpen}
        onClose={() => setCreateModalOpen(false)}
        onSuccess={handleCreateSuccess}
      />

      <EditProductModal
        product={editingProduct}
        isOpen={!!editingProduct}
        onClose={() => setEditingProduct(null)}
        onSuccess={handleEditSuccess}
      />

      <DeactivateProductDialog
        product={deactivatingProduct}
        isOpen={!!deactivatingProduct}
        onClose={() => setDeactivatingProduct(null)}
        onSuccess={handleDeactivateSuccess}
      />
    </div>
  );
};
