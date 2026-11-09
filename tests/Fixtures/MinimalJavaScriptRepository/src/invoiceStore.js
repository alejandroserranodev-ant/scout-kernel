export class InMemoryInvoiceStore {
  constructor() {
    this.invoices = new Map();
  }

  find(id) {
    return this.invoices.get(id);
  }
}

export function formatTotal(invoice) {
  return invoice.total.toFixed(2);
}

function zeroed(id) {
  return { id, total: 0 };
}

export function emptyInvoice(id) {
  return zeroed(id);
}
