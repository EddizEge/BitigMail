import React from 'react';

export interface BadgeProps {
  variant?: 'warning' | 'success' | 'error' | 'info' | 'neutral';
  children: React.ReactNode;
  className?: string;
}

export const Badge: React.FC<BadgeProps> = ({
  variant = 'neutral',
  children,
  className = '',
}) => {
  const variantClass = `badge-${variant}`;
  return <span className={`badge ${variantClass} ${className}`}>{children}</span>;
};
