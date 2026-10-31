using LinqToDB;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Data;
using Nop.Core.Events;
using Nop.Services.Orders;
using Nop.Plugin.Payments.Lunixi.Domain;

namespace Nop.Plugin.Payments.Lunixi.Services;

public sealed class NativeOrders(INopDataProvider database, IOrderService orders, IOrderProcessingService processing, IEventPublisher events)
{
    // Read through the real native ORM without a cached Order snapshot for financial comparisons.
    public async Task<Order> GetAsync(int id)
        => await database.GetTable<Order>().FirstOrDefaultAsync(o => o.Id == id && !o.Deleted) ?? throw new PaymentReviewException();

    public async Task ApplyAsync(Order order, CheckoutAttempt attempt, Settlement settlement)
    {
        if (order.PaymentMethodSystemName != "Payments.Lunixi" || order.OrderStatus == OrderStatus.Cancelled && settlement.State is "paid" or "authorized") throw new PaymentReviewException();
        if (order.PaymentStatus is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded && settlement.State is "paid" or "authorized" or "pending") return;
        if (settlement.State == "paid")
        {
            if (order.PaymentStatus == PaymentStatus.Paid) return;
            order.AuthorizationTransactionId = attempt.PaymentId;
            order.CaptureTransactionId = attempt.PaymentId;
            await orders.UpdateOrderAsync(order);
            // Native processing queues its notifications in the same database. No direct SMTP is sent here.
            await processing.MarkOrderAsPaidAsync(order);
        }
        else if (settlement.State == "authorized" && order.PaymentStatus == PaymentStatus.Pending)
        {
            order.PaymentStatus = PaymentStatus.Authorized;
            order.AuthorizationTransactionId = attempt.PaymentId;
            await orders.UpdateOrderAsync(order);
            await processing.CheckOrderStatusAsync(order);
        }
        else if (settlement.State is "refunded" or "partially_refunded")
        {
            var nativeRefund = MinorMoney.ToDecimal(Math.Min(attempt.BaseMinor, settlement.RefundedMinor), attempt.Currency);
            if (nativeRefund < order.RefundedAmount) return;
            var difference = nativeRefund - order.RefundedAmount;
            var target = settlement.State == "refunded" ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
            if (difference == 0 && order.PaymentStatus == target) return;
            order.RefundedAmount = nativeRefund; order.PaymentStatus = target;
            await orders.UpdateOrderAsync(order);
            if (difference > 0) await events.PublishAsync(new OrderRefundedEvent(order, difference));
            await processing.CheckOrderStatusAsync(order);
        }
        else if (settlement.State == "failed" && order.PaymentStatus == PaymentStatus.Authorized && settlement.CapturedMinor == 0)
        {
            order.PaymentStatus = PaymentStatus.Voided;
            await orders.UpdateOrderAsync(order);
            await processing.CheckOrderStatusAsync(order);
        }
    }
}
