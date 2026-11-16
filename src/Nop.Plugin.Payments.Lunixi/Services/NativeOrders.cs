using LinqToDB;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Data;
using Nop.Core.Events;
using Nop.Services.Common;
using Nop.Services.Orders;
using Nop.Plugin.Payments.Lunixi.Domain;

namespace Nop.Plugin.Payments.Lunixi.Services;

public sealed class NativeOrders(INopDataProvider database, IOrderService orders, IOrderProcessingService processing, IEventPublisher events, IGenericAttributeService attributes)
{
    // Order-level review flag: the admin sees the order note; integrations can filter on the generic attribute.
    public const string ReviewAttribute = "Lunixi.PaymentReview";

    // Read through the real native ORM without a cached Order snapshot for financial comparisons.
    public async Task<Order> GetAsync(int id)
        => await database.GetTable<Order>().FirstOrDefaultAsync(o => o.Id == id && !o.Deleted) ?? throw new PaymentReviewException();

    public async Task ApplyAsync(Order order, CheckoutAttempt attempt, Settlement settlement)
    {
        if (order.PaymentMethodSystemName != "Payments.Lunixi") throw new PaymentReviewException();
        if (order.PaymentStatus is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded && settlement.State is "paid" or "authorized" or "pending") return;
        if (order.OrderStatus == OrderStatus.Cancelled && settlement.State is "paid" or "authorized")
        {
            // Money that arrives after cancellation (for example a bank transfer settled after the order was
            // cancelled) is never applied automatically: restoring stock and fulfilment is a merchant decision.
            var recorded = order.PaymentStatus == PaymentStatus.Paid || settlement.State == "authorized" && order.PaymentStatus == PaymentStatus.Authorized;
            if (!recorded) await FlagReviewAsync(order, attempt.PaymentId ?? throw new PaymentReviewException(), settlement.State == "paid"
                ? "was captured for this cancelled order. The order was not marked as paid. Refund the payment in the Lunixi panel, or restore the order and confirm the payment manually."
                : "is authorized (funds held) for this cancelled order. The order was not changed. Void the payment in the Lunixi panel, or restore the order manually.");
            return;
        }
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

    // A verified payment bound to this order that is not the order's active payment, such as a late bank
    // transfer accepted as a new payment. It is never applied as the order's payment.
    public Task FlagLinkedPaymentAsync(Order order, string paymentId, Settlement settlement)
        => FlagReviewAsync(order, paymentId, settlement.State == "authorized"
            ? "is a separate payment linked to this order and is authorized (funds held). The order was not changed. Review it in the Lunixi panel before fulfilling, capturing or voiding."
            : "is a separate payment linked to this order and has captured funds. The order was not marked as paid automatically. Review it in the Lunixi panel before fulfilling or refunding.");

    public static string ReviewMarker(string paymentId) => "[Lunixi review " + paymentId + "]";

    // Idempotent per payment: one admin-only note and one attribute update, whatever the redelivery count.
    private async Task FlagReviewAsync(Order order, string paymentId, string reason)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(paymentId, @"\A[A-Za-z0-9._:-]{1,256}\z")) throw new PaymentReviewException();
        var marker = ReviewMarker(paymentId);
        // Compared in memory: a provider LIKE pattern would treat the marker's brackets differently per database.
        var notes = await database.GetTable<OrderNote>().Where(n => n.OrderId == order.Id).Select(n => n.Note).ToListAsync();
        if (notes.Any(n => n is not null && n.StartsWith(marker, StringComparison.Ordinal))) return;
        await orders.InsertOrderNoteAsync(new OrderNote { OrderId = order.Id, Note = marker + " Payment " + paymentId + " " + reason, DisplayToCustomer = false, CreatedOnUtc = DateTime.UtcNow });
        await attributes.SaveAttributeAsync(order, ReviewAttribute, paymentId);
    }
}
