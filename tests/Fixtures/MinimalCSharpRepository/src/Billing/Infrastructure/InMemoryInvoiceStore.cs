using Billing.Application;
using Billing.Domain;

namespace Billing.Infrastructure;

/// <summary>
/// Stores invoices in memory for the fixture.
/// </summary>
public sealed class InMemoryInvoiceStore : IInvoiceStore
{
    private readonly Dictionary<Guid, Invoice> invoices = [];

    /// <inheritdoc />
    public Task SaveAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        invoices[invoice.Id] = invoice;
        return Task.CompletedTask;
    }
}
