using Moq;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Services.Payments;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lunixi.NopCommerce.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task FailedWithoutPublicOutcomeCertaintyRetainsAttemptAndNeverRotatesKey()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start();
        var before = await host.Database.Ledger.GetActiveAsync(host.Order.Id);
        host.Gateway.Status = "FAILED"; await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        Assert.Equal("review", (await host.Database.Ledger.GetActiveAsync(host.Order.Id))!.State);
        await host.Start();
        var after = await host.Database.Ledger.GetActiveAsync(host.Order.Id);
        Assert.Equal(before!.Id, after!.Id); Assert.Equal(before.OperationKey, after.OperationKey);
        Assert.Single(host.Gateway.CreateKeys); Assert.Equal(0, host.Database.PaidEvents);
    }

    [Fact]
    public async Task ServerOwnedAttemptAndEncryptedProfileSurviveUnknownCreateOutcome()
    {
        var host = new CoordinatorHost(); await host.Initialize(); host.Gateway.FailCreateOnce = true;
        await Assert.ThrowsAsync<GatewayFault>(() => host.Start());
        var attempt = await host.Database.Ledger.GetActiveAsync(host.Order.Id); Assert.NotNull(attempt); Assert.Null(attempt.PaymentId);
        var page = await host.Start(); Assert.Equal("synthetic-checkout-token", page.Token);
        Assert.Equal(2, host.Gateway.CreateKeys.Count); Assert.Single(host.Gateway.CreateKeys.Distinct());
        var encrypted = await host.Database.Ledger.GetProfileAsync(host.Config.ActiveProfileId);
        Assert.DoesNotContain("PRIVATE KEY", encrypted!.Value.Cipher); Assert.DoesNotContain("synthetic-webhook-secret", encrypted.Value.Cipher);
        Assert.Equal("https://pay.lunixi.com/payment/synthetic-checkout-token", page.HostedUrl);
    }

    [Fact]
    public async Task OwnershipStoreAndConflictingResponseAreRejectedBeforeExposure()
    {
        var host = new CoordinatorHost(); await host.Initialize();
        await Assert.ThrowsAsync<PaymentReviewException>(() => host.Start(customerId: 8));
        await Assert.ThrowsAsync<PaymentReviewException>(() => host.Start(storeId: 2));
        Assert.Empty(host.Gateway.CreateKeys);
        host.Gateway.ConflictAlias = true;
        await Assert.ThrowsAsync<PaymentReviewException>(() => host.Start());
        Assert.Null((await host.Database.Ledger.GetActiveAsync(host.Order.Id))!.PaymentId);
    }

    [Fact]
    public async Task AuthorizedThenNativeCaptureExecutesGatewayOnceAndUsesNativePaidLifecycle()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start();
        host.Gateway.Status = "AUTHORIZED"; await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        var held = await host.Database.Native.GetAsync(host.Order.Id); Assert.Equal(PaymentStatus.Authorized, held.PaymentStatus); Assert.Null(held.PaidDateUtc);
        var errors = await host.Database.Processing.CaptureAsync(held); Assert.Empty(errors);
        Assert.Equal(PaymentStatus.Paid, (await host.Database.Native.GetAsync(host.Order.Id)).PaymentStatus);
        Assert.Single(host.Gateway.MutationKeys); Assert.Equal(1, host.Database.PaidEvents);
        Assert.Null(await host.Database.Ledger.GetPendingOperationAsync((await host.Database.Ledger.GetActiveAsync(host.Order.Id))!.Id));
    }

    [Fact]
    public async Task NativePartialThenFinalRefundIncludesRemainingAuthenticatedFeeWithoutDoubleApplying()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start(); host.Gateway.Fee = 300;
        await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        var order = await host.Database.Native.GetAsync(host.Order.Id);
        var first = await host.Database.Processing.PartiallyRefundAsync(order, 10m); Assert.Empty(first);
        Assert.Equal(1000, host.Gateway.Refunded); Assert.Equal(10m, (await host.Database.Native.GetAsync(host.Order.Id)).RefundedAmount);
        var rest = await host.Database.Processing.PartiallyRefundAsync(await host.Database.Native.GetAsync(host.Order.Id), 90.50m); Assert.Empty(rest);
        var final = await host.Database.Native.GetAsync(host.Order.Id);
        Assert.Equal(10350, host.Gateway.Refunded); Assert.Equal(100.50m, final.RefundedAmount); Assert.Equal(PaymentStatus.Refunded, final.PaymentStatus);
        Assert.Equal(2, host.Gateway.MutationKeys.Count);
    }

    [Fact]
    public async Task UnknownRefundIsRecoveredByAuthenticatedReadWithoutAnotherMoneyRequest()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start(); await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        host.Gateway.FailMutationOnce = true;
        var errors = await host.Database.Processing.PartiallyRefundAsync(await host.Database.Native.GetAsync(host.Order.Id), 10m); Assert.NotEmpty(errors);
        Assert.Equal(PaymentStatus.Paid, (await host.Database.Native.GetAsync(host.Order.Id)).PaymentStatus);
        await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        var reconciled = await host.Database.Native.GetAsync(host.Order.Id); Assert.Equal(10m, reconciled.RefundedAmount); Assert.Equal(PaymentStatus.PartiallyRefunded, reconciled.PaymentStatus);
        Assert.Single(host.Gateway.MutationKeys);
    }

    [Fact]
    public async Task NativeVoidAndPermissionChecksNeverChargeOrBypassCapabilities()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start(); host.Gateway.Status = "AUTHORIZED";
        await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        host.Allowed = false;
        var denied = await host.Plugin.CaptureAsync(new CapturePaymentRequest { Order = await host.Database.Native.GetAsync(host.Order.Id) }); Assert.False(denied.Success); Assert.Empty(host.Gateway.MutationKeys);
        host.Allowed = true;
        var errors = await host.Database.Processing.VoidAsync(await host.Database.Native.GetAsync(host.Order.Id)); Assert.Empty(errors);
        Assert.Equal(PaymentStatus.Voided, (await host.Database.Native.GetAsync(host.Order.Id)).PaymentStatus); Assert.Equal(0, host.Database.PaidEvents);
    }

    [Fact]
    public async Task RealSdkWebhookSignatureBindsNativePaymentAndDurableReplay()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start();
        var id = "native-event-" + host.Order.Id;
        var raw = JsonSerializer.Serialize(new { id, type = "payment.captured", data = new { intentId = "pay-1", capturedAmountMinor = "10050", currency = "TRY" } });
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        Dictionary<string, string> Headers(string content)
        {
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(host.Credentials.WebhookSecret), Encoding.UTF8.GetBytes(id + "." + timestamp + "." + content)));
            return new() { ["x-lunixi-event-id"] = id, ["x-lunixi-signature-timestamp"] = timestamp, ["x-lunixi-signature"] = "v2=" + signature };
        }
        await host.Coordinator.ReceiveAsync(host.Config.ActiveProfileId, Encoding.UTF8.GetBytes(raw), Headers(raw), default);
        await host.Coordinator.ReceiveAsync(host.Config.ActiveProfileId, Encoding.UTF8.GetBytes(raw), Headers(raw), default);
        Assert.Equal(PaymentStatus.Paid, (await host.Database.Native.GetAsync(host.Order.Id)).PaymentStatus); Assert.Equal(1, host.Database.PaidEvents);
        await Assert.ThrowsAsync<GatewayFault>(() => host.Coordinator.ReceiveAsync(host.Config.ActiveProfileId, Encoding.UTF8.GetBytes(raw + " "), Headers(raw), default));
        await Assert.ThrowsAsync<GatewayFault>(() => host.Coordinator.ReceiveAsync(host.Config.ActiveProfileId, Encoding.UTF8.GetBytes(raw + " "), Headers(raw + " "), default));
        Assert.Equal(1, host.Database.PaidEvents);
    }

    [Fact]
    public async Task ConcurrentNativeCaptureRequestsCannotCreateTwoFinancialOperations()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start(); host.Gateway.Status = "AUTHORIZED";
        await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        host.Gateway.MutationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = host.Coordinator.MutateAsync(host.Order.Id, "capture", null, default);
        for (var i = 0; i < 100 && host.Gateway.MutationKeys.Count == 0; i++) await Task.Delay(10);
        Assert.Single(host.Gateway.MutationKeys);
        await Assert.ThrowsAsync<GatewayFault>(() => host.Coordinator.MutateAsync(host.Order.Id, "capture", null, default));
        host.Gateway.MutationGate.SetResult(); var outcome = await first;
        Assert.True(outcome.ApplyThroughNativeCaller); Assert.Single(host.Gateway.MutationKeys);
        // Native IPaymentMethod caller owns application until it acknowledges; a webhook cannot race it.
        await Assert.ThrowsAsync<GatewayFault>(() => host.Coordinator.ReconcileAsync(host.Order.Id, default));
        await host.Coordinator.NativeRequestFinishedAsync(host.Order.Id);
        await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        Assert.Equal(PaymentStatus.Paid, (await host.Database.Native.GetAsync(host.Order.Id)).PaymentStatus);
        Assert.Equal(1, host.Database.PaidEvents); Assert.Single(host.Gateway.MutationKeys);
    }

    [Fact]
    public async Task RotatedEndpointSecretReconcilesAnOlderSigningProfileAndDedupesAcrossEndpoints()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start();
        var originalProfile = host.Config.ActiveProfileId;
        var newProfile = await host.Profiles.SaveAsync(1, new GatewayCredentials { MerchantId = host.Credentials.MerchantId, KeyId = "mk_test_rotated", PrivateKeyPem = CoordinatorHost.SyntheticKey, WebhookSecret = "synthetic-rotated-secret" });
        var id = "rotation-" + host.Order.Id;
        var raw = JsonSerializer.Serialize(new { id, type = "payment.captured", data = new { intentId = "pay-1", capturedAmountMinor = "10050", currency = "TRY" } });
        Dictionary<string, string> Headers(string secret)
        {
            var time = DateTimeOffset.UtcNow.ToString("O");
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(id + "." + time + "." + raw)));
            return new() { ["x-lunixi-event-id"] = id, ["x-lunixi-signature-timestamp"] = time, ["x-lunixi-signature"] = "v2=" + signature };
        }
        await host.Coordinator.ReceiveAsync(newProfile, Encoding.UTF8.GetBytes(raw), Headers("synthetic-rotated-secret"), default);
        await host.Coordinator.ReceiveAsync(originalProfile, Encoding.UTF8.GetBytes(raw), Headers(host.Credentials.WebhookSecret), default);
        Assert.Equal(PaymentStatus.Paid, (await host.Database.Native.GetAsync(host.Order.Id)).PaymentStatus); Assert.Equal(1, host.Database.PaidEvents);
        Assert.Equal(originalProfile, (await host.Database.Ledger.GetActiveAsync(host.Order.Id))!.ProfileId);
    }

    [Fact]
    public async Task ExistingPaymentRetainsCurrencyAndDisplayModeAfterSettingsChange()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start();
        host.CurrentCurrency = "USD"; host.Config.Hosted = false;
        var page = await host.Coordinator.PageAsync(host.Order.Id, 7, 1); Assert.True(page.Hosted);
        await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        Assert.Equal(PaymentStatus.Paid, (await host.Database.Native.GetAsync(host.Order.Id)).PaymentStatus);
        Assert.Equal("TRY", (await host.Database.Ledger.GetActiveAsync(host.Order.Id))!.Currency);
        Assert.Empty(await host.Database.Processing.PartiallyRefundAsync(await host.Database.Native.GetAsync(host.Order.Id), 10m));
        Assert.Equal(1000, host.Gateway.Refunded);
    }

    [Fact]
    public async Task ExpiredOperationKeyIsNeverResubmittedAfterIdempotencyRetention()
    {
        var host = new CoordinatorHost(); await host.Initialize(); await host.Start(); host.Gateway.Status = "AUTHORIZED";
        await host.Coordinator.ReconcileAsync(host.Order.Id, default);
        var attempt = (await host.Database.Ledger.GetActiveAsync(host.Order.Id))!;
        var operation = await host.Database.Ledger.WithOrderAsync(host.Order.Id, () => host.Database.Ledger.AddOperationAsync(attempt.Id, "capture", 10050, 0));
        var old = DateTime.UtcNow.AddHours(-25).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await host.Database.Execute("UPDATE `LunixiOperation` SET `CreatedUtc`='" + old + "' WHERE `Id`=" + operation.Id);
        await Assert.ThrowsAsync<GatewayFault>(() => host.Coordinator.MutateAsync(host.Order.Id, "capture", null, default));
        Assert.Empty(host.Gateway.MutationKeys);
        Assert.Equal(operation.Key, (await host.Database.Ledger.GetPendingOperationAsync(attempt.Id))!.Key);
    }
}
