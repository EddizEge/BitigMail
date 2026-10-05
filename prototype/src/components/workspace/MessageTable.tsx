import React from 'react';
import { Message } from '../../types';

interface MessageTableProps {
  messages: Message[];
  selectedMessageId: string;
  onSelectMessage: (id: string) => void;
  manualSelectionMode: boolean;
  selectedMessageIds: string[];
  onToggleCheckbox: (id: string) => void;
}

export const MessageTable: React.FC<MessageTableProps> = ({
  messages,
  selectedMessageId,
  onSelectMessage,
  manualSelectionMode,
  selectedMessageIds,
  onToggleCheckbox,
}) => {
  if (messages.length === 0) {
    return (
      <div
        style={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          padding: '40px 20px',
          color: 'var(--text-muted)',
          textAlign: 'center',
        }}
        data-testid="empty-messages-placeholder"
      >
        <p style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--text-main)', marginBottom: '4px' }}>
          Eşleşen İleti Bulunamadı
        </p>
        <p style={{ fontSize: '0.8125rem' }}>
          Seçili filtre kriterlerine veya klasöre uygun ileti mevcut değil. Filtreleri temizleyebilirsiniz.
        </p>
      </div>
    );
  }

  const selectedSet = new Set(selectedMessageIds);

  return (
    <div className="table-container" data-testid="messages-table-container">
      <table className="data-table" data-testid="messages-table">
        <thead>
          <tr>
            {manualSelectionMode && (
              <th style={{ width: '40px', textAlign: 'center' }}>Seç</th>
            )}
            <th style={{ width: '22%' }}>Gönderen</th>
            <th style={{ width: '48%' }}>Konu</th>
            <th style={{ width: '18%' }}>Tarih ▾</th>
            <th style={{ width: '12%', textAlign: 'right' }}>Boyut</th>
          </tr>
        </thead>
        <tbody>
          {messages.map((msg) => {
            const isPreviewSelected = msg.id === selectedMessageId;
            const isChecked = selectedSet.has(msg.id);

            return (
              <tr
                key={msg.id}
                className={isPreviewSelected ? 'selected' : ''}
                onClick={() => onSelectMessage(msg.id)}
                data-testid={`message-row-${msg.id}`}
              >
                {manualSelectionMode && (
                  <td
                    style={{ textAlign: 'center' }}
                    onClick={(e) => {
                      e.stopPropagation();
                      onToggleCheckbox(msg.id);
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={isChecked}
                      onChange={() => onToggleCheckbox(msg.id)}
                      style={{ cursor: 'pointer' }}
                      data-testid={`message-checkbox-${msg.id}`}
                    />
                  </td>
                )}
                <td style={{ fontWeight: 500, color: 'var(--text-main)' }}>{msg.sender}</td>
                <td style={{ color: 'var(--text-main)' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                    <span>{msg.subject}</span>
                    {msg.hasAttachment && (
                      <span
                        title="Ekli ileti"
                        style={{ fontSize: '0.75rem', color: '#64748b' }}
                      >
                        📎
                      </span>
                    )}
                    {msg.isDuplicate && (
                      <span className="badge badge-warning" style={{ fontSize: '0.6875rem' }}>
                        Yinelenen
                      </span>
                    )}
                    {msg.isOversized && (
                      <span className="badge badge-warning" style={{ fontSize: '0.6875rem' }}>
                        38 MB
                      </span>
                    )}
                  </div>
                </td>
                <td style={{ color: 'var(--text-muted)' }}>{msg.date}</td>
                <td style={{ textAlign: 'right', color: 'var(--text-muted)' }}>
                  {msg.sizeFormatted}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
};
