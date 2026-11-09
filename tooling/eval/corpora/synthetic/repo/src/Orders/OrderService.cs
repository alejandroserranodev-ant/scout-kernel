namespace Orders;

public sealed class OrderService
{
    private readonly OrderRepository _repository;

    public OrderService(OrderRepository repository)
    {
        _repository = repository;
    }

    public Order CreateOrder(string customerId, IReadOnlyList<OrderLine> lines)
    {
        var order = new Order(customerId, lines, OrderStatus.Pending);
        _repository.Save(order);
        return order;
    }

    public void CancelOrder(string orderId)
    {
        var order = _repository.FindById(orderId);
        order.Status = OrderStatus.Cancelled;
        _repository.Save(order);
    }
}

public enum OrderStatus
{
    Pending,
    Cancelled,
    Fulfilled
}

public sealed record OrderLine(string ProductId, int Quantity, decimal UnitPrice);

public sealed class Order
{
    public Order(string customerId, IReadOnlyList<OrderLine> lines, OrderStatus status)
    {
        CustomerId = customerId;
        Lines = lines;
        Status = status;
    }

    public string CustomerId { get; }

    public IReadOnlyList<OrderLine> Lines { get; }

    public OrderStatus Status { get; set; }
}
