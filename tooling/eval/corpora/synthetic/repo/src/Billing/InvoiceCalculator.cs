using Orders;

namespace Billing;

public static class InvoiceCalculator
{
    public static decimal ComputeTotal(IReadOnlyList<OrderLine> lines)
    {
        decimal total = 0;
        foreach (var line in lines)
        {
            total += line.Quantity * line.UnitPrice;
        }

        return total;
    }

    public static decimal ApplyDiscount(decimal total, decimal percentage)
    {
        return total - (total * percentage / 100m);
    }
}
