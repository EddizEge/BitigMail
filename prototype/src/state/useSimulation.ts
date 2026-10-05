import { useEffect, useRef, useState, useCallback } from 'react';
import { ActiveRunState, Message, RunItemState, TransferPlan } from '../types';
import { loadSavedRun, saveActiveRun } from './storage';
import { getFilteredMessages } from './preflightRules';
import { getMessagesBySourceId } from '../data/sampleMessages';

export function createRunSnapshot(
  allMessages: Message[],
  plan: TransferPlan
): ActiveRunState {
  const scopeMessages = getFilteredMessages(allMessages, plan);

  // Build mutually exclusive deterministic run items
  const items: RunItemState[] = scopeMessages.map((m) => {
    return {
      messageId: m.id,
      subject: m.subject,
      sender: m.sender,
      sizeBytes: m.sizeBytes,
      sizeFormatted: m.sizeFormatted,
      status: 'pending',
    };
  });

  return {
    runId: `run-${Date.now()}`,
    jobId: 'job-1',
    status: 'running',
    startedAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    planSnapshot: JSON.parse(JSON.stringify(plan)),
    items,
    retryHistoryCount: 0,
  };
}

export function useSimulation(defaultMessages?: Message[]) {
  const [activeRun, setActiveRun] = useState<ActiveRunState | null>(() => {
    return loadSavedRun();
  });

  const intervalRef = useRef<number | null>(null);

  // Save active run changes to localStorage for refresh recovery
  useEffect(() => {
    saveActiveRun(activeRun);
  }, [activeRun]);

  // Clean interval on unmount
  useEffect(() => {
    return () => {
      if (intervalRef.current !== null) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    };
  }, []);

  const pauseSimulation = useCallback(() => {
    if (intervalRef.current !== null) {
      clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
    setActiveRun((prev) => {
      if (!prev || prev.status !== 'running') return prev;
      return {
        ...prev,
        status: 'paused',
        updatedAt: new Date().toISOString(),
      };
    });
  }, []);

  const resumeSimulation = useCallback(() => {
    setActiveRun((prev) => {
      if (!prev || prev.status !== 'paused') return prev;
      return {
        ...prev,
        status: 'running',
        updatedAt: new Date().toISOString(),
      };
    });
  }, []);

  const startSimulation = useCallback(
    (plan: TransferPlan, sourceMessages?: Message[]) => {
      if (intervalRef.current !== null) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
      const msgs =
        sourceMessages !== undefined
          ? sourceMessages
          : plan.sourceId
          ? getMessagesBySourceId(plan.sourceId)
          : defaultMessages || [];
      const newRun = createRunSnapshot(msgs, plan);
      setActiveRun(newRun);
    },
    [defaultMessages]
  );

  const retryFailedItems = useCallback(() => {
    setActiveRun((prev) => {
      if (!prev) return null;
      // Find failed items and transition them to transferred cleanly without double counting
      const updatedItems = prev.items.map((item) => {
        if (item.status === 'failed') {
          return {
            ...item,
            status: 'transferred' as const,
            reason: 'Yeniden deneme sonucu başarıyla aktarıldı',
            processedAt: new Date().toISOString(),
          };
        }
        return item;
      });

      return {
        ...prev,
        items: updatedItems,
        retryHistoryCount: prev.retryHistoryCount + 1,
        updatedAt: new Date().toISOString(),
      };
    });
  }, []);

  const resetSimulation = useCallback(() => {
    if (intervalRef.current !== null) {
      clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
    setActiveRun(null);
  }, []);

  // Main simulation tick loop
  useEffect(() => {
    if (!activeRun || activeRun.status !== 'running') {
      if (intervalRef.current !== null) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
      return;
    }

    // Single timer guard
    if (intervalRef.current !== null) {
      return;
    }

    intervalRef.current = window.setInterval(() => {
      setActiveRun((prevRun) => {
        if (!prevRun || prevRun.status !== 'running') {
          return prevRun;
        }

        const pendingIndex = prevRun.items.findIndex((i) => i.status === 'pending');
        if (pendingIndex === -1) {
          // All items processed -> complete run
          if (intervalRef.current !== null) {
            clearInterval(intervalRef.current);
            intervalRef.current = null;
          }
          return {
            ...prevRun,
            status: 'completed',
            completedAt: new Date().toISOString(),
            updatedAt: new Date().toISOString(),
          };
        }

        // Process batch of items deterministically (e.g. 8 items per tick for smooth fast simulation)
        const newItems = [...prevRun.items];
        const batchSize = Math.min(8, newItems.length - pendingIndex);

        for (let b = 0; b < batchSize; b++) {
          const idx = pendingIndex + b;
          if (idx >= newItems.length) break;

          const item = newItems[idx];
          const sourceMsgs = prevRun.planSnapshot?.sourceId
            ? getMessagesBySourceId(prevRun.planSnapshot.sourceId)
            : defaultMessages || [];
          const rawMsg = sourceMsgs.find((m) => m.id === item.messageId);

          if (rawMsg?.isDuplicate && prevRun.planSnapshot.duplicatePolicy === 'skip') {
            newItems[idx] = {
              ...item,
              status: 'skipped',
              reason: 'Yinelenen ileti atlandı (Atla kuralı)',
              processedAt: new Date().toISOString(),
            };
          } else if (rawMsg?.isOversized && prevRun.planSnapshot.oversizedResolution === 'skip_and_report') {
            newItems[idx] = {
              ...item,
              status: 'skipped',
              reason: 'Boyut sınırı aşıldı (38 MB > 35 MB) — Ön kontrol kararıyla atlandı',
              processedAt: new Date().toISOString(),
            };
          } else if (idx === 103 && prevRun.retryHistoryCount === 0) {
            // Exactly 1 deterministic failure on item 104 if not retried yet
            newItems[idx] = {
              ...item,
              status: 'failed',
              reason: 'Sunucu zaman aşımı (ETIMEDOUT: 504 Gateway Timeout)',
              processedAt: new Date().toISOString(),
            };
          } else {
            newItems[idx] = {
              ...item,
              status: 'transferred',
              reason: 'Hedef posta kutusuna başarıyla yazıldı',
              processedAt: new Date().toISOString(),
            };
          }
        }

        const stillPending = newItems.some((i) => i.status === 'pending');
        return {
          ...prevRun,
          status: stillPending ? 'running' : 'completed',
          completedAt: stillPending ? undefined : new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          items: newItems,
        };
      });
    }, 120);

    return () => {
      if (intervalRef.current !== null) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    };
  }, [activeRun?.status, defaultMessages]);

  // Derived counts from items (mutually exclusive)
  const items = activeRun?.items || [];
  const totalCount = items.length;
  const transferredCount = items.filter((i) => i.status === 'transferred').length;
  const skippedCount = items.filter((i) => i.status === 'skipped').length;
  const failedCount = items.filter((i) => i.status === 'failed').length;
  const pendingCount = items.filter((i) => i.status === 'pending').length;
  const processedCount = transferredCount + skippedCount + failedCount;
  const progressPercent = totalCount > 0 ? Math.round((processedCount / totalCount) * 100) : 0;

  return {
    activeRun,
    totalCount,
    processedCount,
    transferredCount,
    skippedCount,
    failedCount,
    pendingCount,
    progressPercent,
    startSimulation,
    pauseSimulation,
    resumeSimulation,
    retryFailedItems,
    resetSimulation,
  };
}

export function generateReportBlob(
  run: ActiveRunState,
  format: 'json' | 'csv'
): { blob: Blob; filename: string } {
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
  const items = run.items;
  const transferred = items.filter((i) => i.status === 'transferred').length;
  const skipped = items.filter((i) => i.status === 'skipped').length;
  const failed = items.filter((i) => i.status === 'failed').length;

  if (format === 'json') {
    const reportData = {
      reportType: 'BitigMail Sentetik Simülasyon Raporu',
      note: 'BU RAPOR YALNIZ SENTETİK ÖRNEK VERİLERLE ÜRETİLMİŞTİR. GERÇEK POSTA KUTUSU VERİSİ İÇERMEZ.',
      generatedAt: new Date().toISOString(),
      runId: run.runId,
      plan: {
        source: `${run.planSnapshot.sourceType} (${run.planSnapshot.sourceAccount})`,
        sourceFolder: run.planSnapshot.sourceFolderName,
        target: `${run.planSnapshot.targetType} (${run.planSnapshot.targetAccount})`,
        targetFolder: run.planSnapshot.targetFolderName,
        duplicatePolicy: run.planSnapshot.duplicatePolicy,
        filters: run.planSnapshot.filters,
      },
      summary: {
        totalItems: items.length,
        transferred,
        skipped,
        failed,
        allAccountedMutuallyExclusive: transferred + skipped + failed === items.length,
      },
      items: items.map((i, idx) => ({
        index: idx + 1,
        messageId: i.messageId,
        subject: i.subject,
        sender: i.sender,
        size: i.sizeFormatted,
        status: i.status,
        reason: i.reason || 'Normal işlem',
      })),
    };

    const blob = new Blob([JSON.stringify(reportData, null, 2)], {
      type: 'application/json;charset=utf-8',
    });
    return { blob, filename: `bitigmail-rapor-${timestamp}.json` };
  } else {
    // CSV format
    const lines: string[] = [];
    lines.push('# BITIGMAIL SENTETIK POSTA GECISI RAPORU');
    lines.push('# NOT: Bu rapor sentetik prototip ciktisidir. Gercek posta verisi icermez.');
    lines.push(`Uretim Tarihi,"${new Date().toLocaleString('tr-TR')}"`);
    lines.push(`Kaynak,"${run.planSnapshot.sourceType} - ${run.planSnapshot.sourceAccount} / ${run.planSnapshot.sourceFolderName}"`);
    lines.push(`Hedef,"${run.planSnapshot.targetType} - ${run.planSnapshot.targetAccount} / ${run.planSnapshot.targetFolderName}"`);
    lines.push(`Toplam,"${items.length}"`);
    lines.push(`Aktarilan,"${transferred}"`);
    lines.push(`Atlanan,"${skipped}"`);
    lines.push(`Basarisiz,"${failed}"`);
    lines.push('');
    lines.push('Sira,Ileti No,Gonderen,Konu,Boyut,Durum,Gerekce');

    items.forEach((item, index) => {
      const escape = (str: string) => `"${str.replace(/"/g, '""')}"`;
      lines.push(
        [
          index + 1,
          escape(item.messageId),
          escape(item.sender),
          escape(item.subject),
          escape(item.sizeFormatted),
          escape(item.status.toUpperCase()),
          escape(item.reason || ''),
        ].join(',')
      );
    });

    const blob = new Blob(['\uFEFF' + lines.join('\r\n')], {
      type: 'text/csv;charset=utf-8',
    });
    return { blob, filename: `bitigmail-rapor-${timestamp}.csv` };
  }
}
