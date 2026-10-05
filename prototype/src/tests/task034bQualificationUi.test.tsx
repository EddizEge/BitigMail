// @vitest-environment jsdom
import { render, screen } from '@testing-library/react';
import { vi, test, expect } from 'vitest';
import { LocalMimeWorkflow } from '../components/convert/LocalMimeWorkflow';

const warning = 'OLM tarih anlamı doğrulanmadı; tarih filtresi kullanılamaz.';
vi.mock('../hooks/useMimeImport', () => ({ useMimeImport: () => ({
  mode:'eml-tree', status:'online', analysis:{sourceFileName:'qualified',totalItems:1,totalAttachments:0,ignoredNonEmlFilesCount:0,folders:[{folderId:'f',folderPath:'Inbox',displayName:'Inbox',itemCount:1}],preflight:{warnings:[],blockers:[],hasTrialBlocker:false}},
  preview:{selectionId:'s',sourceSha256:'x',filters:{timeZone:'UTC+03',datePolicy:'p'},totalSourceMessages:1,selectedMessagesCount:0,excludedMessagesCount:1,missingDateExcludedCount:0,selectedAttachmentsCount:0,folderBreakdown:[],canConvert:false,dateFilterBlocked:true,blockerReason:'[TARİH FİLTRESİ ENGELİ] Kaynak tarih sadakati doğrulanmadı.',qualificationWarnings:[warning]},
  report:null,job:null,folders:['f'],startDate:'2024-01-01',endDate:'',loadingPreview:false,running:false,starting:false,busy:false,target:null,error:null,canStart:false,
  connect:vi.fn(),setMode:vi.fn(),pickSource:vi.fn(),setFolders:vi.fn(),setStartDate:vi.fn(),setEndDate:vi.fn(),pickTarget:vi.fn(),start:vi.fn(),loadJob:vi.fn()
}) }));

test('TASK-034B renders qualification warning and blocked date flow', () => {
  const state:any={companies:[{id:'c',name:'Company'}],projects:[{id:'p',companyId:'c',name:'Project'}],plan:{companyId:'c',projectId:'p'},selectedLocalJobId:null,setSelectedLocalJobId:vi.fn(),updatePlan:vi.fn()};
  render(<LocalMimeWorkflow state={state}/>);
  expect(screen.getByTestId('mime-qualification-warning').textContent).toContain(warning);
  expect(screen.getByRole('alert').textContent).toContain('TARİH FİLTRESİ ENGELİ');
  expect(screen.getByTestId('mime-pick-target').hasAttribute('disabled')).toBe(true);
});
