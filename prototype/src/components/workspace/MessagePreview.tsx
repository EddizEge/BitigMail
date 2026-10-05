import React from 'react';
import { Message } from '../../types';
import { ArchiveMessagePreviewResponse } from '../../types/localEngine';
import { IconFilePdf } from '../ui/Icons';

export interface MessageLocationContext {
  companyName: string;
  projectName: string;
  sourceName: string;
  accountOrFile: string;
  fullPath?: string;
}

interface MessagePreviewProps {
  message?: Message;
  archiveMessage?: ArchiveMessagePreviewResponse | null;
  locationContext?: MessageLocationContext;
  isLoading?: boolean;
  error?: string | null;
}

export const MessagePreview: React.FC<MessagePreviewProps> = ({
  message,
  archiveMessage,
  locationContext,
  isLoading,
  error,
}) => {
  if (isLoading) {
    return (
      <div className="preview-pane" style={{ justifyContent: 'center', alignItems: 'center' }} data-testid="preview-loading-state">
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--brand-orange)', fontSize: '0.875rem' }}>
          <span
            style={{
              width: '16px',
              height: '16px',
              borderRadius: '50%',
              border: '2px solid var(--brand-orange)',
              borderTopColor: 'transparent',
              display: 'inline-block',
              animation: 'spin 1s linear infinite',
            }}
          />
          <span>İleti önizlemesi yükleniyor...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="preview-pane" style={{ justifyContent: 'center', alignItems: 'center' }} data-testid="preview-error-state">
        <p style={{ color: 'var(--status-error)', fontSize: '0.875rem' }}>{error}</p>
      </div>
    );
  }

  if (!archiveMessage && !message) {
    return (
      <div className="preview-pane" style={{ justifyContent: 'center', alignItems: 'center' }} data-testid="preview-empty-state">
        <p style={{ color: 'var(--text-muted)', fontSize: '0.875rem' }}>Önizlenecek ileti seçilmedi.</p>
      </div>
    );
  }

  return (
    <div className="preview-pane" data-testid="message-preview-pane">
      {locationContext && (
        <div
          className="preview-location-badge"
          data-testid="preview-location-context"
          title={locationContext.fullPath || `${locationContext.companyName} > ${locationContext.projectName} > ${locationContext.sourceName} (${locationContext.accountOrFile})`}
          style={{
            flexShrink: 0,
            minHeight: '24px',
            lineHeight: 1.4,
            fontSize: '0.75rem',
            color: '#475569',
            background: 'var(--bg-subtle)',
            padding: '3px 8px',
            borderRadius: '4px',
            marginBottom: '8px',
            display: 'inline-flex',
            alignItems: 'center',
            gap: '4px',
            border: '1px solid var(--border-light)',
            maxWidth: '100%',
            boxSizing: 'border-box',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
          }}
        >
          <span style={{ fontWeight: 600, color: '#1e293b' }} data-testid="preview-location-company">
            {locationContext.companyName}
          </span>
          <span style={{ color: 'var(--text-light)' }}>›</span>
          <span data-testid="preview-location-project">{locationContext.projectName}</span>
          <span style={{ color: 'var(--text-light)' }}>›</span>
          <span style={{ fontWeight: 600, color: 'var(--brand-orange)' }} data-testid="preview-location-source">
            {locationContext.sourceName}
          </span>
          <span style={{ color: '#64748b' }} data-testid="preview-location-account">
            ({locationContext.accountOrFile})
          </span>
        </div>
      )}

      {archiveMessage ? (
        <>
          {archiveMessage.isBodyTruncated && (
            <div
              data-testid="preview-body-truncated-alert"
              style={{
                background: '#fffbeb',
                border: '1px solid #fde68a',
                color: '#92400e',
                fontSize: '0.75rem',
                padding: '6px 10px',
                borderRadius: '4px',
                marginBottom: '8px',
                fontWeight: 600,
              }}
            >
              ⚠️ Gövde 512 KiB sınırını aştığı için kısaltılarak görüntülendi. Tam ileti ham dosyada korunmaktadır.
            </div>
          )}

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
            <h2 className="preview-title" data-testid="preview-subject">{archiveMessage.subject}</h2>
            <span style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }} data-testid="preview-date">
              {archiveMessage.dateUtc
                ? new Date(archiveMessage.dateUtc).toLocaleString('tr-TR', { timeZone: 'UTC' }) + ' (UTC)'
                : 'Tarih yok'}
            </span>
          </div>

          <div className="preview-meta" style={{ display: 'flex', flexDirection: 'column', gap: '3px', marginTop: '6px' }}>
            <span data-testid="preview-sender">
              <strong>Kimden:</strong> {archiveMessage.from}
            </span>
            <span data-testid="preview-to">
              <strong>Kime:</strong> {archiveMessage.to}
            </span>
            {archiveMessage.cc && (
              <span data-testid="preview-cc">
                <strong>Bilgi:</strong> {archiveMessage.cc}
              </span>
            )}
            {archiveMessage.bcc && (
              <span data-testid="preview-bcc">
                <strong>Gizli:</strong> {archiveMessage.bcc}
              </span>
            )}
            {archiveMessage.messageIdHeader && (
              <span data-testid="preview-message-id" style={{ fontSize: '0.6875rem', color: '#64748b' }}>
                <strong>Message-ID:</strong> {archiveMessage.messageIdHeader}
              </span>
            )}
          </div>

          <div
            className="preview-body"
            data-testid="preview-body"
            style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word', overflowWrap: 'anywhere' }}
          >
            {archiveMessage.bodyText}
          </div>

          {archiveMessage.attachments && archiveMessage.attachments.length > 0 && (
            <div style={{ marginTop: '12px', display: 'flex', flexDirection: 'column', gap: '6px' }} data-testid="preview-attachments-list">
              <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--text-muted)' }}>
                EKLER ({archiveMessage.attachments.length})
              </span>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                {archiveMessage.attachments.map((att, i) => (
                  <div key={i} className="attachment-chip" data-testid={`preview-attachment-${i}`}>
                    <IconFilePdf size={16} />
                    <span style={{ fontWeight: 500 }}>{att.fileName}</span>
                    <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>
                      ({att.sizeBytes == null ? 'boyut bilinmiyor' : `${(att.sizeBytes / 1024).toFixed(1)} KB`}{att.isInline ? ' · inline' : ''})
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          <div
            style={{
              marginTop: '12px',
              paddingTop: '8px',
              borderTop: '1px solid var(--border-light)',
              display: 'flex',
              justifyContent: 'space-between',
              fontSize: '0.6875rem',
              color: 'var(--text-muted)',
              flexWrap: 'wrap',
              gap: '8px',
            }}
          >
            <span data-testid="preview-sha256" style={{ fontFamily: 'monospace' }}>
              SHA-256: {archiveMessage.sha256}
            </span>
            <span data-testid="preview-raw-size">
              {(archiveMessage.rawSizeBytes / 1024).toFixed(1)} KB
            </span>
          </div>
        </>
      ) : message ? (
        <>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
            <h2 className="preview-title" data-testid="preview-subject">{message.subject}</h2>
            <span style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }} data-testid="preview-date">
              {message.dateTime}
            </span>
          </div>

          <div className="preview-meta">
            <span data-testid="preview-sender">
              {message.sender} &lt;{message.senderEmail}&gt;
            </span>
          </div>

          <div
            className="preview-body"
            data-testid="preview-body"
            style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word', overflowWrap: 'anywhere' }}
          >
            {message.body}
          </div>

          {message.hasAttachment && message.attachmentName && (
            <div className="attachment-chip" data-testid="preview-attachment-chip">
              <IconFilePdf size={18} />
              <span style={{ fontWeight: 500 }}>{message.attachmentName}</span>
              <span style={{ color: 'var(--text-muted)' }}>· {message.attachmentSizeFormatted}</span>
            </div>
          )}
        </>
      ) : null}
    </div>
  );
};
