'use client';

import { ModelPanel } from './ModelPanel';

type Field = 'company name' | 'email';

const EMPTY = { name: '', city: 'La Paz' };

export class FormMessages {
  public missingName(): string {
    return 'Ingresá tu nombre para enviar la consulta.';
  }
}

export function SupplierForm({ error }: { readonly error: string | null }) {
  const status = error === null ? 'idle' : 'Ingresa el nombre de la empresa.';
  const tag = css`display: flex gap`;
  return (
    <form noValidate>
      <label htmlFor="company">Nombre de la empresa</label>
      <input name="companyName" placeholder="Ej: ElectroMotors S.A." autoComplete="name" />
      <span>{status}</span>
      <ModelPanel brand="BYD" name="Seal" />
      <i className="ph-bold ph-arrow-right">ok</i>
      <p>   </p>
      <p>Sí</p>
    </form>
  );
}

declare function css(strings: TemplateStringsArray): string;
export type { Field };
export { EMPTY };
