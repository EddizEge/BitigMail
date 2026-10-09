import React, { useState, useCallback } from 'react';
import { useImapAccounts } from '../../hooks/useImapAccounts';
import { useMicrosoftOAuth } from '../../hooks/useMicrosoftOAuth';
import { useGoogleOAuth } from '../../hooks/useGoogleOAuth';
import { ImapAccountPublicDto, UpdateImapAccountRequest } from '../../types/localEngine';
import {
  IconServer,
  IconPlus,
  IconRefreshCw,
  IconAlertTriangle,
  IconFolder,
  IconCheckCircle,
  IconClock,
} from '../ui/Icons';
import { Modal } from '../ui/Modal';
import { Badge } from '../ui/Badge';
import { LocalEngineClient } from '../../api/localEngineClient';

interface ProjectImapAccountsProps {
  companyId: string;
  projectId: string;
  projectName: string;
  client?: LocalEngineClient;
}

export const ProjectImapAccounts: React.FC<ProjectImapAccountsProps> = ({
  companyId,
  projectId,
  projectName,
  client,
}) => {
  const {
    accounts,
    loading,
    error,
    conflictInfo,
    testResults,
    testingAccountId,
    foldersState,
    refreshAccounts,
    createAccount,
    updateAccount,
    deleteAccount,
    testConnection,
    testAccount,
    loadFolders,
    clearConflict,
  } = useImapAccounts({ companyId, projectId, client });

  // Modal states
  const [createModalOpen, setCreateModalOpen] = useState<boolean>(false);
  const [editModalAccount, setEditModalAccount] = useState<ImapAccountPublicDto | null>(null);
  const [deleteModalAccount, setDeleteModalAccount] = useState<ImapAccountPublicDto | null>(null);
  const [foldersModalAccount, setFoldersModalAccount] = useState<ImapAccountPublicDto | null>(null);
  const [reconnectModalAccount, setReconnectModalAccount] = useState<ImapAccountPublicDto | null>(null);
  const [googleReconnectAccount, setGoogleReconnectAccount] = useState<ImapAccountPublicDto | null>(null);
  const [googleReconnectSecret, setGoogleReconnectSecret] = useState('');

  // Form type tab in Create modal
  const [accountTypeTab, setAccountTypeTab] = useState<'imap' | 'microsoft365' | 'google'>('imap');

  // Form states for Create IMAP Account
  const [formDisplayName, setFormDisplayName] = useState('');
  const [formEmail, setFormEmail] = useState('');
  const [formHost, setFormHost] = useState('');
  const [formPort, setFormPort] = useState(993);
  const [formTlsMode, setFormTlsMode] = useState<'ssl' | 'starttls' | 'none'>('ssl');
  const [formAllowUnencrypted, setFormAllowUnencrypted] = useState(false);
  const [formUsername, setFormUsername] = useState('');
  const [formPassword, setFormPassword] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Form states for Create Microsoft 365 Account (OAuth2) - Zero password/host/port/TLS
  const [m365AuthMode, setM365AuthMode] = useState<'corporate' | 'personal'>('corporate');
  const [m365DisplayName, setM365DisplayName] = useState('');
  const [m365Email, setM365Email] = useState('');
  const [m365ClientId, setM365ClientId] = useState('');
  const [m365TenantId, setM365TenantId] = useState('');
  const [m365FormError, setM365FormError] = useState<string | null>(null);
  const [googleDisplayName, setGoogleDisplayName] = useState('');
  const [googleEmail, setGoogleEmail] = useState('');
  const [googleClientId, setGoogleClientId] = useState('');
  const [googleClientSecret, setGoogleClientSecret] = useState('');
  const [googleFormError, setGoogleFormError] = useState<string | null>(null);

  // Microsoft OAuth lifecycle hook
  const m365OAuth = useMicrosoftOAuth({
    companyId,
    projectId,
    client,
    onConnected: () => {
      refreshAccounts();
    },
  });
  const googleOAuth = useGoogleOAuth({ companyId, projectId, client, onConnected: () => refreshAccounts() });

  // Connection test state in Create modal
  const [createTestLoading, setCreateTestLoading] = useState(false);
  const [createTestResult, setCreateTestResult] = useState<{ success: boolean; message?: string; error?: string; latencyMs?: number } | null>(null);

  // Edit form states
  const [editDisplayName, setEditDisplayName] = useState('');
  const [editEmail, setEditEmail] = useState('');
  const [editHost, setEditHost] = useState('');
  const [editPort, setEditPort] = useState(993);
  const [editTlsMode, setEditTlsMode] = useState<string>('ssl');
  const [editTlsModeDirty, setEditTlsModeDirty] = useState<boolean>(false);
  const [editAllowUnencrypted, setEditAllowUnencrypted] = useState(false);
  const [editUsername, setEditUsername] = useState('');
  const [editPassword, setEditPassword] = useState('');
  const [editError, setEditError] = useState<string | null>(null);

  const resetCreateForm = useCallback(() => {
    setAccountTypeTab('imap');
    setFormDisplayName('');
    setFormEmail('');
    setFormHost('');
    setFormPort(993);
    setFormTlsMode('ssl');
    setFormAllowUnencrypted(false);
    setFormUsername('');
    // Ensure password is completely cleared from memory
    setFormPassword('');
    setFormError(null);
    setCreateTestResult(null);

    setM365AuthMode('corporate');
    setM365DisplayName('');
    setM365Email('');
    setM365ClientId('');
    setM365TenantId('');
    setM365FormError(null);
    m365OAuth.resetSession(true);
    setGoogleDisplayName(''); setGoogleEmail(''); setGoogleClientId(''); setGoogleClientSecret(''); setGoogleFormError(null);
    googleOAuth.resetSession(true);
  }, [m365OAuth, googleOAuth]);

  const openCreateModal = useCallback(() => {
    resetCreateForm();
    setCreateModalOpen(true);
  }, [resetCreateForm]);

  const closeCreateModal = useCallback(() => {
    resetCreateForm();
    setCreateModalOpen(false);
  }, [resetCreateForm]);

  const handleTlsModeChange = (mode: 'ssl' | 'starttls' | 'none') => {
    setFormTlsMode(mode);
    setFormAllowUnencrypted(false);
    if (mode === 'ssl' && formPort === 143) {
      setFormPort(993);
    } else if (mode === 'starttls' && formPort === 993) {
      setFormPort(143);
    } else if (mode === 'none' && (formPort === 993 || formPort === 143)) {
      setFormPort(143);
    }
  };

  const handleEditTlsModeChange = (mode: 'ssl' | 'starttls' | 'none') => {
    setEditTlsMode(mode);
    setEditTlsModeDirty(true);
    setEditAllowUnencrypted(false);
    if (mode === 'ssl' && editPort === 143) {
      setEditPort(993);
    } else if (mode === 'starttls' && editPort === 993) {
      setEditPort(143);
    }
  };

  const handleTestInCreateModal = async () => {
    if (!formHost.trim() || !formUsername.trim() || !formPassword.trim()) {
      setFormError('Bağlantı testi için Sunucu, Kullanıcı Adı ve Parola alanları zorunludur.');
      return;
    }
    setCreateTestLoading(true);
    setCreateTestResult(null);
    setFormError(null);
    try {
      const res = await testConnection({
        host: formHost.trim(),
        port: formPort,
        tlsMode: formTlsMode,
        username: formUsername.trim(),
        password: formPassword,
        allowUnencryptedConnection: formTlsMode === 'none' && formAllowUnencrypted,
      });
      setCreateTestResult(res);
      if (!res.success && res.error) {
        setFormError(res.error);
      }
    } catch (err: any) {
      setCreateTestResult({ success: false, error: err?.message || 'Bağlantı kurulamadı.' });
      setFormError(err?.message || 'Bağlantı kurulamadı.');
    } finally {
      // Clear password immediately after draft connection-test completes (success or safe failure as appropriate); never retain it.
      setFormPassword('');
      setCreateTestLoading(false);
    }
  };

  const handleCreateSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formDisplayName.trim() || !formEmail.trim() || !formHost.trim() || !formUsername.trim() || !formPassword) {
      setFormError('Lütfen tüm zorunlu alanları doldurun.');
      return;
    }

    setIsSubmitting(true);
    setFormError(null);
    try {
      await createAccount({
        displayName: formDisplayName.trim(),
        email: formEmail.trim(),
        host: formHost.trim(),
        port: formPort,
        tlsMode: formTlsMode,
        username: formUsername.trim(),
        password: formPassword,
        allowUnencryptedConnection: formTlsMode === 'none' && formAllowUnencrypted,
      });
      // Immediately clear password from component state
      setFormPassword('');
      closeCreateModal();
    } catch (err: any) {
      setFormError(err?.message || 'Hesap oluşturulurken hata oluştu.');
    } finally {
      setIsSubmitting(false);
      // Double guarantee password is blanked
      setFormPassword('');
    }
  };

  const handleSwitchM365Mode = (mode: 'corporate' | 'personal') => {
    if (m365AuthMode === mode) return;
    m365OAuth.cancelOperation();
    m365OAuth.resetSession(true);
    setM365FormError(null);
    setM365AuthMode(mode);
  };

  const handleM365Submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setM365FormError(null);

    const name = m365DisplayName.trim();
    const email = m365Email.trim();
    const client = m365ClientId.trim();
    const isPersonal = m365AuthMode === 'personal';
    const tenant = isPersonal ? 'consumers' : m365TenantId.trim();

    if (isPersonal) {
      if (!name || !email || !client) {
        setM365FormError('Lütfen tüm zorunlu alanları doldurun (Hesap Adı, E-posta, Client ID).');
        return;
      }
    } else {
      if (!name || !email || !client || !tenant) {
        setM365FormError('Lütfen tüm zorunlu alanları doldurun (Hesap Adı, E-posta, Client ID, Tenant ID).');
        return;
      }
    }

    const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
    if (!guidRegex.test(client)) {
      setM365FormError('Application (Client) ID geçerli bir GUID formatında olmalıdır (örn: 11111111-2222-3333-4444-555555555555).');
      return;
    }
    if (!isPersonal && !guidRegex.test(tenant)) {
      setM365FormError('Directory (Tenant) ID geçerli bir GUID formatında olmalıdır (örn: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee).');
      return;
    }

    try {
      await m365OAuth.startOperation({
        displayName: name,
        email,
        clientId: client,
        tenantId: tenant,
      });
    } catch (err: any) {
      setM365FormError(err?.message || (isPersonal ? 'Kişisel Outlook oturumu başlatılamadı.' : 'Microsoft 365 oturumu başlatılamadı.'));
    }
  };

  const handleM365Reconnect = async (account: ImapAccountPublicDto) => {
    setM365FormError(null);
    try {
      await m365OAuth.startOperation({
        displayName: account.displayName,
        email: account.email,
        clientId: account.clientId || '',
        tenantId: account.tenantId || '',
        accountId: account.accountId,
        expectedVersion: account.version,
      });
    } catch (err: any) {
      setM365FormError(err?.message || 'Yeniden bağlama başlatılamadı.');
    }
  };

  const handleGoogleSubmit = async (e: React.FormEvent) => {
    e.preventDefault(); setGoogleFormError(null);
    if (!googleDisplayName.trim() || !googleEmail.trim() || !googleClientId.trim() || !googleClientSecret) {
      setGoogleFormError('Hesap adı, e-posta, Google masaüstü Client ID ve Client Secret zorunludur.'); return;
    }
    if (!googleClientId.trim().endsWith('.apps.googleusercontent.com')) {
      setGoogleFormError('Geçerli bir Google masaüstü OAuth Client ID girin.'); return;
    }
    try {
      await googleOAuth.startOperation({ displayName: googleDisplayName.trim(), email: googleEmail.trim(),
        clientId: googleClientId.trim(), clientSecret: googleClientSecret });
      setGoogleClientSecret('');
    } catch (err: any) { setGoogleClientSecret(''); setGoogleFormError(err?.message || 'Google oturumu başlatılamadı.'); }
  };
  const handleGoogleReconnect = async () => {
    if (!googleReconnectAccount || !googleReconnectSecret) return;
    try {
      await googleOAuth.startOperation({ displayName: googleReconnectAccount.displayName, email: googleReconnectAccount.email,
        clientId: googleReconnectAccount.clientId || '', clientSecret: googleReconnectSecret,
        accountId: googleReconnectAccount.accountId, expectedVersion: googleReconnectAccount.version });
      setGoogleReconnectSecret('');
    } catch (err: any) { setGoogleReconnectSecret(''); setGoogleFormError(err?.message || 'Google yeniden bağlama başlatılamadı.'); }
  };

  const openEditModal = (account: ImapAccountPublicDto) => {
    setEditModalAccount(account);
    setEditDisplayName(account.displayName);
    setEditEmail(account.email);
    setEditHost(account.host);
    setEditPort(account.port);
    setEditTlsMode(account.tlsMode);
    setEditAllowUnencrypted(Boolean(account.allowUnencryptedConnection));
    setEditTlsModeDirty(false);
    setEditUsername(account.username);
    // Password input starts completely empty
    setEditPassword('');
    setEditError(null);
  };

  const closeEditModal = () => {
    setEditModalAccount(null);
    setEditPassword('');
    setEditTlsModeDirty(false);
    setEditError(null);
  };

  const handleEditSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editModalAccount) return;

    setIsSubmitting(true);
    setEditError(null);
    try {
      const isOAuth = editModalAccount.authKind === 'microsoft365' || editModalAccount.authKind === 'google';
      const payload: Omit<UpdateImapAccountRequest, 'companyId' | 'projectId'> = isOAuth
        ? {
            displayName: editDisplayName.trim(),
            expectedVersion: editModalAccount.version,
          }
        : {
            displayName: editDisplayName.trim(),
            email: editEmail.trim(),
            host: editHost.trim(),
            port: editPort,
            username: editUsername.trim(),
            password: editPassword.trim() ? editPassword : undefined,
            expectedVersion: editModalAccount.version,
            allowUnencryptedConnection: editTlsMode === 'none' ? editAllowUnencrypted : false,
          };

      // Editing a lab account whose stored tlsMode is none must not silently send/change it to ssl
      // when metadata-only edit omits unchanged TLS fields.
      if (!isOAuth && editTlsModeDirty) {
        payload.tlsMode = editTlsMode as any;
      }

      await updateAccount(editModalAccount.accountId, payload);
      // Clear password immediately after submit
      setEditPassword('');
      closeEditModal();
    } catch (err: any) {
      setEditError(err?.message || 'Hesap güncellenirken hata oluştu.');
    } finally {
      setIsSubmitting(false);
      setEditPassword('');
    }
  };

  const handleDeleteConfirm = async () => {
    if (!deleteModalAccount) return;
    setIsSubmitting(true);
    try {
      await deleteAccount(deleteModalAccount.accountId, deleteModalAccount.version);
      setDeleteModalAccount(null);
    } catch (_err: any) {
      // Error handled by hook
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleOpenFolders = (account: ImapAccountPublicDto) => {
    setFoldersModalAccount(account);
    loadFolders(account.accountId).catch(() => {});
  };

  const renderM365OAuthStateUI = (onRetry?: () => void) => {
    switch (m365OAuth.status) {
      case 'preparing':
        return (
          <div style={{ textAlign: 'center', padding: '24px', background: 'var(--bg-subtle)', borderRadius: '6px' }} data-testid="m365-status-preparing">
            <IconRefreshCw size={24} className="animate-spin" color="var(--brand-orange)" style={{ margin: '0 auto 12px' }} />
            <div style={{ fontWeight: 600, fontSize: '0.875rem' }}>Microsoft oturum açma akışı hazırlanıyor...</div>
            <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '4px' }}>PKCE ve yetkilendirme bağlantısı oluşturuluyor.</div>
          </div>
        );

      case 'awaiting-signin':
        return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }} data-testid="m365-status-awaiting-signin">
            <div style={{ background: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '6px', padding: '12px 14px', fontSize: '0.8125rem', color: '#1e40af' }}>
              <div style={{ fontWeight: 700, marginBottom: '4px' }}>⚠️ Tarayıcı bu bilgisayarda açılmalıdır</div>
              <p style={{ margin: 0 }} data-testid="m365-desktop-browser-guidance">
                <strong>Giriş bağlantısı bu bilgisayardaki tarayıcıda açılmalıdır.</strong> Telefon veya farklı bir cihazdaki tarayıcıda giriş tamamlanamaz. BitigMail tarayıcıyı kendisi açmaz; bağlantıyı bu bilgisayarda açın.
              </p>
            </div>

            {m365OAuth.error && (
              <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '10px 12px', color: '#b91c1c', fontSize: '0.8125rem' }} data-testid="m365-cancel-error">
                <strong>Hata:</strong> {m365OAuth.error}
              </div>
            )}

            {m365OAuth.urlValidationError ? (
              <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '12px', color: '#b91c1c', fontSize: '0.8125rem' }} data-testid="m365-url-validation-error">
                <strong>Güvenlik Uyarısı:</strong> {m365OAuth.urlValidationError}
              </div>
            ) : m365OAuth.validatedUrl ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', alignItems: 'center', padding: '16px', background: '#f8fafc', borderRadius: '6px', border: '1px dashed #cbd5e1' }}>
                <span style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                  Aşağıdaki güvenli bağlantıya tıklayarak Microsoft oturumunuzu açın:
                </span>
                <a
                  href={m365OAuth.validatedUrl}
                  target="_blank"
                  rel="noreferrer noopener"
                  className="btn btn-orange"
                  style={{ padding: '10px 20px', fontSize: '0.875rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '8px', textDecoration: 'none' }}
                  data-testid="m365-authorization-link"
                >
                  <IconServer size={16} />
                  <span>Microsoft 365 ile Giriş Yap</span>
                </a>
                <span style={{ fontSize: '0.6875rem', color: 'var(--text-muted)' }}>
                  login.microsoftonline.com üzerinden doğrulanmış bağlantı (yeni sekmede açılır)
                </span>
              </div>
            ) : null}

            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingTop: '6px' }}>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', display: 'flex', alignItems: 'center', gap: '6px' }}>
                <IconRefreshCw size={12} className="animate-spin" />
                <span>Oturum açmanız bekleniyor...</span>
              </div>
              <button
                type="button"
                className="btn btn-outline-gray"
                style={{ padding: '4px 10px', fontSize: '0.75rem', color: '#dc2626' }}
                onClick={() => m365OAuth.cancelOperation()}
                data-testid="cancel-m365-oauth-btn"
              >
                İşlemi İptal Et
              </button>
            </div>
          </div>
        );

      case 'verifying':
        return (
          <div style={{ textAlign: 'center', padding: '24px', background: 'var(--bg-subtle)', borderRadius: '6px' }} data-testid="m365-status-verifying">
            <IconRefreshCw size={24} className="animate-spin" color="var(--brand-orange)" style={{ margin: '0 auto 12px' }} />
            <div style={{ fontWeight: 600, fontSize: '0.875rem' }}>Giriş ve IMAP bağlantısı doğrulanıyor...</div>
            <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '4px' }}>outlook.office365.com:993 üzerinde OAuth2 doğrulanıyor.</div>
          </div>
        );

      case 'connected':
        return (
          <div style={{ textAlign: 'center', padding: '24px', background: '#f0fdf4', border: '1px solid #bbf7d0', borderRadius: '6px' }} data-testid="m365-status-connected">
            <IconCheckCircle size={28} color="#16a34a" style={{ margin: '0 auto 10px' }} />
            <div style={{ fontWeight: 700, fontSize: '1rem', color: '#15803d' }}>Microsoft 365 Hesabı Başarıyla Bağlandı!</div>
            <div style={{ fontSize: '0.8125rem', color: '#166534', marginTop: '6px' }}>
              Hesap IMAP transferleri ve arşivleme işlemleri için hazır.
            </div>
          </div>
        );

      case 'cancelled':
        return (
          <div style={{ textAlign: 'center', padding: '20px', background: '#fffbeb', border: '1px solid #fde68a', borderRadius: '6px' }} data-testid="m365-status-cancelled">
            <IconAlertTriangle size={24} color="#d97706" style={{ margin: '0 auto 8px' }} />
            <div style={{ fontWeight: 600, fontSize: '0.875rem', color: '#92400e' }}>İşlem İptal Edildi</div>
            <div style={{ fontSize: '0.75rem', color: '#b45309', margin: '4px 0 12px 0' }}>{m365OAuth.message || 'Kullanıcı isteği ile iptal edildi.'}</div>
            <button
              type="button"
              className="btn btn-outline-orange"
              style={{ padding: '4px 12px', fontSize: '0.8125rem' }}
              onClick={() => {
                m365OAuth.resetSession();
                if (onRetry) onRetry();
              }}
              data-testid="retry-m365-cancelled-btn"
            >
              Yeniden Dene
            </button>
          </div>
        );

      case 'expired':
        return (
          <div style={{ textAlign: 'center', padding: '20px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px' }} data-testid="m365-status-expired">
            <IconClock size={24} color="#dc2626" style={{ margin: '0 auto 8px' }} />
            <div style={{ fontWeight: 600, fontSize: '0.875rem', color: '#b91c1c' }}>Oturum Açma Süresi Doldu</div>
            <div style={{ fontSize: '0.75rem', color: '#991b1b', margin: '4px 0 12px 0' }}>{m365OAuth.message || 'İşlem 5 dakikalık zaman aşımına uğradı.'}</div>
            <button
              type="button"
              className="btn btn-outline-orange"
              style={{ padding: '4px 12px', fontSize: '0.8125rem' }}
              onClick={() => {
                m365OAuth.resetSession();
                if (onRetry) onRetry();
              }}
              data-testid="retry-m365-expired-btn"
            >
              Yeniden Dene
            </button>
          </div>
        );

      case 'failed':
        return (
          <div style={{ textAlign: 'center', padding: '20px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px' }} data-testid="m365-status-failed">
            <IconAlertTriangle size={24} color="#dc2626" style={{ margin: '0 auto 8px' }} />
            <div style={{ fontWeight: 600, fontSize: '0.875rem', color: '#b91c1c' }}>Bağlantı Başarısız Oldu</div>
            <div style={{ fontSize: '0.75rem', color: '#991b1b', margin: '4px 0 12px 0' }}>{m365OAuth.error || m365OAuth.message || 'Oturum açılamadı.'}</div>
            <button
              type="button"
              className="btn btn-outline-orange"
              style={{ padding: '4px 12px', fontSize: '0.8125rem' }}
              onClick={() => {
                m365OAuth.resetSession();
                if (onRetry) onRetry();
              }}
              data-testid="retry-m365-failed-btn"
            >
              Yeniden Dene
            </button>
          </div>
        );

      default:
        return null;
    }
  };


  return (
    <div style={{ marginTop: '20px', borderTop: '1px solid var(--border-light)', paddingTop: '16px' }} data-testid={`imap-accounts-section-${projectId}`}>
      {/* Section Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <IconServer size={18} color="var(--brand-orange)" />
          <h3 style={{ fontSize: '1rem', fontWeight: 600, margin: 0, color: 'var(--text-main)' }}>
            IMAP Posta Kutuları
          </h3>
          <Badge variant="neutral">{accounts.length} Hesap</Badge>
        </div>

        <div style={{ display: 'flex', gap: '8px' }}>
          <button
            className="btn btn-outline-gray"
            style={{ padding: '5px 10px', fontSize: '0.75rem' }}
            onClick={refreshAccounts}
            title="Hesapları yenile"
            data-testid={`refresh-accounts-btn-${projectId}`}
          >
            <IconRefreshCw size={12} className={loading ? 'animate-spin' : ''} />
            <span>Yenile</span>
          </button>
          <button
            className="btn btn-orange"
            style={{ padding: '5px 12px', fontSize: '0.8125rem' }}
            onClick={openCreateModal}
            data-testid={`add-account-btn-${projectId}`}
          >
            <IconPlus size={14} />
            <span>Hesap bağla</span>
          </button>
        </div>
      </div>

      {/* 409 Conflict Banner */}
      {conflictInfo && (
        <div
          style={{
            background: '#fff7ed',
            border: '1px solid #fdba74',
            borderRadius: '6px',
            padding: '12px 16px',
            marginBottom: '14px',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
          }}
          data-testid="conflict-alert"
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <IconAlertTriangle size={20} color="#ea580c" />
            <div>
              <div style={{ fontSize: '0.875rem', fontWeight: 700, color: '#9a3412' }}>
                409 Sürüm Uyuşmazlığı (Eşzamanlı Değişiklik)
              </div>
              <div style={{ fontSize: '0.8125rem', color: '#c2410c' }}>
                {conflictInfo.message}
                {conflictInfo.currentVersion && conflictInfo.expectedVersion && (
                  <span> (Sunucu sürümü: v{conflictInfo.currentVersion}, Gönderilen: v{conflictInfo.expectedVersion})</span>
                )}
              </div>
            </div>
          </div>
          <button
            className="btn btn-outline-orange"
            style={{ padding: '4px 10px', fontSize: '0.8125rem' }}
            onClick={() => {
              clearConflict();
              refreshAccounts();
            }}
            data-testid="conflict-refresh-btn"
          >
            Yenile ve Tekrar Dene
          </button>
        </div>
      )}

      {/* General Error Banner */}
      {error && !conflictInfo && (
        <div
          style={{
            background: '#fef2f2',
            border: '1px solid #fecaca',
            borderRadius: '6px',
            padding: '10px 14px',
            marginBottom: '14px',
            fontSize: '0.8125rem',
            color: '#b91c1c',
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
          }}
          data-testid="account-error-banner"
        >
          <IconAlertTriangle size={16} color="#dc2626" />
          <span>{error}</span>
        </div>
      )}

      {/* Accounts List / Table */}
      {loading && accounts.length === 0 ? (
        <div style={{ padding: '24px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '0.875rem' }}>
          IMAP hesapları yükleniyor...
        </div>
      ) : accounts.length === 0 ? (
        <div
          style={{
            padding: '24px',
            background: 'var(--bg-subtle)',
            borderRadius: '6px',
            textAlign: 'center',
            fontSize: '0.875rem',
            color: 'var(--text-muted)',
          }}
          data-testid={`no-accounts-message-${projectId}`}
        >
          Bu projeye henüz IMAP hesabı bağlanmadı. Taşıma veya arşivleme işlemlerinde kullanmak için yeni bir IMAP hesabı ekleyin.
        </div>
      ) : (
        <div className="table-container" style={{ borderRadius: '6px', border: '1px solid var(--border-light)' }}>
          <table className="data-table" data-testid={`imap-accounts-table-${projectId}`}>
            <thead>
              <tr>
                <th style={{ width: '28%' }}>Hesap & E-posta</th>
                <th style={{ width: '22%' }}>Sunucu & Güvenlik</th>
                <th style={{ width: '12%' }}>Sürüm</th>
                <th style={{ width: '18%' }}>Bağlantı Sınaması</th>
                <th style={{ width: '20%', textAlign: 'right' }}>İşlemler</th>
              </tr>
            </thead>
            <tbody>
              {accounts.map((account) => {
                const isTesting = testingAccountId === account.accountId;
                const testResult = testResults[account.accountId];
                const isM365 = account.authKind === 'microsoft365';
                const isGoogle = account.authKind === 'google';
                const isOAuth = isM365 || isGoogle;
                const folderState = foldersState[account.accountId];
                const isReauthRequired = Boolean(
                  testResult?.code === 'reauthorization_required' ||
                  (testResult?.error && testResult.error.toLowerCase().includes('reauthorization_required')) ||
                  folderState?.code === 'reauthorization_required' ||
                  folderState?.apiError?.data?.code === 'reauthorization_required' ||
                  (folderState?.error && folderState.error.toLowerCase().includes('reauthorization_required')) ||
                  account.oauthStatus === 'reauthorization_required'
                );

                const isPersonal = isM365 && account.tenantId === 'consumers';

                return (
                  <tr key={account.accountId} data-testid={`account-row-${account.accountId}`}>
                    {/* Name & Email */}
                    <td>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <IconServer size={16} color={isGoogle ? '#16a34a' : isM365 ? (isPersonal ? '#d97706' : '#0284c7') : 'var(--brand-orange)'} />
                        <div>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                            <span style={{ fontWeight: 600, color: 'var(--text-main)' }}>{account.displayName}</span>
                            {isOAuth && (
                              <span
                                style={{
                                  fontSize: '0.6875rem',
                                  fontWeight: 700,
                                  padding: '1px 6px',
                                  borderRadius: '4px',
                                  background: isGoogle ? '#dcfce7' : isPersonal ? '#fef3c7' : '#e0f2fe',
                                  color: isGoogle ? '#166534' : isPersonal ? '#92400e' : '#0369a1',
                                }}
                                data-testid={`${isGoogle ? 'google' : 'm365'}-badge-${account.accountId}`}
                              >
                                {isGoogle ? 'Google' : isPersonal ? 'Kişisel Outlook' : 'Microsoft 365'}
                              </span>
                            )}
                          </div>
                          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                            {account.email}
                            {isM365 && account.tenantId && (
                              <span style={{ marginLeft: '6px', fontSize: '0.6875rem', color: '#64748b' }}>
                                {isPersonal ? '(Kişisel / consumers)' : `(Tenant: ${account.tenantId.substring(0, 8)}...)`}
                              </span>
                            )}
                          </div>
                        </div>
                      </div>
                    </td>

                    {/* Server & TLS */}
                    <td>
                      <div style={{ fontSize: '0.8125rem', color: 'var(--text-main)', fontFamily: 'monospace' }}>
                        {account.host}:{account.port}
                      </div>
                      <div style={{ marginTop: '2px', display: 'flex', gap: '4px', alignItems: 'center', flexWrap: 'wrap' }}>
                        <span
                          style={{
                            fontSize: '0.75rem',
                            fontWeight: 600,
                            padding: '2px 6px',
                            borderRadius: '4px',
                            background: isM365 ? (isPersonal ? '#fef3c7' : '#f0f9ff') : (account.tlsMode === 'ssl' ? '#eff6ff' : '#f0fdf4'),
                            color: isM365 ? (isPersonal ? '#92400e' : '#0284c7') : (account.tlsMode === 'ssl' ? '#1d4ed8' : '#15803d'),
                          }}
                        >
                          {isGoogle ? 'Gmail / XOAUTH2' : isM365 ? (isPersonal ? 'Outlook.com / XOAUTH2' : 'OAuth2 / SSL') : (account.tlsMode === 'ssl' ? 'SSL/TLS' : 'STARTTLS')}
                        </span>
                        {isReauthRequired && (
                          <span
                            style={{
                              fontSize: '0.75rem',
                              fontWeight: 700,
                              padding: '2px 6px',
                              borderRadius: '4px',
                              background: '#fef3c7',
                              color: '#b45309',
                              border: '1px solid #fde68a',
                            }}
                            data-testid={`reauth-badge-${account.accountId}`}
                          >
                            Yeniden Yetkilendirme Gerekli
                          </span>
                        )}
                      </div>
                    </td>

                    {/* Version */}
                    <td>
                      <span
                        style={{
                          fontSize: '0.75rem',
                          fontWeight: 700,
                          padding: '2px 6px',
                          borderRadius: '4px',
                          background: 'var(--bg-subtle)',
                          color: 'var(--text-muted)',
                        }}
                      >
                        v{account.version}
                      </span>
                    </td>

                    {/* Connection Test */}
                    <td>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                        <button
                          className="btn btn-outline-gray"
                          style={{ padding: '3px 8px', fontSize: '0.75rem', alignSelf: 'flex-start' }}
                          onClick={() => testAccount(account.accountId)}
                          disabled={isTesting}
                          data-testid={`test-account-${account.accountId}`}
                        >
                          <IconRefreshCw size={12} className={isTesting ? 'animate-spin' : ''} />
                          <span>{isTesting ? 'Sınanıyor...' : 'Bağlantıyı Sına'}</span>
                        </button>

                        {testResult && (
                          <div style={{ fontSize: '0.75rem', display: 'flex', alignItems: 'center', gap: '4px' }}>
                            {testResult.success ? (
                              <span style={{ color: '#16a34a', fontWeight: 600 }}>
                                ✓ Başarılı {testResult.latencyMs !== undefined ? `(${testResult.latencyMs} ms)` : ''}
                              </span>
                            ) : (
                              <span style={{ color: '#dc2626' }} title={testResult.error}>
                                ✕ {testResult.error || 'Bağlantı hatası'}
                              </span>
                            )}
                          </div>
                        )}
                      </div>
                    </td>

                    {/* Actions */}
                    <td style={{ textAlign: 'right' }}>
                      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '6px', alignItems: 'center', flexWrap: 'wrap' }}>
                        {isM365 && (
                          <button
                            className="btn btn-outline-gray"
                            style={{ padding: '4px 8px', fontSize: '0.75rem', color: isReauthRequired ? '#b45309' : undefined }}
                            onClick={() => {
                              setReconnectModalAccount(account);
                              m365OAuth.resetSession();
                            }}
                            data-testid={`reconnect-account-${account.accountId}`}
                            title="Microsoft 365 yetkisini yenile"
                          >
                            Yeniden Bağlan
                          </button>
                        )}
                        {isGoogle && (
                          <button className="btn btn-outline-gray" style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                            onClick={() => { setGoogleReconnectAccount(account); setGoogleReconnectSecret(''); googleOAuth.resetSession(); }}
                            data-testid={`reconnect-account-${account.accountId}`} title="Google yetkisini yenile">Yeniden Bağlan</button>
                        )}
                        <button
                          className="btn btn-outline-gray"
                          style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                          onClick={() => handleOpenFolders(account)}
                          data-testid={`view-folders-${account.accountId}`}
                          title="Gerçek klasörleri ve ileti sayılarını incele"
                        >
                          <IconFolder size={12} />
                          <span>Klasörler</span>
                        </button>
                        <button
                          className="btn btn-outline-gray"
                          style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                          onClick={() => openEditModal(account)}
                          data-testid={`edit-account-${account.accountId}`}
                        >
                          Düzenle
                        </button>
                        <button
                          className="btn btn-outline-gray"
                          style={{ padding: '4px 8px', fontSize: '0.75rem', color: '#dc2626' }}
                          onClick={() => setDeleteModalAccount(account)}
                          data-testid={`delete-account-${account.accountId}`}
                          title={isOAuth ? 'Yerel OAuth bağlantısını kaldır (sağlayıcı izinleri iptal edilmez)' : 'Hesabı sil'}
                        >
                          {isOAuth ? 'Yerel bağlantıyı kaldır' : 'Sil'}
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* CREATE ACCOUNT MODAL */}
      <Modal
        isOpen={createModalOpen}
        onClose={closeCreateModal}
        title={accountTypeTab === 'microsoft365' ? 'Microsoft 365 Hesabı Bağla' : accountTypeTab === 'google' ? 'Gmail / Workspace Hesabı Bağla' : 'Yeni IMAP Hesabı Bağla'}
        size="md"
      >
        <div style={{ display: 'flex', gap: '8px', borderBottom: '1px solid var(--border-light)', paddingBottom: '12px', marginBottom: '14px' }}>
          <button
            type="button"
            className={`btn ${accountTypeTab === 'imap' ? 'btn-orange' : 'btn-outline-gray'}`}
            style={{ padding: '6px 14px', fontSize: '0.8125rem' }}
            onClick={() => {
              setAccountTypeTab('imap');
              m365OAuth.resetSession();
            }}
            data-testid="tab-standard-imap"
          >
            IMAP
          </button>
          <button
            type="button"
            className={`btn ${accountTypeTab === 'microsoft365' ? 'btn-orange' : 'btn-outline-gray'}`}
            style={{ padding: '6px 14px', fontSize: '0.8125rem' }}
            onClick={() => setAccountTypeTab('microsoft365')}
            data-testid="tab-microsoft-365"
          >
            Microsoft 365
          </button>
          <button type="button" className={`btn ${accountTypeTab === 'google' ? 'btn-orange' : 'btn-outline-gray'}`}
            style={{ padding: '6px 14px', fontSize: '0.8125rem' }} onClick={() => { setAccountTypeTab('google'); m365OAuth.resetSession(true); }} data-testid="tab-google-oauth">
            Gmail / Workspace
          </button>
        </div>

        {accountTypeTab === 'google' ? (
          <div data-testid="google-oauth-panel">
            {googleOAuth.status === 'idle' ? (
              <form onSubmit={handleGoogleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: 6, padding: 12, fontSize: '0.8125rem' }}>
                  Gmail veya Google Workspace hesabınızı bağlayın. Giriş Google'ın tarayıcı sayfasında tamamlanır; mailleriniz kaynak hesapta korunur.
                </div>
                <details style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: 6, padding: 12, fontSize: '0.8125rem' }} data-testid="google-setup-guide">
                  <summary style={{ cursor: 'pointer', fontWeight: 600 }}>Bağlantı kurulumu</summary>
                  <p>Google Cloud Console'da Desktop app OAuth istemcisi oluşturun. Sabit bağlantı <code>imap.gmail.com:993</code> ve OAuth2 kullanır; servis hesabı veya alan genelinde yetki desteklenmez. Client Secret form gönderilene kadar yalnız geçici arayüz belleğindedir; sonrasında temizlenir ve yalnız hesap kimliğine bağlı korumalı yerel backend zarfında kalıcılaştırılır.</p>
                </details>
                {googleFormError && <div data-testid="google-form-error" style={{ color: '#b91c1c' }}>{googleFormError}</div>}
                <input className="text-input" value={googleDisplayName} onChange={e => setGoogleDisplayName(e.target.value)} placeholder="Hesap adı" data-testid="google-displayname-input" />
                <input className="text-input" type="email" value={googleEmail} onChange={e => setGoogleEmail(e.target.value)} placeholder="kullanici@gmail.com" data-testid="google-email-input" />
                <input className="text-input" value={googleClientId} onChange={e => setGoogleClientId(e.target.value)} placeholder="...apps.googleusercontent.com" data-testid="google-clientid-input" />
                <input className="text-input" type="password" autoComplete="off" value={googleClientSecret} onChange={e => setGoogleClientSecret(e.target.value)} placeholder="Google masaüstü Client Secret" data-testid="google-clientsecret-input" />
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10 }}><button type="button" className="btn btn-outline-gray" onClick={closeCreateModal}>İptal</button><button type="submit" className="btn btn-orange" data-testid="submit-google-connect-btn">Google ile Bağlan</button></div>
              </form>
            ) : (
              <div data-testid={`google-status-${googleOAuth.status}`} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {googleOAuth.validatedUrl && <a className="btn btn-orange" href={googleOAuth.validatedUrl} target="_blank" rel="noreferrer noopener" data-testid="google-authorization-link">Google ile güvenli giriş yap</a>}
                <div>{googleOAuth.error || googleOAuth.message || (googleOAuth.status === 'connected' ? 'Google hesabı bağlandı.' : 'Google oturumu hazırlanıyor...')}</div>
                {googleOAuth.status === 'awaiting-signin' && <button type="button" className="btn btn-outline-gray" onClick={() => googleOAuth.cancelOperation()} data-testid="cancel-google-oauth-btn">İşlemi iptal et</button>}
                <button type="button" className="btn btn-outline-gray" onClick={() => googleOAuth.resetSession(true)}>Forma dön</button>
              </div>
            )}
          </div>
        ) : accountTypeTab === 'microsoft365' ? (
          <div>
            {/* Mode selection: Kurumsal vs Kişisel */}
            <div
              className="m365-mode-selector-row"
              style={{
                display: 'flex',
                flexWrap: 'wrap',
                gap: '8px',
                padding: '4px',
                background: 'var(--bg-subtle)',
                borderRadius: '6px',
                marginBottom: '14px',
              }}
              data-testid="m365-mode-selector"
            >
              <button
                type="button"
                className={`btn ${m365AuthMode === 'corporate' ? 'btn-orange' : 'btn-outline-gray'} m365-mode-choice-btn`}
                style={{
                  flex: '1 1 200px',
                  minWidth: 0,
                  padding: '6px 12px',
                  fontSize: '0.8125rem',
                  justifyContent: 'center',
                  whiteSpace: 'normal',
                  textAlign: 'center',
                }}
                onClick={() => handleSwitchM365Mode('corporate')}
                data-testid="m365-choice-corporate"
              >
                Kurumsal Microsoft 365
              </button>
              <button
                type="button"
                className={`btn ${m365AuthMode === 'personal' ? 'btn-orange' : 'btn-outline-gray'} m365-mode-choice-btn`}
                style={{
                  flex: '1 1 200px',
                  minWidth: 0,
                  padding: '6px 12px',
                  fontSize: '0.8125rem',
                  justifyContent: 'center',
                  whiteSpace: 'normal',
                  textAlign: 'center',
                }}
                onClick={() => handleSwitchM365Mode('personal')}
                data-testid="m365-choice-personal"
              >
                Kişisel Outlook.com / Hotmail
              </button>
            </div>

            {m365OAuth.status === 'idle' ? (
              <form onSubmit={handleM365Submit} style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                {m365AuthMode === 'corporate' ? (
                  <details
                    style={{
                      background: '#f8fafc',
                      border: '1px solid #e2e8f0',
                      borderRadius: '6px',
                      padding: '12px 14px',
                      fontSize: '0.8125rem',
                      color: '#334155',
                    }}
                    data-testid="m365-setup-guide"
                  >
                    <summary style={{ cursor: 'pointer', fontWeight: 600, color: 'var(--brand-orange)', userSelect: 'none' }}>
                      Microsoft 365 Kurulum ve Yetkilendirme Kılavuzu (Genişlet)
                    </summary>
                    <div style={{ marginTop: '10px', display: 'flex', flexDirection: 'column', gap: '8px', lineHeight: 1.5 }}>
                      <p style={{ margin: 0 }}>
                        Microsoft 365 kurumsal posta kutunuzu bağlamak için Microsoft Entra ID üzerinde bir uygulama kaydı oluşturmanız gerekir:
                      </p>
                      <ol style={{ margin: 0, paddingLeft: '20px', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                        <li>Microsoft Entra yönetim merkezine (entra.microsoft.com) giriş yapın.</li>
                        <li><strong>Uygulama Kayıtları (App registrations) &gt; Yeni kayıt</strong> adımından tek kiracılı uygulama kaydı (single-tenant app registration) oluşturun.</li>
                        <li>Yeniden yönlendirme URI'si türü olarak <strong>Mobil ve masaüstü uygulamaları (Mobile and desktop applications)</strong> seçin ve <code>http://localhost</code> adresini ekleyin.</li>
                        <li><strong>API İzinleri (API permissions)</strong> altında <strong>Office 365 Exchange Online</strong> seçip temsilci izni (delegated) olarak <code>IMAP.AccessAsUser.All</code> ekleyin. MSAL standart <code>offline_access</code> kapsamını otomatik olarak yönetir (MSAL automatically handles the standard offline_access scope). Uygulama izinleri veya istemci parolası oluşturmayın (do not create app-only/application permission or a client secret). Yönetici onayı yalnızca kiracı politikası gerektiriyorsa gerekir (admin consent is needed only if tenant policy requires it).</li>
                        <li>Genel Bakış (Overview) sayfasındaki <strong>Uygulama (İstemci) Kimliği (Client ID)</strong> ve <strong>Dizin (Kiracı) Kimliği (Tenant ID)</strong> değerlerini aşağıdaki forma girin.</li>
                      </ol>
                      <div style={{ marginTop: '4px' }}>
                        <a
                          href="https://learn.microsoft.com/tr-tr/entra/identity-platform/quickstart-register-app"
                          target="_blank"
                          rel="noreferrer noopener"
                          style={{ color: 'var(--brand-orange)', fontWeight: 600, textDecoration: 'underline' }}
                          data-testid="m365-official-help-link"
                        >
                          Resmi Microsoft Entra Uygulama Kaydı Rehberi
                        </a>
                      </div>
                    </div>
                  </details>
                ) : (
                  <details
                    style={{
                      background: '#f8fafc',
                      border: '1px solid #e2e8f0',
                      borderRadius: '6px',
                      padding: '12px 14px',
                      fontSize: '0.8125rem',
                      color: '#334155',
                    }}
                    data-testid="m365-personal-setup-guide"
                  >
                    <summary style={{ cursor: 'pointer', fontWeight: 600, color: 'var(--brand-orange)', userSelect: 'none' }}>
                      Kişisel Outlook.com / Hotmail Kurulum ve Güvenlik Kılavuzu (Genişlet)
                    </summary>
                    <div style={{ marginTop: '10px', display: 'flex', flexDirection: 'column', gap: '8px', lineHeight: 1.5 }}>
                      <p style={{ margin: 0 }}>
                        Kişisel Outlook.com / Hotmail hesabınızı bağlamak için kişisel Microsoft hesaplarını destekleyen BitigMail Entra uygulama kaydı kullanılır:
                      </p>
                      <ul style={{ margin: 0, paddingLeft: '20px', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                        <li><strong>Uygulama Kaydı:</strong> Kişisel Microsoft hesaplarını destekleyen bir uygulama kaydı ve ilgili <strong>Client ID</strong> değeri gereklidir.</li>
                        <li><strong>Redirect URI:</strong> Masaüstü loopback yönlendirmesi için <code>http://localhost</code> kullanılır.</li>
                        <li><strong>API İzni:</strong> Office 365 Exchange Online için temsilci izni olarak <code>IMAP.AccessAsUser.All</code> eklenir.</li>
                        <li><strong>İstemci Parolası:</strong> İstemci parolası gerekmez; yetkilendirme güvenli PKCE akışıyla tamamlanır.</li>
                        <li><strong>Oturum Açma:</strong> Yetkilendirme bu bilgisayardaki tarayıcıda kullanıcı girişi ile tamamlanır.</li>
                        <li><strong>Outlook Ayarları:</strong> Gerekirse Outlook web ayarlarında (Ayarlar &gt; Posta &gt; POP ve IMAP) IMAP erişimi seçeneğinin açık olduğu doğrulanmalıdır.</li>
                        <li><strong>Canlı Pilot Güvenliği:</strong> Gelecekteki canlı pilot, mevcut kişisel e-postalarınıza erişmeden veya silme yapmadan, yalnızca ayrı bir <code>BitigMail-Test</code> klasöründeki yapay (sentetik) test e-postalarıyla çalışacaktır. Otomatik testlerde gerçek posta kutusu taranmaz.</li>
                      </ul>
                      <div style={{ marginTop: '4px' }}>
                        <a
                          href="https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth"
                          target="_blank"
                          rel="noreferrer noopener"
                          style={{ color: 'var(--brand-orange)', fontWeight: 600, textDecoration: 'underline' }}
                          data-testid="m365-personal-official-help-link"
                        >
                          Resmi Outlook IMAP OAuth Rehberi
                        </a>
                      </div>
                    </div>
                  </details>
                )}

                {m365FormError && (
                  <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '4px', padding: '8px 12px', fontSize: '0.8125rem', color: '#b91c1c' }} data-testid="m365-form-error">
                    {m365FormError}
                  </div>
                )}

                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '12px' }}>
                  <div style={{ minWidth: 0 }}>
                    <label htmlFor="m365-name-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                      Hesap Adı (Görünen Ad) *
                    </label>
                    <input
                      id="m365-name-input"
                      type="text"
                      className="text-input"
                      style={{ width: '100%', boxSizing: 'border-box' }}
                      placeholder={m365AuthMode === 'personal' ? 'Örn: Kişisel Outlook' : 'Örn: Kurumsal M365'}
                      value={m365DisplayName}
                      onChange={(e) => setM365DisplayName(e.target.value)}
                      required
                      data-testid="m365-displayname-input"
                    />
                  </div>

                  <div style={{ minWidth: 0 }}>
                    <label htmlFor="m365-email-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                      Posta Kutusu (E-posta Adresi) *
                    </label>
                    <input
                      id="m365-email-input"
                      type="email"
                      className="text-input"
                      style={{ width: '100%', boxSizing: 'border-box' }}
                      placeholder={m365AuthMode === 'personal' ? 'kullanici@outlook.com veya hotmail.com' : 'kullanici@kurum.onmicrosoft.com'}
                      value={m365Email}
                      onChange={(e) => setM365Email(e.target.value)}
                      required
                      data-testid="m365-email-input"
                    />
                  </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: m365AuthMode === 'personal' ? '1fr' : 'repeat(auto-fit, minmax(240px, 1fr))', gap: '12px' }}>
                  <div style={{ minWidth: 0 }}>
                    <label htmlFor="m365-client-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                      Application (Client) ID (GUID) *
                    </label>
                    <input
                      id="m365-client-input"
                      type="text"
                      className="text-input"
                      style={{ width: '100%', boxSizing: 'border-box' }}
                      placeholder="11111111-2222-3333-4444-555555555555"
                      value={m365ClientId}
                      onChange={(e) => setM365ClientId(e.target.value)}
                      required
                      data-testid="m365-clientid-input"
                    />
                  </div>

                  {m365AuthMode === 'corporate' && (
                    <div style={{ minWidth: 0 }}>
                      <label htmlFor="m365-tenant-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                        Directory (Tenant) ID (GUID) *
                      </label>
                      <input
                        id="m365-tenant-input"
                        type="text"
                        className="text-input"
                        style={{ width: '100%', boxSizing: 'border-box' }}
                        placeholder="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
                        value={m365TenantId}
                        onChange={(e) => setM365TenantId(e.target.value)}
                        required
                        data-testid="m365-tenantid-input"
                      />
                    </div>
                  )}
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '8px' }}>
                  <button
                    type="button"
                    className="btn btn-outline-gray"
                    onClick={closeCreateModal}
                  >
                    İptal
                  </button>
                  <button
                    type="submit"
                    className="btn btn-orange"
                    disabled={m365OAuth.isStarting}
                    data-testid="submit-m365-connect-btn"
                  >
                    {m365OAuth.isStarting
                      ? 'Başlatılıyor...'
                      : (m365AuthMode === 'personal' ? 'Kişisel Outlook ile Bağlan' : 'Kurumsal Microsoft 365 ile Bağlan')}
                  </button>
                </div>
              </form>
            ) : (
              <div>
                {renderM365OAuthStateUI(() => m365OAuth.resetSession())}
                <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '16px' }}>
                  <button
                    type="button"
                    className="btn btn-outline-gray"
                    onClick={closeCreateModal}
                  >
                    Kapat
                  </button>
                </div>
              </div>
            )}
          </div>
        ) : (
          <form onSubmit={handleCreateSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
            <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
              <strong>{projectName}</strong> projesine güvenli IMAP hesabı tanımlayın. Parolanız Windows DPAPI ile şifrelenerek saklanır, düz metin tutulmaz.
            </div>

            {formError && (
              <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '4px', padding: '8px 12px', fontSize: '0.8125rem', color: '#b91c1c' }}>
                {formError}
              </div>
            )}

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                  Hesap Adı *
                </label>
                <input
                  type="text"
                  className="text-input"
                  placeholder="Örn: Kurumsal Destek"
                  value={formDisplayName}
                  onChange={(e) => setFormDisplayName(e.target.value)}
                  required
                  data-testid="create-account-name-input"
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                  E-posta Adresi *
                </label>
                <input
                  type="email"
                  className="text-input"
                  placeholder="destek@sirket.com"
                  value={formEmail}
                  onChange={(e) => setFormEmail(e.target.value)}
                  required
                  data-testid="create-account-email-input"
                />
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr', gap: '12px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                  IMAP Sunucusu (Host) *
                </label>
                <input
                  type="text"
                  className="text-input"
                  placeholder="imap.sirket.com"
                  value={formHost}
                  onChange={(e) => { setFormHost(e.target.value); setFormAllowUnencrypted(false); }}
                  required
                  data-testid="create-account-host-input"
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                  Port *
                </label>
                <input
                  type="number"
                  className="text-input"
                  value={formPort}
                  onChange={(e) => { setFormPort(parseInt(e.target.value, 10) || 993); setFormAllowUnencrypted(false); }}
                  min={1}
                  max={65535}
                  required
                  data-testid="create-account-port-input"
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                  Güvenlik (TLS) *
                </label>
                <select
                  className="text-input"
                  value={formTlsMode}
                  onChange={(e) => handleTlsModeChange(e.target.value as 'ssl' | 'starttls' | 'none')}
                  data-testid="create-account-tls-select"
                >
                  <option value="ssl">SSL/TLS (Port 993)</option>
                  <option value="starttls">STARTTLS (Port 143)</option>
                  <option value="none">Şifreleme yok (önerilmez)</option>
                </select>
              </div>
            </div>

            {formTlsMode === 'none' && <label className="plaintext-consent" data-testid="imap-plaintext-consent"><input type="checkbox" checked={formAllowUnencrypted} onChange={(e) => setFormAllowUnencrypted(e.target.checked)} /><span><strong>Şifresiz bağlantı riskini anlıyorum.</strong> Kullanıcı adı, parola ve iletiler ağ üzerinde okunabilir. Yalnızca güvendiğiniz özel ağdaki eski bir sunucu için kullanın.</span></label>}

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                  Kullanıcı Adı *
                </label>
                <input
                  type="text"
                  className="text-input"
                  placeholder="destek@sirket.com"
                  value={formUsername}
                  onChange={(e) => setFormUsername(e.target.value)}
                  required
                  data-testid="create-account-username-input"
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                  Parola *
                </label>
                <input
                  type="password"
                  className="text-input"
                  placeholder="••••••••"
                  value={formPassword}
                  onChange={(e) => setFormPassword(e.target.value)}
                  required
                  autoComplete="new-password"
                  data-testid="create-account-password-input"
                />
              </div>
            </div>

            {/* Test connection row inside modal */}
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 14px', background: 'var(--bg-subtle)', borderRadius: '6px' }}>
              <div>
                <button
                  type="button"
                  className="btn btn-outline-gray"
                  style={{ padding: '4px 10px', fontSize: '0.8125rem' }}
                  onClick={handleTestInCreateModal}
                  disabled={createTestLoading}
                  data-testid="test-new-account-conn-btn"
                >
                  <IconRefreshCw size={12} className={createTestLoading ? 'animate-spin' : ''} />
                  <span>{createTestLoading ? 'Sınanıyor...' : 'Kaydetmeden Önce Sına'}</span>
                </button>
              </div>

              {createTestResult && (
                <div style={{ fontSize: '0.8125rem', fontWeight: 600 }}>
                  {createTestResult.success ? (
                    <span style={{ color: '#16a34a' }}>✓ Bağlantı başarılı ({createTestResult.latencyMs} ms)</span>
                  ) : (
                    <span style={{ color: '#dc2626' }}>✕ {createTestResult.error}</span>
                  )}
                </div>
              )}
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '8px' }}>
              <button
                type="button"
                className="btn btn-outline-gray"
                onClick={closeCreateModal}
              >
                İptal
              </button>
              <button
                type="submit"
                className="btn btn-orange"
                disabled={isSubmitting || (formTlsMode === 'none' && !formAllowUnencrypted)}
                data-testid="submit-new-account-btn"
              >
                {isSubmitting ? 'Kaydediliyor...' : 'Hesabı Kaydet'}
              </button>
            </div>
          </form>
        )}
      </Modal>

      {/* EDIT ACCOUNT MODAL */}
      {/* EDIT ACCOUNT MODAL */}
      <Modal
        isOpen={editModalAccount !== null}
        onClose={closeEditModal}
        title={editModalAccount?.authKind === 'microsoft365' ? 'Microsoft 365 Hesabını Düzenle' : 'IMAP Hesabını Düzenle'}
        size="md"
      >
        {editModalAccount && (
          editModalAccount.authKind === 'microsoft365' ? (
            <form onSubmit={handleEditSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
              <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', display: 'flex', justifyContent: 'space-between' }}>
                <span>Hesap ID: <code style={{ fontSize: '0.75rem' }}>{editModalAccount.accountId}</code></span>
                <span style={{ fontWeight: 600 }}>Geçerli Sürüm: v{editModalAccount.version}</span>
              </div>

              <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '10px 12px', fontSize: '0.8125rem', color: '#475569' }}>
                Microsoft 365 hesaplarında sunucu, port, güvenlik, kullanıcı adı ve parola değiştirilemez. Yalnızca görünen adı güncelleyebilirsiniz.
              </div>

              {editError && (
                <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '4px', padding: '8px 12px', fontSize: '0.8125rem', color: '#b91c1c' }}>
                  {editError}
                </div>
              )}

              <div>
                <label htmlFor="edit-m365-name-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                  Hesap Adı (Görünen Ad) *
                </label>
                <input
                  id="edit-m365-name-input"
                  type="text"
                  className="text-input"
                  value={editDisplayName}
                  onChange={(e) => setEditDisplayName(e.target.value)}
                  required
                  data-testid="edit-account-name-input"
                />
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                <div>
                  <label htmlFor="edit-m365-email-readonly" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px', color: 'var(--text-muted)' }}>
                    Posta Kutusu (Değişmez)
                  </label>
                  <input
                    id="edit-m365-email-readonly"
                    type="text"
                    className="text-input"
                    value={editModalAccount.email}
                    disabled
                    style={{ opacity: 0.7, background: 'var(--bg-subtle)' }}
                  />
                </div>
                <div>
                  <label htmlFor="edit-m365-server-readonly" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px', color: 'var(--text-muted)' }}>
                    Sunucu (Sabit Exchange Online)
                  </label>
                  <input
                    id="edit-m365-server-readonly"
                    type="text"
                    className="text-input"
                    value="outlook.office365.com:993 (OAuth2)"
                    disabled
                    style={{ opacity: 0.7, background: 'var(--bg-subtle)' }}
                  />
                </div>
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '8px' }}>
                <button
                  type="button"
                  className="btn btn-outline-gray"
                  onClick={closeEditModal}
                >
                  İptal
                </button>
                <button
                  type="submit"
                  className="btn btn-orange"
                  disabled={isSubmitting}
                  data-testid="submit-edit-account-btn"
                >
                  {isSubmitting ? 'Güncelleniyor...' : 'Güncellemeleri Kaydet'}
                </button>
              </div>
            </form>
          ) : (
            <form onSubmit={handleEditSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
              <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', display: 'flex', justifyContent: 'space-between' }}>
                <span>Hesap ID: <code style={{ fontSize: '0.75rem' }}>{editModalAccount.accountId}</code></span>
                <span style={{ fontWeight: 600 }}>Geçerli Sürüm: v{editModalAccount.version}</span>
              </div>

              {editError && (
                <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '4px', padding: '8px 12px', fontSize: '0.8125rem', color: '#b91c1c' }}>
                  {editError}
                </div>
              )}

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                <div>
                  <label htmlFor="edit-account-name-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                    Hesap Adı *
                  </label>
                  <input
                    id="edit-account-name-input"
                    type="text"
                    className="text-input"
                    value={editDisplayName}
                    onChange={(e) => setEditDisplayName(e.target.value)}
                    required
                    data-testid="edit-account-name-input"
                  />
                </div>

                <div>
                  <label htmlFor="edit-account-email-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                    E-posta Adresi *
                  </label>
                  <input
                    id="edit-account-email-input"
                    type="email"
                    className="text-input"
                    value={editEmail}
                    onChange={(e) => setEditEmail(e.target.value)}
                    required
                    data-testid="edit-account-email-input"
                  />
                </div>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr', gap: '12px' }}>
                <div>
                  <label htmlFor="edit-account-host-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                    IMAP Sunucusu (Host) *
                  </label>
                  <input
                    id="edit-account-host-input"
                    type="text"
                    className="text-input"
                    value={editHost}
                    onChange={(e) => { setEditHost(e.target.value); setEditAllowUnencrypted(false); }}
                    required
                    data-testid="edit-account-host-input"
                  />
                </div>

                <div>
                  <label htmlFor="edit-account-port-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                    Port *
                  </label>
                  <input
                    id="edit-account-port-input"
                    type="number"
                    className="text-input"
                    value={editPort}
                    onChange={(e) => { setEditPort(parseInt(e.target.value, 10) || 993); setEditAllowUnencrypted(false); }}
                    min={1}
                    max={65535}
                    required
                    data-testid="edit-account-port-input"
                  />
                </div>

                <div>
                  <label htmlFor="edit-account-tls-select" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                    Güvenlik (TLS) *
                  </label>
                  <select
                    id="edit-account-tls-select"
                    className="text-input"
                    value={editTlsMode}
                    onChange={(e) => handleEditTlsModeChange(e.target.value as 'ssl' | 'starttls' | 'none')}
                    data-testid="edit-account-tls-select"
                  >
                    <option value="none">Şifreleme yok (önerilmez)</option>
                    <option value="ssl">SSL/TLS (Port 993)</option>
                    <option value="starttls">STARTTLS (Port 143)</option>
                  </select>
                </div>
              </div>

              {editTlsMode === 'none' && <label className="plaintext-consent" data-testid="edit-imap-plaintext-consent"><input type="checkbox" checked={editAllowUnencrypted} onChange={(e) => setEditAllowUnencrypted(e.target.checked)} /><span><strong>Şifresiz bağlantı riskini anlıyorum.</strong> Sunucu veya port değiştiğinde bu onay güvenliğiniz için sıfırlanır.</span></label>}

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                <div>
                  <label htmlFor="edit-account-username-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                    Kullanıcı Adı *
                  </label>
                  <input
                    id="edit-account-username-input"
                    type="text"
                    className="text-input"
                    value={editUsername}
                    onChange={(e) => setEditUsername(e.target.value)}
                    required
                    data-testid="edit-account-username-input"
                  />
                </div>

                <div>
                  <label htmlFor="edit-account-password-input" style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 600, marginBottom: '4px' }}>
                    Parolayı Güncelle (İsteğe Bağlı)
                  </label>
                  <input
                    id="edit-account-password-input"
                    type="password"
                    className="text-input"
                    placeholder="Değiştirmek istemiyorsanız boş bırakın"
                    value={editPassword}
                    onChange={(e) => setEditPassword(e.target.value)}
                    autoComplete="new-password"
                    data-testid="edit-account-password-input"
                  />
                </div>
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '8px' }}>
                <button
                  type="button"
                  className="btn btn-outline-gray"
                  onClick={closeEditModal}
                >
                  İptal
                </button>
                <button
                  type="submit"
                  className="btn btn-orange"
                  disabled={isSubmitting || (editTlsMode === 'none' && !editAllowUnencrypted)}
                  data-testid="submit-edit-account-btn"
                >
                  {isSubmitting ? 'Güncelleniyor...' : 'Güncellemeleri Kaydet'}
                </button>
              </div>
            </form>
          )
        )}
      </Modal>

      {/* RECONNECT MODAL */}
      <Modal
        isOpen={reconnectModalAccount !== null}
        onClose={() => {
          if (m365OAuth.status === 'awaiting-signin' || m365OAuth.status === 'preparing') {
            m365OAuth.cancelOperation();
          }
          setReconnectModalAccount(null);
          m365OAuth.resetSession(true);
        }}
        title={reconnectModalAccount?.tenantId === 'consumers' ? 'Kişisel Outlook Hesabını Yeniden Yetkilendir' : 'Microsoft 365 Hesabını Yeniden Yetkilendir'}
        size="md"
      >
        {reconnectModalAccount && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
            <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
              <strong>{reconnectModalAccount.displayName}</strong> ({reconnectModalAccount.tenantId === 'consumers' ? 'Kişisel Outlook.com / Hotmail' : 'Kurumsal Microsoft 365'}) hesabının oturumunu güvenle yenileyin. {reconnectModalAccount.tenantId === 'consumers' ? 'İstemci (Client) ve Posta Kutusu kimlikleri değişmezdir.' : 'Kiracı (Tenant), İstemci (Client) ve Posta Kutusu kimlikleri değişmezdir.'}
            </div>

            <div style={{ background: 'var(--bg-subtle)', padding: '12px', borderRadius: '6px', fontSize: '0.8125rem', display: 'flex', flexDirection: 'column', gap: '6px', wordBreak: 'break-all' }}>
              <div><strong>Hesap Türü:</strong> {reconnectModalAccount.tenantId === 'consumers' ? 'Kişisel Outlook.com / Hotmail' : 'Kurumsal Microsoft 365'}</div>
              <div><strong>Hesap:</strong> {reconnectModalAccount.displayName} (v{reconnectModalAccount.version})</div>
              <div><strong>E-posta:</strong> <code style={{ color: 'var(--text-main)', wordBreak: 'break-all' }}>{reconnectModalAccount.email}</code></div>
              <div><strong>Application (Client) ID:</strong> <code style={{ color: 'var(--text-main)', wordBreak: 'break-all' }}>{reconnectModalAccount.clientId}</code></div>
              <div><strong>{reconnectModalAccount.tenantId === 'consumers' ? 'Authority (Tenant):' : 'Directory (Tenant) ID:'}</strong> <code style={{ color: 'var(--text-main)', wordBreak: 'break-all' }}>{reconnectModalAccount.tenantId}</code></div>
            </div>

            {m365OAuth.status === 'idle' ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '10px 12px', fontSize: '0.8125rem', color: '#334155' }}>
                  Yetkilendirme bu bilgisayardaki tarayıcıda tamamlanacaktır.
                </div>
                <button
                  type="button"
                  className="btn btn-orange"
                  style={{ width: '100%', padding: '10px', fontWeight: 600 }}
                  onClick={() => handleM365Reconnect(reconnectModalAccount)}
                  disabled={m365OAuth.isStarting}
                  data-testid="start-reconnect-btn"
                >
                  {m365OAuth.isStarting ? 'Yeniden Bağlama Başlatılıyor...' : 'Yeniden Yetkilendirmeyi Başlat'}
                </button>
              </div>
            ) : (
              renderM365OAuthStateUI(() => handleM365Reconnect(reconnectModalAccount))
            )}

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '8px' }}>
              <button
                type="button"
                className="btn btn-outline-gray"
                onClick={() => {
                  if (m365OAuth.status === 'awaiting-signin' || m365OAuth.status === 'preparing') {
                    m365OAuth.cancelOperation();
                  }
                  setReconnectModalAccount(null);
                  m365OAuth.resetSession();
                }}
              >
                Kapat
              </button>
            </div>
          </div>
        )}
      </Modal>

      <Modal isOpen={googleReconnectAccount !== null} onClose={() => { googleOAuth.resetSession(true); setGoogleReconnectSecret(''); setGoogleReconnectAccount(null); }} title="Google Hesabını Yeniden Yetkilendir" size="md">
        {googleReconnectAccount && <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }} data-testid="google-reconnect-panel">
          <div><strong>{googleReconnectAccount.displayName}</strong> · {googleReconnectAccount.email}</div>
          {googleOAuth.status === 'idle' ? <>
            <input className="text-input" type="password" autoComplete="off" value={googleReconnectSecret} onChange={e => setGoogleReconnectSecret(e.target.value)} placeholder="Google masaüstü Client Secret" data-testid="google-reconnect-secret" />
            <button type="button" className="btn btn-orange" onClick={handleGoogleReconnect} disabled={!googleReconnectSecret} data-testid="start-google-reconnect-btn">Yeniden Yetkilendirmeyi Başlat</button>
          </> : <div>{googleOAuth.validatedUrl && <a href={googleOAuth.validatedUrl} target="_blank" rel="noreferrer noopener" className="btn btn-orange">Google ile giriş yap</a>}{googleOAuth.message || googleOAuth.error}</div>}
        </div>}
      </Modal>

      {/* DELETE CONFIRMATION MODAL */}
      <Modal
        isOpen={deleteModalAccount !== null}
        onClose={() => setDeleteModalAccount(null)}
        title={deleteModalAccount?.authKind === 'microsoft365' ? 'Microsoft 365 Yerel Bağlantısını Kaldır' : deleteModalAccount?.authKind === 'google' ? 'Google Yerel Bağlantısını Kaldır' : 'IMAP Hesabını Sil'}
        size="sm"
      >
        {deleteModalAccount && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
            <div style={{ display: 'flex', alignItems: 'flex-start', gap: '12px' }}>
              <IconAlertTriangle size={24} color="#dc2626" />
              <div>
                <div style={{ fontWeight: 600, fontSize: '0.9375rem', color: 'var(--text-main)', marginBottom: '4px' }}>
                  {deleteModalAccount.displayName} ({deleteModalAccount.email})
                </div>
                {deleteModalAccount.authKind === 'microsoft365' || deleteModalAccount.authKind === 'google' ? (
                  <p style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', margin: 0 }}>
                    Bu işlem yalnızca BitigMail içindeki yerel hesap kaydını ve kimlik önbelleğini kaldırır.
                    <strong> {deleteModalAccount.authKind === 'microsoft365' ? 'Microsoft tarafında verilmiş olan izinleri iptal etmez' : 'Google tarafında verilmiş olan izinleri iptal etmez'} (grant revocation yapılmaz).</strong> Uzak posta kutusundaki iletiler veya klasörler kesinlikle silinmez.
                  </p>
                ) : (
                  <p style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', margin: 0 }}>
                    Bu işlem yalnızca BitigMail içindeki yerel hesap kaydını ve şifrelenmiş kimlik bilgilerini siler.
                    <strong> Uzak IMAP sunucusundaki iletiler veya klasörler kesinlikle silinmez.</strong>
                  </p>
                )}
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '10px' }}>
              <button
                type="button"
                className="btn btn-outline-gray"
                onClick={() => setDeleteModalAccount(null)}
              >
                İptal
              </button>
              <button
                type="button"
                className="btn btn-orange"
                style={{ background: '#dc2626', borderColor: '#dc2626' }}
                onClick={handleDeleteConfirm}
                disabled={isSubmitting}
                data-testid="confirm-delete-account-btn"
              >
                {isSubmitting ? 'Kaldırılıyor...' : (deleteModalAccount.authKind === 'microsoft365' || deleteModalAccount.authKind === 'google' ? 'Yerel bağlantıyı kaldır' : 'Evet, Hesabı Kaldır')}
              </button>
            </div>
          </div>
        )}
      </Modal>

      {/* REAL FOLDER INSPECTION MODAL */}
      <Modal
        isOpen={foldersModalAccount !== null}
        onClose={() => setFoldersModalAccount(null)}
        title={`${foldersModalAccount?.displayName || 'IMAP'} — Sunucu Klasörleri`}
        size="lg"
      >
        {foldersModalAccount && (
          <div>
            <div style={{ marginBottom: '14px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                Sunucudan doğrudan okunan gerçek klasör hiyerarşisi ve ileti sayıları
              </span>
              <button
                className="btn btn-outline-gray"
                style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                onClick={() => loadFolders(foldersModalAccount.accountId)}
                disabled={foldersState[foldersModalAccount.accountId]?.loading}
                data-testid="refresh-folders-btn"
              >
                <IconRefreshCw size={12} className={foldersState[foldersModalAccount.accountId]?.loading ? 'animate-spin' : ''} />
                <span>Yeniden Tara</span>
              </button>
            </div>

            {(() => {
              const state = foldersState[foldersModalAccount.accountId];
              if (!state || state.loading) {
                return (
                  <div style={{ padding: '36px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '0.875rem' }}>
                    IMAP klasörleri taranıyor...
                  </div>
                );
              }

              if (state.error) {
                const isReauth = state.code === 'reauthorization_required' || state.apiError?.data?.code === 'reauthorization_required' || state.error.toLowerCase().includes('reauthorization_required');
                return (
                  <div style={{ background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '6px', padding: '14px', color: '#b91c1c', fontSize: '0.875rem' }} data-testid="folder-error-alert">
                    <div style={{ fontWeight: 600, marginBottom: '4px' }}>Klasör listesi alınamadı:</div>
                    <div>{state.error}</div>
                    {isReauth && foldersModalAccount?.authKind === 'microsoft365' && (
                      <div style={{ marginTop: '12px' }}>
                        <button
                          type="button"
                          className="btn btn-orange"
                          style={{ padding: '6px 12px', fontSize: '0.8125rem' }}
                          onClick={() => {
                            const acc = foldersModalAccount;
                            setFoldersModalAccount(null);
                            setReconnectModalAccount(acc);
                            m365OAuth.resetSession();
                          }}
                          data-testid="folders-reauth-btn"
                        >
                          Microsoft 365 ile Yeniden Yetkilendir
                        </button>
                      </div>
                    )}
                  </div>
                );
              }

              const folders = state.folders || [];
              if (folders.length === 0) {
                return (
                  <div style={{ padding: '24px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '0.875rem' }}>
                    Sunucuda taranabilir klasör bulunamadı.
                  </div>
                );
              }

              return (
                <div className="table-container" style={{ maxHeight: '420px', overflowY: 'auto' }}>
                  <table className="data-table" data-testid="imap-folders-table">
                    <thead>
                      <tr>
                        <th style={{ width: '45%' }}>Tam Klasör Yolu</th>
                        <th style={{ width: '12%' }}>Ayırıcı</th>
                        <th style={{ width: '18%' }}>Durum</th>
                        <th style={{ width: '25%', textAlign: 'right' }}>İleti Sayısı</th>
                      </tr>
                    </thead>
                    <tbody>
                      {folders.map((folder) => (
                        <tr key={folder.fullPath} data-testid={`folder-row-${folder.fullPath}`}>
                          <td>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                              <IconFolder size={14} color="#64748b" />
                              <span style={{ fontWeight: 600, fontFamily: 'monospace', fontSize: '0.8125rem' }}>
                                {folder.fullPath}
                              </span>
                            </div>
                          </td>
                          <td style={{ fontFamily: 'monospace', fontSize: '0.8125rem' }}>
                            {folder.delimiter || '/'}
                          </td>
                          <td>
                            {folder.isSelectable ? (
                              <span style={{ fontSize: '0.75rem', color: '#15803d', fontWeight: 600 }}>Seçilebilir</span>
                            ) : (
                              <span style={{ fontSize: '0.75rem', color: '#64748b' }}>Kapsayıcı (NoSelect)</span>
                            )}
                          </td>
                          <td style={{ textAlign: 'right' }}>
                            {!folder.isSelectable ? (
                              <span style={{ color: 'var(--text-muted)', fontSize: '0.8125rem' }}>—</span>
                            ) : !folder.countValid ? (
                              <span
                                style={{
                                  fontSize: '0.75rem',
                                  fontWeight: 600,
                                  color: '#b45309',
                                  background: '#fffbeb',
                                  padding: '2px 6px',
                                  borderRadius: '4px',
                                  border: '1px solid #fde68a',
                                }}
                                title={folder.statusError || 'İleti sayısı okunamadı'}
                                data-testid={`folder-count-unknown-${folder.fullPath}`}
                              >
                                ⚠️ Okunamadı (Bilinmiyor)
                              </span>
                            ) : (
                              <span style={{ fontWeight: 600, fontSize: '0.8125rem' }} data-testid={`folder-count-${folder.fullPath}`}>
                                {folder.messageCount ?? 0} ileti
                                {folder.unreadCount !== null && folder.unreadCount !== undefined && folder.unreadCount > 0 && (
                                  <span style={{ color: '#2563eb', marginLeft: '6px', fontSize: '0.75rem' }}>
                                    ({folder.unreadCount} okunmamış)
                                  </span>
                                )}
                              </span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              );
            })()}

            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '16px' }}>
              <button
                className="btn btn-outline-gray"
                onClick={() => setFoldersModalAccount(null)}
              >
                Kapat
              </button>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
};
