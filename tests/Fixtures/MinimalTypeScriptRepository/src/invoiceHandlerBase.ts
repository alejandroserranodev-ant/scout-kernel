import type { Invoice, InvoiceStore } from './invoiceStore';

export abstract class InvoiceHandlerBase {
  protected constructor(protected readonly store: InvoiceStore) {
  }

  public abstract handle(id: string): Invoice | undefined;
}
