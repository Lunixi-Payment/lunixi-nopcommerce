using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Payments.Lunixi;
using Nop.Plugin.Payments.Lunixi.Controllers;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Plugin.Payments.Lunixi.Infrastructure;
using Nop.Plugin.Payments.Lunixi.Models;
using Nop.Plugin.Payments.Lunixi.Services;
using Nop.Services.Payments;
using Nop.Web.Framework.Mvc.Filters;

namespace Lunixi.NopCommerce.Tests;

public class PaymentEvidenceTests
{
    internal static readonly OrderIdentity Identity = new("merchant-1", "nop:1:order-1", 10050, "TRY");
    internal static JsonElement Payment(string status = "CAPTURED", long captured = 10050, long refunded = 0, long gross = 10050, string? fingerprint = null, string merchant = "merchant-1", object? split = null)
        => JsonSerializer.SerializeToElement(new { data = new { id = "pay-1", merchantId = merchant, orderId = Identity.Reference, currency = "TRY", amount = gross, status, metadata = new { lunixiCommerce = new { fingerprint } }, details = new { capturedAmount = captured, refundedAmount = refunded }, split } });

    [Fact]
    public void RealNativePaymentInterfaceIsImplemented() => Assert.True(typeof(IPaymentMethod).IsAssignableFrom(typeof(LunixiPaymentProcessor)));

    [Theory]
    [InlineData("AUTHORIZED", 0, 0, "authorized")]
    [InlineData("CAPTURED", 10050, 0, "paid")]
    [InlineData("PARTIALLY_REFUNDED", 10050, 500, "partially_refunded")]
    [InlineData("REFUNDED", 10050, 10050, "refunded")]
    [InlineData("PROCESSING", 0, 0, "pending")]
    [InlineData("PARTIALLY_CAPTURED", 500, 0, "review")]
    [InlineData("FAILED", 0, 0, "review")]
    [InlineData("EXPIRED", 0, 0, "failed")]
    [InlineData("VOIDED", 0, 0, "failed")]
    public void OnlyCompleteAuthenticatedSettlementCanFulfill(string status, long captured, long refunded, string expected)
        => Assert.Equal(expected, PaymentEvidence.Evaluate(Identity, "pay-1", Payment(status, captured, refunded)).State);

    [Fact]
    public void MerchantAndApprovedFeeFingerprintMustMatch()
    {
        Assert.Throws<PaymentReviewException>(() => PaymentEvidence.Evaluate(Identity, "pay-1", Payment(merchant: "other-merchant")));
        Assert.Throws<PaymentReviewException>(() => PaymentEvidence.Evaluate(Identity, "pay-1", Payment(gross: 10350, captured: 10350)));
        var fee = PaymentEvidence.Evaluate(Identity, "pay-1", Payment(gross: 10350, captured: 10350, fingerprint: Identity.Fingerprint));
        Assert.Equal(10350, fee.GrossMinor); Assert.Equal("paid", fee.State);
    }
    [Fact]
    public void SplitRequiresParentCompleteBalancedAggregateAndNoReconciliation()
    {
        var parent = Payment("SETTLED_BY_SPLIT", split: new { role = "PARENT", splitPaymentId = "split-1" });
        JsonElement Aggregate(long captured, bool balanced = true, bool review = false) => JsonSerializer.SerializeToElement(new
        { data = new { id = "split-1", orderId = Identity.Reference, currency = "TRY", status = "COMPLETED", needsReconciliation = review, aggregate = new { totalAmount = 10050, legsAmount = 10050, capturedAmount = captured, compensatedAmount = 0, authorizedAmount = 0, amountsBalanced = balanced }, refund = new { refundedAmount = 0 } } });
        Assert.Throws<PaymentReviewException>(() => PaymentEvidence.Evaluate(Identity, "pay-1", parent));
        Assert.Equal("pending", PaymentEvidence.Evaluate(Identity, "pay-1", parent, Aggregate(500)).State);
        Assert.Throws<PaymentReviewException>(() => PaymentEvidence.Evaluate(Identity, "pay-1", parent, Aggregate(10050, balanced: false)));
        Assert.Throws<PaymentReviewException>(() => PaymentEvidence.Evaluate(Identity, "pay-1", parent, Aggregate(10050, review: true)));
        Assert.Equal("paid", PaymentEvidence.Evaluate(Identity, "pay-1", parent, Aggregate(10050)).State);
        Assert.Throws<PaymentReviewException>(() => PaymentEvidence.Evaluate(Identity, "pay-1", Payment("SETTLED_BY_SPLIT", split: new { role = "LEG", splitPaymentId = "split-1" }), Aggregate(10050)));
    }

    [Theory]
    [InlineData("TRY", "100.50", 10050)]
    [InlineData("JPY", "100", 100)]
    [InlineData("KWD", "1.234", 1234)]
    public void DecimalMoneyUsesCurrencyExponentWithoutFloatingPoint(string currency, string amount, long expected)
        => Assert.Equal(expected, MinorMoney.FromDecimal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency));
    [Fact]
    public void FractionalMinorAndUnsafeValuesAreRejected()
    {
        Assert.Throws<PaymentReviewException>(() => MinorMoney.FromDecimal(1.001m, "TRY"));
        Assert.Throws<PaymentReviewException>(() => MinorMoney.FromDecimal(90_071_992_547_409.92m, "TRY"));
        Assert.Throws<PaymentReviewException>(() => MinorMoney.FromDecimal(-1m, "TRY"));
    }
    [Fact]
    public void SecretEncryptionIsAuthenticatedAndPurposeBound()
    {
        var protection = new SecretProtection(Convert.ToBase64String(new byte[32]));
        var encrypted = protection.Protect("synthetic-secret", "profile:one");
        Assert.DoesNotContain("synthetic-secret", encrypted); Assert.Equal("synthetic-secret", protection.Unprotect(encrypted, "profile:one"));
        Assert.Throws<InvalidOperationException>(() => protection.Unprotect(encrypted, "profile:two"));
        Assert.Throws<InvalidOperationException>(() => protection.Unprotect(encrypted[..^4] + "AAAA", "profile:one"));
        Assert.DoesNotContain("synthetic-secret", new ConfigurationModel { PrivateKeyPem = "synthetic-secret" }.ToString());
        Assert.DoesNotContain("synthetic-secret", JsonSerializer.Serialize(new ConfigurationModel { PrivateKeyPem = "synthetic-secret" }));
    }
    [Fact]
    public void NativeAdminAndFinancialEndpointsHaveCsrfAndPermissionGuards()
    {
        Assert.NotEmpty(typeof(PaymentLunixiController).GetCustomAttributes(typeof(AuthorizeAdminAttribute), true));
        Assert.NotEmpty(typeof(PaymentLunixiController).GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        foreach (var action in typeof(PaymentLunixiController).GetMethods().Where(m => m.Name is "Configure" or "Reconcile"))
            Assert.NotEmpty(action.GetCustomAttributes(typeof(CheckPermissionAttribute), true));
        Assert.NotEmpty(typeof(LunixiCheckoutController).GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.NotEmpty(typeof(LunixiCheckoutController).GetMethod("Webhook")!.GetCustomAttributes(typeof(IgnoreAntiforgeryTokenAttribute), true));
    }
    [Fact]
    public void EnglishAndTurkishResourcesAreComplete() => Assert.Equal(LocaleResources.English.Keys.Order(), LocaleResources.Turkish.Keys.Order());
}
