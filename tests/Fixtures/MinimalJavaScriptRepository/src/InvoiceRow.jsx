import { formatTotal } from "./invoiceStore.js";

export function InvoiceRow({ invoice }) {
  return <td className="invoice-cell">{formatTotal(invoice)}</td>;
}
