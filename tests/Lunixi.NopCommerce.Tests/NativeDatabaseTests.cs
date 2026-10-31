using Moq;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Lunixi.Domain;

namespace Lunixi.NopCommerce.Tests;

public sealed class NativeDatabaseTests
{
    [Fact]
    public async Task RealNativeMappingsMigrationsAndPaidLifecyclePersistAtomicallyWithInbox()
    {
        var database = new NativeDatabase(); await database.Initialize();
        var order = await database.Order(); var attempt = await database.Attempt(order);
        var beforeInbox = await database.Count("LunixiInbox");
        await database.Ledger.WithOrderAsync(order.Id, async () =>
        {
            var fresh = await database.Native.GetAsync(order.Id);
            await database.Native.ApplyAsync(fresh, attempt, new("paid", 10050, 10050, 0));
            await database.Ledger.UpdateSettlementAsync(attempt.Id, new("paid", 10050, 10050, 0));
            await database.Ledger.CompleteInboxAsync(attempt.ProfileId, "event-" + order.Id, "synthetic-hash"); return true;
        });
        var persisted = await database.Native.GetAsync(order.Id);
        Assert.Equal(PaymentStatus.Paid, persisted.PaymentStatus); Assert.Equal(OrderStatus.Processing, persisted.OrderStatus);
        Assert.Equal(attempt.PaymentId, persisted.CaptureTransactionId); Assert.NotNull(persisted.PaidDateUtc);
        Assert.Equal(beforeInbox + 1, await database.Count("LunixiInbox")); Assert.Equal(1, database.PaidEvents);
        await database.Ledger.WithOrderAsync(order.Id, async () =>
        {
            if (!await database.Ledger.InboxCompletedAsync(attempt.ProfileId, "event-" + order.Id, "synthetic-hash")) throw new InvalidOperationException();
            await database.Native.ApplyAsync(await database.Native.GetAsync(order.Id), attempt, new("paid", 10050, 10050, 0)); return true;
        });
        Assert.Equal(1, database.PaidEvents);
    }

    [Fact]
    public async Task NativeProcessingFailureRollsBackOrderNotesLedgerAndInbox()
    {
        var database = new NativeDatabase(); await database.Initialize(); var order = await database.Order(); var attempt = await database.Attempt(order);
        var beforeInbox = await database.Count("LunixiInbox"); var beforeNotes = await database.Count("OrderNote");
        database.Publisher.Setup(p => p.PublishAsync(It.IsAny<OrderPaidEvent>())).ThrowsAsync(new InvalidOperationException("synthetic native failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Ledger.WithOrderAsync(order.Id, async () =>
        {
            await database.Native.ApplyAsync(await database.Native.GetAsync(order.Id), attempt, new("paid", 10050, 10050, 0));
            await database.Ledger.CompleteInboxAsync(attempt.ProfileId, "rollback-" + order.Id, "hash"); return true;
        }));
        Assert.Equal(PaymentStatus.Pending, (await database.Native.GetAsync(order.Id)).PaymentStatus);
        Assert.Equal(beforeInbox, await database.Count("LunixiInbox")); Assert.Equal(beforeNotes, await database.Count("OrderNote"));
        Assert.Equal("pending", (await database.Ledger.GetActiveAsync(order.Id))!.State);
    }

    [Fact]
    public async Task ConcurrentDeliveriesSerializePerNativeOrderAndFulfillOnce()
    {
        var database = new NativeDatabase(); await database.Initialize(); var order = await database.Order(); var attempt = await database.Attempt(order);
        async Task Deliver() => await database.Ledger.WithOrderAsync(order.Id, async () =>
        {
            if (await database.Ledger.InboxCompletedAsync(attempt.ProfileId, "concurrent-" + order.Id, "hash")) return false;
            await database.Native.ApplyAsync(await database.Native.GetAsync(order.Id), attempt, new("paid", 10050, 10050, 0));
            await database.Ledger.CompleteInboxAsync(attempt.ProfileId, "concurrent-" + order.Id, "hash"); return true;
        });
        await Task.WhenAll(Deliver(), Deliver());
        Assert.Equal(PaymentStatus.Paid, (await database.Native.GetAsync(order.Id)).PaymentStatus); Assert.Equal(1, database.PaidEvents);
        await Assert.ThrowsAsync<GatewayFault>(() => database.Ledger.InboxCompletedAsync(attempt.ProfileId, "concurrent-" + order.Id, "different-body"));
    }

    [Fact]
    public async Task NativeAuthorizationRefundAndStaleObservationPreserveFinancialState()
    {
        var database = new NativeDatabase(); await database.Initialize(); var order = await database.Order(); var attempt = await database.Attempt(order);
        async Task Apply(Settlement settlement) => await database.Ledger.WithOrderAsync(order.Id, async () =>
        { await database.Native.ApplyAsync(await database.Native.GetAsync(order.Id), attempt, settlement); return true; });
        await Apply(new("authorized", 10050, 0, 0));
        var authorized = await database.Native.GetAsync(order.Id);
        Assert.Equal(PaymentStatus.Authorized, authorized.PaymentStatus); Assert.Null(authorized.PaidDateUtc); Assert.Equal(0, database.PaidEvents);
        await Apply(new("paid", 10050, 10050, 0));
        await Apply(new("partially_refunded", 10050, 10050, 1000));
        Assert.Equal(10m, (await database.Native.GetAsync(order.Id)).RefundedAmount);
        await Apply(new("refunded", 10050, 10050, 10050));
        await Apply(new("paid", 10050, 10050, 0));
        var refunded = await database.Native.GetAsync(order.Id);
        Assert.Equal(PaymentStatus.Refunded, refunded.PaymentStatus); Assert.Equal(100.50m, refunded.RefundedAmount); Assert.Equal(1, database.PaidEvents);
    }

    [Fact]
    public async Task PersistedOperationIdentitySurvivesStoreObjectsAndNativeTransactions()
    {
        var database = new NativeDatabase(); await database.Initialize(); var order = await database.Order(); var attempt = await database.Attempt(order);
        var operation = await database.Ledger.WithOrderAsync(order.Id, () => database.Ledger.AddOperationAsync(attempt.Id, "refund", 1000, 0));
        var second = new Nop.Plugin.Payments.Lunixi.Data.PaymentLedger(database.Connections);
        var restored = await second.GetPendingOperationAsync(attempt.Id);
        Assert.Equal(operation.Key, restored!.Key); Assert.Equal(1000, restored.AmountMinor);
        await database.Ledger.WithOrderAsync(order.Id, async () => { await second.SetOperationStateAsync(operation.Id, "applied"); return true; });
        Assert.Null(await database.Ledger.GetPendingOperationAsync(attempt.Id));
    }

    [Fact]
    public async Task NontransactionalNativeOrderStorageIsRejectedBeforeMoneyCalls()
    {
        var database = new NativeDatabase(); await database.Initialize();
        try
        {
            await database.Execute("ALTER TABLE `Order` ENGINE=MyISAM");
            var fresh = new Nop.Plugin.Payments.Lunixi.Data.NativeConnectionFactory();
            await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.VerifyTransactionalStorageAsync());
        }
        finally { await database.Execute("ALTER TABLE `Order` ENGINE=InnoDB"); }
    }
    [Fact]
    public async Task SchedulerReservesCapacityForUnresolvedAndSettledOrders()
    {
        var database = new NativeDatabase(); await database.Initialize();
        var old = await database.Order(); var settled = await database.Attempt(old);
        await database.Ledger.UpdateSettlementAsync(settled.Id, new("paid", 10050, 10050, 0));
        var fresh = await database.Order(); await database.Attempt(fresh);
        await database.Execute($"UPDATE `LunixiAttempt` SET `LastCheckedUtc`='2000-01-01T00:00:00.0000000Z' WHERE `NativeOrderId` IN ({old.Id},{fresh.Id})");
        var batch = await database.Ledger.PendingOrderIdsAsync();
        Assert.Contains(fresh.Id, batch); Assert.Contains(old.Id, batch);
        Assert.True(batch.ToList().IndexOf(fresh.Id) < batch.ToList().IndexOf(old.Id));
        Assert.True(batch.Count <= 100);
    }

}
