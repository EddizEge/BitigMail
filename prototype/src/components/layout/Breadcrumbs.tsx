import React from 'react';

interface BreadcrumbsProps {
  items: string[];
  onItemClick?: (index: number) => void;
}

export const Breadcrumbs: React.FC<BreadcrumbsProps> = ({ items, onItemClick }) => {
  return (
    <nav aria-label="Ekmek kırıntısı navigasyonu" className="breadcrumbs" data-testid="breadcrumbs">
      {items.map((item, index) => {
        const isLast = index === items.length - 1;
        return (
          <span key={index}>
            {index > 0 && <span style={{ margin: '0 6px', color: '#cbd5e1' }}>/</span>}
            {isLast ? (
              <span style={{ color: 'var(--text-main)', fontWeight: 600 }}>{item}</span>
            ) : (
              <span
                style={{ cursor: onItemClick ? 'pointer' : 'default' }}
                onClick={() => onItemClick && onItemClick(index)}
              >
                {item}
              </span>
            )}
          </span>
        );
      })}
    </nav>
  );
};
