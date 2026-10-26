import React from 'react';
import { Loader2 } from 'lucide-react';

interface LoadingStateProps {
  message?: string;
}

export const LoadingState: React.FC<LoadingStateProps> = ({
  message = 'Loading data from backend...',
}) => {
  return (
    <div className="loading-box" role="status" aria-live="polite">
      <Loader2 className="spinner" size={32} />
      <p className="loading-text">{message}</p>
    </div>
  );
};
