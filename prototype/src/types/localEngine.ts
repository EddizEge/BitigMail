export interface OutlookStorageFormatInfo {
  isValidOutlookStorage: boolean;
  isOstSignature: boolean;
  isPstSignature: boolean;
  formatName: string;
  magicHex: string;
  clientMagic: number;
  clientMagicHex: string;
  clientMagicDescription: string;
  version: number;
  versionHex: string;
  versionDescription: string;
  authoritativeRuntimeFormat: string;
  authoritativeRuntimeValidation: string;
}

export interface FolderSummary {
  folderId: string;
  folderPath: string;
  displayName: string;
  itemCount: number;
  subFolderCount: number;
  category: 'Active' | 'Empty' | 'System';
  isIpmFolder: boolean;
}

export interface SelectedFolderSnapshot {
  folderId: string;
  folderPath: string;
  displayName: string;
}

export interface ConversionSelectionFilter {
  folderIds?: string[] | null;
  selectedFolders?: SelectedFolderSnapshot[] | null;
  startDate?: string | null;
  endDate?: string | null;
  timeZone: string;
  datePolicy: string;
}

export interface FolderSelectionSummary {
  folderId: string;
  folderPath: string;
  displayName: string;
  totalItems: number;
  selectedItems: number;
  isSelected: boolean;
}

export interface SelectionPreviewResult {
  selectionId: string;
  sourceHandle: string;
  sourceSha256: string;
  filters: ConversionSelectionFilter;
  totalSourceMessages: number;
  selectedMessagesCount: number;
  excludedMessagesCount: number;
  missingDateExcludedCount: number;
  selectedAttachmentsCount: number;
  folderBreakdown: FolderSelectionSummary[];
  canConvert: boolean;
  blockerReason?: string | null;
  blockReason?: string | null;
  dateFilterBlocked?: boolean;
  qualificationWarnings?: string[];
  advancedFilterFingerprint?: string | null;
  advancedFilterUnknownCount?: number;
  folderMappingFingerprint?: string;
  duplicatePolicy?: DuplicatePolicy;
  skippedDuplicateItemIds?: string[];
  executionPolicyFingerprint?: string;
}

export interface SampleMessageSummary {
  entryId: string;
  folderPath: string;
  subject: string;
  sender: string;
  displayTo: string;
  dateUtc: string;
  hasAttachments: boolean;
  attachmentCount: number;
}

export interface PreflightCheckResult {
  canConvert: boolean;
  hasTrialBlocker: boolean;
  trialBlockerReason?: string | null;
  blockers: string[];
  warnings: string[];
  estimatedPstSizeBytes: number;
  availableDiskSizeBytes: number;
  diskCapacityAvailable?: boolean | null;
  availableDiskBytes?: number | null;
  diskCapacityError?: string | null;
  estimatedRequiredBytes?: number | null;
  estimateBasis?: string | null;
}

export interface OstAnalysisResult {
  sourceFileName: string;
  sourceSizeBytes: number;
  sourceSha256: string;
  formatInfo: OutlookStorageFormatInfo;
  totalFolders: number;
  activeFoldersCount: number;
  emptyFoldersCount: number;
  systemFoldersCount: number;
  totalItems: number;
  totalAttachments: number;
  folders: FolderSummary[];
  sampleMessages: SampleMessageSummary[];
  preflight: PreflightCheckResult;
}

export interface ClientProjectContext {
  companyId: string;
  companyName: string;
  projectId: string;
  projectName: string;
}

export interface LocalJobRecord {
  recoveryOutcome?: string | null;
  recoveryOriginalTotal?: number | null;
  recoveryFailedBoundaryCount?: number;
  recoveryReportSha256?: string | null;
  sourceKind?: string;
  sourceSetFingerprint?: string;
  mimeImport?: MimeImportReportDetail | null;
  jobId: string;
  idempotencyKey?: string | null;
  clientContext: ClientProjectContext;
  sourceFileName: string;
  targetFileName: string;
  outputPath?: string | null;
  status: 'queued' | 'converting' | 'verifying' | 'completed' | 'failed' | 'interrupted';
  stage: string;
  itemsRead: number;
  itemsWritten: number;
  failedItems: number;
  totalItems: number;
  currentFolder: string;
  percentComplete: number;
  progressPhase?: string | null;
  phaseCompleted?: number | null;
  phaseTotal?: number | null;
  errorMessage?: string | null;
  isFiltered?: boolean;
  selectionFilter?: ConversionSelectionFilter | null;
  selectionId?: string | null;
  selectionContentHash?: string | null;
  totalSourceMessages?: number;
  selectedMessagesCount?: number;
  excludedMessagesCount?: number;
  missingDateExcludedCount?: number;
  selectedAttachmentsCount?: number;
  jobKind?: 'convert' | 'split' | 'mime-import' | 'imap-transfer' | string;
  splitMode?: 'year' | 'size' | string | null;
  splitSizeCapBytes?: number | null;
  outputDirectoryPath?: string | null;
  parts?: SplitPartReport[];
  imapTransfer?: ImapTransferReportDetail | null;
  bridgeTransfer?: BridgeTransferReportDetail | null;
  archiveId?: string | null;
  archiveName?: string | null;
  planId?: string | null;
  createdAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
  waitingAtShutdown?: boolean;
  neverStartedQueued?: boolean;
  waitingPriority?: number;
  advancedFilterFingerprint?: string | null;
  advancedFilterUnknownCount?: number;
}

export interface PagedJobsResult {
  items: LocalJobRecord[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface FilePickResult {
  cancelled: boolean;
  handle?: string | null;
  fileName?: string | null;
  displayPath?: string | null;
  sizeBytes?: number;
  error?: string | null;
}

export interface SplitYearGroupPreview {
  year: string;
  messageCount: number;
  attachmentCount: number;
  targetFileName: string;
}

export interface SplitPlanResult {
  planId: string;
  sourceHandle: string;
  sourceSha256: string;
  selectionId: string;
  splitMode: 'year' | 'size' | string;
  sizeCapBytes?: number | null;
  totalSourceMessages: number;
  selectedMessagesCount: number;
  excludedMessagesCount: number;
  selectedAttachmentsCount: number;
  yearGroups?: SplitYearGroupPreview[] | null;
  canSplit: boolean;
  blockerReason?: string | null;
  estimatedRequiredBytes?: number | null;
  estimatedPartCount?: number | null;
  estimateBasis?: string | null;
}

export interface SplitPartReport {
  partFileName: string;
  partFullPath: string;
  partSizeBytes: number;
  partSha256: string;
  itemsWritten: number;
  totalAttachmentsVerified: number;
  totalCidVerified: number;
  groupKey?: string | null;
  reopenedPstVerification: {
    verifiedWith: string;
    totalFoldersFound: number;
    totalPhysicalItemsFound: number;
    itemCountMatch: boolean;
    totalAttachmentsVerified: number;
    totalCidVerified: number;
    verificationNotes: string[];
  };
}

export interface ConversionReport {
  sourceSetFingerprint?: string;
  mimeImport?: MimeImportReportDetail | null;
  jobId: string;
  evidenceLabel: string;
  clientContext: ClientProjectContext;
  sourceFileName: string;
  sourceSizeBytes: number;
  sourceSha256Before: string;
  sourceSha256After: string;
  sourceHashMatch: boolean;
  outputPstFileName: string;
  outputPath?: string;
  outputPstSizeBytes: number;
  outputPstSha256: string;
  conversionSuccess: boolean;
  itemsRead: number;
  itemsWritten: number;
  failedItems: number;
  elapsedMilliseconds: number;
  totalFoldersProcessed: number;
  fidelityStatus: string;
  overallStatus: string;
  isFiltered?: boolean;
  selectionFilter?: ConversionSelectionFilter | null;
  totalSourceMessages?: number;
  selectedMessagesCount?: number;
  excludedMessagesCount?: number;
  missingDateExcludedCount?: number;
  selectedAttachmentsCount?: number;
  selectionId?: string | null;
  jobKind?: 'convert' | 'split' | 'mime-import' | 'imap-transfer' | string;
  splitMode?: 'year' | 'size' | string | null;
  splitSizeCapBytes?: number | null;
  outputDirectoryPath?: string | null;
  parts?: SplitPartReport[];
  imapTransfer?: ImapTransferReportDetail | null;
  bridgeTransfer?: BridgeTransferReportDetail | null;
  trialDifferences: {
    hasObservedTrialModifications: boolean;
    observedModificationsInThisRun: string[];
    observedSummary: string;
    documentedTrialCapabilitiesAndLimits: string[];
    watermarkPolicy: string;
  };
  unmeasuredFields: {
    status: string;
    fields: string[];
    note: string;
  };
  reopenedPstVerification: {
    verifiedWith: string;
    totalFoldersFound: number;
    totalPhysicalItemsFound: number;
    itemCountMatch: boolean;
    totalAttachmentsVerified: number;
    totalCidVerified: number;
    verificationNotes: string[];
  };
  errors: string[];
  warnings: string[];
}

export type MimeSourceMode = 'eml-files' | 'eml-tree' | 'mbox';
export interface AsposeSdkStatus { licenseState:string; initializationState:string; restartRequired:boolean; qualification:string; errorCode?:string|null; }
export interface EmlxPreviewResult { sourceHandle: string; sourceFingerprint: string; totalItems: number; totalBytes: number; qualification: string; warnings: string[]; canNormalize: boolean; }
export interface OutlookEmlPreviewResult { sourceHandle: string; sourceFormat: 'pst'|'ost'|'olm'; sourceSha256: string; sourceSizeBytes: number; qualification: string; canNormalize: boolean; }
export interface PopAccountPublicDto { accountId:string; companyId:string; projectId:string; displayName:string; email:string; host:string; port:number; tlsMode:string; username:string; version:number; createdAtUtc:string; updatedAtUtc:string; allowUnencryptedConnection?:boolean; }
export interface CreatePopAccountRequest { companyId:string; projectId:string; displayName:string; email:string; host:string; port:number; tlsMode:'ssl'|'starttls'|'none'; username:string; password:string; allowUnencryptedConnection?:boolean; }
export interface PopSnapshotPlan { planId:string; accountId:string; accountVersion:number; companyId:string; projectId:string; items:Array<{sequence:number;uidl:string;sizeBytes:number}>; snapshotSha256:string; createdAtUtc:string; }

export interface MimeAnalysisResult extends Omit<OstAnalysisResult, 'formatInfo'> {
  sourceHandle: string;
  sourceKind: MimeSourceMode;
  dialect: string;
  sourceFingerprint: string;
  physicalTotalItems: number;
  ignoredNonEmlFilesCount: number;
}

export interface MimeImportReportDetail {
  sdkQualification?: string;
  qualificationFingerprint?: string | null;
  dateFilterBlocked?: boolean;
  qualificationIsPartial?: boolean;
  qualificationWarnings?: string[];
  sourceKind: MimeSourceMode;
  dialect: string;
  sourceSetFingerprint: string;
  totalSourceEntries: number;
  importedMessagesCount: number;
  ignoredNonEmlFilesCount: number;
  folderCounts: Record<string, number>;
  overallQualification: string;
  observedDifferences: string[];
  restoredTrailingLfCount: number;
  checkedAttachmentsCount: number;
  checkedOriginalCidCount: number;
  generatedCidCount: number;
  messageDifferences: Array<{
    ordinal: number;
    messageId: string;
    mappedFolder: string;
    hasPlain: boolean;
    hasHtml: boolean;
    plainBodySha256?: string | null;
    htmlBodySha256?: string | null;
    attachmentCount: number;
    metadataAdditions: string[];
  }>;
}

// TASK-014 IMAP Account & Folder DTOs
export interface ImapAccountPublicDto {
  accountId: string;
  companyId: string;
  projectId: string;
  displayName: string;
  email: string;
  host: string;
  port: number;
  tlsMode: 'ssl' | 'starttls' | string;
  username: string;
  authKind?: 'password' | 'microsoft365' | 'google' | string;
  tenantId?: string | null;
  clientId?: string | null;
  oauthStatus?: 'connected' | 'reauthorization_required' | string | null;
  version: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  allowUnencryptedConnection?: boolean;
}

// TASK-015 Microsoft OAuth DTOs
export type MicrosoftOAuthState =
  | 'preparing'
  | 'awaiting-signin'
  | 'verifying'
  | 'connected'
  | 'cancelled'
  | 'expired'
  | 'failed';

export interface StartMicrosoftOAuthRequest {
  companyId: string;
  projectId: string;
  displayName: string;
  email: string;
  clientId: string;
  tenantId: string;
  accountId?: string;
  expectedVersion?: number;
}

export interface StartMicrosoftOAuthResponse {
  operationId: string;
  status: MicrosoftOAuthState | string;
  createdAtUtc: string;
  expiresAtUtc: string;
}

export interface MicrosoftOAuthOperationStatusDto {
  operationId: string;
  status: MicrosoftOAuthState;
  authorizationUrl?: string | null;
  accountId?: string | null;
  message?: string | null;
  createdAtUtc: string;
  expiresAtUtc: string;
}

export interface CancelMicrosoftOAuthRequest {
  companyId?: string;
  projectId?: string;
}

export interface CancelMicrosoftOAuthResponse {
  operationId: string;
  status: MicrosoftOAuthState | string;
  message: string;
  accountId?: string | null;
}


export interface CreateImapAccountRequest {
  companyId: string;
  projectId: string;
  displayName: string;
  email: string;
  host: string;
  port: number;
  tlsMode: 'ssl' | 'starttls' | 'none';
  allowUnencryptedConnection?: boolean;
  username: string;
  password?: string;
}

export interface UpdateImapAccountRequest {
  companyId: string;
  projectId: string;
  expectedVersion: number;
  displayName?: string;
  email?: string;
  host?: string;
  port?: number;
  tlsMode?: 'ssl' | 'starttls' | 'none';
  allowUnencryptedConnection?: boolean;
  username?: string;
  password?: string;
}

export interface TestImapConnectionRequest {
  accountId?: string;
  companyId?: string;
  projectId?: string;
  host?: string;
  port?: number;
  tlsMode?: string;
  allowUnencryptedConnection?: boolean;
  username?: string;
  password?: string;
}

export interface TestConnectionResult {
  success: boolean;
  message?: string;
  error?: string;
  latencyMs?: number;
  code?: string;
  accountId?: string;
}

export interface ImapFolderDto {
  name: string;
  fullPath: string;
  delimiter: string;
  isSelectable: boolean;
  messageCount?: number | null;
  unreadCount?: number | null;
  countValid: boolean;
  statusError?: string | null;
}

export interface ImapFolderListResponse {
  accountId: string;
  folders: ImapFolderDto[];
}

export interface AccountConflictErrorPayload {
  error: string;
  currentVersion: number;
  expectedVersion: number;
}

// TASK-014 IMAP Transfer Workflow DTOs
export interface ImapFolderMappingRequest {
  sourceFolderPath: string;
  targetFolderPath: string;
}

export interface ImapTransferPreviewRequest {
  companyName?: string;
  projectName?: string;
  companyId: string;
  projectId: string;
  sourceAccountId: string;
  targetAccountId: string;
  selectedFolders: ImapFolderMappingRequest[];
  startDate?: string | null;
  endDate?: string | null;
  advancedFilter?: MailFilterDefinition | null;
}

export interface ImapTransferFolderSummary {
  sourceFolder: string;
  targetFolder: string;
  sourceUidValidity: number;
  totalItems: number;
  eligibleItems: number;
  excludedCount: number;
}

export interface ImapTransferPreviewResponse {
  previewId: string;
  companyId: string;
  projectId: string;
  sourceAccountId: string;
  sourceAccountVersion: number;
  targetAccountId: string;
  targetAccountVersion: number;
  totalSourceItems: number;
  eligibleItemsCount: number;
  excludedCount: number;
  missingDateExcludedCount: number;
  deletedExcludedCount: number;
  folders: ImapTransferFolderSummary[];
  createdAtUtc: string;
  canTransfer: boolean;
  blockerReason?: string | null;
  advancedFilterFingerprint?: string | null;
  advancedFilterUnknownCount?: number;
}

export interface ImapTransferStartRequest {
  previewId: string;
  idempotencyKey: string;
  companyId?: string;
  projectId?: string;
  enqueueIfBusy?: boolean;
}

export interface ImapTransferResumeRequest {
  companyId?: string;
  projectId?: string;
  enqueueIfBusy?: boolean;
}

export interface ImapTransferItemAuditRecord {
  itemId: string;
  sourceFolder: string;
  sourceUid: number;
  targetFolder: string;
  targetUid?: number | null;
  bitigMailKeyword: string;
  status: string;
  sourceSha256: string;
  verifiedSha256?: string | null;
  originalMimeDateUtc?: string | null;
  internalDateUtc: string;
  errorMessage?: string | null;
}

export interface ImapTransferReportDetail {
  startDate?: string | null;
  endDate?: string | null;
  jobId: string;
  planId: string;
  sourceAccountId: string;
  sourceAccountVersion: number;
  targetAccountId: string;
  targetAccountVersion: number;
  totalPlanned: number;
  totalVerified: number;
  totalFailed: number;
  totalNeedsAttention: number;
  items: ImapTransferItemAuditRecord[];
}

// TASK-017 File <-> IMAP Account Bridge DTOs
export type BridgeDirection = 'file-to-imap' | 'imap-to-file';
export type BridgeExportFormat = 'eml-tree' | 'mboxrd';

export interface BridgeSourceFolderDescriptor {
  folderName: string;
  itemCount: number;
  totalSizeBytes: number;
}

export interface BridgeSourceDescriptorResponse {
  sourceHandle: string;
  sourceKind: string;
  dialect: string;
  displayPath: string;
  totalFiles: number;
  totalItems: number;
  totalSizeBytes: number;
  folders: BridgeSourceFolderDescriptor[];
  ignoredNonEmlFilesCount: number;
}

export interface BridgeFolderSummary {
  sourceFolder: string;
  targetFolder: string;
  totalItems: number;
  eligibleItems: number;
  excludedCount: number;
  missingDateExcludedCount: number;
  deletedExcludedCount: number;
}

export interface BridgeImportPreviewRequest {
  companyName?: string;
  projectName?: string;
  companyId: string;
  projectId: string;
  sourceHandle: string;
  targetAccountId: string;
  selectedFolders: string[];
  targetFolderMappings?: Record<string, string>;
  startDate?: string | null;
  endDate?: string | null;
  advancedFilter?: MailFilterDefinition | null;
}

export interface BridgeImportPreviewResponse {
  previewId: string;
  companyId: string;
  projectId: string;
  sourceHandle: string;
  sourceFingerprint: string;
  sourceKind: string;
  targetAccountId: string;
  targetAccountVersion: number;
  totalSourceItems: number;
  eligibleItemsCount: number;
  excludedCount: number;
  missingDateExcludedCount: number;
  folders: BridgeFolderSummary[];
  createdAtUtc: string;
  canTransfer: boolean;
  blockerReason?: string | null;
  advancedFilterFingerprint?: string | null;
  advancedFilterUnknownCount?: number;
}

export interface BridgeExportPreviewRequest {
  companyName?: string;
  projectName?: string;
  companyId: string;
  projectId: string;
  sourceAccountId: string;
  targetDirHandle: string;
  targetFormat: BridgeExportFormat | string;
  selectedFolders: string[];
  startDate?: string | null;
  endDate?: string | null;
  advancedFilter?: MailFilterDefinition | null;
}

export interface BridgeExportPreviewResponse {
  previewId: string;
  companyId: string;
  projectId: string;
  sourceAccountId: string;
  sourceAccountVersion: number;
  targetDirHandle: string;
  targetFormat: string;
  totalSourceItems: number;
  eligibleItemsCount: number;
  excludedCount: number;
  missingDateExcludedCount: number;
  deletedExcludedCount: number;
  folders: BridgeFolderSummary[];
  createdAtUtc: string;
  canTransfer: boolean;
  blockerReason?: string | null;
  dateFilterBlocked?: boolean;
  qualificationWarnings?: string[];
  estimatedRequiredBytes?: number | null;
  availableFreeBytes?: number | null;
  advancedFilterFingerprint?: string | null;
  advancedFilterUnknownCount?: number;
}

export type GoogleOAuthState = MicrosoftOAuthState;
export interface StartGoogleOAuthRequest {
  companyId: string; projectId: string; displayName: string; email: string;
  clientId: string; clientSecret: string; accountId?: string; expectedVersion?: number;
}
export interface StartGoogleOAuthResponse { operationId: string; status: GoogleOAuthState | string; createdAtUtc: string; expiresAtUtc: string; }
export interface ProviderDiagnosticDto { code: string; category: string; retryable: boolean; retryAfterSeconds?: number | null; message: string; }
export interface GoogleOAuthOperationStatusDto {
  operationId: string; status: GoogleOAuthState; authorizationUrl?: string | null; accountId?: string | null;
  message?: string | null; diagnostic?: ProviderDiagnosticDto | null; createdAtUtc: string; expiresAtUtc: string;
}
export type CancelGoogleOAuthResponse = CancelMicrosoftOAuthResponse;

export interface BridgeStartRequest {
  previewId: string;
  idempotencyKey: string;
  companyId?: string;
  projectId?: string;
  enqueueIfBusy?: boolean;
}

export interface BridgeResumeRequest {
  jobId?: string;
  companyId?: string;
  projectId?: string;
  enqueueIfBusy?: boolean;
}

export interface BridgeAuditItem {
  itemId: string;
  status: string;
  sourceIdentity: string;
  targetIdentity: string;
  sourceSha256: string;
  verifiedSha256?: string | null;
  originalMimeDateUtc?: string | null;
  internalDateUtc?: string | null;
  bitigMailKeyword?: string | null;
  convertedLfToCrLf: boolean;
  addedTerminalNewline: boolean;
  errorMessage?: string | null;
}

export interface BridgeTransferReportDetail {
  jobId: string;
  planId: string;
  direction: 'import' | 'export' | string;
  sourceIdentifier: string;
  targetIdentifier: string;
  targetFormat?: string | null;
  outputPath?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  totalPlanned: number;
  totalVerified: number;
  totalFailed: number;
  totalNeedsAttention: number;
  folders: BridgeFolderSummary[];
  items: BridgeAuditItem[];
  sidecarManifestPath?: string | null;
}

// TASK-018 Local Archive & Search DTOs
export interface ArchiveScopeTriple {
  companyId: string;
  projectId: string;
  archiveId: string;
}

export interface ArchiveCatalogItemDto {
  archiveId: string;
  archiveName: string;
  companyId: string;
  projectId: string;
  companyName?: string | null;
  projectName?: string | null;
  sourceKind: string;
  dialect: string;
  sourceFingerprint: string;
  totalItems: number;
  totalSizeBytes: number;
  truncatedItemsCount: number;
  status: 'indexing' | 'ready' | 'index_failed' | 'corrupted' | string;
  createdAtUtc: string;
  indexedAtUtc?: string | null;
  indexGeneration: number;
  folders?: ArchiveFolderSummary[];
}

export interface ArchiveFolderSummary {
  folderName: string;
  itemCount: number;
  totalSizeBytes: number;
}

export interface ArchiveIngestPreviewRequest {
  sourceHandle?: string | null;
  sourceJobId?: string | null;
  archiveName: string;
  companyId: string;
  projectId: string;
  companyName?: string | null;
  projectName?: string | null;
}

export interface ArchiveIngestPreviewResponse {
  previewId: string;
  archiveName: string;
  companyId: string;
  projectId: string;
  companyName?: string | null;
  projectName?: string | null;
  sourceKind: string;
  dialect: string;
  sourceFingerprint: string;
  totalFiles: number;
  totalItems: number;
  totalSizeBytes: number;
  folders: ArchiveFolderSummary[];
  canIngest: boolean;
  blockerReason?: string | null;
  estimatedRequiredBytes?: number | null;
  availableFreeBytes?: number | null;
  estimateBasis?: string | null;
  dateFilterBlocked?: boolean;
  qualificationIsPartial?: boolean;
  qualificationWarnings?: string[];
}

export interface ArchiveIngestStartRequest {
  previewId: string;
  idempotencyKey: string;
  enqueueIfBusy?: boolean;
}

export interface ArchiveIngestResumeRequest {
  jobId?: string;
  enqueueIfBusy?: boolean;
}

export type ArchiveSearchField = 'all' | 'subject' | 'sender' | 'recipient' | 'body' | 'attachmentName';

export interface ArchiveSearchRequest {
  selectedScopes: ArchiveScopeTriple[];
  query?: string | null;
  field?: ArchiveSearchField | string;
  startDate?: string | null;
  endDate?: string | null;
  hasAttachment?: boolean | null;
  folder?: string | null;
  page?: number;
  pageSize?: number;
  advancedFilter?: MailFilterDefinition | null;
}

export interface ArchiveSearchResultItem {
  messageId: string;
  archiveId: string;
  companyId: string;
  projectId: string;
  archiveName: string;
  originalFolder: string;
  sender: string;
  senderEmail?: string | null;
  recipients: string;
  subject: string;
  snippet: string;
  originalMimeDateUtc?: string | null;
  formattedDate: string;
  hasAttachments: boolean;
  attachmentNames: string[];
  sizeBytes: number;
  isBodyTruncated: boolean;
}

export interface ArchiveSearchResponse {
  totalCount: number;
  page: number;
  pageSize: number;
  items: ArchiveSearchResultItem[];
  indexHealthy: boolean;
  warning?: string | null;
  advancedFilterFingerprint?: string | null;
  advancedFilterUnknownCount?: number;
}

export interface ArchiveAttachmentInfo {
  fileName: string;
  contentType: string;
  sizeBytes: number | null;
  isInline: boolean;
  contentId?: string | null;
}

export interface ArchiveMessagePreviewRequest {
  messageId: string;
  searchRequest: ArchiveSearchRequest;
}

export interface ArchiveMessagePreviewResponse {
  messageId: string;
  archiveId: string;
  subject: string;
  from: string;
  to: string;
  cc?: string | null;
  bcc?: string | null;
  dateUtc?: string | null;
  messageIdHeader?: string | null;
  bodyText: string;
  isBodyTruncated: boolean;
  attachments: ArchiveAttachmentInfo[];
  rawSizeBytes: number;
  sha256: string;
}

export interface ArchiveReindexRequest {
  archiveId?: string;
}

export interface RecoveryPreview { sourceHandle:string; outputDirectoryHandle:string; sourceFileName:string; sourceSha256:string; mode:string; originalTotal:number|null; qualification:string; canStart:boolean; warnings:string[]; }
export interface RecoveryJob { jobId:string; sourceFileName:string; status:string; stage:string; outcome:string; originalTotal:number|null; recoveredCount:number; failedBoundaryCount:number; partial:boolean|null; reportAvailable:boolean; error?:string|null; }
export interface MailFilterNode { kind:'and'|'or'|'condition'; field?:'subject'|'body'|'sender'|'recipient'|'attachmentName'|'hasAttachment'|'size'|'date'; operator?:'eq'|'contains'|'gte'|'gt'|'lte'|'lt'; text?:string; number?:number; date?:string; boolean?:boolean; children?:MailFilterNode[]; }
export interface MailFilterDefinition { version:1; root:MailFilterNode; }
export interface FolderMappingRule { sourceFolderId:string; targetFolderPath:string; }
export type DuplicatePolicy = 'PreservePhysical'|'ContentOnly'|'ContentAndMetadata';
export interface ArchiveSelectedPlan { planId:string; mappingFingerprint:string; duplicatePolicy:DuplicatePolicy; frozen:{fingerprint:string;items:Array<{physicalItemId:string;rawContentSha256:string}>}; }
export interface TransferTemplate { version:1; name:string; canonicalFilterJson?:string|null; folderMappings:FolderMappingRule[]; duplicatePolicy:DuplicatePolicy; waitingPriority:number; }
export interface StoredTransferTemplate { templateId:string; template:TransferTemplate; createdAt:string; }

