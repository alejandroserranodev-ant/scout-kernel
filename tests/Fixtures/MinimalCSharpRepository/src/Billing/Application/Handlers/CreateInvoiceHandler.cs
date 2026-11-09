using Billing.Domain;

namespace Billing.Application.Handlers;

/// <summary>
/// Creates and stores an invoice.
/// </summary>
public sealed class CreateInvoiceHandler(IInvoiceStore invoiceStore) : InvoiceHandlerBase
{
    /// <inheritdoc />
    public override string Operation => "create";

    /// <summary>
    /// Handles invoice creation.
    /// </summary>
    /// <param name="invoice">The invoice to create.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the invoice has been stored.</returns>
    public Task HandleAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        return invoiceStore.SaveAsync(invoice, cancellationToken);
    }
}
