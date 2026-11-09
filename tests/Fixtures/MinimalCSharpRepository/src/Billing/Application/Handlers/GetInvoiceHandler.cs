namespace Billing.Application.Handlers;

/// <summary>
/// Retrieves an invoice.
/// </summary>
public sealed class GetInvoiceHandler : InvoiceHandlerBase
{
    /// <inheritdoc />
    public override string Operation => "get";
}
