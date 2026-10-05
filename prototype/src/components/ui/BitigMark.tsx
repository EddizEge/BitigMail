import React from 'react';

interface BitigMarkProps {
  size?: number;
  className?: string;
}

/**
 * Official BitigMail Brand Mark.
 * Source of truth: design/assets/bitigmail-mark-v1.png (white background).
 * Copied to public/bitigmail-mark-v1.png.
 */
export const BitigMark: React.FC<BitigMarkProps> = ({ size = 32, className = '' }) => {
  return (
    <img
      src="/bitigmail-mark-v1.png"
      alt="BitigMail Logo"
      width={size}
      height={size}
      className={`bitigmail-brand-logo ${className}`}
      style={{
        width: `${size}px`,
        height: `${size}px`,
        objectFit: 'contain',
        display: 'inline-block',
      }}
      data-testid="bitigmail-logo"
    />
  );
};
