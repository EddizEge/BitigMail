import { useState, useEffect, useCallback, useRef } from 'react';
import { localEngineClient, LocalEngineClient, ApiError } from '../api/localEngineClient';
import {
  ImapAccountPublicDto,
  CreateImapAccountRequest,
  UpdateImapAccountRequest,
  TestImapConnectionRequest,
  TestConnectionResult,
  ImapFolderDto,
} from '../types/localEngine';

export interface UseImapAccountsOptions {
  companyId?: string | null;
  projectId?: string | null;
  client?: LocalEngineClient;
  autoFetch?: boolean;
}

export interface AccountConflictInfo {
  accountId: string;
  currentVersion?: number;
  expectedVersion?: number;
  message: string;
}

export interface FolderInspectionState {
  loading: boolean;
  error: string | null;
  folders: ImapFolderDto[] | null;
  code?: string | null;
  apiError?: ApiError | null;
}

export function useImapAccounts(options: UseImapAccountsOptions) {
  const { companyId, projectId, client = localEngineClient, autoFetch = true } = options;

  const [accounts, setAccounts] = useState<ImapAccountPublicDto[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const [conflictInfo, setConflictInfo] = useState<AccountConflictInfo | null>(null);
  const [testResults, setTestResults] = useState<Record<string, TestConnectionResult>>({});
  const [testingAccountId, setTestingAccountId] = useState<string | null>(null);
  const [foldersState, setFoldersState] = useState<Record<string, FolderInspectionState>>({});

  const isMountedRef = useRef<boolean>(true);
  const generationRef = useRef<number>(0);
  const activeScopeRef = useRef<{ companyId?: string | null; projectId?: string | null }>({
    companyId,
    projectId,
  });

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  // Update active scope ref and advance generation counter on scope changes
  useEffect(() => {
    activeScopeRef.current = { companyId, projectId };
    generationRef.current += 1;
    // Stale responses from prior scope cannot apply to new scope
    setAccounts([]);
    setError(null);
    setConflictInfo(null);
    setFoldersState({});
    setTestResults({});
  }, [companyId, projectId]);

  const refreshAccounts = useCallback(async () => {
    const capturedScope = { companyId, projectId };
    const currentGen = ++generationRef.current;

    if (!companyId || !projectId) {
      setAccounts([]);
      setLoading(false);
      setError(null);
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const data = await client.listAccounts(companyId, projectId);
      if (
        isMountedRef.current &&
        generationRef.current === currentGen &&
        activeScopeRef.current.companyId === capturedScope.companyId &&
        activeScopeRef.current.projectId === capturedScope.projectId
      ) {
        setAccounts(data);
      }
    } catch (err: any) {
      if (
        isMountedRef.current &&
        generationRef.current === currentGen &&
        activeScopeRef.current.companyId === capturedScope.companyId &&
        activeScopeRef.current.projectId === capturedScope.projectId
      ) {
        setError(err?.message || 'Hesaplar yüklenirken hata oluştu.');
      }
    } finally {
      if (isMountedRef.current && generationRef.current === currentGen) {
        setLoading(false);
      }
    }
  }, [client, companyId, projectId]);

  useEffect(() => {
    if (autoFetch && companyId && projectId) {
      refreshAccounts();
    }
  }, [autoFetch, companyId, projectId, refreshAccounts]);

  const createAccount = useCallback(
    async (payload: Omit<CreateImapAccountRequest, 'companyId' | 'projectId'>): Promise<ImapAccountPublicDto> => {
      if (!companyId || !projectId) {
        throw new Error('Müşteri ve proje seçimi zorunludur.');
      }

      setError(null);
      setConflictInfo(null);
      try {
        const created = await client.createAccount({
          ...payload,
          companyId,
          projectId,
        });
        await refreshAccounts();
        return created;
      } catch (err: any) {
        setError(err?.message || 'Hesap oluşturulamadı.');
        throw err;
      }
    },
    [client, companyId, projectId, refreshAccounts]
  );

  const updateAccount = useCallback(
    async (accountId: string, payload: Omit<UpdateImapAccountRequest, 'companyId' | 'projectId'>): Promise<ImapAccountPublicDto> => {
      if (!companyId || !projectId) {
        throw new Error('Müşteri ve proje seçimi zorunludur.');
      }

      setError(null);
      setConflictInfo(null);
      try {
        const updated = await client.updateAccount(accountId, {
          ...payload,
          companyId,
          projectId,
        });
        await refreshAccounts();
        return updated;
      } catch (err: any) {
        if (err instanceof ApiError && err.isConflict) {
          const conflict: AccountConflictInfo = {
            accountId,
            currentVersion: err.currentVersion,
            expectedVersion: err.expectedVersion,
            message: err.message || 'Hesap başka bir işlem tarafından güncellendi. Lütfen yenileyip tekrar deneyin.',
          };
          setConflictInfo(conflict);
        } else {
          setError(err?.message || 'Hesap güncellenemedi.');
        }
        throw err;
      }
    },
    [client, companyId, projectId, refreshAccounts]
  );

  const deleteAccount = useCallback(
    async (accountId: string, expectedVersion?: number): Promise<void> => {
      if (!companyId || !projectId) {
        throw new Error('Müşteri ve proje seçimi zorunludur.');
      }

      setError(null);
      setConflictInfo(null);
      try {
        await client.deleteAccount(accountId, companyId, projectId, expectedVersion);
        await refreshAccounts();
      } catch (err: any) {
        if (err instanceof ApiError && err.isConflict) {
          const conflict: AccountConflictInfo = {
            accountId,
            currentVersion: err.currentVersion,
            expectedVersion: err.expectedVersion,
            message: err.message || 'Hesap sürümü değiştiği için silinemedi. Lütfen yenileyip tekrar deneyin.',
          };
          setConflictInfo(conflict);
        } else {
          setError(err?.message || 'Hesap silinemedi.');
        }
        throw err;
      }
    },
    [client, companyId, projectId, refreshAccounts]
  );

  const testConnection = useCallback(
    async (req: TestImapConnectionRequest): Promise<TestConnectionResult> => {
      return client.testConnection(req);
    },
    [client]
  );

  const testAccount = useCallback(
    async (accountId: string): Promise<TestConnectionResult> => {
      if (!companyId || !projectId) {
        throw new Error('Müşteri ve proje seçimi zorunludur.');
      }

      const capturedScope = { companyId, projectId };
      setTestingAccountId(accountId);
      try {
        const result = await client.testAccountConnection(accountId, companyId, projectId);
        if (
          isMountedRef.current &&
          activeScopeRef.current.companyId === capturedScope.companyId &&
          activeScopeRef.current.projectId === capturedScope.projectId
        ) {
          setTestResults((prev) => ({ ...prev, [accountId]: result }));
        }
        return result;
      } catch (err: any) {
        const errorMsg = err?.data?.error || err?.message || 'Bağlantı hatası';
        const code = err instanceof ApiError
          ? (err.data?.code || (err.status === 409 ? 'reauthorization_required' : undefined))
          : (err?.data?.code || err?.code);
        const failResult: TestConnectionResult = {
          success: false,
          error: errorMsg,
          code,
          accountId: err?.data?.accountId || accountId,
        };
        if (
          isMountedRef.current &&
          activeScopeRef.current.companyId === capturedScope.companyId &&
          activeScopeRef.current.projectId === capturedScope.projectId
        ) {
          setTestResults((prev) => ({ ...prev, [accountId]: failResult }));
        }
        return failResult;
      } finally {
        if (isMountedRef.current) {
          setTestingAccountId(null);
        }
      }
    },
    [client, companyId, projectId]
  );

  const loadFolders = useCallback(
    async (accountId: string): Promise<ImapFolderDto[]> => {
      if (!companyId || !projectId) {
        throw new Error('Müşteri ve proje seçimi zorunludur.');
      }

      const capturedScope = { companyId, projectId };
      setFoldersState((prev) => ({
        ...prev,
        [accountId]: { loading: true, error: null, folders: prev[accountId]?.folders || null },
      }));

      try {
        const res = await client.listAccountFolders(accountId, companyId, projectId);
        if (
          isMountedRef.current &&
          activeScopeRef.current.companyId === capturedScope.companyId &&
          activeScopeRef.current.projectId === capturedScope.projectId
        ) {
          setFoldersState((prev) => ({
            ...prev,
            [accountId]: { loading: false, error: null, folders: res.folders },
          }));
        }
        return res.folders;
      } catch (err: any) {
        const msg = err?.data?.error || err?.message || 'Klasörler alınırken hata oluştu.';
        const code = err instanceof ApiError
          ? (err.data?.code || (err.status === 409 ? 'reauthorization_required' : undefined))
          : (err?.data?.code || err?.code);
        if (
          isMountedRef.current &&
          activeScopeRef.current.companyId === capturedScope.companyId &&
          activeScopeRef.current.projectId === capturedScope.projectId
        ) {
          setFoldersState((prev) => ({
            ...prev,
            [accountId]: { loading: false, error: msg, folders: null, code, apiError: err instanceof ApiError ? err : null },
          }));
        }
        throw err;
      }
    },
    [client, companyId, projectId]
  );

  const clearConflict = useCallback(() => {
    setConflictInfo(null);
  }, []);

  return {
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
  };
}
