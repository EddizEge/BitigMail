import {expect,test} from '@playwright/test';
import { openDevelopmentSession, useDevelopmentTestingHost } from './support/testingHost';

useDevelopmentTestingHost();

test('TASK-037C gerçek MIME ekranında filtre, eşleme, tekilleştirme ve şablon yeniden önizlemesi',async({page})=>{
  await openDevelopmentSession(page);
  const headers:Record<string,string>={Origin:'http://127.0.0.1:5173','Content-Type':'application/json'};
  const session=await page.request.post('http://127.0.0.1:6175/api/session',{headers,data:{}});
  headers['X-BitigMail-Session']=(await session.json()).token;
  expect((await page.request.post('http://127.0.0.1:6175/api/testing/set-mime-source',{headers,data:{fixtureId:'corpus-tree'}})).status()).toBe(200);

  await page.goto('/');
  await page.getByTestId('nav-tab-transfers').click();
  await page.getByTestId('op-tab-convert').click();
  await page.getByTestId('convert-input-mime').click();
  await expect(page.getByTestId('mime-service-status')).toContainText('hazır');
  await page.getByTestId('mime-mode-eml-tree').click();
  await page.getByTestId('mime-pick-source').click();
  await expect(page.getByTestId('mime-preview-selected')).toHaveText('12');

  const builder=page.getByTestId('mime-advanced-filter');
  await builder.locator('summary').click();
  await builder.getByRole('button',{name:'Kural ekle'}).click();
  const value=builder.getByLabel('Değer 1');
  const filterRequestPromise=page.waitForRequest(r=>r.url().endsWith('/api/mime/selection/preview')&&r.postDataJSON()?.advancedFilter?.root?.children?.[0]?.text==='alpha beta');
  await value.fill('alpha beta');
  await expect(value).toHaveValue('alpha beta');
  const filterRequest=await filterRequestPromise;
  expect(filterRequest.postDataJSON().advancedFilter.root.children[0].field).toBe('subject');

  await value.fill('');
  const target=page.locator('input[aria-label$="hedef klasörü"]').first();
  await target.fill('Birleştirilmiş/Kutu');
  const policyRequestPromise=page.waitForRequest(r=>r.url().endsWith('/api/mime/selection/preview')&&r.postDataJSON()?.duplicatePolicy==='ContentOnly');
  await page.getByLabel('Yinelenen ileti politikası').selectOption('ContentOnly');
  const policyRequest=await policyRequestPromise;
  expect(policyRequest.postDataJSON().folderMappings[0].targetFolderPath).toBe('Birleştirilmiş/Kutu');

  await value.fill('alpha beta');
  await builder.getByLabel('Şablon adı').fill(`TASK037C ${Date.now()}`);
  const saveResponsePromise=page.waitForResponse(r=>r.url().endsWith('/api/templates')&&r.request().method()==='POST');
  await builder.getByRole('button',{name:'Mevcut filtreyi şablon olarak kaydet'}).click();
  const saveResponse=await saveResponsePromise;
  expect(saveResponse.status(),await saveResponse.text()).toBe(200);
  await expect(builder.getByLabel('Kayıtlı şablon')).not.toHaveValue('');
  await builder.getByRole('button',{name:'Uygula ve yeniden önizle'}).click();
  await expect(builder.getByRole('status')).toContainText('yeniden önizleyin');

  await builder.getByRole('button',{name:'Filtreyi temizle'}).click();
  await page.getByLabel('Yinelenen ileti politikası').selectOption('PreservePhysical');
  await expect(page.getByTestId('mime-preview-selected')).toHaveText('12');
  await page.getByTestId('mime-pick-target').click();
  await expect(page.getByTestId('mime-start')).toBeEnabled();
  const startResponsePromise=page.waitForResponse(r=>r.url().endsWith('/api/mime/start')&&r.request().method()==='POST');
  await page.getByTestId('mime-start').click();
  const started=await (await startResponsePromise).json();
  await expect(page.getByTestId('mime-result-count')).toHaveText('12',{timeout:20_000});
  const reportResponse=await page.request.get(`http://127.0.0.1:6175/api/jobs/${started.jobId}/report`,{headers});
  expect(reportResponse.status()).toBe(200);
  const report=await reportResponse.json();
  expect(report.mimeImport.folderMappingFingerprint).toMatch(/^[a-f0-9]{64}$/);
  expect(report.mimeImport.duplicatePolicy).toBe('PreservePhysical');
});
