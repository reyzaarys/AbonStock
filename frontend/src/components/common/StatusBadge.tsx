import React from 'react';
import type { BatchStatus, OrderStatus } from '../../types';

interface StatusBadgeProps {
  status: BatchStatus | OrderStatus | string | boolean;
}

export const StatusBadge: React.FC<StatusBadgeProps> = ({ status }) => {
  if (typeof status === 'boolean') {
    return status ? (
      <span className="badge badge-success">Active</span>
    ) : (
      <span className="badge badge-neutral">Inactive</span>
    );
  }

  const normalized = String(status);

  switch (normalized) {
    // Batch & Order positive / ready states
    case 'Released':
    case 'Completed':
      return <span className="badge badge-success">{normalized}</span>;

    // Batch & Order pending states
    case 'PendingRelease':
      return <span className="badge badge-warning">Pending Release</span>;
    case 'Pending':
      return <span className="badge badge-warning">Pending</span>;

    // Order confirmed / in-progress
    case 'Confirmed':
      return <span className="badge badge-info">Confirmed</span>;

    // Danger / problem states
    case 'Quarantined':
    case 'Cancelled':
      return <span className="badge badge-danger">{normalized}</span>;

    // Depleted / neutral states
    case 'Depleted':
      return <span className="badge badge-neutral">Depleted</span>;

    default:
      return <span className="badge badge-neutral">{normalized}</span>;
  }
};
