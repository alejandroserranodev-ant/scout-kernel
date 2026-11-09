import type { Invoice } from './invoiceStore';

export interface InvoiceRowProps {
  readonly invoice: Invoice;
}

export function InvoiceRow({ invoice }: InvoiceRowProps) {
  return (
    <tr className="invoice-row">
      <td className="invoice-cell">{invoice.id}</td>
      <td className="invoice-cell invoice-total">{invoice.total}</td>
    </tr>
  );
}

export function InvoiceTable({ invoices }: { readonly invoices: readonly Invoice[] }) {
  return (
    <table className="invoice-table">
      <tbody>{invoices.map((invoice) => <InvoiceRow key={invoice.id} invoice={invoice} />)}</tbody>
    </table>
  );
}
