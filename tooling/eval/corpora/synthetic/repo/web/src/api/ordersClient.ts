export interface OrderDto {
  id: string;
  customerId: string;
  status: string;
}

export async function fetchOrder(orderId: string): Promise<OrderDto> {
  const response = await fetch(`/api/orders/${orderId}`);
  if (!response.ok) {
    throw new Error(`failed to fetch order ${orderId}`);
  }

  return response.json();
}

export async function cancelOrderRequest(orderId: string): Promise<void> {
  await fetch(`/api/orders/${orderId}/cancel`, { method: 'POST' });
}
