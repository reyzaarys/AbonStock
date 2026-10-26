import React from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import { Package, Layers, ShoppingBag, LogOut, User as UserIcon } from 'lucide-react';
import { getBaseUrl } from '../../lib/apiClient';
import { useAuth } from '../../context/AuthContext';

export const Navbar: React.FC = () => {
  const apiBaseUrl = getBaseUrl();
  const { user, logout, isAuthenticated } = useAuth();
  const navigate = useNavigate();

  const handleLogout = () => {
    logout();
    navigate('/login', { replace: true });
  };

  return (
    <header className="navbar">
      <div className="navbar-inner">
        <div className="brand-section">
          <div className="brand-logo">A</div>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <span className="brand-name">AbonStock</span>
              <span className="brand-tag">ERP &amp; Fulfillment</span>
            </div>
          </div>
        </div>

        <nav className="nav-links">
          <NavLink
            to="/inventory"
            className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
          >
            <Layers size={18} />
            <span>Inventory</span>
          </NavLink>

          <NavLink
            to="/products"
            className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
          >
            <Package size={18} />
            <span>Products</span>
          </NavLink>

          <NavLink
            to="/orders"
            className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
          >
            <ShoppingBag size={18} />
            <span>Orders</span>
          </NavLink>
        </nav>

        <div style={{ display: 'flex', alignItems: 'center', gap: '0.875rem' }}>
          <div className="nav-badge" title={`Connected to backend at ${apiBaseUrl}`}>
            <span className="pulse-dot" />
            <span>API: {apiBaseUrl.replace('http://', '').replace('https://', '')}</span>
          </div>

          {isAuthenticated && user && (
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '0.75rem',
                paddingLeft: '0.75rem',
                borderLeft: '1px solid var(--color-border)',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.375rem', fontSize: '0.875rem' }}>
                <UserIcon size={15} style={{ color: 'var(--color-text-muted)' }} />
                <span style={{ fontWeight: 600, color: 'var(--color-text-main)' }}>
                  {user.displayName || user.username}
                </span>
                <span
                  className={`badge ${user.role === 'Admin' ? 'badge-info' : 'badge-neutral'}`}
                  style={{
                    fontSize: '0.6875rem',
                    padding: '0.125rem 0.375rem',
                    textTransform: 'uppercase',
                    letterSpacing: '0.03em',
                  }}
                >
                  {user.role}
                </span>
              </div>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={handleLogout}
                title="Sign out of AbonStock"
                style={{ padding: '0.375rem 0.625rem', fontSize: '0.8125rem', display: 'flex', alignItems: 'center', gap: '0.25rem' }}
              >
                <LogOut size={14} />
                <span>Logout</span>
              </button>
            </div>
          )}
        </div>
      </div>
    </header>
  );
};
