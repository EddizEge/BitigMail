import { useState, useEffect, useCallback, useRef } from 'react';
import {
  LocalEngineClient,
  localEngineClient,
  validateMicrosoftAuthorizationUrl,
} from '../api/localEngineClient';
import {
  MicrosoftOAuthState,
  StartMicrosoftOAuthRequest,
  MicrosoftOAuthOperationStatusDto,
} from '../types/localEngine';

export interface UseMicrosoftOAuthOptions {
  companyId?: string | null;
  projectId?: string | null;
  client?: LocalEngineClient;
  pollIntervalMs?: number;
  maxPollAttempts?: number;
  onConnected?: (accountId: string) => void;
}

export interface MicrosoftOAuthSessionState {
  operationId: string | null;
  status: MicrosoftOAuthState | 'idle';
  authorizationUrl: string | null;
  validatedUrl: string | null;
  urlValidationError: string | null;
  accountId: string | null;
  message: string | null;
  error: string | null;
  isStarting: boolean;
}

export function useMicrosoftOAuth(options: UseMicrosoftOAuthOptions) {
  const {
    companyId,
    projectId,
    client = localEngineClient,
    pollIntervalMs = 1500,
    maxPollAttempts = 220, // ~5.5 minutes with 1.5s intervals (backend TTL is 5 min)
    onConnected,
  } = options;

  const [session, setSession] = useState<MicrosoftOAuthSessionState>({
    operationId: null,
    status: 'idle',
    authorizationUrl: null,
    validatedUrl: null,
    urlValidationError: null,
    accountId: null,
    message: null,
    error: null,
    isStarting: false,
  });

  const isMountedRef = useRef<boolean>(true);
  const activeScopeRef = useRef<{ companyId?: string | null; projectId?: string | null }>({
    companyId,
    projectId,
  });
  const generationRef = useRef<number>(0);
  const activeOpIdRef = useRef<string | null>(null);
  const pollTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const expectedTenantRef = useRef<string>('');
  const isStartingRef = useRef<boolean>(false);

  // Clear polling timer safely
  const clearPollTimer = useCallback(() => {
    if (pollTimerRef.current) {
      clearTimeout(pollTimerRef.current);
      pollTimerRef.current = null;
    }
  }, []);

  // Cleanup helper for memory and active operation
  const cleanupOperation = useCallback((cancelBackend = false) => {
    clearPollTimer();
    const opId = activeOpIdRef.current;
    const currentScope = activeScopeRef.current;
    activeOpIdRef.current = null;
    expectedTenantRef.current = '';
    isStartingRef.current = false;

    if (cancelBackend && opId && currentScope.companyId && currentScope.projectId) {
      client
        .cancelMicrosoftOAuthOperation(opId, currentScope.companyId, currentScope.projectId)
        .catch(() => {});
    }
  }, [clearPollTimer, client]);

  // Track mount / unmount
  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      cleanupOperation(true);
    };
  }, [cleanupOperation]);

  // Cancel & clean on scope change; stale op cannot update new scope
  useEffect(() => {
    const prevScope = activeScopeRef.current;
    if (prevScope.companyId !== companyId || prevScope.projectId !== projectId) {
      // Scope has changed: cancel any in-flight operation from previous scope
      cleanupOperation(true);
      activeScopeRef.current = { companyId, projectId };
      generationRef.current += 1;

      // Reset state in React memory
      setSession({
        operationId: null,
        status: 'idle',
        authorizationUrl: null,
        validatedUrl: null,
        urlValidationError: null,
        accountId: null,
        message: null,
        error: null,
        isStarting: false,
      });
    }
  }, [companyId, projectId, cleanupOperation]);

  // Polling loop
  const pollOperation = useCallback(
    async (opId: string, tenantId: string, gen: number, attempt = 0) => {
      if (
        !isMountedRef.current ||
        gen !== generationRef.current ||
        activeOpIdRef.current !== opId ||
        !companyId ||
        !projectId
      ) {
        return;
      }

      if (attempt >= maxPollAttempts) {
        setSession((prev) => ({
          ...prev,
          status: 'expired',
          authorizationUrl: null,
          validatedUrl: null,
          message: 'Oturum açma işlemi zaman aşımına uğradı (5 dk).',
        }));
        cleanupOperation(true);
        return;
      }

      try {
        const statusDto: MicrosoftOAuthOperationStatusDto = await client.getMicrosoftOAuthOperation(
          opId,
          companyId,
          projectId
        );

        if (!isMountedRef.current || gen !== generationRef.current || activeOpIdRef.current !== opId) {
          return;
        }

        const rawStatus = statusDto.status;
        const authUrl = rawStatus === 'awaiting-signin' ? statusDto.authorizationUrl ?? null : null;

        // Strict URL validation if awaiting signin
        let validUrl: string | null = null;
        let urlErr: string | null = null;
        if (authUrl) {
          const validation = validateMicrosoftAuthorizationUrl(authUrl, tenantId);
          if (validation.valid) {
            validUrl = authUrl;
          } else {
            urlErr = validation.error || 'Geçersiz yetkilendirme adresi.';
          }
        }

        setSession((prev) => ({
          operationId: opId,
          status: rawStatus,
          // Authorization URL exists only during awaiting-signin in React memory
          authorizationUrl: authUrl,
          validatedUrl: validUrl,
          urlValidationError: urlErr,
          accountId: statusDto.accountId ?? null,
          message: statusDto.message ?? null,
          error: rawStatus === 'failed' ? (statusDto.message || 'Oturum açılamadı.') : (rawStatus === 'connected' ? null : prev.error),
          isStarting: false,
        }));

        // If terminal state, stop polling
        if (
          rawStatus === 'connected' ||
          rawStatus === 'cancelled' ||
          rawStatus === 'expired' ||
          rawStatus === 'failed'
        ) {
          activeOpIdRef.current = null;
          clearPollTimer();
          if (rawStatus === 'connected' && onConnected) {
            onConnected(statusDto.accountId || '');
          }
          return;
        }

        // Schedule next poll
        pollTimerRef.current = setTimeout(() => {
          pollOperation(opId, tenantId, gen, attempt + 1);
        }, pollIntervalMs);
      } catch (err: any) {
        if (!isMountedRef.current || gen !== generationRef.current || activeOpIdRef.current !== opId) {
          return;
        }

        // If 404/not found or conflict, mark as failed safely
        setSession((prev) => ({
          ...prev,
          status: 'failed',
          authorizationUrl: null,
          validatedUrl: null,
          error: err?.message || 'İşlem durumu sorgulanamadı.',
        }));
        cleanupOperation(false);
      }
    },
    [
      companyId,
      projectId,
      client,
      maxPollAttempts,
      pollIntervalMs,
      onConnected,
      clearPollTimer,
      cleanupOperation,
    ]
  );

  // Start Microsoft OAuth Operation (deduplicates repeated clicks)
  const startOperation = useCallback(
    async (params: {
      displayName: string;
      email: string;
      clientId: string;
      tenantId: string;
      accountId?: string;
      expectedVersion?: number;
    }) => {
      if (!companyId || !projectId) {
        throw new Error('Müşteri kimliği ve proje kimliği zorunludur.');
      }

      // Deduplicate repeated clicks
      if (isStartingRef.current || session.isStarting) {
        return;
      }

      // Cancel any existing active operation first
      cleanupOperation(true);

      const curGen = ++generationRef.current;
      isStartingRef.current = true;
      expectedTenantRef.current = params.tenantId.trim();

      setSession({
        operationId: null,
        status: 'preparing',
        authorizationUrl: null,
        validatedUrl: null,
        urlValidationError: null,
        accountId: params.accountId ?? null,
        message: 'Microsoft 365 oturum açma işlemi hazırlanıyor...',
        error: null,
        isStarting: true,
      });

      const requestPayload: StartMicrosoftOAuthRequest = {
        companyId,
        projectId,
        displayName: params.displayName.trim(),
        email: params.email.trim(),
        clientId: params.clientId.trim(),
        tenantId: params.tenantId.trim(),
        accountId: params.accountId,
        expectedVersion: params.expectedVersion,
      };

      try {
        const res = await client.startMicrosoftOAuth(requestPayload);

        if (!isMountedRef.current || curGen !== generationRef.current) {
          // Scope changed or unmounted while start was in flight: cancel orphan
          client.cancelMicrosoftOAuthOperation(res.operationId, companyId, projectId).catch(() => {});
          return;
        }

        isStartingRef.current = false;
        activeOpIdRef.current = res.operationId;

        setSession((prev) => ({
          ...prev,
          operationId: res.operationId,
          status: (res.status as MicrosoftOAuthState) || 'preparing',
          isStarting: false,
        }));

        // Begin polling immediately
        pollOperation(res.operationId, params.tenantId.trim(), curGen, 0);
      } catch (err: any) {
        isStartingRef.current = false;
        if (!isMountedRef.current || curGen !== generationRef.current) {
          return;
        }

        const errMsg = err?.message || 'Microsoft 365 oturumu başlatılamadı.';
        setSession({
          operationId: null,
          status: 'failed',
          authorizationUrl: null,
          validatedUrl: null,
          urlValidationError: null,
          accountId: params.accountId ?? null,
          message: null,
          error: errMsg,
          isStarting: false,
        });
        throw err;
      }
    },
    [companyId, projectId, session.isStarting, client, cleanupOperation, pollOperation]
  );

  // Cancel the active operation
  const cancelOperation = useCallback(async () => {
    const opId = activeOpIdRef.current || session.operationId;
    const snapScope = { companyId, projectId };
    const snapGen = generationRef.current;
    const tenantId = expectedTenantRef.current;

    clearPollTimer();
    isStartingRef.current = false;

    if (!opId || !snapScope.companyId || !snapScope.projectId) {
      activeOpIdRef.current = null;
      setSession((prev) => ({
        ...prev,
        status: 'cancelled',
        authorizationUrl: null,
        validatedUrl: null,
        urlValidationError: null,
        message: 'İşlem iptal edildi.',
      }));
      return;
    }

    try {
      const cancelRes = await client.cancelMicrosoftOAuthOperation(
        opId,
        snapScope.companyId,
        snapScope.projectId
      );

      // Guard against unmount, scope change, or new operation generation started while cancel was in-flight
      if (
        !isMountedRef.current ||
        snapGen !== generationRef.current ||
        activeScopeRef.current.companyId !== snapScope.companyId ||
        activeScopeRef.current.projectId !== snapScope.projectId
      ) {
        return;
      }

      const authoritativeStatus = (cancelRes?.status as MicrosoftOAuthState) || 'cancelled';
      const authoritativeAccountId = cancelRes?.accountId ?? session.accountId ?? null;
      const message = cancelRes?.message || 'İşlem iptal edildi.';

      if (authoritativeStatus === 'connected') {
        activeOpIdRef.current = null;
        setSession((prev) => ({
          ...prev,
          status: 'connected',
          accountId: authoritativeAccountId,
          authorizationUrl: null,
          validatedUrl: null,
          urlValidationError: null,
          message,
          error: null,
          isStarting: false,
        }));
        if (onConnected && authoritativeAccountId) {
          onConnected(authoritativeAccountId);
        }
        return;
      }

      if (authoritativeStatus === 'failed') {
        activeOpIdRef.current = null;
        setSession((prev) => ({
          ...prev,
          status: 'failed',
          accountId: authoritativeAccountId,
          authorizationUrl: null,
          validatedUrl: null,
          urlValidationError: null,
          message,
          error: message || 'İşlem başarısız oldu.',
          isStarting: false,
        }));
        return;
      }

      if (authoritativeStatus === 'expired') {
        activeOpIdRef.current = null;
        setSession((prev) => ({
          ...prev,
          status: 'expired',
          accountId: authoritativeAccountId,
          authorizationUrl: null,
          validatedUrl: null,
          urlValidationError: null,
          message,
          error: null,
          isStarting: false,
        }));
        return;
      }

      // Authoritative cancelled
      activeOpIdRef.current = null;
      setSession((prev) => ({
        ...prev,
        status: 'cancelled',
        accountId: authoritativeAccountId,
        authorizationUrl: null,
        validatedUrl: null,
        urlValidationError: null,
        message,
        error: null,
        isStarting: false,
      }));
    } catch (err: any) {
      // On cancellation network/error, never claim success.
      // Reconcile using scoped GET if safe, or retain safe error and continue/restore polling so terminal state is not lost.
      if (
        !isMountedRef.current ||
        snapGen !== generationRef.current ||
        activeScopeRef.current.companyId !== snapScope.companyId ||
        activeScopeRef.current.projectId !== snapScope.projectId
      ) {
        return;
      }

      try {
        const statusDto = await client.getMicrosoftOAuthOperation(
          opId,
          snapScope.companyId,
          snapScope.projectId
        );

        if (
          !isMountedRef.current ||
          snapGen !== generationRef.current ||
          activeScopeRef.current.companyId !== snapScope.companyId ||
          activeScopeRef.current.projectId !== snapScope.projectId
        ) {
          return;
        }

        const reconciledStatus = statusDto.status;
        const reconciledAccountId = statusDto.accountId ?? session.accountId ?? null;
        const reconciledMessage = statusDto.message ?? null;

        if (reconciledStatus === 'connected') {
          activeOpIdRef.current = null;
          setSession((prev) => ({
            ...prev,
            status: 'connected',
            accountId: reconciledAccountId,
            authorizationUrl: null,
            validatedUrl: null,
            urlValidationError: null,
            message: reconciledMessage,
            error: null,
            isStarting: false,
          }));
          if (onConnected && reconciledAccountId) {
            onConnected(reconciledAccountId);
          }
          return;
        }

        if (
          reconciledStatus === 'cancelled' ||
          reconciledStatus === 'expired' ||
          reconciledStatus === 'failed'
        ) {
          activeOpIdRef.current = null;
          setSession((prev) => ({
            ...prev,
            status: reconciledStatus,
            accountId: reconciledAccountId,
            authorizationUrl: null,
            validatedUrl: null,
            urlValidationError: null,
            message: reconciledMessage,
            error: reconciledStatus === 'failed' ? (reconciledMessage || 'İşlem başarısız oldu.') : null,
            isStarting: false,
          }));
          return;
        }

        // Operation is still in flight on backend: retain safe error and restore polling so terminal state is not lost
        activeOpIdRef.current = opId;
        const cancelErrMsg = err?.message || 'İptal isteği sunucuya iletilemedi.';
        setSession((prev) => ({
          ...prev,
          error: `İptal işlemi başarısız: ${cancelErrMsg}. İşlem izlenmeye devam ediliyor.`,
        }));
        pollTimerRef.current = setTimeout(() => {
          pollOperation(opId, tenantId, snapGen, 0);
        }, pollIntervalMs);
      } catch (_getErr) {
        if (
          !isMountedRef.current ||
          snapGen !== generationRef.current ||
          activeScopeRef.current.companyId !== snapScope.companyId ||
          activeScopeRef.current.projectId !== snapScope.projectId
        ) {
          return;
        }

        // Both cancel and GET failed: retain safe error and restore polling so terminal state is not lost
        activeOpIdRef.current = opId;
        const cancelErrMsg = err?.message || 'İptal isteği sunucuya iletilemedi.';
        setSession((prev) => ({
          ...prev,
          error: `İptal işlemi başarısız: ${cancelErrMsg}. İşlem izlenmeye devam ediliyor.`,
        }));
        pollTimerRef.current = setTimeout(() => {
          pollOperation(opId, tenantId, snapGen, 0);
        }, pollIntervalMs);
      }
    }
  }, [
    session.operationId,
    session.accountId,
    companyId,
    projectId,
    client,
    clearPollTimer,
    onConnected,
    pollOperation,
  ]);

  // Reset session back to idle
  const resetSession = useCallback((cancelBackend = false) => {
    cleanupOperation(cancelBackend);
    generationRef.current += 1;
    setSession({
      operationId: null,
      status: 'idle',
      authorizationUrl: null,
      validatedUrl: null,
      urlValidationError: null,
      accountId: null,
      message: null,
      error: null,
      isStarting: false,
    });
  }, [cleanupOperation]);

  return {
    ...session,
    startOperation,
    cancelOperation,
    resetSession,
  };
}
