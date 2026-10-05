import React, { useState } from 'react';
import { DataSource } from '../../types';
import {
  IconChevronDown,
  IconChevronRight,
  IconFile,
  IconFolder,
  IconMail,
  IconPlus,
  IconSend,
  IconServer,
} from '../ui/Icons';

export interface SourceTreeProps {
  sources: DataSource[];
  selectedSourceId: string;
  selectedFolderId: string;
  onSelectSourceFolder: (source: DataSource, folderId: string, folderName: string) => void;
  onOpenAddSource: () => void;
}

export const SourceTree: React.FC<SourceTreeProps> = ({
  sources,
  selectedSourceId,
  selectedFolderId,
  onSelectSourceFolder,
  onOpenAddSource,
}) => {
  const [expandedSources, setExpandedSources] = useState<Record<string, boolean>>({});

  const toggleExpand = (sourceId: string, e: React.MouseEvent) => {
    e.stopPropagation();
    setExpandedSources((prev) => ({
      ...prev,
      [sourceId]: prev[sourceId] !== undefined ? !prev[sourceId] : false,
    }));
  };

  const isExpanded = (sourceId: string) => {
    return expandedSources[sourceId] !== false; // Default expanded
  };

  return (
    <aside
      className="workspace-panel"
      style={{ width: '100%', display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}
      data-testid="sources-tree-panel"
    >
      {/* Header */}
      <div className="panel-header">
        <span className="panel-title">Kaynaklar ({sources.length})</span>
        <button
          className="btn-outline-gray"
          style={{ padding: '3px 8px', borderRadius: '4px', cursor: 'pointer' }}
          onClick={onOpenAddSource}
          aria-label="Örnek kaynak ekle"
          data-testid="add-source-btn"
        >
          <IconPlus size={16} />
        </button>
      </div>

      {/* Tree Content */}
      <div style={{ padding: '8px 0', overflowY: 'auto', flex: 1, minHeight: 0 }}>
        {sources.length === 0 ? (
          <div
            style={{ padding: '24px 16px', fontSize: '0.8125rem', color: 'var(--text-muted)', textAlign: 'center' }}
            data-testid="empty-sources-message"
          >
            Bu projede bağlı kaynak yok.
            <br />
            Yukarıdaki &apos;+&apos; butonuna basarak yeni kaynak ekleyin.
          </div>
        ) : (
          sources.map((source) => {
            const expanded = isExpanded(source.id);
            const isSelectedSource = source.id === selectedSourceId;

            return (
              <div key={source.id} style={{ marginBottom: '8px' }} data-testid={`source-group-${source.id}`}>
                {/* Source Node */}
                <div
                  className={`tree-node ${isSelectedSource ? 'selected-source' : ''}`}
                  style={{
                    fontWeight: 600,
                    cursor: 'pointer',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    padding: '6px 12px',
                    background: isSelectedSource ? 'var(--brand-yellow-subtle)' : undefined,
                    borderRadius: '4px',
                  }}
                  onClick={() => {
                    const firstFolder = source.folders[0] || { id: 'inbox', name: 'Gelen kutusu' };
                    onSelectSourceFolder(source, firstFolder.id, firstFolder.name);
                  }}
                  data-testid={`source-node-${source.id}`}
                >
                  <span
                    onClick={(e) => toggleExpand(source.id, e)}
                    style={{ cursor: 'pointer', display: 'inline-flex', alignItems: 'center' }}
                  >
                    {expanded ? (
                      <IconChevronDown size={14} color="#64748b" />
                    ) : (
                      <IconChevronRight size={14} color="#64748b" />
                    )}
                  </span>

                  {source.kind === 'mailbox' ? (
                    <IconServer size={16} color="var(--brand-orange)" />
                  ) : (
                    <IconFile size={16} color="var(--brand-orange)" />
                  )}

                  <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 }}>
                    <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {source.name}
                    </span>
                    <span
                      style={{
                        fontSize: '0.6875rem',
                        fontWeight: 400,
                        color: 'var(--text-muted)',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                      }}
                      data-testid={`source-account-${source.id}`}
                    >
                      {source.accountOrFileName}
                    </span>
                  </div>
                </div>

                {/* Folders List */}
                {expanded && (
                  <div style={{ paddingLeft: '20px', marginTop: '2px' }}>
                    {source.folders.map((folder) => {
                      const isSelectedFolder = isSelectedSource && selectedFolderId === folder.id;

                      return (
                        <div
                          key={folder.id}
                          className={`tree-node ${isSelectedFolder ? 'selected' : ''}`}
                          onClick={() => onSelectSourceFolder(source, folder.id, folder.name)}
                          data-testid={`source-folder-${source.id}-${folder.id}`}
                          style={{ cursor: 'pointer' }}
                        >
                          {folder.id === 'sent' ? (
                            <IconSend
                              size={15}
                              color={isSelectedFolder ? '#78350f' : '#64748b'}
                            />
                          ) : folder.id === 'inbox' ? (
                            <IconMail
                              size={15}
                              color={isSelectedFolder ? '#78350f' : '#64748b'}
                            />
                          ) : (
                            <IconFolder
                              size={15}
                              color={isSelectedFolder ? '#78350f' : '#64748b'}
                            />
                          )}
                          <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {folder.name}
                          </span>
                          {folder.count !== undefined && (
                            <span
                              style={{
                                fontSize: '0.6875rem',
                                color: isSelectedFolder ? '#78350f' : 'var(--text-muted)',
                                marginLeft: 'auto',
                                paddingLeft: '6px',
                              }}
                            >
                              {folder.count}
                            </span>
                          )}
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>
    </aside>
  );
};
