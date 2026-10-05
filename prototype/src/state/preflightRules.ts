import {
  Message,
  PreflightCheck,
  PreflightState,
  TransferPlan,
} from '../types';
import { TARGET_OPTIONS } from '../data/sampleSources';
import { computePlanHash } from './storage';

export function getFilteredMessages(
  allMessages: Message[],
  plan: TransferPlan
): Message[] {
  let filtered = allMessages.filter((m) => m.folderId === plan.sourceFolderId);

  // Year filter
  if (plan.filters.year !== 'all') {
    const targetYear = parseInt(plan.filters.year, 10);
    filtered = filtered.filter((m) => m.year === targetYear);
  }

  // Attachment filter
  if (plan.filters.attachment === 'with') {
    filtered = filtered.filter((m) => m.hasAttachment);
  } else if (plan.filters.attachment === 'without') {
    filtered = filtered.filter((m) => !m.hasAttachment);
  }

  // Search term (subject, sender, body)
  if (plan.filters.searchTerm.trim()) {
    const term = plan.filters.searchTerm.trim().toLowerCase();
    filtered = filtered.filter(
      (m) =>
        m.subject.toLowerCase().includes(term) ||
        m.sender.toLowerCase().includes(term) ||
        m.body.toLowerCase().includes(term)
    );
  }

  // Sender filter
  if (plan.filters.sender.trim()) {
    const senderQuery = plan.filters.sender.trim().toLowerCase();
    filtered = filtered.filter(
      (m) =>
        m.sender.toLowerCase().includes(senderQuery) ||
        m.senderEmail.toLowerCase().includes(senderQuery)
    );
  }

  // Min size
  if (plan.filters.minSizeBytes !== null && plan.filters.minSizeBytes > 0) {
    filtered = filtered.filter((m) => m.sizeBytes >= (plan.filters.minSizeBytes as number));
  }

  // Max size
  if (plan.filters.maxSizeBytes !== null && plan.filters.maxSizeBytes > 0) {
    filtered = filtered.filter((m) => m.sizeBytes <= (plan.filters.maxSizeBytes as number));
  }

  // Individual selection mode
  if (plan.manualSelectionMode) {
    const selectedSet = new Set(plan.selectedMessageIds);
    filtered = filtered.filter((m) => selectedSet.has(m.id));
  }

  return filtered;
}

export function evaluatePreflight(
  allMessages: Message[],
  plan: TransferPlan
): PreflightState {
  const scopeMessages = getFilteredMessages(allMessages, plan);
  const scopeCount = scopeMessages.length;
  const scopeBytes = scopeMessages.reduce((sum, m) => sum + m.sizeBytes, 0);

  const target = TARGET_OPTIONS.find((t) => t.name === plan.targetType) || TARGET_OPTIONS[0];
  const maxLimit = target.maxItemSizeBytes;

  const duplicates = scopeMessages.filter((m) => m.isDuplicate);
  const oversizedItems = scopeMessages.filter((m) => m.sizeBytes > maxLimit);
  const primaryOversized = oversizedItems[0];

  const checks: PreflightCheck[] = [
    {
      key: 'source_access',
      title: 'Kaynak erişimi',
      status: 'passed',
      resultLabel: 'Uygun',
    },
    {
      key: 'target_permission',
      title: 'Hedef yazma izni',
      status: 'passed',
      resultLabel: 'Uygun',
    },
    {
      key: 'folder_mapping',
      title: 'Klasör eşlemesi',
      status: 'passed',
      resultLabel: 'Uygun',
    },
    {
      key: 'target_capacity',
      title: 'Hedef kapasitesi',
      status: 'passed',
      resultLabel: 'Yeterli',
    },
  ];

  // Zero-scope guard
  if (scopeCount === 0) {
    checks.push({
      key: 'scope_guard',
      title: 'Aktarım kapsamı',
      status: 'error',
      resultLabel: 'Aktarılacak ileti bulunamadı',
      detail: 'Mevcut filtreler veya manuel seçim sonucu 0 ileti kapsam dahilinde. Ön kontrol onaylanamaz.',
    });

    return {
      planHash: computePlanHash(plan),
      checkedAt: new Date().toISOString(),
      isStale: false,
      hasBlocker: true,
      resolved: false,
      checks,
      duplicateCount: 0,
      scopeCount: 0,
      scopeBytes: 0,
    };
  }

  // Duplicate check
  if (duplicates.length > 0) {
    let label = `${duplicates.length} ileti atlanacak`;
    if (plan.duplicatePolicy === 'overwrite') {
      label = `${duplicates.length} ileti üzerine yazılacak`;
    } else if (plan.duplicatePolicy === 'separate_folder') {
      label = `${duplicates.length} ileti ayrı klasöre aktarılacak`;
    }
    checks.push({
      key: 'duplicate_policy',
      title: 'Yinelenen iletiler',
      status: 'passed',
      resultLabel: label,
    });
  } else {
    checks.push({
      key: 'duplicate_policy',
      title: 'Yinelenen iletiler',
      status: 'passed',
      resultLabel: 'Yinelenen yok',
    });
  }

  // Size limit blocker check
  let hasBlocker = false;
  let resolved = true;

  if (oversizedItems.length > 0) {
    if (plan.oversizedResolution === 'none') {
      checks.push({
        key: 'item_size',
        title: 'İleti boyutu',
        status: 'warning',
        resultLabel: `${oversizedItems.length} ileti için karar gerekli`,
        detail: `Hedef sunucu sınırı (${target.maxItemSizeFormatted}) aşıldı.`,
      });
      hasBlocker = true;
      resolved = false;
    } else if (plan.oversizedResolution === 'skip_and_report') {
      checks.push({
        key: 'item_size',
        title: 'İleti boyutu',
        status: 'passed',
        resultLabel: `${oversizedItems.length} ileti atlanacak ve raporlanacak`,
        detail: 'Büyük boyutlu ileti atlama kararı kaydedildi.',
      });
      hasBlocker = false;
      resolved = true;
    } else if (plan.oversizedResolution === 'change_target') {
      checks.push({
        key: 'item_size',
        title: 'İleti boyutu',
        status: 'warning',
        resultLabel: 'Hedef değiştirme seçildi',
        detail: 'Hedef yapılandırmasına yönlendiriliyor.',
      });
      hasBlocker = true;
      resolved = false;
    }
  } else {
    checks.push({
      key: 'item_size',
      title: 'İleti boyutu',
      status: 'passed',
      resultLabel: 'Uygun',
    });
  }

  return {
    planHash: computePlanHash(plan),
    checkedAt: new Date().toISOString(),
    isStale: false,
    hasBlocker,
    resolved,
    checks,
    oversizedItem: primaryOversized,
    duplicateCount: duplicates.length,
    scopeCount,
    scopeBytes,
  };
}
