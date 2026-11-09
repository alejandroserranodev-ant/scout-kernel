export interface OrderStatusBadgeProps {
  readonly status: string;
}

export function OrderStatusBadge({ status }: OrderStatusBadgeProps) {
  const label = status === 'Cancelled' ? 'Order cancelled' : 'Order confirmed';
  return (
    <span className="order-status-badge" data-status={status}>
      {label}
    </span>
  );
}
