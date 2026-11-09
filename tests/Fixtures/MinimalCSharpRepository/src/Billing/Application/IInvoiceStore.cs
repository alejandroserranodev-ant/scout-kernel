using Billing.Domain;

namespace Billing.Application;

/// <summary>
/// Stores invoices for the billing module.
/// </summary>
public interface IInvoiceStore
{
    /// <summary>
    /// Saves an invoice.
    /// </summary>
    /// <param name="invoice">The invoice to save.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the invoice has been saved.</returns>
    Task SaveAsync(Invoice invoice, CancellationToken cancellationToken = default);
}
