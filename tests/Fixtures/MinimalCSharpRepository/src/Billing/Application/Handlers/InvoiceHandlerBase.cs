namespace Billing.Application.Handlers;

/// <summary>
/// Provides the common contract for invoice handlers.
/// </summary>
public abstract class InvoiceHandlerBase
{
    /// <summary>
    /// Gets the operation represented by the handler.
    /// </summary>
    public abstract string Operation { get; }
}
