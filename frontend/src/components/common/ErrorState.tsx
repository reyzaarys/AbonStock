import React from 'react';
import { AlertCircle, RefreshCw, ServerOff } from 'lucide-react';
import { ApiError } from '../../lib/apiClient';

interface ErrorStateProps {
  error: unknown;
  onRetry?: () => void;
  title?: string;
}

export const ErrorState: React.FC<ErrorStateProps> = ({
  error,
  onRetry,
  title = 'Failed to load data',
}) => {
  const isApiError = error instanceof ApiError;
  const isNetwork = isApiError && error.isNetworkError;
  const status = isApiError ? error.status : undefined;
  const detail =
    error instanceof Error ? error.message : 'An unexpected error occurred';

  return (
    <div className="error-box" role="alert">
      <div className="error-header">
        {isNetwork ? (
          <ServerOff className="error-icon" size={28} />
        ) : (
          <AlertCircle className="error-icon" size={28} />
        )}
        <div>
          <h3 className="error-title">
            {isNetwork ? 'Backend API Unreachable' : title}
            {status ? <span className="error-code"> (HTTP {status})</span> : null}
          </h3>
          <p className="error-desc">{detail}</p>
        </div>
      </div>

      {isNetwork && (
        <div className="error-hint">
          <strong>Troubleshooting tip:</strong> Ensure the ASP.NET Core backend is
          running locally on <code>http://localhost:5299</code>. Run{' '}
          <code>dotnet run --project src/AbonStock.Api</code> to start the API.
        </div>
      )}

      {onRetry && (
        <div className="error-actions">
          <button
            type="button"
            className="btn btn-secondary error-retry-btn"
            onClick={onRetry}
          >
            <RefreshCw size={16} />
            <span>Try Again</span>
          </button>
        </div>
      )}
    </div>
  );
};
