import React, { useState, useEffect, useCallback } from 'react';
import {
  ShieldCheck,
  ShieldAlert,
  AlertTriangle,
  CheckCircle2,
  RefreshCw,
  Info,
  Layers,
  Database,
  Search,
} from 'lucide-react';
import { inventoryService } from '../../services';
import type { InventoryAuditResponse } from '../../types';
import { LoadingState, ErrorState } from '../../components/common';
import { formatDateTime } from '../../lib';
import { useAuth } from '../../context/AuthContext';

export const InventoryAuditTab: React.FC = () => {
  const { isAdmin } = useAuth();
  const [data, setData] = useState<InventoryAuditResponse | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<unknown | null>(null);

  // Filter by product
  const [productIdInput, setProductIdInput] = useState<string>('');
  const [selectedProductId, setSelectedProductId] = useState<number | undefined>(undefined);

  const fetchAudit = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);

      inventoryService
        .auditInventory(selectedProductId, signal)
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
    [selectedProductId]
  );

  useEffect(() => {
    if (!isAdmin) {
      setLoading(false);
      return;
    }

    const controller = new AbortController();
    fetchAudit(controller.signal);

    return () => {
      controller.abort();
    };
  }, [fetchAudit, isAdmin]);

  const handleFilterSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = productIdInput.trim();
    if (!trimmed) {
      setSelectedProductId(undefined);
    } else {
      const parsed = parseInt(trimmed, 10);
      if (!isNaN(parsed) && parsed > 0) {
        setSelectedProductId(parsed);
      }
    }
  };

  const handleClearFilter = () => {
    setProductIdInput('');
    setSelectedProductId(undefined);
  };

  if (!isAdmin) {
    return (
      <div className="card" style={{ padding: '2rem', textAlign: 'center' }}>
        <ShieldAlert size={48} className="text-warning" style={{ margin: '0 auto 1rem' }} />
        <h3 style={{ marginBottom: '0.5rem', fontWeight: 600 }}>Access Restricted</h3>
        <p className="text-muted" style={{ maxWidth: '480px', margin: '0 auto', fontSize: '0.875rem' }}>
          The Inventory Invariant & Ledger Audit diagnostic dashboard requires Administrator privileges. Please contact an administrator to inspect data integrity.
        </p>
      </div>
    );
  }

  const renderLedgerStatusBadge = (status: string) => {
    switch (status) {
      case 'Valid':
        return <span className="badge badge-success">Valid</span>;
      case 'Unverifiable':
        return <span className="badge badge-warning">Unverifiable</span>;
      case 'Compromised':
        return <span className="badge badge-danger">Compromised</span>;
      default:
        return <span className="badge badge-neutral">{status}</span>;
    }
  };

  return (
    <div>
      {/* Top Controls Toolbar */}
      <div className="card" style={{ marginBottom: '1.25rem', padding: '1rem' }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem', alignItems: 'center', justifyContent: 'space-between' }}>
          <form onSubmit={handleFilterSubmit} style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
            <span style={{ fontSize: '0.8125rem', fontWeight: 600 }}>Scope:</span>
            <input
              type="number"
              className="form-input"
              placeholder="Product ID (optional)"
              value={productIdInput}
              onChange={(e) => setProductIdInput(e.target.value)}
              style={{ width: '160px', padding: '0.375rem 0.625rem', fontSize: '0.8125rem' }}
              min={1}
            />
            <button type="submit" className="btn btn-secondary btn-sm">
              <Search size={13} />
              <span>Apply</span>
            </button>
            {selectedProductId !== undefined && (
              <button type="button" className="btn btn-secondary btn-sm" onClick={handleClearFilter}>
                Show All Products
              </button>
            )}
          </form>

          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => fetchAudit()}
            disabled={loading}
          >
            <RefreshCw size={13} className={loading ? 'animate-spin' : ''} />
            <span>Run Invariant Audit</span>
          </button>
        </div>
      </div>

      {loading && !data && <LoadingState message="Executing read-only invariant diagnostic scan..." />}

      {!!error && !loading && (
        <ErrorState
          error={error}
          onRetry={() => fetchAudit()}
        />
      )}

      {!loading && !error && data && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
          {/* Consistency Health Banner */}
          <div
            className={`card ${data.isSystemConsistent ? 'alert-success' : 'alert-warning'}`}
            style={{
              padding: '1.25rem',
              borderLeft: `4px solid ${data.isSystemConsistent ? '#10b981' : '#f59e0b'}`,
              backgroundColor: data.isSystemConsistent ? '#f0fdf4' : '#fffbeb',
            }}
          >
            <div style={{ display: 'flex', gap: '0.875rem', alignItems: 'flex-start' }}>
              {data.isSystemConsistent ? (
                <CheckCircle2 size={24} style={{ color: '#10b981', flexShrink: 0, marginTop: '2px' }} />
              ) : (
                <AlertTriangle size={24} style={{ color: '#f59e0b', flexShrink: 0, marginTop: '2px' }} />
              )}
              <div style={{ flex: 1 }}>
                <h4 style={{ fontWeight: 600, marginBottom: '0.25rem', color: data.isSystemConsistent ? '#065f46' : '#92400e' }}>
                  {data.isSystemConsistent
                    ? 'All System Invariants Strictly Reconciled'
                    : 'System Integrity Attention Required'}
                </h4>
                <p style={{ fontSize: '0.8125rem', color: data.isSystemConsistent ? '#047857' : '#b45309' }}>
                  {data.isSystemConsistent
                    ? 'All product quantities on hand equal the sum of their physical batch quantities, zero batch invariant violations were found, and the ledger chain is verified.'
                    : `Discrepancies found: ${data.inconsistentProductsCount} inconsistent product balance(s), ${data.invariantViolations.length} batch violation(s), and ledger status: ${data.ledgerAudit.status}.`}
                </p>
                <div style={{ fontSize: '0.75rem', marginTop: '0.5rem', color: '#64748b' }}>
                  Audit Timestamp: <strong>{formatDateTime(data.auditTimestampUtc)}</strong> (Read-Only Scan)
                </div>
              </div>
            </div>
          </div>

          {/* Metric Cards Grid */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '1rem' }}>
            <div className="card" style={{ padding: '1rem' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
                <span className="text-muted" style={{ fontSize: '0.8125rem' }}>Product Balances</span>
                <Database size={16} className="text-muted" />
              </div>
              <div style={{ fontSize: '1.5rem', fontWeight: 700 }}>
                {data.consistentProductsCount} / {data.totalProductsChecked}
              </div>
              <div style={{ fontSize: '0.75rem', color: data.inconsistentProductsCount === 0 ? '#10b981' : '#ef4444' }}>
                {data.inconsistentProductsCount === 0 ? '100% Mathematically Reconciled' : `${data.inconsistentProductsCount} Discrepancies`}
              </div>
            </div>

            <div className="card" style={{ padding: '1rem' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
                <span className="text-muted" style={{ fontSize: '0.8125rem' }}>Batch Invariant Violations</span>
                <Layers size={16} className="text-muted" />
              </div>
              <div style={{ fontSize: '1.5rem', fontWeight: 700 }}>
                {data.invariantViolations.length}
              </div>
              <div style={{ fontSize: '0.75rem', color: data.invariantViolations.length === 0 ? '#10b981' : '#ef4444' }}>
                {data.invariantViolations.length === 0 ? 'Zero Violations Detected' : 'Safety Invariants Breached'}
              </div>
            </div>

            <div className="card" style={{ padding: '1rem' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
                <span className="text-muted" style={{ fontSize: '0.8125rem' }}>Stock Ledger Chain</span>
                {renderLedgerStatusBadge(data.ledgerAudit.status)}
              </div>
              <div style={{ fontSize: '1.5rem', fontWeight: 700 }}>
                {data.ledgerAudit.totalMovementsChecked}
              </div>
              <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted, #64748b)' }}>
                Movements Audited in Ledger
              </div>
            </div>
          </div>

          {/* Section 1: Product Consistency Findings */}
          <div className="card" style={{ padding: '1.25rem' }}>
            <h4 style={{ fontWeight: 600, marginBottom: '0.75rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <Database size={18} />
              <span>Product Invariant: QuantityOnHand == SUM(Batch.PhysicalQuantity)</span>
            </h4>

            {data.discrepancies.length === 0 ? (
              <div style={{ padding: '0.75rem 1rem', borderRadius: '4px', backgroundColor: '#f0fdf4', color: '#166534', fontSize: '0.8125rem' }}>
                ✓ All {data.totalProductsChecked} audited product(s) have exact parity between `Product.QuantityOnHand` and their physical batch balances.
              </div>
            ) : (
              <div className="table-container">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Product</th>
                      <th className="text-right">Sum of Batches</th>
                      <th className="text-right">Quantity On Hand</th>
                      <th className="text-right">Discrepancy</th>
                      <th className="text-center">Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.discrepancies.map((d) => (
                      <tr key={d.productId}>
                        <td>
                          <strong>{d.sku}</strong> — {d.productName}
                        </td>
                        <td className="text-right mono">{d.expectedQuantityOnHand}</td>
                        <td className="text-right mono">{d.actualQuantityOnHand}</td>
                        <td className="text-right mono" style={{ color: '#ef4444', fontWeight: 700 }}>
                          {d.discrepancy > 0 ? `+${d.discrepancy}` : d.discrepancy}
                        </td>
                        <td className="text-center">
                          <span className="badge badge-danger">Inconsistent</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* Section 2: Batch Invariant Violations */}
          <div className="card" style={{ padding: '1.25rem' }}>
            <h4 style={{ fontWeight: 600, marginBottom: '0.75rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <Layers size={18} />
              <span>Batch Invariants Check</span>
            </h4>

            {data.invariantViolations.length === 0 ? (
              <div style={{ padding: '0.75rem 1rem', borderRadius: '4px', backgroundColor: '#f0fdf4', color: '#166534', fontSize: '0.8125rem' }}>
                ✓ Zero batch violations detected. Physical quantities are non-negative, reservation limits (0 &le; Reserved &le; Physical) are respected, and depleted batch states are coherent.
              </div>
            ) : (
              <div className="table-container">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Batch No</th>
                      <th>Product</th>
                      <th>Status</th>
                      <th className="text-right">Physical</th>
                      <th className="text-right">Reserved</th>
                      <th>Violation Type</th>
                      <th>Description</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.invariantViolations.map((v) => (
                      <tr key={v.batchId}>
                        <td className="mono" style={{ fontWeight: 600 }}>{v.batchNo}</td>
                        <td>{v.productSku ?? `PID #${v.productId}`}</td>
                        <td><span className="badge badge-neutral">{v.status}</span></td>
                        <td className="text-right mono">{v.physicalQuantity}</td>
                        <td className="text-right mono">{v.reservedQuantity}</td>
                        <td><span className="badge badge-danger">{v.violationType}</span></td>
                        <td style={{ fontSize: '0.8125rem', color: '#dc2626' }}>{v.description}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* Section 3: Ledger Chain Audit & Limitations */}
          <div className="card" style={{ padding: '1.25rem' }}>
            <h4 style={{ fontWeight: 600, marginBottom: '0.75rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <ShieldCheck size={18} />
              <span>Stock Ledger Integrity Analysis</span>
            </h4>

            <div style={{ marginBottom: '1rem', padding: '0.75rem 1rem', borderRadius: '4px', backgroundColor: 'var(--color-bg, #f8fafc)', border: '1px solid var(--color-border, #e2e8f0)' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.25rem' }}>
                <span style={{ fontWeight: 600, fontSize: '0.875rem' }}>Ledger Summary:</span>
                {renderLedgerStatusBadge(data.ledgerAudit.status)}
              </div>
              <p style={{ fontSize: '0.8125rem', color: 'var(--color-text-muted, #64748b)' }}>
                {data.ledgerAudit.summary}
              </p>
            </div>

            {/* Contextual Warning for Unverifiable Status */}
            {data.ledgerAudit.status === 'Unverifiable' && (
              <div className="alert alert-warning" style={{ marginBottom: '1rem', display: 'flex', gap: '0.5rem', alignItems: 'flex-start' }}>
                <Info size={18} style={{ flexShrink: 0, marginTop: '2px' }} />
                <div style={{ fontSize: '0.8125rem' }}>
                  <strong>Understanding "Unverifiable" Status:</strong> This status indicates that legacy batches (e.g. seed data created prior to immutable ledger tracking) do not have corresponding historical initial receipts in `StockMovements`. <em>This is an audit trail completeness limitation, not proof of an active stock mismatch.</em>
                </div>
              </div>
            )}

            {/* Discovered Ledger Issues */}
            {data.ledgerAudit.issues.length > 0 && (
              <div style={{ marginBottom: '1rem' }}>
                <div style={{ fontWeight: 600, fontSize: '0.8125rem', marginBottom: '0.375rem', color: data.ledgerAudit.status === 'Compromised' ? '#ef4444' : '#b45309' }}>
                  Audit Trail Observations ({data.ledgerAudit.issues.length}):
                </div>
                <ul style={{ paddingLeft: '1.25rem', fontSize: '0.75rem', color: 'var(--color-text-muted, #64748b)' }}>
                  {data.ledgerAudit.issues.map((issue, idx) => (
                    <li key={idx} style={{ marginBottom: '0.25rem' }}>{issue}</li>
                  ))}
                </ul>
              </div>
            )}

            {/* Documented Architectural Limitations */}
            {data.ledgerAudit.limitations.length > 0 && (
              <div style={{ borderTop: '1px solid var(--color-border, #e2e8f0)', paddingTop: '0.75rem' }}>
                <div style={{ fontWeight: 600, fontSize: '0.8125rem', marginBottom: '0.375rem', color: 'var(--color-text-muted, #64748b)' }}>
                  Documented Design Boundaries & Ledger Model Limitations:
                </div>
                <ul style={{ paddingLeft: '1.25rem', fontSize: '0.75rem', color: 'var(--color-text-muted, #64748b)' }}>
                  {data.ledgerAudit.limitations.map((lim, idx) => (
                    <li key={idx} style={{ marginBottom: '0.25rem' }}>{lim}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
