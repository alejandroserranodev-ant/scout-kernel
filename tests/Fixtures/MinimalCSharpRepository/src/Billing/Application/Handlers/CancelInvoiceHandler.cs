namespace Billing.Application.Handlers;

/// <summary>
/// Cancels an invoice.
/// </summary>
public sealed class CancelInvoiceHandler : InvoiceHandlerBase
{
    /// <inheritdoc />
    public override string Operation => "cancel";
}
