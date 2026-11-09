namespace Billing.Domain;

/// <summary>
/// Represents an invoice handled by the billing module.
/// </summary>
/// <param name="Id">The invoice identifier.</param>
/// <param name="Amount">The invoice amount.</param>
public sealed record Invoice(Guid Id, decimal Amount);
