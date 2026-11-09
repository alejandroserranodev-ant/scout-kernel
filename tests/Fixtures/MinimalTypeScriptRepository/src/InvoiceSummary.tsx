import type { Invoice } from './invoiceStore';
import { InvoiceRow } from './InvoiceTable';

export function InvoiceSummary({ invoice }: { readonly invoice: Invoice }) {
  return <table className="invoice-table"><tbody><InvoiceRow invoice={invoice} /></tbody></table>;
}
