import React from 'react';
import { localEngineClient } from '../../api/localEngineClient';
import type { MailFilterDefinition, MailFilterNode, StoredTransferTemplate } from '../../types/localEngine';

type Rule = { id: string; field: NonNullable<MailFilterNode['field']>; operator: NonNullable<MailFilterNode['operator']>; value: string };
type Props = { value: MailFilterDefinition | null; onChange: (value: MailFilterDefinition | null) => void; disabled?: boolean; unknownCount?: number; testId?: string };

const labels: Record<Rule['field'], string> = { subject:'Konu', body:'Gövde', sender:'Gönderen', recipient:'Alıcı', attachmentName:'Ek adı', hasAttachment:'Ek var mı?', size:'Boyut (bayt)', date:'Tarih (UTC)' };
const textFields = new Set<Rule['field']>(['subject','body','sender','recipient','attachmentName']);
const newRule = (): Rule => ({ id:'new', field:'subject', operator:'contains', value:'' });
const dateInputValue = (value:string) => /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00Z$/.test(value) ? value.slice(0,16) : value;

function toRules(value: MailFilterDefinition | null): { join:'and'|'or'; rules:Rule[]; nested:boolean } {
  if (!value) return { join:'and', rules:[], nested:false };
  const root = value.root;
  const nodes = root.kind === 'condition' ? [root] : root.children ?? [];
  if (nodes.some(node => node.kind !== 'condition')) return { join:root.kind === 'or' ? 'or' : 'and', rules:[], nested:true };
  return { join: root.kind === 'or' ? 'or' : 'and', rules: nodes.map((node,index) => ({ id:String(index), field:node.field!, operator:node.operator!, value: node.field==='date' ? dateInputValue(node.date ?? '') : node.text ?? String(node.number ?? node.boolean ?? '') })), nested:false };
}

function build(join:'and'|'or', rules:Rule[]): MailFilterDefinition | null {
  if (rules.length === 0) return null;
  const children: MailFilterNode[] = rules.map(rule => {
    const base: MailFilterNode = { kind:'condition', field:rule.field, operator:rule.operator };
    if (textFields.has(rule.field)) base.text = rule.value;
    else if (rule.field === 'size') base.number = Number(rule.value);
    else if (rule.field === 'hasAttachment') base.boolean = rule.value === 'true';
    else base.date = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(rule.value) ? `${rule.value}:00Z` : rule.value;
    return base;
  });
  return { version:1, root:{ kind:join, children } };
}

export const AdvancedFilterBuilder: React.FC<Props> = ({ value, onChange, disabled=false, unknownCount=0, testId='advanced-filter-builder' }) => {
  const model = React.useMemo(() => toRules(value), [value]);
  const [templates,setTemplates] = React.useState<StoredTransferTemplate[]>([]);
  const [templateName,setTemplateName] = React.useState('');
  const [templateId,setTemplateId] = React.useState('');
  const [templateError,setTemplateError] = React.useState('');
  React.useEffect(()=>{let active=true;localEngineClient.listTransferTemplates().then(items=>{if(active)setTemplates(items);}).catch(()=>{});return()=>{active=false;};},[]);
  const update = (join:'and'|'or', rules:Rule[]) => onChange(build(join, rules));
  const saveTemplate = async () => {
    const name=templateName.trim();
    if(!name){setTemplateError('Şablon adı gereklidir.');return;}
    try {
      const saved=await localEngineClient.saveTransferTemplate({version:1,name,canonicalFilterJson:value?JSON.stringify(value):null,folderMappings:[],duplicatePolicy:'PreservePhysical',waitingPriority:0});
      setTemplates(current=>[...current.filter(x=>x.templateId!==saved.templateId),saved]);
      setTemplateId(saved.templateId);setTemplateName('');setTemplateError('');
    } catch(error) { setTemplateError(error instanceof Error?error.message:'Şablon kaydedilemedi.'); }
  };
  const applyTemplate = async () => {
    if(!templateId){setTemplateError('Uygulanacak şablonu seçin.');return;}
    try {
      const applied=await localEngineClient.applyTransferTemplate(templateId);
      onChange(applied.template.canonicalFilterJson?JSON.parse(applied.template.canonicalFilterJson) as MailFilterDefinition:null);
      setTemplateError('Şablon uygulandı. Devam etmeden önce yeniden önizleyin.');
    } catch(error) { setTemplateError(error instanceof Error?error.message:'Şablon uygulanamadı.'); }
  };
  return <details data-testid={testId} className="advanced-filter-details">
    <summary>Gelişmiş filtre <span>{model.rules.length ? `${model.rules.length} kural` : 'İsteğe bağlı'}</span></summary>
    <fieldset disabled={disabled} style={{border:0,borderTop:'1px solid #e2e8f0',padding:12,margin:0}}>
    <div style={{display:'flex',gap:8,alignItems:'center',flexWrap:'wrap',marginBottom:10}}>
      <span>Kuralların tümü</span>
      <select disabled={model.nested} aria-label="Kural bağlantısı" value={model.join} onChange={e=>update(e.target.value as 'and'|'or',model.rules)}><option value="and">VE</option><option value="or">VEYA</option></select>
      <button disabled={model.nested} type="button" onClick={()=>update(model.join,[...model.rules,{...newRule(),id:String(model.rules.length)}])}>Kural ekle</button>
      {model.rules.length>0&&<button type="button" onClick={()=>onChange(null)}>Filtreyi temizle</button>}
    </div>
    {model.rules.map((rule,index)=><div key={rule.id} data-testid={`${testId}-rule-${index}`} style={{display:'grid',gridTemplateColumns:'minmax(120px,1fr) minmax(110px,1fr) minmax(150px,2fr) auto',gap:8,marginTop:8}}>
      <select aria-label={`Alan ${index+1}`} value={rule.field} onChange={e=>{const field=e.target.value as Rule['field'];const operator=textFields.has(field)?'contains':'eq';update(model.join,model.rules.map(x=>x.id===rule.id?{...x,field,operator,value:''}:x));}}>{Object.entries(labels).map(([field,label])=><option key={field} value={field}>{label}</option>)}</select>
      <select aria-label={`İşlem ${index+1}`} value={rule.operator} onChange={e=>update(model.join,model.rules.map(x=>x.id===rule.id?{...x,operator:e.target.value as Rule['operator']}:x))}>
        {textFields.has(rule.field)&&<><option value="contains">içerir</option><option value="eq">eşittir</option></>}
        {rule.field==='hasAttachment'&&<option value="eq">eşittir</option>}
        {(rule.field==='size'||rule.field==='date')&&<><option value="eq">eşittir</option><option value="gte">en az</option><option value="gt">büyük</option><option value="lte">en çok</option><option value="lt">küçük</option></>}
      </select>
      {rule.field==='hasAttachment'?<select aria-label={`Değer ${index+1}`} value={rule.value} onChange={e=>update(model.join,model.rules.map(x=>x.id===rule.id?{...x,value:e.target.value}:x))}><option value="">Seçin</option><option value="true">Var</option><option value="false">Yok</option></select>:<input aria-label={`Değer ${index+1}`} type={rule.field==='date'?'datetime-local':rule.field==='size'?'number':'text'} value={rule.value} onChange={e=>update(model.join,model.rules.map(x=>x.id===rule.id?{...x,value:e.target.value}:x))}/>} 
      <button type="button" aria-label={`Kural ${index+1} sil`} onClick={()=>update(model.join,model.rules.filter(x=>x.id!==rule.id))}>Sil</button>
    </div>)}
    {model.nested?<p role="alert" style={{color:'#92400e',fontSize:12}}>Bu şablon iç içe VE/VEYA grupları içeriyor. Koşulların sessizce kaybolmaması için değiştirme kapatıldı. <button type="button" onClick={()=>onChange(null)}>İç içe filtreyi açıkça temizle</button></p>:model.rules.length===0&&<p style={{color:'#64748b',fontSize:12}}>Filtre yok. İsterseniz alan, işlem ve değer seçerek kural ekleyin.</p>}
    {unknownCount>0&&<p role="status" style={{color:'#92400e',fontSize:12}}>{unknownCount} ileti gerekli metadata olmadığı için filtre sonucuna dahil edilmedi.</p>}
    <div style={{borderTop:'1px solid #e2e8f0',marginTop:12,paddingTop:10,display:'flex',gap:8,alignItems:'center',flexWrap:'wrap'}}>
      <input aria-label="Şablon adı" placeholder="Şablon adı" value={templateName} onChange={e=>setTemplateName(e.target.value)}/>
      <button type="button" onClick={saveTemplate}>Mevcut filtreyi şablon olarak kaydet</button>
      <select aria-label="Kayıtlı şablon" value={templateId} onChange={e=>setTemplateId(e.target.value)}><option value="">Şablon seçin</option>{templates.map(item=><option key={item.templateId} value={item.templateId}>{item.template.name}</option>)}</select>
      <button type="button" onClick={applyTemplate}>Uygula ve yeniden önizle</button>
    </div>
    {templateError&&<p role="status" style={{fontSize:12,color:templateError.startsWith('Şablon uygulandı')?'#166534':'#92400e'}}>{templateError}</p>}
    </fieldset>
  </details>;
};
