// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useImapAccounts } from '../hooks/useImapAccounts';
import { LocalEngineClient, ApiError } from '../api/localEngineClient';
import { ImapAccountPublicDto, ImapFolderDto } from '../types/localEngine';

describe('IMAP Account UI Boundary & CRUD Tests', () => {
  let mockClient: LocalEngineClient;

  beforeEach(() => {
    vi.restoreAllMocks();
    mockClient = new LocalEngineClient('http://127.0.0.1:6174');
  });

  it('performs account CRUD operations correctly', async () => {
    const mockAccounts: ImapAccountPublicDto[] = [
      {
        accountId: 'acc-1',
        companyId: 'comp-1',
        projectId: 'proj-1',
        displayName: 'Destek Hesabı',
        email: 'destek@sirket.com',
        host: 'imap.sirket.com',
        port: 993,
        tlsMode: 'ssl',
        username: 'destek@sirket.com',
        version: 1,
        createdAtUtc: '2026-09-13T12:00:00Z',
        updatedAtUtc: '2026-09-13T12:00:00Z',
      },
    ];

    mockClient.listAccounts = vi.fn().mockResolvedValue(mockAccounts);
    mockClient.createAccount = vi.fn().mockResolvedValue({
      ...mockAccounts[0],
      accountId: 'acc-2',
      displayName: 'Yeni Hesap',
      version: 1,
    });
    mockClient.updateAccount = vi.fn().mockResolvedValue({
      ...mockAccounts[0],
      displayName: 'Güncellenmiş Hesap',
      version: 2,
    });
    mockClient.deleteAccount = vi.fn().mockResolvedValue({ success: true, accountId: 'acc-1' });

    const { result } = renderHook(() =>
      useImapAccounts({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetch: false })
    );

    await act(async () => {
      await result.current.refreshAccounts();
    });
    expect(result.current.accounts).toHaveLength(1);
    expect(result.current.accounts[0].displayName).toBe('Destek Hesabı');

    // Create
    await act(async () => {
      const created = await result.current.createAccount({
        displayName: 'Yeni Hesap',
        email: 'yeni@sirket.com',
        host: 'imap.sirket.com',
        port: 993,
        tlsMode: 'ssl',
        username: 'yeni@sirket.com',
        password: 'secretPassword1',
      });
      expect(created.accountId).toBe('acc-2');
    });

    // Update
    await act(async () => {
      const updated = await result.current.updateAccount('acc-1', {
        expectedVersion: 1,
        displayName: 'Güncellenmiş Hesap',
      });
      expect(updated.version).toBe(2);
    });

    // Delete
    await act(async () => {
      await result.current.deleteAccount('acc-1', 2);
    });
    expect(mockClient.deleteAccount).toHaveBeenCalledWith('acc-1', 'comp-1', 'proj-1', 2);
  });

  it('captures expectedVersion and presents 409 conflict guidance', async () => {
    const conflictError = new ApiError('Hesap sürüm uyuşmazlığı: beklenen 1, güncel 2.', 409, {
      currentVersion: 2,
      expectedVersion: 1,
    });
    conflictError.isConflict = true;
    conflictError.currentVersion = 2;
    conflictError.expectedVersion = 1;

    mockClient.listAccounts = vi.fn().mockResolvedValue([]);
    mockClient.updateAccount = vi.fn().mockRejectedValue(conflictError);

    const { result } = renderHook(() =>
      useImapAccounts({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetch: false })
    );

    let thrownError: any = null;
    await act(async () => {
      try {
        await result.current.updateAccount('acc-1', {
          expectedVersion: 1,
          displayName: 'Çakışan İsim',
        });
      } catch (err) {
        thrownError = err;
      }
    });

    expect(thrownError).toBeDefined();
    expect(result.current.conflictInfo).toBeDefined();
    expect(result.current.conflictInfo?.currentVersion).toBe(2);
    expect(result.current.conflictInfo?.expectedVersion).toBe(1);
    expect(result.current.conflictInfo?.message).toBe(conflictError.message);

    act(() => {
      result.current.clearConflict();
    });
    expect(result.current.conflictInfo).toBeNull();
  });

  it('proves stale prior-scope result cannot overwrite current scope', async () => {
    let resolveFirstCall: (val: ImapAccountPublicDto[]) => void;
    const firstCallPromise = new Promise<ImapAccountPublicDto[]>((resolve) => {
      resolveFirstCall = resolve;
    });

    const secondCallAccounts: ImapAccountPublicDto[] = [
      {
        accountId: 'acc-scope-2',
        companyId: 'comp-B',
        projectId: 'proj-B',
        displayName: 'Scope B Hesabı',
        email: 'b@example.test',
        host: 'imap.b.test',
        port: 993,
        tlsMode: 'ssl',
        username: 'b@example.test',
        version: 1,
        createdAtUtc: '2026-09-13T12:00:00Z',
        updatedAtUtc: '2026-09-13T12:00:00Z',
      },
    ];

    mockClient.listAccounts = vi.fn().mockImplementation((companyId: string) => {
      if (companyId === 'comp-A') {
        return firstCallPromise;
      }
      return Promise.resolve(secondCallAccounts);
    });

    // Start with Scope A
    const { result, rerender } = renderHook(
      ({ comp, proj }) => useImapAccounts({ companyId: comp, projectId: proj, client: mockClient, autoFetch: true }),
      { initialProps: { comp: 'comp-A', proj: 'proj-A' } }
    );

    expect(result.current.accounts).toEqual([]);

    // Scope changes to Scope B while Scope A request is still unresolved in flight
    rerender({ comp: 'comp-B', proj: 'proj-B' });

    // Wait for microtasks so Scope B resolves
    await act(async () => {
      await Promise.resolve();
    });

    expect(result.current.accounts).toEqual(secondCallAccounts);

    // Now Scope A finally finishes late
    await act(async () => {
      resolveFirstCall!([
        {
          accountId: 'acc-stale-A',
          companyId: 'comp-A',
          projectId: 'proj-A',
          displayName: 'Stale Account A',
          email: 'a@example.test',
          host: 'imap.a.test',
          port: 993,
          tlsMode: 'ssl',
          username: 'a@example.test',
          version: 1,
          createdAtUtc: '2026-09-13T12:00:00Z',
          updatedAtUtc: '2026-09-13T12:00:00Z',
        },
      ]);
    });

    // Crucial check: Stale Scope A response MUST NOT overwrite Scope B
    expect(result.current.accounts).toEqual(secondCallAccounts);
    expect(result.current.accounts[0].accountId).toBe('acc-scope-2');
  });

  it('handles exact Turkish folder paths and unreadable message counts', async () => {
    const mockFolders: ImapFolderDto[] = [
      {
        name: 'INBOX',
        fullPath: 'INBOX',
        delimiter: '/',
        isSelectable: true,
        messageCount: 12,
        unreadCount: 3,
        countValid: true,
        statusError: null,
      },
      {
        name: 'İstanbul',
        fullPath: 'İstanbul',
        delimiter: '/',
        isSelectable: true,
        messageCount: 5,
        unreadCount: 1,
        countValid: true,
        statusError: null,
      },
      {
        name: 'Arşiv',
        fullPath: 'Arşiv/2026/Gelen',
        delimiter: '/',
        isSelectable: true,
        messageCount: 150,
        unreadCount: 0,
        countValid: true,
        statusError: null,
      },
      {
        name: 'Sorunlu Klasör',
        fullPath: 'Sorunlu Klasör',
        delimiter: '/',
        isSelectable: true,
        messageCount: null,
        unreadCount: null,
        countValid: false,
        statusError: 'Klasör ileti sayısı okunamadı.',
      },
    ];

    mockClient.listAccountFolders = vi.fn().mockResolvedValue({
      accountId: 'acc-1',
      folders: mockFolders,
    });

    const { result } = renderHook(() =>
      useImapAccounts({ companyId: 'comp-1', projectId: 'proj-1', client: mockClient, autoFetch: false })
    );

    let loadedFolders: ImapFolderDto[] = [];
    await act(async () => {
      loadedFolders = await result.current.loadFolders('acc-1');
    });

    expect(loadedFolders).toHaveLength(4);
    // Exact Turkish folder paths
    expect(loadedFolders[1].fullPath).toBe('İstanbul');
    expect(loadedFolders[2].fullPath).toBe('Arşiv/2026/Gelen');

    // Unknown / unreadable count folder
    const unreadable = loadedFolders[3];
    expect(unreadable.fullPath).toBe('Sorunlu Klasör');
    expect(unreadable.countValid).toBe(false);
    expect(unreadable.messageCount).toBeNull();
    expect(unreadable.statusError).toBe('Klasör ileti sayısı okunamadı.');
  });
});
