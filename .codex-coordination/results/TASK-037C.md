TASK_ID: TASK-037C
STATUS: DONE
EXECUTOR: SOL 5.6 LIMITED (AUTHORIZED ANTIGRAVITY FAILURE EXCEPTION)
CONTROLLER: SOL 5.6 LIMITED
CHANGED_FILES:
- prototype/src/components/filters/AdvancedFilterBuilder.tsx
- prototype/src/components/convert/LocalMimeWorkflow.tsx
- prototype/src/components/transfer/ImapTransferWorkflow.tsx
- prototype/src/components/transfer/BridgeTransferWorkflow.tsx
- prototype/src/components/transfer/PopSnapshotWorkflow.tsx
- prototype/src/components/archive/ArchiveSearchView.tsx
- prototype/src/components/jobs/JobCenter.tsx
- prototype/src/hooks/useMimeImport.ts
- prototype/src/hooks/useImapTransfer.ts
- prototype/src/hooks/useBridgeTransfer.ts
- prototype/src/types/localEngine.ts
- prototype/src/types/index.ts
- prototype/src/api/localEngineClient.ts
- prototype/src/tests/task037cAdvancedFilterBuilder.test.tsx
- prototype/tests/e2e/task037c-filter-ui.spec.ts
- prototype/tests/e2e/task028-jobcenter.spec.ts
- prototype/tests/e2e/task034-pop-ui.spec.ts
- engine/BitigMail.Engine/Archive/ArchiveModels.cs
- engine/BitigMail.Engine/Planning/Stage7Policies.cs
- engine/BitigMail.LocalHost/Archive/ArchiveCatalogService.cs
- engine/BitigMail.LocalHost/LocalEngineApiEndpoints.Archive.cs
- engine/BitigMail.LocalHost/Archive/ArchiveSelectedJobService.cs
- engine/BitigMail.Engine.Tests/Task037CArchiveAdvancedFilterTests.cs
- engine/BitigMail.Engine.Tests/Task037BArchiveSelectedJobTests.cs
- docs/FORMAT_DIRECTION_MATRIX.md
- .codex-coordination/results/TASK-038-security-contract.md
SUMMARY:
- Ortak Türkçe alan/işlem/değer filtre oluşturucu MIME, IMAP, Bridge ve arşiv akışlarına bağlandı. Üst düzey VE/VEYA desteklenir; iç içe AST sessizce kaybedilmez, düzenleme açık temizlemeye kadar engellenir. Kısmi tarih girişi hata üretmez; UTC değeri saat dilimi kaydırması olmadan `Z` biçimine çevrilir.
- Filtre şablonları gerçek store üzerinden kaydedilip uygulanır ve taze önizleme zorunluluğu görünürdür. camelCase kanonik filtre JSON doğrulaması backend’de düzeltildi.
- Local MIME klasör eşleme ve PreservePhysical/ContentOnly/ContentAndMetadata kontrolleri gerçek preview politikasına bağlandı; tüm politika değişiklikleri preview kapsamını geçersizleştirir. Root backend oracle’sı gerçek PST çıktısında 3/1/2 fiziksel ileti sonuçlarını doğruladı.
- Bekleyen iş önceliği gerçek API kontrolü olarak gösterildi; çalışan işe müdahale etmez.
- Arşiv gelişmiş AST filtresi tam, hash doğrulanmış raw MIME üzerinde sayım/sayfalama öncesi çalışır; aynı AST preview’da yeniden doğrulanır. İptal, 100 öğelik tarama sayfası, 64 MiB/ileti sınırı, disposal ve O(1) manifest item lookup sağlandı. Geç indeks kesiminin ötesindeki gövde eşleşmesi test edildi.
- Seçili arşiv planı sorgu ve kaynak metadata fingerprint’lerini başlangıç/dispatch anında yeniden doğrular; birleşik archiveId+folder eşleme anahtarı ve arşivler arası benzersiz çıktı adı kullanır.
- POP ekranı doğrudan indirme-öncesi tam gövde filtresini açıkça desteklenmiyor olarak gösterir ve doğrulanmış EML → ortak filtre rotasını tarif eder.
- TASK-038 için yalnız güvenlik sözleşmesi hazırlandı; Stage 8 implementation başlatılmadı.
VERIFICATION:
- Backend Release full regression: 725 passed, 0 failed, 0 skipped.
- TASK-037B/TASK-037C focused archive tests: 2 passed, including >600 KiB late-body exact match and preview parity.
- Root local MIME policy oracle: focused 16/16; broader MIME 75/75; real mapped/dedup PST counts 3/1/2 with reopened folder/attachment checks and tamper/collision rejection.
- Frontend Vitest: 151 passed, 0 failed across 16 files.
- TypeScript typecheck: PASS. ESLint: PASS. Production build: PASS.
- TestingHost 6175 Playwright: TASK-037C real MIME filter/mapping/dedup/template/re-preview/PST report 1/1; selected archive verified export 1/1; queued priority UI/API 1/1; POP rendered guidance and real snapshot failure/resume 2/2.
- Normal LocalHost 6174 was not stopped or modified.
RISKS:
- Production bundle retains the pre-existing >500 kB chunk-size warning; build succeeds.
- Stage 8 authorization is intentionally not implemented yet; current development endpoints are not identity/role acceptance evidence.
UNCERTAINTIES:
- No unresolved Stage 7 functional uncertainty. Real external IMAP providers and licensed Aspose output remain governed by their existing qualification boundaries.
