import React from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { LoadingState } from '../common/LoadingState';
import { ShieldAlert } from 'lucide-react';
import type { UserRole } from '../../types';

interface ProtectedRouteProps {
  requiredRole?: UserRole | UserRole[];
}

export const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ requiredRole }) => {
  const { isAuthenticated, isLoading, hasRole } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return (
      <div style={{ padding: '4rem 1.5rem', maxWidth: '600px', margin: '0 auto' }}>
        <LoadingState message="Verifying user session..." />
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  if (requiredRole && !hasRole(requiredRole)) {
    return (
      <div style={{ padding: '3rem 1.5rem', maxWidth: '600px', margin: '0 auto' }}>
        <div className="card" style={{ textAlign: 'center', borderColor: '#fca5a5' }}>
          <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '1rem' }}>
            <ShieldAlert size={48} style={{ color: '#ef4444' }} />
          </div>
          <h2 style={{ fontSize: '1.25rem', fontWeight: 600, color: '#991b1b', marginBottom: '0.5rem' }}>
            403 — Access Denied
          </h2>
          <p style={{ color: '#64748b', fontSize: '0.875rem', marginBottom: '1.5rem' }}>
            Your account does not have sufficient administrative permissions to access this feature.
          </p>
        </div>
      </div>
    );
  }

  return <Outlet />;
};
