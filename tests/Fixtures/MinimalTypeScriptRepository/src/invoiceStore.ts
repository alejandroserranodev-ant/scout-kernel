export interface Invoice {
  readonly id: string;
  readonly total: number;
}

export interface InvoiceStore {
  find(id: string): Invoice | undefined;
  save(invoice: Invoice): void;
}
