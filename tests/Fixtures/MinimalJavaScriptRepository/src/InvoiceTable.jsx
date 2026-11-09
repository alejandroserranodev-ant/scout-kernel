import { InvoiceRow } from "./InvoiceRow.jsx";
import { InMemoryInvoiceStore, emptyInvoice, formatTotal } from "./invoiceStore.js";

export function InvoiceTable({ ids }) {
  const store = new InMemoryInvoiceStore();
  return (
    <table className="invoice-table">
      <caption>{formatTotal(emptyInvoice("none"))}</caption>
      <tbody>{ids.map((id) => <InvoiceRow invoice={store.find(id)} />)}</tbody>
    </table>
  );
}
