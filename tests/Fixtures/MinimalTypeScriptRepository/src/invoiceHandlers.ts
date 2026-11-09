import type { Invoice } from './invoiceStore';
import { InvoiceHandlerBase } from './invoiceHandlerBase';

export class GetInvoiceHandler extends InvoiceHandlerBase {
  public handle(id: string): Invoice | undefined {
    return this.store.find(id);
  }
}

function zeroed(invoice: Invoice): Invoice {
  return { id: invoice.id, total: 0 };
}

export class CancelInvoiceHandler extends InvoiceHandlerBase {
  public handle(id: string): Invoice | undefined {
    const invoice = this.store.find(id);
    if (invoice !== undefined) {
      this.store.save(zeroed(invoice));
    }
    return invoice;
  }
}
