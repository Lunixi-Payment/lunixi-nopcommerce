using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Moq;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Plugin.Payments.Lunixi;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Plugin.Payments.Lunixi.Services;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Services.Localization;
using Nop.Core.Domain.Localization;

namespace Lunixi.NopCommerce.Tests;

internal sealed class CoordinatorHost
{
    public readonly NativeDatabase Database = new();
    public readonly FakeGatewayFactory Factory = new();
    public readonly LunixiSettings Config = new() { Enabled = true };
    public CredentialProfiles Profiles { get; private set; } = null!;
    public PaymentCoordinator Coordinator { get; private set; } = null!;
    public GatewayCredentials Credentials { get; private set; } = null!;
    public LunixiPaymentProcessor Plugin { get; private set; } = null!;
    public Order Order { get; private set; } = null!;
    public bool Allowed { get; set; } = true;
    public string CurrentCurrency { get; set; } = "TRY";
    public async Task Initialize()
    {
        await Database.Initialize(); Order = await Database.Order();
        Environment.SetEnvironmentVariable("LUNIXI_NOP_ENCRYPTION_KEY", Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray()));
        Profiles = new(Database.Ledger, Factory);
        Config.ActiveProfileId = await Profiles.SaveAsync(1, new GatewayCredentials { MerchantId = "merchant-" + Order.OrderGuid.ToString("N"), KeyId = "mk_test_synthetic", PrivateKeyPem = SyntheticKey, WebhookSecret = "synthetic-webhook-secret" });
        Credentials = (await Profiles.GetAsync(Config.ActiveProfileId)).Credentials;
        var settings = new Mock<ISettingService>(); settings.Setup(s => s.LoadSettingAsync<LunixiSettings>(It.IsAny<int>())).ReturnsAsync(Config);
        var currencies = new Mock<ICurrencyService>(); currencies.Setup(c => c.GetCurrencyByIdAsync(It.IsAny<int>())).ReturnsAsync(() => new Currency { CurrencyCode = CurrentCurrency });
        var addresses = new Mock<IAddressService>(); addresses.Setup(a => a.GetAddressByIdAsync(It.IsAny<int>())).ReturnsAsync(new Address { FirstName = "Ada", LastName = "Example", Email = "buyer@example.invalid", PhoneNumber = "+905350000000", City = "Istanbul", Address1 = "Synthetic test address", ZipPostalCode = "34000", CountryId = 90 });
        var countries = new Mock<ICountryService>(); countries.Setup(c => c.GetCountryByIdAsync(It.IsAny<int>())).ReturnsAsync(new Country { Name = "Turkey" });
        var languages = new Mock<ILanguageService>(); languages.Setup(l => l.GetLanguageByIdAsync(It.IsAny<int>())).ReturnsAsync(new Language { LanguageCulture = "en-US" });
        Coordinator = new(Database.Ledger, Profiles, Factory, Database.Native, settings.Object, currencies.Object, new CurrencySettings { PrimaryStoreCurrencyId = 1 }, addresses.Object, countries.Object, languages.Object);
        var permission = new Mock<IPermissionService>(); permission.Setup(p => p.AuthorizeAsync(It.IsAny<string>())).ReturnsAsync(() => Allowed);
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var constructor = typeof(LunixiPaymentProcessor).GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(p => p.ParameterType == typeof(PaymentCoordinator) ? Coordinator :
            p.ParameterType == typeof(IPermissionService) ? permission.Object : p.ParameterType == typeof(IHttpContextAccessor) ? http :
            p.ParameterType == typeof(ISettingService) ? settings.Object : EmptyDefaults.Value(p.ParameterType)).ToArray();
        Plugin = (LunixiPaymentProcessor)constructor.Invoke(arguments);
        Database.Payments.Setup(p => p.SupportCaptureAsync(It.IsAny<string>())).ReturnsAsync(true);
        Database.Payments.Setup(p => p.SupportRefundAsync(It.IsAny<string>())).ReturnsAsync(true);
        Database.Payments.Setup(p => p.SupportPartiallyRefundAsync(It.IsAny<string>())).ReturnsAsync(true);
        Database.Payments.Setup(p => p.SupportVoidAsync(It.IsAny<string>())).ReturnsAsync(true);
        Database.Payments.Setup(p => p.CaptureAsync(It.IsAny<CapturePaymentRequest>())).Returns((CapturePaymentRequest request) => Plugin.CaptureAsync(request));
        Database.Payments.Setup(p => p.RefundAsync(It.IsAny<RefundPaymentRequest>())).Returns((RefundPaymentRequest request) => Plugin.RefundAsync(request));
        Database.Payments.Setup(p => p.VoidAsync(It.IsAny<VoidPaymentRequest>())).Returns((VoidPaymentRequest request) => Plugin.VoidAsync(request));
        Database.Publisher.Setup(p => p.PublishAsync(It.IsAny<EntityUpdatedEvent<Order>>())).Returns((EntityUpdatedEvent<Order> value) => Coordinator.AcknowledgeNativeAsync(value.Entity.Id));
    }
    public Task<CheckoutPage> Start(int customerId = 7, int storeId = 1) => Coordinator.StartAsync(Order.Id, customerId, storeId, "https://merchant.example/lunixi/return/" + Order.Id, CancellationToken.None);
    public FakeGateway Gateway => Factory.Gateway;
    public static string SyntheticKey => System.Security.Cryptography.PemEncoding.WriteString("PRIVATE KEY", Convert.FromHexString("302e020100300506032b657004220420").Concat(Enumerable.Repeat((byte)7, 32)).ToArray());
}

internal sealed class FakeGatewayFactory : ILunixiGatewayFactory
{
    public FakeGateway Gateway { get; } = new();
    public ILunixiGateway Create(GatewayCredentials credentials) { Gateway.MerchantId = credentials.MerchantId; Gateway.Credentials = credentials; return Gateway; }
}

internal sealed class FakeGateway : ILunixiGateway
{
    private JsonElement body;
    public string MerchantId { get; set; } = "merchant-1";
    public GatewayCredentials Credentials { get; set; } = null!;
    public string Status { get; set; } = "CAPTURED";
    public long Refunded { get; set; }
    public bool FailCreateOnce { get; set; }
    public bool FailMutationOnce { get; set; }
    public bool ConflictAlias { get; set; }
    public string? PaymentPageUrl { get; set; } = "https://pay.lunixi.com/payment/separate-hosted-token-12345";
    public long Fee { get; set; }
    public List<string> CreateKeys { get; } = [];
    public List<string> MutationKeys { get; } = [];
    public TaskCompletionSource? MutationGate { get; set; }
    public Task<GatewayReply> CreateIntentAsync(JsonElement input, string key, CancellationToken cancellationToken)
    {
        body = input.Clone(); CreateKeys.Add(key);
        if (FailCreateOnce) { FailCreateOnce = false; throw new GatewayFault(outcomeUnknown: true); }
        return Task.FromResult(Reply(new { paymentId = "pay-1", token = "synthetic-checkout-token", paymentPageUrl = PaymentPageUrl, intentId = ConflictAlias ? "conflicting-id" : "pay-1" }));
    }
    public Task<GatewayReply> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
    {
        if (paymentId != "pay-1") throw new GatewayFault(statusCode: 404);
        var amount = body.GetProperty("amount").GetInt64() + Fee;
        return Task.FromResult(Reply(new
        {
            id = "pay-1",
            merchantId = MerchantId,
            orderId = body.GetProperty("orderId").GetString(),
            currency = body.GetProperty("currency").GetString(),
            amount,
            status = Status,
            metadata = body.GetProperty("metadata"),
            details = new { capturedAmount = Status is "AUTHORIZED" or "VOIDED" or "EXPIRED" or "FAILED" ? 0 : amount, refundedAmount = Refunded }
        }));
    }
    public Task<GatewayReply> GetSplitAsync(string splitId, CancellationToken cancellationToken) => throw new GatewayFault("not_configured");
    public async Task<GatewayReply> MutateAsync(string paymentId, string operation, long? amount, string key, CancellationToken cancellationToken)
    {
        MutationKeys.Add(key);
        if (MutationGate is not null) await MutationGate.Task.WaitAsync(cancellationToken);
        if (operation == "capture") Status = "CAPTURED";
        if (operation == "void") Status = "VOIDED";
        if (operation == "refund") { Refunded += amount!.Value; Status = Refunded == body.GetProperty("amount").GetInt64() + Fee ? "REFUNDED" : "PARTIALLY_REFUNDED"; }
        if (FailMutationOnce) { FailMutationOnce = false; throw new GatewayFault(outcomeUnknown: true); }
        return Reply(new { status = "PENDING" }); // The coordinator must ignore this and query settlement.
    }
    public VerifiedDelivery VerifyWebhook(byte[] raw, IEnumerable<KeyValuePair<string, string>> headers, string secret)
    {
        using var bridge = new global::Lunixi.NopCommerce.Gateway.GatewayClient(Credentials, new ForbiddenNetwork());
        return bridge.VerifyWebhook(raw, headers, secret);
    }
    public void Dispose() { }
    private static GatewayReply Reply(object data) => new(200, JsonSerializer.SerializeToElement(new { data }), "req-synthetic");
    private sealed class ForbiddenNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("No network request expected during webhook signature verification.");
    }
}
