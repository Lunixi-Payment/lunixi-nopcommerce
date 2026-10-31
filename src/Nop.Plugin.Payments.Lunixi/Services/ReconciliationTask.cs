using Nop.Plugin.Payments.Lunixi.Data;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Payments.Lunixi.Services;

public sealed class ReconciliationTask(PaymentLedger ledger, PaymentCoordinator coordinator) : IScheduleTask
{
    public async Task ExecuteAsync()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        foreach (var orderId in await ledger.PendingOrderIdsAsync())
        {
            try { await coordinator.ReconcileAsync(orderId, budget.Token); }
            catch (OperationCanceledException) when (budget.IsCancellationRequested) { break; }
            catch (Exception error) when (error is GatewayFault or PaymentReviewException) { /* Keep pending/review without logging secrets or marking paid. */ }
            finally { await ledger.TouchAsync(orderId); }
        }
    }
}
