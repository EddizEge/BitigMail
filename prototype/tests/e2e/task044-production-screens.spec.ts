import { expect, test } from '@playwright/test';
import { spawn, ChildProcessWithoutNullStreams } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

// TASK-044: oturum açılmış (üretim kimlik) görünümünde her ana ekranın ve aktarım alt akışlarının ekran görüntüsü.
// TestingHost ayrı test profili (runtime/testing-engine-secure) ve 6175 portu kullanır; kullanıcı profiline dokunmaz.
const VIEWPORTS = [
  { name: '1440x900', width: 1440, height: 900 },
  { name: '390x844', width: 390, height: 844 },
];

for (const viewport of VIEWPORTS) {
  test(`TASK-044 production screens ${viewport.name}`, async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop-reference', 'Görünüm boyutu test içinde ayarlanır.');
    test.setTimeout(180_000);
    const root = path.resolve('..');
    const runtime = path.join(root, 'runtime', 'testing-engine-secure');
    if (path.relative(root, path.resolve(runtime)).split('\\').join('/') !== 'runtime/testing-engine-secure') throw new Error('Unexpected test directory');
    fs.rmSync(runtime, { recursive: true, force: true });
    const evidence = path.join(root, '.codex-coordination', 'evidence', 'TASK-044', viewport.name);
    fs.mkdirSync(evidence, { recursive: true });

    const proof = crypto.randomBytes(32);
    const dotnet = path.join(root, '.tools', 'dotnet', 'dotnet.exe');
    const dll = path.join(root, 'engine', 'BitigMail.TestingHost', 'bin', 'Release', 'net8.0-windows', 'BitigMail.TestingHost.dll');
    const child = spawn(dotnet, [dll], { cwd: root, env: { ...process.env, BITIGMAIL_TEST_PRODUCTION_SECURITY: '1' }, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] }) as ChildProcessWithoutNullStreams;
    child.stdin.write(proof); child.stdin.end();

    const browserErrors: string[] = [];
    page.on('console', (message) => { if (message.type() === 'error') browserErrors.push(message.text()); });
    page.on('pageerror', (error) => browserErrors.push(error.message));
    const failedResponses: string[] = [];
    page.on('response', (response) => { if (response.status() >= 400) failedResponses.push(`${response.status()} ${response.request().method()} ${new URL(response.url()).pathname}`); });
    const shots: Record<string, unknown>[] = [];
    let errorsBeforeTeardown: string[] | null = null;

    const capture = async (name: string) => {
      await page.waitForTimeout(350);
      await page.screenshot({ path: path.join(evidence, `${name}.png`), fullPage: true });
      const layout = await page.evaluate(() => ({
        documentOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        bodyOverflow: document.body.scrollWidth - document.body.clientWidth,
        errorOverlay: Boolean(document.querySelector('vite-error-overlay')),
        textLength: (document.querySelector('[data-testid="app-shell"]')?.textContent || '').trim().length,
        visibleDemoWords: /Sentetik|sentetik|Örnek simülasyon|Demo Verilerini|Tasarım taslağı|Sözleşme & Yol Haritası|127\.0\.0\.1|\.ps1/.test(document.body.innerText),
      }));
      shots.push({ name, ...layout });
      expect(layout.documentOverflow, `${name}: yatay taşma`).toBeLessThanOrEqual(0);
      expect(layout.errorOverlay, `${name}: hata kaplaması`).toBe(false);
      expect(layout.textLength, `${name}: boş ekran`).toBeGreaterThan(40);
      expect(layout.visibleDemoWords, `${name}: örnek/geliştirici metni`).toBe(false);
    };
    const nav = async (tab: string) => { await page.getByTestId(`nav-tab-${tab}`).click(); };
    const pickOperation = async (operation: string) => { await page.getByTestId(`op-tab-${operation}`).click(); };

    try {
      await expect.poll(async () => fetch('http://127.0.0.1:6175/api/setup/status', { headers: { Host: '127.0.0.1:6175' } }).then((r) => r.status).catch(() => 0), { timeout: 30_000 }).toBe(200);
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await page.addInitScript((value) => { (window as any).__BITIGMAIL_ENGINE_URL__ = 'http://127.0.0.1:6175'; (window as any).__BITIGMAIL_SETUP_PROOF__ = value; }, proof.toString('hex'));
      await page.goto('/');
      await expect(page.getByTestId('identity-gate')).toContainText('İlk yönetici kurulumu');
      await page.getByLabel('Kullanıcı adı').fill('local-admin');
      await page.getByLabel('Parola').fill('correct horse battery');
      await page.getByRole('button', { name: 'Yönetici oluştur' }).click();
      await expect(page.getByTestId('header-account-control')).toBeVisible();

      // 1) İlk kullanım: hiç müşteri/proje yok
      await nav('clients');
      await expect(page.getByTestId('getting-started-card')).toBeVisible();
      await capture('01-musteriler-baslarken');
      await nav('transfers');
      await expect(page.getByTestId('transfers-empty-state')).toBeVisible();
      await capture('02-aktarim-proje-yok');

      // 2) Sentetik müşteri ve proje (yönetim paneli üzerinden)
      await page.getByTestId('header-account-control').click();
      await expect(page.getByTestId('identity-management-drawer')).toBeVisible();
      await page.getByLabel('Müşteri adı').fill('Kuzey Lojistik (test)');
      await page.getByLabel('İlk proje').fill('2026 posta geçişi');
      await page.getByRole('button', { name: 'Çalışma alanı oluştur' }).click();
      await expect(page.getByText('Müşteri ve ilk proje oluşturuldu.')).toBeVisible();
      await capture('03-hesap-ve-calisma-alanlari');
      await page.getByRole('button', { name: 'Yönetimi kapat' }).click();

      await nav('clients');
      await expect(page.getByTestId('getting-started-card')).toHaveCount(0);
      await capture('04-musteriler');
      await page.locator('[data-testid^="company-card-"]').first().click();
      await expect(page.getByTestId('client-detail-view')).toBeVisible();
      await capture('05-musteri-detay');

      // 3) Aktarım ve dönüşüm: işlem → alt tür → akış
      await nav('transfers');
      await pickOperation('migration');
      for (const [direction, name] of [['file-to-imap', '06-aktarim-dosyadan-hesaba'], ['imap-to-file', '07-aktarim-hesaptan-dosyaya'], ['imap-to-imap', '08-aktarim-hesaptan-hesaba'], ['pop-to-file', '09-aktarim-pop-eml']] as const) {
        await page.getByTestId(`tab-direction-${direction}`).click();
        await expect(page.getByTestId('transfer-direction-selector')).toBeVisible();
        await expect(page.getByTestId('transfer-mode-switcher')).toHaveCount(0);
        await capture(name);
      }
      await pickOperation('convert');
      for (const [kind, name] of [['ost', '10-donusum-ost-pst'], ['mime', '11-donusum-eml-mbox-pst'], ['outlook-eml', '12-donusum-pst-ost-olm-eml'], ['emlx', '13-donusum-emlx-eml']] as const) {
        await page.getByTestId(`convert-input-${kind}`).click();
        await capture(name);
      }
      await pickOperation('archive');
      await expect(page.getByTestId('local-archive-workflow')).toBeVisible();
      await capture('14-pst-bolme');
      await pickOperation('recovery');
      await expect(page.getByTestId('recovery-workflow')).toBeVisible();
      await capture('15-veri-kurtarma');

      // 4) İş merkezi: boş durum ve "Yeni iş" yönlendirmesi
      await nav('jobs');
      await expect(page.getByTestId('jobs-empty-state')).toBeVisible();
      await capture('16-is-merkezi');
      await page.getByTestId('new-job-dropdown-btn').click();
      await page.getByTestId('new-job-recovery').click();
      await expect(page.getByTestId('recovery-workflow')).toBeVisible();
      await expect(page.getByTestId('op-tab-recovery')).toHaveAttribute('aria-pressed', 'true');

      // 5) Arşiv ve arama, Raporlar, Ayarlar
      await nav('search');
      await expect(page.getByTestId('archive-search-view')).toBeVisible();
      await expect(page.getByTestId('archive-mode-demo-btn')).toHaveCount(0);
      await capture('17-arsiv-ve-arama');
      await nav('reports');
      await expect(page.getByTestId('reports-empty-state')).toBeVisible();
      await capture('18-raporlar');
      await page.getByTestId('header-settings-btn').click();
      await expect(page.getByTestId('settings-drawer-dialog')).toBeVisible();
      await expect(page.getByTestId('reset-demo-storage-btn')).toHaveCount(0);
      await capture('19-ayarlar');
      await page.getByTestId('settings-close-bottom-btn').click();
      await expect(page.getByTestId('status-bar-version')).toContainText('v0.9.4');
      // Hataları sunucu kapanmadan önce topla; kapanışta yarıda kalan istekler ürün hatası değildir.
      errorsBeforeTeardown = [...browserErrors];
      await page.goto('about:blank');
    } finally {
      fs.writeFileSync(path.join(evidence, 'layout.json'), JSON.stringify({ viewport, shots, browserErrors: errorsBeforeTeardown ?? browserErrors, failedResponses }, null, 2));
      if (child.exitCode === null) { const exited = new Promise((resolve) => child.once('exit', resolve)); child.kill(); await exited; }
    }
    expect(errorsBeforeTeardown, 'konsol hataları').toEqual([]);
  });
}

