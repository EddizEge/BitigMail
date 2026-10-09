import React from 'react';

interface PageHeaderProps {
  title: React.ReactNode;
  description: React.ReactNode;
  actions?: React.ReactNode;
  breadcrumb?: React.ReactNode;
  testId?: string;
}

/** Her ana ekranın başındaki tek tip başlık: ad + tek cümlelik açıklama + isteğe bağlı eylemler. */
export const PageHeader: React.FC<PageHeaderProps> = ({ title, description, actions, breadcrumb, testId }) => (
  <div className="page-subheader page-header" data-testid={testId}>
    <div className="page-header-copy">
      {breadcrumb && <div className="page-header-breadcrumb">{breadcrumb}</div>}
      <h1 className="page-title">{title}</h1>
      <p className="page-subtitle" data-testid={testId ? `${testId}-description` : undefined}>{description}</p>
    </div>
    {actions && <div className="page-header-actions">{actions}</div>}
  </div>
);

/** Motor (yerel hizmet) bağlantısı yokken kullanıcıya gösterilen sade yönlendirme. */
export const EngineOfflineNotice: React.FC<{ action: string }> = ({ action }) => (
  <div className="notice notice-warning" role="alert" data-testid="service-offline-banner">
    <strong>BitigMail'in posta motoruna ulaşılamıyor</strong>
    <p>
      {action} için uygulamayı sistem tepsisinden <b>Çıkış</b> ile kapatıp yeniden açın.
      Sorun sürerse bilgisayarı yeniden başlatıp tekrar deneyin.
    </p>
  </div>
);

/** Teknik ayrıntılar (hash, biçim imzası, protokol açıklamaları) için açılır bölüm. */
export const TechnicalDetails: React.FC<{ children: React.ReactNode; summary?: string; testId?: string }> = ({
  children,
  summary = 'Teknik ayrıntılar',
  testId,
}) => (
  <details className="tech-details" data-testid={testId}>
    <summary>{summary}</summary>
    <div className="tech-details-body">{children}</div>
  </details>
);

export interface ChoiceOption<T extends string> {
  value: T;
  label: string;
  hint?: string;
  testId?: string;
}

interface ChoiceSelectorProps<T extends string> {
  label: string;
  options: ChoiceOption<T>[];
  value: T;
  onChange: (value: T) => void;
  testId?: string;
}

/** Aktarım ekranının 2. adımı: işlem içindeki tek alt tür / yön seçimi. */
export function ChoiceSelector<T extends string>({ label, options, value, onChange, testId }: ChoiceSelectorProps<T>) {
  return (
    <div className="choice-selector" role="group" aria-label={label} data-testid={testId}>
      <span className="step-label">{label}</span>
      <div className="choice-selector-options">
        {options.map((option) => (
          <button
            key={option.value}
            type="button"
            className={value === option.value ? 'active' : ''}
            aria-pressed={value === option.value}
            onClick={() => onChange(option.value)}
            data-testid={option.testId}
          >
            <strong>{option.label}</strong>
            {option.hint && <span>{option.hint}</span>}
          </button>
        ))}
      </div>
    </div>
  );
}

interface EmptyStateProps {
  title: string;
  children: React.ReactNode;
  action?: React.ReactNode;
  testId?: string;
}

export const EmptyState: React.FC<EmptyStateProps> = ({ title, children, action, testId }) => (
  <div className="empty-state-card" data-testid={testId}>
    <h2>{title}</h2>
    <div className="empty-state-copy">{children}</div>
    {action && <div className="empty-state-action">{action}</div>}
  </div>
);
