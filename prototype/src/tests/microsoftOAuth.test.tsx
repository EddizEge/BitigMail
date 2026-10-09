// @vitest-environment jsdom
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { renderHook } from '@testing-library/react';
import { ProjectImapAccounts } from '../components/clients/ProjectImapAccounts';
import { ImapTransferWorkflow } from '../components/transfer/ImapTransferWorkflow';
import { useMicrosoftOAuth } from '../hooks/useMicrosoftOAuth';
import { LocalEngineClient, validateMicrosoftAuthorizationUrl } from '../api/localEngineClient';
import { ImapAccountPublicDto } from '../types/localEngine';

describe('TASK-015 Microsoft 365 Connection UI Tests', () => {
  let mockClient: LocalEngineClient;

  beforeEach(() => {
    vi.restoreAllMocks();
    mockClient = new LocalEngineClient('http://127.0.0.1:6174');
    mockClient.listAccounts = vi.fn().mockResolvedValue([]);
    mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
      operationId: 'op-default',
      status: 'preparing',
      createdAtUtc: '2026-09-13T12:00:00Z',
      expiresAtUtc: '2026-09-13T12:05:00Z',
    });
    mockClient.getMicrosoftOAuthOperation = vi.fn().mockResolvedValue({
      operationId: 'op-default',
      status: 'preparing',
      createdAtUtc: '2026-09-13T12:00:00Z',
      expiresAtUtc: '2026-09-13T12:05:00Z',
    });
    mockClient.cancelMicrosoftOAuthOperation = vi.fn().mockResolvedValue({
      operationId: 'op-default',
      status: 'cancelled',
      message: 'Cancelled',
    });
    mockClient.updateAccount = vi.fn().mockResolvedValue({});
    mockClient.deleteAccount = vi.fn().mockResolvedValue({});
    // Ensure storage is clean
    localStorage.clear();
    sessionStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  describe('1. Safe Link Allowlist & Strict URL Validation', () => {
    const canonicalTenant = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

    it('approves strict HTTPS login.microsoftonline.com with exact tenant GUID path', () => {
      const validUrl = `https://login.microsoftonline.com/${canonicalTenant}/oauth2/v2.0/authorize?client_id=11111111-2222-3333-4444-555555555555&response_type=code`;
      const res = validateMicrosoftAuthorizationUrl(validUrl, canonicalTenant);
      expect(res.valid).toBe(true);
      expect(res.error).toBeUndefined();
    });

    it('rejects HTTP, untrusted domains, mismatched tenant GUID, javascript/data schemes', () => {
      // Insecure HTTP
      expect(
        validateMicrosoftAuthorizationUrl(`http://login.microsoftonline.com/${canonicalTenant}/oauth2`, canonicalTenant).valid
      ).toBe(false);

      // Phishing / untrusted domain
      expect(
        validateMicrosoftAuthorizationUrl(`https://evil-login.microsoftonline.com.phish.com/${canonicalTenant}/oauth2`, canonicalTenant).valid
      ).toBe(false);

      // Mismatched tenant GUID
      expect(
        validateMicrosoftAuthorizationUrl(`https://login.microsoftonline.com/99999999-9999-9999-9999-999999999999/oauth2`, canonicalTenant).valid
      ).toBe(false);

      // javascript: pseudo-protocol
      expect(validateMicrosoftAuthorizationUrl('javascript:alert(1)', canonicalTenant).valid).toBe(false);

      // Empty or null
      expect(validateMicrosoftAuthorizationUrl('', canonicalTenant).valid).toBe(false);
      expect(validateMicrosoftAuthorizationUrl(null, canonicalTenant).valid).toBe(false);
    });
  });

  describe('2. Memory Isolation & URL Cleanup (Zero Storage)', () => {
    it('guarantees authorizationUrl never enters localStorage or sessionStorage', async () => {
      const tenantId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
      const authUrl = `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/authorize?code=test`;

      mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
        operationId: 'op-mem-test',
        status: 'preparing',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });

      mockClient.getMicrosoftOAuthOperation = vi.fn().mockResolvedValue({
        operationId: 'op-mem-test',
        status: 'awaiting-signin',
        authorizationUrl: authUrl,
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });

      const { result } = renderHook(() =>
        useMicrosoftOAuth({
          companyId: 'comp-1',
          projectId: 'proj-1',
          client: mockClient,
          pollIntervalMs: 50,
        })
      );

      await act(async () => {
        await result.current.startOperation({
          displayName: 'Test M365',
          email: 'test@contoso.com',
          clientId: '11111111-2222-3333-4444-555555555555',
          tenantId,
        });
      });

      // Status transitioned to awaiting-signin in React memory
      expect(result.current.status).toBe('awaiting-signin');
      expect(result.current.authorizationUrl).toBe(authUrl);

      // Check localStorage and sessionStorage - MUST BE COMPLETELY CLEAN
      expect(localStorage.getItem('authorizationUrl')).toBeNull();
      expect(localStorage.getItem('m365_auth_url')).toBeNull();
      expect(sessionStorage.getItem('authorizationUrl')).toBeNull();
      expect(sessionStorage.getItem('m365_auth_url')).toBeNull();

      // Reset / cancel clears authorizationUrl from React memory
      act(() => {
        result.current.resetSession();
      });

      expect(result.current.authorizationUrl).toBeNull();
      expect(result.current.validatedUrl).toBeNull();
    });
  });

  describe('3. Click Deduplication & Stale Scope Isolation', () => {
    it('deduplicates repeated start clicks', async () => {
      let resolveStart: (val: any) => void;
      const startPromise = new Promise((resolve) => {
        resolveStart = resolve;
      });

      mockClient.startMicrosoftOAuth = vi.fn().mockReturnValue(startPromise);

      const { result } = renderHook(() =>
        useMicrosoftOAuth({
          companyId: 'comp-1',
          projectId: 'proj-1',
          client: mockClient,
        })
      );

      // Trigger 1st click
      act(() => {
        result.current.startOperation({
          displayName: 'Test M365',
          email: 'test@contoso.com',
          clientId: '11111111-2222-3333-4444-555555555555',
          tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        });
      });

      // Trigger 2nd click immediately while 1st is in-flight
      act(() => {
        result.current.startOperation({
          displayName: 'Test M365 Duplicate',
          email: 'test@contoso.com',
          clientId: '11111111-2222-3333-4444-555555555555',
          tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        });
      });

      // Verify client was called only ONCE
      expect(mockClient.startMicrosoftOAuth).toHaveBeenCalledTimes(1);

      // Resolve in-flight
      await act(async () => {
        resolveStart!({
          operationId: 'op-1',
          status: 'preparing',
          createdAtUtc: '2026-09-13T12:00:00Z',
          expiresAtUtc: '2026-09-13T12:05:00Z',
        });
      });
    });

    it('cancels active operation and discards stale results upon scope change', async () => {
      mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
        operationId: 'op-scope-A',
        status: 'preparing',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });
      mockClient.cancelMicrosoftOAuthOperation = vi.fn().mockResolvedValue({
        operationId: 'op-scope-A',
        status: 'cancelled',
        message: 'Cancelled',
      });

      const { result, rerender } = renderHook(
        ({ comp, proj }) =>
          useMicrosoftOAuth({
            companyId: comp,
            projectId: proj,
            client: mockClient,
            pollIntervalMs: 50,
          }),
        { initialProps: { comp: 'comp-A', proj: 'proj-A' } }
      );

      await act(async () => {
        await result.current.startOperation({
          displayName: 'Test M365',
          email: 'test@contoso.com',
          clientId: '11111111-2222-3333-4444-555555555555',
          tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        });
      });

      expect(result.current.status).toBe('preparing');

      // Change scope to Scope B
      rerender({ comp: 'comp-B', proj: 'proj-B' });

      // Active operation from Scope A must be cancelled on backend and session reset
      expect(mockClient.cancelMicrosoftOAuthOperation).toHaveBeenCalledWith('op-scope-A', 'comp-A', 'proj-A');
      expect(result.current.status).toBe('idle');
      expect(result.current.operationId).toBeNull();
    });

    it('authoritative connected status wins when cancel response reports connected with accountId', async () => {
      const onConnectedSpy = vi.fn();
      mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
        operationId: 'op-race-connect',
        status: 'preparing',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });
      // Root backend returns authoritative connected status with accountId
      mockClient.cancelMicrosoftOAuthOperation = vi.fn().mockResolvedValue({
        operationId: 'op-race-connect',
        status: 'connected',
        message: 'Concurrent signin succeeded before cancellation processed.',
        accountId: 'acc-m365-winner',
      });

      const { result } = renderHook(() =>
        useMicrosoftOAuth({
          companyId: 'comp-1',
          projectId: 'proj-1',
          client: mockClient,
          onConnected: onConnectedSpy,
        })
      );

      await act(async () => {
        await result.current.startOperation({
          displayName: 'Test M365',
          email: 'test@contoso.com',
          clientId: '11111111-2222-3333-4444-555555555555',
          tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        });
      });

      expect(result.current.status).toBe('preparing');

      // User initiates cancellation
      await act(async () => {
        await result.current.cancelOperation();
      });

      // Crucial: cancelOperation must NOT unconditionally force cancelled in finally.
      // Connected must win, record accountId, and trigger onConnected.
      expect(result.current.status).toBe('connected');
      expect(result.current.accountId).toBe('acc-m365-winner');
      expect(onConnectedSpy).toHaveBeenCalledWith('acc-m365-winner');
    });

    it('retains safe error, avoids claiming success, and preserves polling on cancellation network failure', async () => {
      mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
        operationId: 'op-fail-cancel',
        status: 'awaiting-signin',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });
      // Cancel throws network error
      mockClient.cancelMicrosoftOAuthOperation = vi.fn().mockRejectedValue(
        new Error('Network offline during cancel')
      );
      // Reconcile GET returns in-flight status awaiting-signin
      mockClient.getMicrosoftOAuthOperation = vi.fn().mockResolvedValue({
        operationId: 'op-fail-cancel',
        status: 'awaiting-signin',
        authorizationUrl: 'https://login.microsoftonline.com/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/oauth2/v2.0/authorize',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });

      const { result } = renderHook(() =>
        useMicrosoftOAuth({
          companyId: 'comp-1',
          projectId: 'proj-1',
          client: mockClient,
          pollIntervalMs: 50,
        })
      );

      await act(async () => {
        await result.current.startOperation({
          displayName: 'Test M365',
          email: 'test@contoso.com',
          clientId: '11111111-2222-3333-4444-555555555555',
          tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        });
      });

      // Cancellation triggered but network fails
      await act(async () => {
        await result.current.cancelOperation();
      });

      // Must never claim success (must NOT be 'cancelled')
      expect(result.current.status).not.toBe('cancelled');
      // Must retain safe error message
      expect(result.current.error).toContain('İptal işlemi başarısız');
      expect(result.current.error).toContain('Network offline during cancel');

      // Now simulate subsequent poll reaching terminal connected state
      mockClient.getMicrosoftOAuthOperation = vi.fn().mockResolvedValue({
        operationId: 'op-fail-cancel',
        status: 'connected',
        accountId: 'acc-eventual-success',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });

      // Fast-forward / wait for continued polling to resolve terminal state
      await waitFor(() => {
        expect(result.current.status).toBe('connected');
        expect(result.current.accountId).toBe('acc-eventual-success');
      });
    });

    it('snapshots scope/generation before cancel so late cancel response does not overwrite newer scope', async () => {
      let resolveCancel: (val: any) => void;
      const cancelPromise = new Promise((resolve) => {
        resolveCancel = resolve;
      });

      mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
        operationId: 'op-scope-old',
        status: 'preparing',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });
      mockClient.cancelMicrosoftOAuthOperation = vi.fn().mockReturnValue(cancelPromise);

      const { result, rerender } = renderHook(
        ({ comp, proj }) =>
          useMicrosoftOAuth({
            companyId: comp,
            projectId: proj,
            client: mockClient,
          }),
        { initialProps: { comp: 'comp-1', proj: 'proj-1' } }
      );

      await act(async () => {
        await result.current.startOperation({
          displayName: 'Test M365',
          email: 'test@contoso.com',
          clientId: '11111111-2222-3333-4444-555555555555',
          tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        });
      });

      // Start cancel on Scope 1 (promise is pending)
      act(() => {
        result.current.cancelOperation();
      });

      // Scope changes to Scope 2 while cancel is in flight
      rerender({ comp: 'comp-2', proj: 'proj-2' });

      expect(result.current.status).toBe('idle');

      // Now late cancel from Scope 1 finishes with connected
      await act(async () => {
        resolveCancel!({
          operationId: 'op-scope-old',
          status: 'connected',
          accountId: 'acc-stale-leak',
        });
      });

      // Crucial: Scope 2 must NOT be overwritten by late cancel response from Scope 1
      expect(result.current.status).toBe('idle');
      expect(result.current.accountId).toBeNull();
    });
  });

  describe('4. Form Regression & Microsoft Form Security (Zero Password)', () => {
    it('preserves standard IMAP form with password/host/port/TLS', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([]);

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      // Open Create Modal
      fireEvent.click(screen.getByTestId('add-account-btn-proj-1'));

      // Standard IMAP tab is active by default
      expect(screen.getByTestId('create-account-name-input')).toBeDefined();
      expect(screen.getByTestId('create-account-email-input')).toBeDefined();
      expect(screen.getByTestId('create-account-host-input')).toBeDefined();
      expect(screen.getByTestId('create-account-port-input')).toBeDefined();
      expect(screen.getByTestId('create-account-tls-select')).toBeDefined();
      expect(screen.getByTestId('create-account-username-input')).toBeDefined();
      expect(screen.getByTestId('create-account-password-input')).toBeDefined();
    });

    it('ensures Microsoft 365 form has displayName, email, client GUID, tenant GUID and STRICTLY NO password/host/port/TLS', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([]);

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      // Open Create Modal
      fireEvent.click(screen.getByTestId('add-account-btn-proj-1'));

      // Switch to Microsoft 365 tab
      fireEvent.click(screen.getByTestId('tab-microsoft-365'));

      // Verify Microsoft form fields
      expect(screen.getByTestId('m365-displayname-input')).toBeDefined();
      expect(screen.getByTestId('m365-email-input')).toBeDefined();
      expect(screen.getByTestId('m365-clientid-input')).toBeDefined();
      expect(screen.getByTestId('m365-tenantid-input')).toBeDefined();

      // STRICTLY VERIFY: Zero password, host, port, or TLS inputs exist in Microsoft form
      expect(screen.queryByTestId('create-account-password-input')).toBeNull();
      expect(screen.queryByTestId('create-account-host-input')).toBeNull();
      expect(screen.queryByTestId('create-account-port-input')).toBeNull();
      expect(screen.queryByTestId('create-account-tls-select')).toBeNull();
      expect(screen.queryByPlaceholderText('••••••••')).toBeNull();

      // Check Turkish setup guidance expandable section and official Microsoft HTTPS link
      const setupGuide = screen.getByTestId('m365-setup-guide');
      expect(setupGuide).toBeDefined();
      expect(screen.getByText(/Microsoft 365 Kurulum ve Yetkilendirme Kılavuzu/i)).toBeDefined();

      const helpLink = screen.getByTestId('m365-official-help-link');
      expect(helpLink.getAttribute('href')).toMatch(/^https:\/\/learn\.microsoft\.com\//);
      expect(helpLink.getAttribute('target')).toBe('_blank');
      expect(helpLink.getAttribute('rel')).toBe('noreferrer noopener');

      // Verify raw repo filename and technical disclaimer are strictly removed from UI
      expect(screen.queryByText(/docs\/MICROSOFT365_SETUP\.md/i)).toBeNull();
      expect(screen.queryByText(/Bu form canlı kiracı doğrulaması yapmaz/i)).toBeNull();
    });
  });

  describe('5. OAuth State Rendering & Desktop Browser Guidance', () => {
    it('renders preparing, awaiting-signin with safe link and local PC notice', async () => {
      const tenantId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
      const authUrl = `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/authorize?client_id=11111111-2222-3333-4444-555555555555`;

      mockClient.listAccounts = vi.fn().mockResolvedValue([]);
      mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
        operationId: 'op-ui-test',
        status: 'preparing',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });
      mockClient.getMicrosoftOAuthOperation = vi.fn().mockResolvedValue({
        operationId: 'op-ui-test',
        status: 'awaiting-signin',
        authorizationUrl: authUrl,
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      fireEvent.click(screen.getByTestId('add-account-btn-proj-1'));
      fireEvent.click(screen.getByTestId('tab-microsoft-365'));

      // Fill valid GUIDs
      fireEvent.change(screen.getByTestId('m365-displayname-input'), { target: { value: 'Pilot M365' } });
      fireEvent.change(screen.getByTestId('m365-email-input'), { target: { value: 'pilot@contoso.example' } });
      fireEvent.change(screen.getByTestId('m365-clientid-input'), { target: { value: '11111111-2222-3333-4444-555555555555' } });
      fireEvent.change(screen.getByTestId('m365-tenantid-input'), { target: { value: tenantId } });

      await act(async () => {
        fireEvent.click(screen.getByTestId('submit-m365-connect-btn'));
      });

      // Awaiting signin rendered
      await waitFor(() => {
        expect(screen.getByTestId('m365-status-awaiting-signin')).toBeDefined();
      });

      // Check desktop browser guidance
      expect(
        screen.getByText(/Giriş bağlantısı bu bilgisayardaki tarayıcıda açılmalıdır/i)
      ).toBeDefined();
      expect(
        screen.getByText(/Telefon veya farklı bir cihazdaki tarayıcıda giriş tamamlanamaz/i)
      ).toBeDefined();

      // Check safe link anchor attributes
      const link = screen.getByTestId('m365-authorization-link');
      expect(link.getAttribute('href')).toBe(authUrl);
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toBe('noreferrer noopener');

      // Cancel button present
      expect(screen.getByTestId('cancel-m365-oauth-btn')).toBeDefined();
    });
  });

  describe('6. Reconnect with accountId & expectedVersion, Immutable Identity, and Disclaimer', () => {
    const mockM365Account: ImapAccountPublicDto = {
      accountId: 'acc-m365-01',
      companyId: 'comp-1',
      projectId: 'proj-1',
      displayName: 'M365 Kurumsal',
      email: 'user@contoso.com',
      host: 'outlook.office365.com',
      port: 993,
      tlsMode: 'ssl',
      username: 'user@contoso.com',
      authKind: 'microsoft365',
      tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      clientId: '11111111-2222-3333-4444-555555555555',
      oauthStatus: 'reauthorization_required',
      version: 3,
      createdAtUtc: '2026-09-13T12:00:00Z',
      updatedAtUtc: '2026-09-13T12:00:00Z',
    };

    it('displays Microsoft 365 badge and reauthorization_required warning in accounts table', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([mockM365Account]);

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      await waitFor(() => {
        expect(screen.getByTestId('account-row-acc-m365-01')).toBeDefined();
      });

      expect(screen.getByTestId('m365-badge-acc-m365-01')).toBeDefined();
      expect(screen.getByTestId('reauth-badge-acc-m365-01')).toBeDefined();
      expect(screen.getByTestId('reconnect-account-acc-m365-01')).toBeDefined();
    });

    it('performs reconnect passing accountId and expectedVersion with immutable identity', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([mockM365Account]);
      mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
        operationId: 'op-reconnect-1',
        status: 'preparing',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      await waitFor(() => {
        expect(screen.getByTestId('reconnect-account-acc-m365-01')).toBeDefined();
      });

      fireEvent.click(screen.getByTestId('reconnect-account-acc-m365-01'));

      // Check modal shows immutable details
      expect(screen.getAllByText(/user@contoso.com/)).toHaveLength(2);
      expect(screen.getByText(/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/)).toBeDefined();
      expect(screen.getByText(/11111111-2222-3333-4444-555555555555/)).toBeDefined();

      // Click start reconnect
      await act(async () => {
        fireEvent.click(screen.getByTestId('start-reconnect-btn'));
      });

      expect(mockClient.startMicrosoftOAuth).toHaveBeenCalledWith({
        companyId: 'comp-1',
        projectId: 'proj-1',
        displayName: 'M365 Kurumsal',
        email: 'user@contoso.com',
        clientId: '11111111-2222-3333-4444-555555555555',
        tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        accountId: 'acc-m365-01',
        expectedVersion: 3,
      });
    });

    it('updates only displayName on Microsoft 365 edit (no host/port/tls/pass changes)', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([mockM365Account]);
      mockClient.updateAccount = vi.fn().mockResolvedValue({
        ...mockM365Account,
        displayName: 'Yeni Görünen Ad',
        version: 4,
      });

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      await waitFor(() => {
        expect(screen.getByTestId('edit-account-acc-m365-01')).toBeDefined();
      });

      fireEvent.click(screen.getByTestId('edit-account-acc-m365-01'));

      // Verify no password, host, or port fields are editable
      expect(screen.queryByTestId('edit-account-password-input')).toBeNull();
      expect(screen.queryByTestId('edit-account-host-input')).toBeNull();

      // Update displayName
      fireEvent.change(screen.getByTestId('edit-account-name-input'), {
        target: { value: 'Yeni Görünen Ad' },
      });

      await act(async () => {
        fireEvent.click(screen.getByTestId('submit-edit-account-btn'));
      });

      expect(mockClient.updateAccount).toHaveBeenCalledWith('acc-m365-01', {
        displayName: 'Yeni Görünen Ad',
        expectedVersion: 3,
        companyId: 'comp-1',
        projectId: 'proj-1',
      });
    });

    it('displays Yerel bağlantıyı kaldır label with disclaimer that Microsoft grant is NOT revoked', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([mockM365Account]);

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      await waitFor(() => {
        expect(screen.getByTestId('delete-account-acc-m365-01')).toBeDefined();
      });

      // Verify button says "Yerel bağlantıyı kaldır"
      const delBtn = screen.getByTestId('delete-account-acc-m365-01');
      expect(delBtn.textContent).toContain('Yerel bağlantıyı kaldır');

      fireEvent.click(delBtn);

      // Verify modal text has disclaimer
      expect(
        screen.getByText(/Microsoft tarafında verilmiş olan izinleri iptal etmez/i)
      ).toBeDefined();
      expect(
        screen.getByText(/grant revocation yapılmaz/i)
      ).toBeDefined();
      expect(screen.getByTestId('confirm-delete-account-btn').textContent).toContain('Yerel bağlantıyı kaldır');
    });
  });

  describe('7. Source/Target Selector & reauthorization_required Guidance', () => {
    const mockM365Account: ImapAccountPublicDto = {
      accountId: 'acc-m365-01',
      companyId: 'comp-1',
      projectId: 'proj-1',
      displayName: 'Microsoft Pilot',
      email: 'pilot@m365.example',
      host: 'outlook.office365.com',
      port: 993,
      tlsMode: 'ssl',
      username: 'pilot@m365.example',
      authKind: 'microsoft365',
      tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      clientId: '11111111-2222-3333-4444-555555555555',
      version: 1,
      createdAtUtc: '2026-09-13T12:00:00Z',
      updatedAtUtc: '2026-09-13T12:00:00Z',
    };

    const mockImapAccount: ImapAccountPublicDto = {
      accountId: 'acc-imap-02',
      companyId: 'comp-1',
      projectId: 'proj-1',
      displayName: 'Hedef Arşiv',
      email: 'arsiv@yerel.test',
      host: 'mail.yerel.test',
      port: 993,
      tlsMode: 'ssl',
      username: 'arsiv@yerel.test',
      version: 1,
      createdAtUtc: '2026-09-13T12:00:00Z',
      updatedAtUtc: '2026-09-13T12:00:00Z',
    };

    it('renders connected Microsoft 365 accounts in source and target selectors', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([mockM365Account, mockImapAccount]);

      render(
        <ImapTransferWorkflow
          companyId="comp-1"
          projectId="proj-1"
          companyName="Acme Corp"
          projectName="Proje 1"
          client={mockClient}
        />
      );

      await waitFor(() => {
        const sourceSelect = screen.getByTestId('source-account-select') as HTMLSelectElement;
        expect(sourceSelect.options.length).toBe(3); // Default + 2 accounts
      });

      const sourceSelect = screen.getByTestId('source-account-select') as HTMLSelectElement;
      expect(sourceSelect.textContent).toContain('Microsoft Pilot');
      expect(sourceSelect.textContent).toContain('Microsoft 365 [OAuth2]');

      const targetSelect = screen.getByTestId('target-account-select') as HTMLSelectElement;
      expect(targetSelect.options.length).toBe(3);
      expect(targetSelect.textContent).toContain('Microsoft Pilot');
      expect(targetSelect.textContent).toContain('Microsoft 365 [OAuth2]');
    });

    it('displays reauthorization_required guidance banner preserving resume context', async () => {
      const mockReauthAccount: ImapAccountPublicDto = {
        ...mockM365Account,
        oauthStatus: 'reauthorization_required',
      };

      mockClient.listAccounts = vi.fn().mockResolvedValue([mockReauthAccount, mockImapAccount]);

      render(
        <ImapTransferWorkflow
          companyId="comp-1"
          projectId="proj-1"
          companyName="Acme Corp"
          projectName="Proje 1"
          client={mockClient}
          initialJob={{
            jobId: 'job-transfer-123',
            clientContext: { companyId: 'comp-1', companyName: 'Acme', projectId: 'proj-1', projectName: 'P1' },
            sourceFileName: 'pilot@m365.example',
            targetFileName: 'arsiv@yerel.test',
            status: 'interrupted',
            stage: 'Interrupted(reauthorization_required)',
            itemsRead: 15,
            itemsWritten: 15,
            failedItems: 0,
            totalItems: 50,
            currentFolder: 'INBOX',
            percentComplete: 30,
            errorMessage: 'reauthorization_required: MsalUiRequiredException',
            createdAt: '2026-09-13T12:00:00Z',
          }}
        />
      );

      await waitFor(() => {
        expect(screen.getByTestId('reauth-required-banner')).toBeDefined();
      });

      expect(
        screen.getByText(/Microsoft 365 Yeniden Yetkilendirme Gerekli \(reauthorization_required\)/i)
      ).toBeDefined();
      expect(
        screen.getByText(/Mevcut aktarım günlüğü ve tamamlanan iletiler korunmuştur/i)
      ).toBeDefined();

      // Resume button is still present and preserved
      expect(screen.getByTestId('resume-transfer-btn')).toBeDefined();
    });
  });

  describe('8. Public DTO Secret Absence', () => {
    it('verifies ImapAccountPublicDto contains only safe fields and zero tokens/ciphers/secrets', () => {
      const safeDto: ImapAccountPublicDto = {
        accountId: 'acc-safe-01',
        companyId: 'comp-1',
        projectId: 'proj-1',
        displayName: 'Safe Public Account',
        email: 'user@example.test',
        host: 'outlook.office365.com',
        port: 993,
        tlsMode: 'ssl',
        username: 'user@example.test',
        authKind: 'microsoft365',
        tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        clientId: '11111111-2222-3333-4444-555555555555',
        oauthStatus: 'connected',
        version: 1,
        createdAtUtc: '2026-09-13T12:00:00Z',
        updatedAtUtc: '2026-09-13T12:00:00Z',
      };

      const keys = Object.keys(safeDto);
      expect(keys).not.toContain('password');
      expect(keys).not.toContain('accessToken');
      expect(keys).not.toContain('refreshToken');
      expect(keys).not.toContain('idToken');
      expect(keys).not.toContain('clientSecret');
      expect(keys).not.toContain('tokenCache');
      expect(keys).not.toContain('codeVerifier');
      expect(keys).not.toContain('cipher');
      expect(keys).not.toContain('state');
      expect(keys).not.toContain('secret');
    });
  });

  describe('9. TASK-016 Personal Outlook.com / Hotmail OAuth Mode', () => {
    const validClientGuid = '11111111-2222-3333-4444-555555555555';
    const personalAccount: ImapAccountPublicDto = {
      accountId: 'acc-personal-01',
      companyId: 'comp-1',
      projectId: 'proj-1',
      displayName: 'Kişisel Outlook Hesabım',
      email: 'personal.user@outlook.com',
      host: 'outlook.office365.com',
      port: 993,
      tlsMode: 'ssl',
      username: 'personal.user@outlook.com',
      authKind: 'microsoft365',
      tenantId: 'consumers',
      clientId: validClientGuid,
      version: 2,
      createdAtUtc: '2026-09-13T12:00:00Z',
      updatedAtUtc: '2026-09-13T12:00:00Z',
    };

    it('approves strict HTTPS login.microsoftonline.com with consumers path', () => {
      const validPersonalUrl = `https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize?client_id=${validClientGuid}&response_type=code`;
      const res = validateMicrosoftAuthorizationUrl(validPersonalUrl, 'consumers');
      expect(res.valid).toBe(true);
      expect(res.error).toBeUndefined();
    });

    it('denies wrong-authority (common, organizations, mismatched GUID, empty) via validateMicrosoftAuthorizationUrl', () => {
      // Rejects common authority
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/common/oauth2/v2.0/authorize', 'common').valid).toBe(false);
      // Rejects organizations authority
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize', 'organizations').valid).toBe(false);
      // Rejects common URL when consumers is expected
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/common/oauth2/v2.0/authorize', 'consumers').valid).toBe(false);
      // Rejects corporate GUID URL when consumers is expected
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/oauth2/v2.0/authorize', 'consumers').valid).toBe(false);
      // Rejects consumers URL when corporate GUID is expected
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize', 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee').valid).toBe(false);
      // Rejects empty or invalid strings
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize', '').valid).toBe(false);
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize', 'invalid-tenant').valid).toBe(false);

      // Rejects non-443 port
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com:8443/consumers/oauth2/v2.0/authorize', 'consumers').valid).toBe(false);
      // Allows explicit port 443
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com:443/consumers/oauth2/v2.0/authorize', 'consumers').valid).toBe(true);
      // Rejects userinfo
      expect(validateMicrosoftAuthorizationUrl('https://user:pass@login.microsoftonline.com/consumers/oauth2/v2.0/authorize', 'consumers').valid).toBe(false);
      // Rejects fragment
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize#frag', 'consumers').valid).toBe(false);
      // Rejects non-authorize path
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/consumers/oauth2/v2.0/token', 'consumers').valid).toBe(false);
      expect(validateMicrosoftAuthorizationUrl('https://login.microsoftonline.com/consumers/authorize', 'consumers').valid).toBe(false);
    });

    it('provides explicit corporate vs personal choice and verifies personal form has no tenant or password inputs', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([]);

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      // Open modal and switch to Microsoft 365
      fireEvent.click(screen.getByTestId('add-account-btn-proj-1'));
      fireEvent.click(screen.getByTestId('tab-microsoft-365'));

      // Both explicit choices are rendered with responsive wrapping
      const modeSelector = screen.getByTestId('m365-mode-selector');
      expect(modeSelector).toBeDefined();
      expect(modeSelector.className).toContain('m365-mode-selector-row');
      expect(modeSelector.style.flexWrap).toBe('wrap');

      const corporateBtn = screen.getByTestId('m365-choice-corporate');
      const personalBtn = screen.getByTestId('m365-choice-personal');
      expect(corporateBtn).toBeDefined();
      expect(personalBtn).toBeDefined();
      expect(corporateBtn.className).toContain('m365-mode-choice-btn');
      expect(personalBtn.className).toContain('m365-mode-choice-btn');
      expect(corporateBtn.style.whiteSpace).toBe('normal');
      expect(personalBtn.style.whiteSpace).toBe('normal');

      // Switch to Personal mode
      fireEvent.click(screen.getByTestId('m365-choice-personal'));

      // Personal form fields: display name, email, client ID ONLY
      expect(screen.getByTestId('m365-displayname-input')).toBeDefined();
      expect(screen.getByTestId('m365-email-input')).toBeDefined();
      expect(screen.getByTestId('m365-clientid-input')).toBeDefined();

      // STRICTLY VERIFY: No tenant input in personal mode
      expect(screen.queryByTestId('m365-tenantid-input')).toBeNull();

      // STRICTLY VERIFY: Zero password or TLS/host/port inputs exist
      expect(screen.queryByTestId('create-account-password-input')).toBeNull();
      expect(screen.queryByTestId('create-account-host-input')).toBeNull();
      expect(screen.queryByTestId('create-account-port-input')).toBeNull();
      expect(screen.queryByTestId('create-account-tls-select')).toBeNull();

      // Check Turkish setup copy for Personal Outlook contains all required security/pilot statements
      const personalGuide = screen.getByTestId('m365-personal-setup-guide');
      expect(personalGuide).toBeDefined();
      const guideText = personalGuide.textContent || '';
      expect(guideText).toContain('kişisel Microsoft hesaplarını destekleyen');
      expect(guideText).toContain('Client ID');
      expect(guideText).toContain('http://localhost');
      expect(guideText).toContain('temsilci izni');
      expect(guideText).toContain('IMAP.AccessAsUser.All');
      expect(guideText).toContain('İstemci parolası gerekmez');
      expect(guideText).toContain('PKCE');
      expect(guideText).toContain('tarayıcıda kullanıcı girişi');
      expect(guideText).toContain('POP ve IMAP');
      expect(guideText).toContain('BitigMail-Test');
      expect(guideText).toContain('mevcut kişisel e-postalarınıza erişmeden veya silme yapmadan');
    });

    it('submits real-shaped personal request sending tenantId consumers without password or tenant input', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([]);
      mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
        operationId: 'op-personal-start',
        status: 'preparing',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      fireEvent.click(screen.getByTestId('add-account-btn-proj-1'));
      fireEvent.click(screen.getByTestId('tab-microsoft-365'));
      fireEvent.click(screen.getByTestId('m365-choice-personal'));

      fireEvent.change(screen.getByTestId('m365-displayname-input'), { target: { value: 'Kişisel Hesabım' } });
      fireEvent.change(screen.getByTestId('m365-email-input'), { target: { value: 'pilot@hotmail.com' } });
      fireEvent.change(screen.getByTestId('m365-clientid-input'), { target: { value: validClientGuid } });

      await act(async () => {
        fireEvent.click(screen.getByTestId('submit-m365-connect-btn'));
      });

      // Crucial: sends consumers literal as tenantId, never asks user for tenant
      expect(mockClient.startMicrosoftOAuth).toHaveBeenCalledWith({
        companyId: 'comp-1',
        projectId: 'proj-1',
        displayName: 'Kişisel Hesabım',
        email: 'pilot@hotmail.com',
        clientId: validClientGuid,
        tenantId: 'consumers',
      });
    });

    it('guarantees mode-switch isolation: clears/cancels active OAuth session and blocks stale URLs/responses', async () => {
      let resolveStart: (val: any) => void;
      const startPromise = new Promise((resolve) => {
        resolveStart = resolve;
      });

      mockClient.listAccounts = vi.fn().mockResolvedValue([]);
      mockClient.startMicrosoftOAuth = vi.fn().mockReturnValue(startPromise);
      mockClient.cancelMicrosoftOAuthOperation = vi.fn().mockResolvedValue({
        operationId: 'op-corp-cancel',
        status: 'cancelled',
        message: 'Cancelled',
      });

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      fireEvent.click(screen.getByTestId('add-account-btn-proj-1'));
      fireEvent.click(screen.getByTestId('tab-microsoft-365'));

      // In Corporate mode, start operation
      fireEvent.change(screen.getByTestId('m365-displayname-input'), { target: { value: 'Corp Account' } });
      fireEvent.change(screen.getByTestId('m365-email-input'), { target: { value: 'corp@contoso.com' } });
      fireEvent.change(screen.getByTestId('m365-clientid-input'), { target: { value: validClientGuid } });
      fireEvent.change(screen.getByTestId('m365-tenantid-input'), { target: { value: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee' } });

      act(() => {
        fireEvent.click(screen.getByTestId('submit-m365-connect-btn'));
      });

      // User switches to Personal mode while corporate start is in flight
      act(() => {
        fireEvent.click(screen.getByTestId('m365-choice-personal'));
      });

      // Now late corporate start resolves
      await act(async () => {
        resolveStart!({
          operationId: 'op-corp-late',
          status: 'awaiting-signin',
          authorizationUrl: 'https://login.microsoftonline.com/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/oauth2/v2.0/authorize',
        });
      });

      // Crucial: Personal form is rendered in idle state, stale corporate URL is not rendered
      expect(screen.queryByTestId('m365-status-awaiting')).toBeNull();
      expect(screen.queryByTestId('m365-auth-url-link')).toBeNull();
      expect(screen.getByTestId('m365-displayname-input')).toBeDefined();
    });

    it('renders saved personal account with personal badge and labels, and reconnects with consumers authority', async () => {
      mockClient.listAccounts = vi.fn().mockResolvedValue([personalAccount]);
      mockClient.startMicrosoftOAuth = vi.fn().mockResolvedValue({
        operationId: 'op-personal-reconnect',
        status: 'preparing',
        createdAtUtc: '2026-09-13T12:00:00Z',
        expiresAtUtc: '2026-09-13T12:05:00Z',
      });

      render(
        <ProjectImapAccounts companyId="comp-1" projectId="proj-1" projectName="Proje 1" client={mockClient} />
      );

      await waitFor(() => {
        expect(screen.getByTestId('account-row-acc-personal-01')).toBeDefined();
      });

      // Check badge displays "Kişisel Outlook"
      const badge = screen.getByTestId('m365-badge-acc-personal-01');
      expect(badge.textContent).toBe('Kişisel Outlook');

      // Check tenant display indicates personal consumers
      expect(screen.getByText(/\(Kişisel \/ consumers\)/i)).toBeDefined();

      // Open Reconnect modal
      fireEvent.click(screen.getByTestId('reconnect-account-acc-personal-01'));

      // Modal title and labels clearly identify Personal Outlook
      expect(screen.getByText('Kişisel Outlook Hesabını Yeniden Yetkilendir')).toBeDefined();
      const personalLabels = screen.getAllByText(/Kişisel Outlook\.com \/ Hotmail/i);
      expect(personalLabels.length).toBeGreaterThanOrEqual(1);
      expect(personalLabels[0]).toBeDefined();

      // Click reconnect
      await act(async () => {
        fireEvent.click(screen.getByTestId('start-reconnect-btn'));
      });

      expect(mockClient.startMicrosoftOAuth).toHaveBeenCalledWith({
        companyId: 'comp-1',
        projectId: 'proj-1',
        displayName: 'Kişisel Outlook Hesabım',
        email: 'personal.user@outlook.com',
        clientId: validClientGuid,
        tenantId: 'consumers',
        accountId: 'acc-personal-01',
        expectedVersion: 2,
      });
    });

    it('clearly labels personal and corporate accounts in both source and target transfer selectors', async () => {
      const corporateAccount: ImapAccountPublicDto = {
        accountId: 'acc-corp-02',
        companyId: 'comp-1',
        projectId: 'proj-1',
        displayName: 'Kurumsal Şirket',
        email: 'admin@corp.example',
        host: 'outlook.office365.com',
        port: 993,
        tlsMode: 'ssl',
        username: 'admin@corp.example',
        authKind: 'microsoft365',
        tenantId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        clientId: validClientGuid,
        version: 1,
        createdAtUtc: '2026-09-13T12:00:00Z',
        updatedAtUtc: '2026-09-13T12:00:00Z',
      };

      mockClient.listAccounts = vi.fn().mockResolvedValue([personalAccount, corporateAccount]);

      render(
        <ImapTransferWorkflow
          companyId="comp-1"
          projectId="proj-1"
          companyName="Acme Corp"
          projectName="Proje 1"
          client={mockClient}
        />
      );

      await waitFor(() => {
        const sourceSelect = screen.getByTestId('source-account-select') as HTMLSelectElement;
        expect(sourceSelect.options.length).toBe(3);
      });

      const sourceSelect = screen.getByTestId('source-account-select') as HTMLSelectElement;
      const targetSelect = screen.getByTestId('target-account-select') as HTMLSelectElement;

      // Both selectors clearly label personal from tenantId consumers
      expect(sourceSelect.textContent).toContain('Kişisel Outlook [OAuth2]');
      expect(sourceSelect.textContent).toContain('Microsoft 365 [OAuth2] (Kurumsal)');

      expect(targetSelect.textContent).toContain('Kişisel Outlook [OAuth2]');
      expect(targetSelect.textContent).toContain('Microsoft 365 [OAuth2] (Kurumsal)');
    });
  });
});
