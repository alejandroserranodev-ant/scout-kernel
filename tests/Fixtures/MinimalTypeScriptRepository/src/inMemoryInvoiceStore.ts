import type { Invoice, InvoiceStore } from './invoiceStore';

export class InMemoryInvoiceStore implements InvoiceStore {
  private readonly invoices = new Map<string, Invoice>();

  public find(id: string): Invoice | undefined {
    return this.invoices.get(id);
  }

  public save(invoice: Invoice): void {
    this.invoices.set(invoice.id, invoice);
  }
}
