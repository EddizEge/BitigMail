import {
  ClientProjectContext,
  ConversionReport,
  FilePickResult,
  LocalJobRecord,
  PagedJobsResult,
  OstAnalysisResult,
  SelectionPreviewResult,
  SplitPlanResult,
  MimeAnalysisResult,
  MimeSourceMode,
  ImapAccountPublicDto,
  CreateImapAccountRequest,
  UpdateImapAccountRequest,
  TestImapConnectionRequest,
  TestConnectionResult,
  ImapFolderListResponse,
  ImapTransferPreviewRequest,
  ImapTransferPreviewResponse,
  ImapTransferStartRequest,
  StartMicrosoftOAuthRequest,
  StartMicrosoftOAuthResponse,
  MicrosoftOAuthOperationStatusDto,
  CancelMicrosoftOAuthResponse,
  StartGoogleOAuthRequest,
  StartGoogleOAuthResponse,
  GoogleOAuthOperationStatusDto,
  CancelGoogleOAuthResponse,
  BridgeSourceDescriptorResponse,
  BridgeImportPreviewRequest,
  BridgeImportPreviewResponse,
  BridgeExportPreviewRequest,
  BridgeExportPreviewResponse,
  BridgeStartRequest,
  ArchiveCatalogItemDto,
  ArchiveIngestPreviewRequest,
  ArchiveIngestPreviewResponse,
  ArchiveIngestStartRequest,
  ArchiveSearchRequest,
  ArchiveSearchResponse,
  ArchiveMessagePreviewRequest,
  ArchiveMessagePreviewResponse,
  PopAccountPublicDto,
  CreatePopAccountRequest,
  PopSnapshotPlan,
} from '../types/localEngine';

export class ApiError extends Error {
  public status: number;
  public data?: any;
  public isConflict?: boolean;
  public currentVersion?: number;
  public expectedVersion?: number;

  constructor(message: string, status: number, data?: any) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.data = data;
    if (status === 409) {
      this.isConflict = true;
      this.currentVersion = data?.currentVersion;
      this.expectedVersion = data?.expectedVersion;
    }
  }
}

export class LocalEngineClient {
  public async pickArchiveBackup():Promise<FilePickResult>{return this.mimePost('/api/picker/archive-backup',{});}
  public async createArchiveBackup(params:{scopes:Array<{companyId:string;projectId:string;archiveId:string}>;outputDirectoryHandle:string}):Promise<any>{return this.mimePost('/api/admin/archive/backup',params);}
  public async restoreArchiveBackup(params:{sourceHandle:string;companyId:string;projectId:string}):Promise<any>{return this.mimePost('/api/admin/archive/restore',params);}
  public async previewArchiveRetention(params:{companyId:string;projectId?:string|null;olderThanDays:number;largerThanBytes?:number|null}):Promise<any>{return this.mimePost('/api/admin/archive/retention-preview',params);}
  public async getAuditTrail():Promise<any>{await this.ensureSession();const res=await fetch(`${this.getBaseUrl()}/api/admin/audit`,{headers:this.getHeaders(false)});if(!res.ok)throw new Error(`Denetim kaydı alınamadı: HTTP ${res.status}`);return res.json();}
  public async getDeliveryReport(jobId:string):Promise<any>{await this.ensureSession();const res=await fetch(`${this.getBaseUrl()}/api/admin/jobs/${encodeURIComponent(jobId)}/delivery-report`,{headers:this.getHeaders(false)});if(!res.ok)throw new Error(`Teslim raporu alınamadı: HTTP ${res.status}`);return res.json();}
  public previewArchiveSelected(params:{searchRequest:ArchiveSearchRequest;selectedMessageIds:string[];folderMappings:import('../types/localEngine').FolderMappingRule[];duplicatePolicy:import('../types/localEngine').DuplicatePolicy;companyId:string;projectId:string}):Promise<import('../types/localEngine').ArchiveSelectedPlan>{return this.mimePost('/api/archive/selected/preview',params);}
  public startArchiveSelected(params:{planId:string;outputDirectoryHandle:string;clientContext:ClientProjectContext}):Promise<LocalJobRecord>{return this.mimePost('/api/archive/selected/start',params);}
  public async listTransferTemplates():Promise<import('../types/localEngine').StoredTransferTemplate[]>{await this.ensureSession();const res=await fetch(`${this.getBaseUrl()}/api/templates`,{headers:this.getHeaders(),cache:'no-store'});if(!res.ok)throw new Error('Şablonlar alınamadı.');return res.json();}
  public saveTransferTemplate(template:import('../types/localEngine').TransferTemplate):Promise<import('../types/localEngine').StoredTransferTemplate>{return this.mimePost('/api/templates',template);}
  public async applyTransferTemplate(id:string):Promise<{template:import('../types/localEngine').TransferTemplate;rePreviewRequired:boolean;liveSnapshotIncluded:boolean}>{await this.ensureSession();const res=await fetch(`${this.getBaseUrl()}/api/templates/${encodeURIComponent(id)}/apply`,{headers:this.getHeaders(),cache:'no-store'});if(!res.ok)throw new Error('Şablon uygulanamadı.');return res.json();}
  public previewRecovery(sourceHandle:string,outputDirectoryHandle:string):Promise<import('../types/localEngine').RecoveryPreview>{return this.mimePost('/api/recovery/preview',{sourceHandle,outputDirectoryHandle});}
  public startRecovery(sourceHandle:string,outputDirectoryHandle:string,expectedSourceSha256:string,clientContext:ClientProjectContext,timeoutMinutes=30):Promise<import('../types/localEngine').RecoveryJob>{return this.mimePost('/api/recovery/start',{sourceHandle,outputDirectoryHandle,expectedSourceSha256,clientContext,timeoutMinutes});}
  public async getRecoveryJob(jobId:string):Promise<import('../types/localEngine').RecoveryJob>{await this.ensureSession();const res=await fetch(`${this.getBaseUrl()}/api/recovery/jobs/${encodeURIComponent(jobId)}`,{headers:this.getHeaders(),cache:'no-store'});if(!res.ok)throw new Error('Kurtarma işi alınamadı.');return res.json();}
  public cancelRecovery(jobId:string):Promise<{cancelled:boolean}>{return this.mimePost(`/api/recovery/jobs/${encodeURIComponent(jobId)}/cancel`,{});}
  public async getRecoveryReport(jobId: string): Promise<Blob> {
    await this.ensureSession();
    const response = await fetch(`${this.getBaseUrl()}/api/recovery/jobs/${encodeURIComponent(jobId)}/report`, { headers: this.getHeaders(), cache: 'no-store' });
    if (!response.ok) throw new Error('Kurtarma raporu doğrulanamadı veya alınamadı.');
    return response.blob();
  }
  public async getSdkStatus():Promise<import('../types/localEngine').AsposeSdkStatus>{await this.ensureSession();const res=await fetch(`${this.getBaseUrl()}/api/sdk/status`,{headers:this.getHeaders(),cache:'no-store'});if(!res.ok)throw new Error('SDK durumu alınamadı.');return res.json();}
  public async selectSdkLicense():Promise<{cancelled:boolean;restartRequired:boolean}>{await this.ensureSession();const res=await fetch(`${this.getBaseUrl()}/api/sdk/license/select`,{method:'POST',headers:this.getHeaders()});if(!res.ok)throw new Error('Lisans yapılandırması kaydedilemedi.');return res.json();}
  private async mimePost<T>(path: string, body: unknown): Promise<T> {
    await this.ensureSession();
    const response = await fetch(`${this.getBaseUrl()}${path}`, {
      method: 'POST', headers: this.getHeaders(true), body: JSON.stringify(body), cache: 'no-store',
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.error || `İşlem başarısız: HTTP ${response.status}`);
    }
    return response.json();
  }

  public pickMimeSource(mode: MimeSourceMode): Promise<FilePickResult> {
    return this.mimePost('/api/picker/mime-source', { mode });
  }

  public pickEmlxSource(mode: 'tree' | 'files'): Promise<FilePickResult> { return this.mimePost('/api/picker/emlx-source', { mode }); }
  public previewEmlx(sourceHandle: string): Promise<import('../types/localEngine').EmlxPreviewResult> { return this.mimePost('/api/emlx/preview', { sourceHandle }); }
  public startEmlx(params: { sourceHandle: string; outputDirHandle: string; expectedSourceFingerprint: string; idempotencyKey: string; clientContext: ClientProjectContext; enqueueIfBusy?: boolean }): Promise<LocalJobRecord> { return this.mimePost('/api/emlx/start', params); }
  public pickOutlookEmlSource(): Promise<FilePickResult> { return this.mimePost('/api/picker/outlook-eml-source', {}); }
  public previewOutlookEml(sourceHandle: string): Promise<import('../types/localEngine').OutlookEmlPreviewResult> { return this.mimePost('/api/outlook-eml/preview', { sourceHandle }); }
  public startOutlookEml(params: { sourceHandle: string; outputDirHandle: string; expectedSourceSha256: string; idempotencyKey: string; clientContext: ClientProjectContext; enqueueIfBusy?: boolean }): Promise<LocalJobRecord> { return this.mimePost('/api/outlook-eml/start', params); }
  public createPopAccount(params: CreatePopAccountRequest): Promise<PopAccountPublicDto> { return this.mimePost('/api/pop/accounts', params); }
  public previewPop(params: {accountId:string;companyId:string;projectId:string}): Promise<PopSnapshotPlan> { return this.mimePost('/api/pop/preview', params); }
  public startPop(params: {planId:string;companyId:string;projectId:string;outputDirHandle:string;idempotencyKey:string;enqueueIfBusy?:boolean}): Promise<LocalJobRecord> { return this.mimePost('/api/pop/start', params); }
  public resumePop(jobId:string,companyId:string,projectId:string,enqueueIfBusy=true):Promise<LocalJobRecord> { return this.mimePost(`/api/pop/resume/${encodeURIComponent(jobId)}`,{companyId,projectId,enqueueIfBusy}); }
  public async listPopAccounts(companyId:string,projectId:string):Promise<PopAccountPublicDto[]> { await this.ensureSession(); const query=new URLSearchParams({companyId,projectId}); const res=await fetch(`${this.getBaseUrl()}/api/pop/accounts?${query}`,{headers:this.getHeaders(),cache:'no-store'}); if(!res.ok){const e=await res.json().catch(()=>null);throw new Error(e?.error||'POP hesapları alınamadı.')} return res.json(); }

  public analyzeMimeSource(sourceHandle: string): Promise<MimeAnalysisResult> {
    return this.mimePost('/api/mime/source/analyze', { sourceHandle });
  }

  public previewMimeSelection(params: { sourceHandle: string; folderIds?: string[]; startDate?: string | null; endDate?: string | null; advancedFilter?: import('../types/localEngine').MailFilterDefinition | null; folderMappings?: import('../types/localEngine').FolderMappingRule[]; duplicatePolicy?: import('../types/localEngine').DuplicatePolicy }): Promise<SelectionPreviewResult> {
    return this.mimePost('/api/mime/selection/preview', params);
  }

  public startMimeJob(params: { sourceHandle: string; targetHandle: string; selectionId: string; expectedSourceSha256: string; idempotencyKey: string; clientContext: ClientProjectContext; enqueueIfBusy?: boolean }): Promise<LocalJobRecord> {
    return this.mimePost('/api/mime/start', params);
  }
  private baseUrl: string;
  private sessionToken: string | null = null;
  // Oturum, ilk yönetici kurulumu ya da giriş ile (üretim kimliği) açıldıysa true; anonim geliştirme oturumunda false.
  private identitySession = false;

  constructor(baseUrl?: string) {
    const configuredUrl =
      baseUrl ||
      (typeof window !== 'undefined' && (window as any).__BITIGMAIL_ENGINE_URL__) ||
      'http://127.0.0.1:6174';
    this.baseUrl = configuredUrl.replace(/\/+$/, '');
  }

  public setBaseUrl(url: string) {
    this.baseUrl = url.replace(/\/+$/, '');
  }

  public getBaseUrl(): string {
    if (typeof window !== 'undefined' && (window as any).__BITIGMAIL_ENGINE_URL__) {
      return (window as any).__BITIGMAIL_ENGINE_URL__.replace(/\/+$/, '');
    }
    return this.baseUrl;
  }

  public hasToken(): boolean {
    return this.sessionToken !== null;
  }

  public clearSession(): void {
    this.sessionToken = null;
    this.identitySession = false;
  }

  /**
   * Motorun hazır olup olmadığını denetler. Üretim kimliğiyle oturum açılmışsa anonim oturum uçları
   * (/api/session, /api/session/status) motor tarafından bilerek kapalıdır; bu durumda kimlik ucu kullanılır.
   * Kimliksiz geliştirme / test oturumunda eski davranış korunur.
   */
  public async checkReady(options: { reuseSession?: boolean } = {}): Promise<void> {
    if (this.identitySession && this.sessionToken) {
      const res = await fetch(`${this.getBaseUrl()}/api/auth/me`, { headers: this.getHeaders(), cache: 'no-store' });
      if (!res.ok) throw new Error(`Servis durumu alınamadı: HTTP ${res.status}`);
      return;
    }
    if (options.reuseSession) {
      await this.getStatus().catch(async () => { await this.initSession(); await this.getStatus(); });
      return;
    }
    await this.initSession();
    await this.getStatus();
  }

  public async getSetupStatus():Promise<{initialized:boolean;nativeProofAvailable:boolean}>{const res=await fetch(`${this.getBaseUrl()}/api/setup/status`,{headers:this.getHeaders()});if(!res.ok)throw new Error(`Kurulum durumu alınamadı: HTTP ${res.status}`);return res.json();}
  public async setupFirstAdmin(userName:string,password:string,proof:string):Promise<any>{const res=await fetch(`${this.getBaseUrl()}/api/setup/first-admin`,{method:'POST',headers:this.getHeaders(true),body:JSON.stringify({userName,password,proof})});if(!res.ok){const e=await res.json().catch(()=>null);if(res.status===401)throw new Error('Güvenli kurulum kanıtının süresi doldu veya doğrulanamadı. Yeniden deneyin; sorun sürerse BitigMail uygulamasını kapatıp açın.');throw new Error(e?.error||'İlk yönetici oluşturulamadı.');}const data=await res.json();this.sessionToken=data.token;this.identitySession=true;return data;}
  public async login(userName:string,password:string):Promise<any>{const res=await fetch(`${this.getBaseUrl()}/api/auth/login`,{method:'POST',headers:this.getHeaders(true),body:JSON.stringify({userName,password})});if(!res.ok)throw new Error('Kullanıcı adı veya parola geçersiz.');const data=await res.json();this.sessionToken=data.token;this.identitySession=true;return data;}
  public async logout():Promise<void>{if(this.sessionToken)await fetch(`${this.getBaseUrl()}/api/auth/logout`,{method:'POST',headers:this.getHeaders(true),body:'{}'});this.sessionToken=null;this.identitySession=false;}
  public async getCurrentIdentity():Promise<any>{await this.ensureSession();const res=await fetch(`${this.getBaseUrl()}/api/auth/me`,{headers:this.getHeaders()});if(!res.ok)throw new Error('Oturum bilgisi alınamadı.');return res.json();}
  public async createCatalogCompany(name:string,projectName:string):Promise<any>{return this.mimePost('/api/catalog/companies',{name,projectName});}
  public async createCatalogProject(companyId:string,name:string):Promise<any>{return this.mimePost(`/api/catalog/companies/${encodeURIComponent(companyId)}/projects`,{name});}
  public async createLocalUser(userName:string,password:string,role:'Admin'|'Operator',grants:Array<{companyId:string;projectIds:string[]}> = []):Promise<any>{return this.mimePost('/api/admin/users',{userName,password,role,grants});}

  private getHeaders(isMutating: boolean = false): HeadersInit {
    const headers: Record<string, string> = {
      'Accept': 'application/json',
    };

    if (this.sessionToken) {
      headers['X-BitigMail-Session'] = this.sessionToken;
    }

    if (isMutating) {
      headers['Content-Type'] = 'application/json';
    }

    return headers;
  }

  public async initSession(): Promise<{ token: string; version: string }> {
    const res = await fetch(`${this.getBaseUrl()}/api/session`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({}),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Oturum başlatılamadı: HTTP ${res.status}`);
    }

    const data = await res.json();
    this.sessionToken = data.token;
    return data;
  }

  public async getStatus(): Promise<{ status: string; hasActiveSession: boolean }> {
    const res = await fetch(`${this.getBaseUrl()}/api/session/status`, {
      method: 'GET',
      headers: this.getHeaders(),
    });

    if (!res.ok) {
      throw new Error(`Servis durumu alınamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async pickSource(): Promise<FilePickResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/picker/source`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({}),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Kaynak seçici açılamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async pickTarget(sourceHandle?: string | null): Promise<FilePickResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/picker/target`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ sourceHandle: sourceHandle || null }),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Hedef seçici açılamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async analyzeSource(sourceHandle: string): Promise<OstAnalysisResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/source/analyze`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ sourceHandle }),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `OST analizi başarısız: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async getSelectionPreview(params: {
    sourceHandle: string;
    folderIds?: string[] | null;
    startDate?: string | null;
    endDate?: string | null;
  }): Promise<SelectionPreviewResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/source/selection/preview`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(params),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Filtre önizlemesi alınamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async startJob(params: {
    sourceHandle: string;
    targetHandle: string;
    selectionId?: string | null;
    idempotencyKey?: string;
    expectedSourceSha256?: string;
    estimatedTotalItems?: number;
    clientContext: ClientProjectContext;
    enqueueIfBusy?: boolean;
  }): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/jobs/start`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(params),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Dönüştürme başlatılamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async pickSplitSource(): Promise<FilePickResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/picker/split-source`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({}),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Bölümleme kaynak seçici açılamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async analyzeSplitSource(sourceHandle: string): Promise<OstAnalysisResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/split/source/analyze`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ sourceHandle }),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Kaynak analizi başarısız: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async pickOutputDir(): Promise<FilePickResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/picker/output-dir`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({}),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Hedef klasör seçici açılamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async createSplitPlan(params: {
    sourceHandle: string;
    selectionId?: string | null;
    splitMode: 'year' | 'size' | string;
    sizeCapBytes?: number | null;
    folderIds?: string[] | null;
    startDate?: string | null;
    endDate?: string | null;
  }): Promise<SplitPlanResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/split/plan`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(params),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Bölümleme planı oluşturulamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async startSplitJob(params: {
    planId: string;
    outputDirHandle: string;
    idempotencyKey?: string | null;
    clientContext?: ClientProjectContext | null;
    enqueueIfBusy?: boolean;
  }): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/split/start`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(params),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Bölümleme başlatılamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async getJob(jobId: string): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/jobs/${encodeURIComponent(jobId)}`, {
      method: 'GET',
      headers: this.getHeaders(),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `İş durumu alınamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async getJobReport(jobId: string): Promise<ConversionReport> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/jobs/${encodeURIComponent(jobId)}/report`, {
      method: 'GET',
      headers: this.getHeaders(),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Rapor alınamadı: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async getAllJobs(): Promise<LocalJobRecord[]> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/jobs`, {
      method: 'GET',
      headers: this.getHeaders(),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `İşler listelenemedi: HTTP ${res.status}`);
    }

    return res.json();
  }

  public async getJobsPage(params: { page?: number; pageSize?: number; status?: string; search?: string; jobKind?: string } = {}): Promise<PagedJobsResult> {
    // Preserve injected legacy clients used by embedders while production stays on the bounded endpoint.
    if (Object.prototype.hasOwnProperty.call(this, 'getAllJobs')) {
      const all = await this.getAllJobs();
      const page = Math.max(1, params.page ?? 1);
      const pageSize = Math.min(100, Math.max(1, params.pageSize ?? 50));
      const filtered = all.filter(job => {
        if (params.jobKind && job.jobKind !== params.jobKind) return false;
        if (params.status === 'active' && !['converting', 'verifying', 'queued'].includes(job.status)) return false;
        if (params.status && !['all', 'active'].includes(params.status) && job.status !== params.status) return false;
        return true;
      });
      return { items: filtered.slice((page - 1) * pageSize, page * pageSize), page, pageSize, totalCount: filtered.length };
    }
    await this.ensureSession();
    const query = new URLSearchParams({
      page: String(params.page ?? 1),
      pageSize: String(params.pageSize ?? 50),
    });
    if (params.status && params.status !== 'all') query.set('status', params.status);
    if (params.search?.trim()) query.set('search', params.search.trim());
    if (params.jobKind?.trim()) query.set('jobKind', params.jobKind.trim());
    const res = await fetch(`${this.getBaseUrl()}/api/jobs/page?${query}`, { method: 'GET', headers: this.getHeaders() });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `İş geçmişi alınamadı: HTTP ${res.status}`);
    }
    return res.json();
  }

  public async cancelPendingJob(jobId: string, companyId: string, projectId: string): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/jobs/${encodeURIComponent(jobId)}/cancel-pending`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ companyId, projectId }),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(err.error || `Bekleyen iş iptal edilemedi: HTTP ${res.status}`);
    }
    return res.json();
  }

  public async setWaitingPriority(jobId: string, priority: number, companyId: string, projectId: string): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/jobs/${encodeURIComponent(jobId)}/priority`, { method:'POST', headers:this.getHeaders(true), body:JSON.stringify({ priority, companyId, projectId }) });
    if (!res.ok) { const err=await res.json().catch(()=>({error:`HTTP ${res.status}`})); throw new Error(err.error || 'Bekleyen iş önceliği değiştirilemedi.'); }
    return res.json();
  }

  // TASK-014 IMAP Account APIs
  public async listAccounts(companyId: string, projectId: string): Promise<ImapAccountPublicDto[]> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/accounts?${query.toString()}`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Hesaplar listelenemedi: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async getAccount(accountId: string, companyId: string, projectId: string): Promise<ImapAccountPublicDto> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/accounts/${encodeURIComponent(accountId)}?${query.toString()}`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Hesap alınamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async createAccount(req: CreateImapAccountRequest): Promise<ImapAccountPublicDto> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/accounts`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Hesap oluşturulamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async updateAccount(accountId: string, req: UpdateImapAccountRequest): Promise<ImapAccountPublicDto> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/accounts/${encodeURIComponent(accountId)}`, {
      method: 'PUT',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Hesap güncellenemedi: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async deleteAccount(accountId: string, companyId: string, projectId: string, expectedVersion?: number): Promise<{ success: boolean; accountId: string }> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    if (expectedVersion !== undefined) {
      query.set('expectedVersion', expectedVersion.toString());
    }
    const res = await fetch(`${this.getBaseUrl()}/api/accounts/${encodeURIComponent(accountId)}?${query.toString()}`, {
      method: 'DELETE',
      headers: this.getHeaders(),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Hesap silinemedi: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async testConnection(req: TestImapConnectionRequest): Promise<TestConnectionResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/accounts/test`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    const data = await res.json().catch(() => ({ success: false, error: `HTTP ${res.status}` }));
    return data;
  }

  public async testAccountConnection(accountId: string, companyId: string, projectId: string): Promise<TestConnectionResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/accounts/${encodeURIComponent(accountId)}/test`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ companyId, projectId }),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Bağlantı sınanamadı: HTTP ${res.status}`, res.status, err);
    }

    const data = await res.json().catch(() => ({ success: false, error: `HTTP ${res.status}` }));
    return data;
  }

  public async listAccountFolders(accountId: string, companyId: string, projectId: string): Promise<ImapFolderListResponse> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/accounts/${encodeURIComponent(accountId)}/folders?${query.toString()}`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Klasör listesi alınamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  // TASK-014 IMAP Transfer endpoints
  public async createImapTransferPreview(req: ImapTransferPreviewRequest): Promise<ImapTransferPreviewResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/imap/preview`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Aktarım önizlemesi oluşturulamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async getImapTransferPreview(previewId: string, companyId: string, projectId: string): Promise<ImapTransferPreviewResponse> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/imap/preview/${encodeURIComponent(previewId)}?${query.toString()}`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Önizleme bulunamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async startImapTransfer(req: ImapTransferStartRequest): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/imap/start`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Aktarım başlatılamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async resumeImapTransfer(jobId: string, companyId: string, projectId: string, enqueueIfBusy = false): Promise<LocalJobRecord> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/imap/resume/${encodeURIComponent(jobId)}?${query.toString()}`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ companyId, projectId, enqueueIfBusy }),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Aktarım devam ettirilemedi: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  // TASK-017 Bridge Transfer endpoints (Vendor-free EML/MBOX <-> IMAP)
  public async describeMimeSource(sourceHandle: string): Promise<BridgeSourceDescriptorResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/bridge/source/describe`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ sourceHandle }),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Kaynak bilgisi alınamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async createBridgeImportPreview(req: BridgeImportPreviewRequest): Promise<BridgeImportPreviewResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/bridge/import/preview`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Köprü içe aktarım önizlemesi oluşturulamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async getBridgeImportPreview(previewId: string, companyId: string, projectId: string): Promise<BridgeImportPreviewResponse> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/bridge/import/preview/${encodeURIComponent(previewId)}?${query.toString()}`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Önizleme bulunamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async startBridgeImport(req: BridgeStartRequest): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/bridge/import/start`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Köprü içe aktarım başlatılamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async resumeBridgeImport(jobId: string, companyId: string, projectId: string, enqueueIfBusy = false): Promise<LocalJobRecord> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/bridge/import/resume/${encodeURIComponent(jobId)}?${query.toString()}`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ companyId, projectId, enqueueIfBusy }),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Köprü içe aktarım devam ettirilemedi: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async createBridgeExportPreview(req: BridgeExportPreviewRequest): Promise<BridgeExportPreviewResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/bridge/export/preview`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Köprü dışa aktarım önizlemesi oluşturulamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async getBridgeExportPreview(previewId: string, companyId: string, projectId: string): Promise<BridgeExportPreviewResponse> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/bridge/export/preview/${encodeURIComponent(previewId)}?${query.toString()}`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Önizleme bulunamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async startBridgeExport(req: BridgeStartRequest): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/bridge/export/start`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Köprü dışa aktarım başlatılamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async resumeBridgeExport(jobId: string, companyId: string, projectId: string, enqueueIfBusy = false): Promise<LocalJobRecord> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/transfer/bridge/export/resume/${encodeURIComponent(jobId)}?${query.toString()}`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ companyId, projectId, enqueueIfBusy }),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Köprü dışa aktarım devam ettirilemedi: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  // TASK-015 Microsoft OAuth endpoints
  public async startMicrosoftOAuth(req: StartMicrosoftOAuthRequest): Promise<StartMicrosoftOAuthResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/oauth/microsoft/start`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Microsoft 365 oturumu başlatılamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async getMicrosoftOAuthOperation(id: string, companyId: string, projectId: string): Promise<MicrosoftOAuthOperationStatusDto> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/oauth/microsoft/operations/${encodeURIComponent(id)}?${query.toString()}`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `İşlem durumu sorgulanamadı: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async cancelMicrosoftOAuthOperation(id: string, companyId: string, projectId: string): Promise<CancelMicrosoftOAuthResponse> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/oauth/microsoft/operations/${encodeURIComponent(id)}/cancel?${query.toString()}`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ companyId, projectId }),
      cache: 'no-store',
    });

    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `İşlem iptal edilemedi: HTTP ${res.status}`, res.status, err);
    }

    return res.json();
  }

  public async startGoogleOAuth(req: StartGoogleOAuthRequest): Promise<StartGoogleOAuthResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/oauth/google/start`, { method: 'POST', headers: this.getHeaders(true), body: JSON.stringify(req), cache: 'no-store' });
    if (!res.ok) { const err = await res.json().catch(() => null); throw new ApiError(err?.error || `Google oturumu başlatılamadı: HTTP ${res.status}`, res.status, err); }
    return res.json();
  }
  public async getGoogleOAuthOperation(id: string, companyId: string, projectId: string): Promise<GoogleOAuthOperationStatusDto> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/oauth/google/operations/${encodeURIComponent(id)}?${query}`, { headers: this.getHeaders(), cache: 'no-store' });
    if (!res.ok) { const err = await res.json().catch(() => null); throw new ApiError(err?.error || `Google işlem durumu sorgulanamadı: HTTP ${res.status}`, res.status, err); }
    return res.json();
  }
  public async cancelGoogleOAuthOperation(id: string, companyId: string, projectId: string): Promise<CancelGoogleOAuthResponse> {
    await this.ensureSession();
    const query = new URLSearchParams({ companyId, projectId });
    const res = await fetch(`${this.getBaseUrl()}/api/oauth/google/operations/${encodeURIComponent(id)}/cancel?${query}`, { method: 'POST', headers: this.getHeaders(true), body: JSON.stringify({ companyId, projectId }), cache: 'no-store' });
    if (!res.ok) { const err = await res.json().catch(() => null); throw new ApiError(err?.error || `Google işlemi iptal edilemedi: HTTP ${res.status}`, res.status, err); }
    return res.json();
  }

  // TASK-018 Local Archive & Search Endpoints
  public async pickArchiveSource(mode: 'eml-files' | 'eml-tree' | 'mbox'): Promise<FilePickResult> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/picker/archive-source`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ mode }),
      cache: 'no-store',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Arşiv kaynak seçici açılamadı: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async createArchivePreview(req: ArchiveIngestPreviewRequest): Promise<ArchiveIngestPreviewResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/ingest/preview`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Arşiv önizlemesi oluşturulamadı: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async getArchivePreview(previewId: string): Promise<ArchiveIngestPreviewResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/ingest/preview/${encodeURIComponent(previewId)}`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Arşiv önizleme bilgisi alınamadı: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async startArchiveIngest(req: ArchiveIngestStartRequest): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/ingest/start`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Arşivleme başlatılamadı: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async resumeArchiveIngest(jobId: string, enqueueIfBusy = false): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/ingest/resume/${encodeURIComponent(jobId)}`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ jobId, enqueueIfBusy }),
      cache: 'no-store',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Arşivleme devam ettirilemedi: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async getArchiveCatalog(): Promise<ArchiveCatalogItemDto[]> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/catalog`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Arşiv kataloğu listelenemedi: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async getArchiveManifest(archiveId: string): Promise<any> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/${encodeURIComponent(archiveId)}/manifest`, {
      method: 'GET',
      headers: this.getHeaders(),
      cache: 'no-store',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Arşiv manifestosu bulunamadı: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async searchArchive(req: ArchiveSearchRequest, signal?: AbortSignal): Promise<ArchiveSearchResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/search`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
      signal,
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Arşiv araması başarısız: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async previewArchiveMessage(req: ArchiveMessagePreviewRequest, signal?: AbortSignal): Promise<ArchiveMessagePreviewResponse> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/message/preview`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify(req),
      cache: 'no-store',
      signal,
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `İleti önizlemesi alınamadı: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async reindexArchive(archiveId: string, enqueueIfBusy = false): Promise<LocalJobRecord> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/${encodeURIComponent(archiveId)}/reindex`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({ archiveId, enqueueIfBusy }),
      cache: 'no-store',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Yeniden indeksleme başlatılamadı: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  public async reindexAllArchives(): Promise<{ success: boolean; message: string }> {
    await this.ensureSession();
    const res = await fetch(`${this.getBaseUrl()}/api/archive/reindex-all`, {
      method: 'POST',
      headers: this.getHeaders(true),
      body: JSON.stringify({}),
      cache: 'no-store',
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      throw new ApiError(err?.error || `Tüm arşivler yeniden indekslenemedi: HTTP ${res.status}`, res.status, err);
    }
    return res.json();
  }

  private async ensureSession(): Promise<void> {

    if (!this.sessionToken) {
      await this.initSession();
    }
  }
}

// Global default singleton instance
export const localEngineClient = new LocalEngineClient();

/**
 * Strict validator for Microsoft OAuth authorization URL.
 * URL must be HTTPS, host must be login.microsoftonline.com,
 * and first path segment must strictly match the canonical tenant GUID.
 */
export function validateMicrosoftAuthorizationUrl(
  url: string | null | undefined,
  expectedTenantId: string
): { valid: boolean; error?: string } {
  if (!url || typeof url !== 'string' || !url.trim()) {
    return { valid: false, error: 'Yetkilendirme bağlantısı boş veya geçersiz.' };
  }

  const cleanedExpected = expectedTenantId ? expectedTenantId.toLowerCase().trim() : '';
  const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
  if (cleanedExpected !== 'consumers' && !guidRegex.test(cleanedExpected)) {
    return { valid: false, error: 'Geçersiz kiracı kimliği veya yetki seçicisi.' };
  }

  try {
    const parsed = new URL(url.trim());
    if (parsed.protocol !== 'https:') {
      return { valid: false, error: 'Yetkilendirme bağlantısı HTTPS protokolü kullanmalıdır.' };
    }

    if (parsed.hostname.toLowerCase() !== 'login.microsoftonline.com') {
      return { valid: false, error: 'Yetkilendirme bağlantısı yalnızca login.microsoftonline.com alan adına ait olmalıdır.' };
    }

    if (parsed.port !== '' && parsed.port !== '443') {
      return { valid: false, error: 'Yetkilendirme bağlantısı yalnızca standart 443 portunu kullanabilir.' };
    }

    if (parsed.username || parsed.password) {
      return { valid: false, error: 'Yetkilendirme bağlantısında kullanıcı bilgisi (userinfo) bulunamaz.' };
    }

    if (parsed.hash) {
      return { valid: false, error: 'Yetkilendirme bağlantısında parça tanımlayıcı (fragment) bulunamaz.' };
    }

    if (parsed.pathname.toLowerCase() !== `/${cleanedExpected}/oauth2/v2.0/authorize`) {
      return { valid: false, error: 'Yetkilendirme bağlantısındaki yetki yolu formdaki değerle ve OAuth uç noktasıyla tam eşleşmiyor.' };
    }

    return { valid: true };
  } catch (_e) {
    return { valid: false, error: 'Geçersiz yetkilendirme URL formatı.' };
  }
}

export function validateGoogleAuthorizationUrl(url: string | null | undefined, expectedClientId: string): { valid: boolean; error?: string } {
  if (!url) return { valid: false, error: 'Yetkilendirme bağlantısı boş veya geçersiz.' };
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== 'https:' || parsed.hostname !== 'accounts.google.com' || parsed.pathname !== '/o/oauth2/v2/auth' ||
        (parsed.port && parsed.port !== '443') || parsed.username || parsed.password || parsed.hash)
      return { valid: false, error: 'Google yetkilendirme bağlantısı güvenli değil.' };
    if (parsed.searchParams.get('client_id') !== expectedClientId || parsed.searchParams.get('response_type') !== 'code' ||
        parsed.searchParams.get('code_challenge_method') !== 'S256') return { valid: false, error: 'Google yetkilendirme parametreleri eşleşmiyor.' };
    const redirect = new URL(parsed.searchParams.get('redirect_uri') || '');
    if (redirect.protocol !== 'http:' || redirect.hostname !== '127.0.0.1') return { valid: false, error: 'Google dönüş adresi yalnız loopback olabilir.' };
    return { valid: true };
  } catch { return { valid: false, error: 'Geçersiz Google yetkilendirme URL formatı.' }; }
}
