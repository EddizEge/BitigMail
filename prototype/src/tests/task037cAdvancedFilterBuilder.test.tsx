// @vitest-environment jsdom
import React from 'react';
import {fireEvent,render,screen} from '@testing-library/react';
import {beforeEach,describe,expect,it,vi} from 'vitest';
import {localEngineClient} from '../api/localEngineClient';
import {AdvancedFilterBuilder} from '../components/filters/AdvancedFilterBuilder';
import type {MailFilterDefinition} from '../types/localEngine';

const Harness=({initial=null}:{initial?:MailFilterDefinition|null})=>{const [value,setValue]=React.useState(initial);return <><AdvancedFilterBuilder value={value} onChange={setValue}/><output data-testid="value">{JSON.stringify(value)}</output></>;};

describe('TASK037C gelişmiş filtre oluşturucu',()=>{
  beforeEach(()=>{vi.spyOn(localEngineClient,'listTransferTemplates').mockImplementation(()=>new Promise(()=>{}));});

  it('çok karakterli yazımı odağı kaybetmeden korur',()=>{
    render(<Harness/>);
    fireEvent.click(screen.getByText('Kural ekle'));
    const input=screen.getByLabelText('Değer 1');
    fireEvent.change(input,{target:{value:'alpha beta'}});
    expect((screen.getByLabelText('Değer 1') as HTMLInputElement).value).toBe('alpha beta');
    expect(screen.getByTestId('value').textContent).toContain('alpha beta');
  });

  it('UTC alanını saat dilimine göre kaydırmadan Z biçimine dönüştürür',()=>{
    render(<Harness initial={{version:1,root:{kind:'condition',field:'date',operator:'gte',date:'2026-03-29T01:30:00Z'}}}/>);
    expect((screen.getByLabelText('Değer 1') as HTMLInputElement).value).toBe('2026-03-29T01:30');
    fireEvent.change(screen.getByLabelText('Değer 1'),{target:{value:'2026-10-25T01:30'}});
    expect(screen.getByTestId('value').textContent).toContain('2026-10-25T01:30:00Z');
  });

  it('iç içe ASTyi açık temizleme olmadan değiştirmez',()=>{
    const nested:MailFilterDefinition={version:1,root:{kind:'and',children:[{kind:'or',children:[{kind:'condition',field:'subject',operator:'contains',text:'x'}]}]}};
    render(<Harness initial={nested}/>);
    expect(screen.getByRole('alert').textContent).toContain('sessizce kaybolmaması');
    expect(screen.getByTestId('value').textContent).toBe(JSON.stringify(nested));
    fireEvent.click(screen.getByText('İç içe filtreyi açıkça temizle'));
    expect(screen.getByTestId('value').textContent).toBe('null');
  });
});
