namespace Orders;

public sealed class OrderRepository
{
    private readonly Dictionary<string, Order> _ordersById = new();

    public void Save(Order order)
    {
        _ordersById[order.CustomerId] = order;
    }

    public Order FindById(string orderId)
    {
        return _ordersById[orderId];
    }
}
