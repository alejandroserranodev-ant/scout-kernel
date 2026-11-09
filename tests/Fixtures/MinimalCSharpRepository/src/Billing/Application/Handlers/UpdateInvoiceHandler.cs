namespace Billing.Application.Handlers;

/// <summary>
/// Updates an invoice.
/// </summary>
public sealed class UpdateInvoiceHandler : InvoiceHandlerBase
{
    /// <inheritdoc />
    public override string Operation => "update";
}
