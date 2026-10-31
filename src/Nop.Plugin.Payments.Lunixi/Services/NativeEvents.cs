using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Plugin.Payments.Lunixi.Infrastructure;
using Nop.Services.Events;
using Nop.Services.Localization;

namespace Nop.Plugin.Payments.Lunixi.Services;

public sealed class NativeEvents(PaymentCoordinator coordinator, ILocalizationService localization) :
    IConsumer<OrderPaidEvent>, IConsumer<OrderRefundedEvent>, IConsumer<EntityUpdatedEvent<Order>>, IConsumer<EntityInsertedEvent<Language>>
{
    public Task HandleEventAsync(OrderPaidEvent value) => coordinator.AcknowledgeNativeAsync(value.Order.Id);
    public Task HandleEventAsync(OrderRefundedEvent value) => coordinator.AcknowledgeNativeAsync(value.Order.Id);
    public Task HandleEventAsync(EntityUpdatedEvent<Order> value)
        => value.Entity.PaymentMethodSystemName == "Payments.Lunixi" ? coordinator.AcknowledgeNativeAsync(value.Entity.Id) : Task.CompletedTask;
    public Task HandleEventAsync(EntityInsertedEvent<Language> value)
        => localization.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>(value.Entity.LanguageCulture.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? LocaleResources.Turkish : LocaleResources.English), value.Entity.Id);
}
