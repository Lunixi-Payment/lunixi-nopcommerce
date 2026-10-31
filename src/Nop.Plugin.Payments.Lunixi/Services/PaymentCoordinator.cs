using System.Text.Json;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Plugin.Payments.Lunixi.Data;
using Nop.Plugin.Payments.Lunixi.Domain;

namespace Nop.Plugin.Payments.Lunixi.Services;

public sealed record NativeMutationResult(bool ApplyThroughNativeCaller, Settlement Settlement, string PaymentId);

public sealed class PaymentCoordinator(PaymentLedger ledger, CredentialProfiles profiles, ILunixiGatewayFactory gateways,
    NativeOrders native, ISettingService settings, ICurrencyService currencies, CurrencySettings currencySettings, IAddressService addresses, ICountryService countries, ILanguageService languages)
{
    public async Task<string> CurrencyAsync(int orderId) => (await ledger.GetActiveAsync(orderId) ?? throw new PaymentReviewException()).Currency;
    public async Task EnsureOwnerAsync(int orderId, int customerId, int storeId) => Owner(await native.GetAsync(orderId), customerId, storeId);
    private static string Reference(Order order) => "nop:" + order.StoreId + ":" + order.OrderGuid.ToString("N");
    private async Task<OrderIdentity> Identity(Order order, string merchant, string? persistedCurrency = null)
    {
        var code = persistedCurrency ?? (await currencies.GetCurrencyByIdAsync(currencySettings.PrimaryStoreCurrencyId) ?? throw new PaymentReviewException()).CurrencyCode;
        var amount = MinorMoney.FromDecimal(order.OrderTotal, code);
        if (amount < 1) throw new PaymentReviewException();
        return new(merchant, Reference(order), amount, code);
    }
    private static void Owner(Order order, int customerId, int storeId)
    {
        if (order.CustomerId != customerId || order.StoreId != storeId || order.PaymentMethodSystemName != "Payments.Lunixi" || order.OrderStatus == OrderStatus.Cancelled || order.Deleted)
            throw new PaymentReviewException();
    }
    public async Task<CheckoutPage> StartAsync(int orderId, int customerId, int storeId, string callbackUrl, CancellationToken cancellationToken)
    {
        var config = await settings.LoadSettingAsync<LunixiSettings>(storeId);
        if (!config.Enabled || !Uri.TryCreate(callbackUrl, UriKind.Absolute, out var callback) || callback.Scheme != "https") throw new PaymentReviewException();
        var profile = await profiles.GetAsync(config.ActiveProfileId);
        if (profile.StoreId != 0 && profile.StoreId != storeId) throw new PaymentReviewException();
        var attempt = await ledger.WithOrderAsync(orderId, async () =>
        {
            var order = await native.GetAsync(orderId); Owner(order, customerId, storeId);
            if (order.PaymentStatus != PaymentStatus.Pending) throw new PaymentReviewException();
            var current = await ledger.GetActiveAsync(orderId);
            var identity = await Identity(order, profile.Credentials.MerchantId, current?.Currency);
            if (current is not null && current.State != "failed")
            {
                if (current.Fingerprint != identity.Fingerprint || current.ProfileId != config.ActiveProfileId) throw new PaymentReviewException();
                return current;
            }
            var address = await addresses.GetAddressByIdAsync(order.BillingAddressId) ?? throw new PaymentReviewException();
            var country = address.CountryId is { } countryId ? await countries.GetCountryByIdAsync(countryId) : null;
            var language = await languages.GetLanguageByIdAsync(order.CustomerLanguageId);
            var metadata = new Dictionary<string, object> { ["lunixiCommerce"] = new { fingerprint = identity.Fingerprint } };
            if (config.Hosted) metadata["allowedOrigin"] = "https://pay.lunixi.com";
            var buyer = new Dictionary<string, object> { ["id"] = order.CustomerId.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            void Prefill(string name, string? value, int maximum)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (value.Length > maximum || value.Any(char.IsControl)) throw new PaymentReviewException();
                buyer[name] = value;
            }
            Prefill("name", address.FirstName, 120); Prefill("surname", address.LastName, 120); Prefill("email", address.Email, 254);
            Prefill("gsmNumber", address.PhoneNumber, 32); Prefill("city", address.City, 120); Prefill("country", country?.Name, 120);
            Prefill("zipCode", address.ZipPostalCode, 20); Prefill("ip", order.CustomerIp, 64);
            var body = new Dictionary<string, object>
            {
                ["amount"] = identity.AmountMinor,
                ["currency"] = identity.Currency,
                ["orderId"] = identity.Reference,
                ["callbackUrl"] = callbackUrl,
                ["preauth"] = config.Preauthorize,
                ["buyer"] = buyer,
                ["locale"] = language?.LanguageCulture.StartsWith("tr", StringComparison.OrdinalIgnoreCase) == true ? "tr" : "en",
                ["basketItems"] = new[] { new { id = identity.Reference, name = order.CustomOrderNumber, price = identity.AmountMinor, itemType = "PHYSICAL" } },
                ["metadata"] = metadata,
            };
            var line = string.Join(' ', new[] { address.Address1, address.Address2 }.Where(v => !string.IsNullOrWhiteSpace(v)));
            var contact = string.Join(' ', new[] { address.FirstName, address.LastName }.Where(v => !string.IsNullOrWhiteSpace(v)));
            // AddressDto requires all five values. An incomplete native address is omitted, never fabricated.
            if (new[] { line, contact, address.City, address.ZipPostalCode, country?.Name }.All(v => !string.IsNullOrWhiteSpace(v)))
                body["billingAddress"] = new { address = line, contactName = contact, city = address.City, zipCode = address.ZipPostalCode, country = country!.Name };
            var payload = JsonSerializer.Serialize(body);
            var created = new CheckoutAttempt
            {
                NativeOrderId = orderId,
                ProfileId = config.ActiveProfileId,
                MerchantId = identity.MerchantId,
                OrderReference = identity.Reference,
                BaseMinor = identity.AmountMinor,
                Currency = identity.Currency,
                Fingerprint = identity.Fingerprint,
                OperationKey = "nop:checkout:" + Guid.NewGuid().ToString("N"),
                CreatedUtc = DateTime.UtcNow,
            };
            created.PayloadCipher = profiles.Encrypt(payload, "intent:" + created.OperationKey);
            created.Id = await ledger.AddAttemptAsync(created); return created;
        });
        if (attempt.PaymentId is null)
        {
            if (DateTime.UtcNow - attempt.CreatedUtc > TimeSpan.FromHours(23)) throw new PaymentReviewException();
            using var payload = JsonDocument.Parse(profiles.Decrypt(attempt.PayloadCipher, "intent:" + attempt.OperationKey));
            var response = await gateways.Create(profile.Credentials).CreateIntentAsync(payload.RootElement, attempt.OperationKey, cancellationToken);
            if (response.Data.ValueKind != JsonValueKind.Object) throw new PaymentReviewException();
            var paymentId = PaymentEvidence.Text(response.Data, "paymentId"); var token = PaymentEvidence.Text(response.Data, "token");
            if (string.IsNullOrWhiteSpace(paymentId) || paymentId.Length > 256 || string.IsNullOrWhiteSpace(token) || token.Length > 16384 || token.Any(c => c < 0x21 || c > 0x7e) ||
                response.Data.TryGetProperty("intentId", out _) && PaymentEvidence.Text(response.Data, "intentId") != paymentId) throw new PaymentReviewException();
            var pageUrl = "";
            if (response.Data.TryGetProperty("paymentPageUrl", out var backendUrl))
            {
                if (backendUrl.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw new PaymentReviewException();
                pageUrl = CheckoutSecrets.ValidatePageUrl(backendUrl.ValueKind == JsonValueKind.Null ? null : backendUrl.GetString());
            }
            await ledger.WithOrderAsync(orderId, async () =>
            {
                await ledger.BindPaymentAsync(attempt, paymentId, profiles.Encrypt(CheckoutSecrets.Encode(token, pageUrl), "token:" + attempt.OperationKey)); return true;
            });
        }
        return await PageAsync(orderId, customerId, storeId);
    }

    public async Task<CheckoutPage> PageAsync(int orderId, int customerId, int storeId)
    {
        var order = await native.GetAsync(orderId); Owner(order, customerId, storeId);
        var attempt = await ledger.GetActiveAsync(orderId) ?? throw new PaymentReviewException();
        if (attempt.State is "paid" or "refunded" or "partially_refunded" or "authorized" || attempt.TokenCipher is null || DateTime.UtcNow - attempt.CreatedUtc > TimeSpan.FromMinutes(14)) throw new PaymentReviewException();
        var profile = await profiles.GetAsync(attempt.ProfileId);
        var identity = await Identity(order, attempt.MerchantId, attempt.Currency);
        if (identity.Fingerprint != attempt.Fingerprint) throw new PaymentReviewException();
        using var request = JsonDocument.Parse(profiles.Decrypt(attempt.PayloadCipher, "intent:" + attempt.OperationKey));
        var hosted = request.RootElement.TryGetProperty("metadata", out var metadata) && PaymentEvidence.Text(metadata, "allowedOrigin") == "https://pay.lunixi.com";
        var secrets = CheckoutSecrets.Decode(profiles.Decrypt(attempt.TokenCipher, "token:" + attempt.OperationKey));
        return new(secrets.Token, profile.Credentials.PublishableKey, hosted, orderId, secrets.PaymentPageUrl);
    }

    private async Task<Settlement> ReadSettlement(CheckoutAttempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.PaymentId is null) throw new PaymentReviewException();
        var profile = await profiles.GetAsync(attempt.ProfileId); var gateway = gateways.Create(profile.Credentials);
        var payment = await gateway.GetPaymentAsync(attempt.PaymentId, cancellationToken);
        if (payment.Data.ValueKind != JsonValueKind.Object) throw new PaymentReviewException();
        JsonElement? split = null;
        if (payment.Data.TryGetProperty("split", out var reference) && reference.ValueKind == JsonValueKind.Object && PaymentEvidence.Text(reference, "splitPaymentId") is { Length: > 0 } splitId)
            split = (await gateway.GetSplitAsync(splitId, cancellationToken)).Envelope;
        return PaymentEvidence.Evaluate(attempt.Identity, attempt.PaymentId, payment.Envelope, split);
    }
    public async Task ReconcileAsync(int orderId, CancellationToken cancellationToken, VerifiedDelivery? delivery = null)
    {
        var attempt = await ledger.GetActiveAsync(orderId) ?? throw new PaymentReviewException();
        if (delivery is not null)
        {
            var paymentId = PaymentEvidence.Text(delivery.Data, "paymentId") ?? PaymentEvidence.Text(delivery.Data, "intentId");
            if (paymentId != attempt.PaymentId || delivery.Data.TryGetProperty("paymentId", out _) && delivery.Data.TryGetProperty("intentId", out _) && PaymentEvidence.Text(delivery.Data, "paymentId") != PaymentEvidence.Text(delivery.Data, "intentId"))
                throw new GatewayFault("unmatched_payment", statusCode: 503);
        }
        if (delivery is not null && await ledger.InboxCompletedAsync(attempt.ProfileId, delivery.Id, delivery.BodyHash)) return;
        var evidence = await ReadSettlement(attempt, cancellationToken);
        await ledger.WithOrderAsync(orderId, async () =>
        {
            var current = await ledger.GetActiveAsync(orderId) ?? throw new PaymentReviewException();
            if (current.Id != attempt.Id) throw new PaymentReviewException();
            if (delivery is not null && await ledger.InboxCompletedAsync(current.ProfileId, delivery.Id, delivery.BodyHash)) return true;
            var order = await native.GetAsync(orderId);
            if ((await Identity(order, current.MerchantId, current.Currency)).Fingerprint != current.Fingerprint) throw new PaymentReviewException();
            var operation = await ledger.GetPendingOperationAsync(current.Id);
            if (operation?.State == "native_pending")
            {
                // Core applies the returned result after the IPaymentMethod call. Never race that write.
                if (!NativeAlreadyReflects(order, evidence, current.Currency)) throw new GatewayFault("native_application_pending", statusCode: 503);
                await ledger.SetOperationStateAsync(operation.Id, "applied");
            }
            if (current.RefundedMinor <= evidence.RefundedMinor && !(current.State is "paid" or "partially_refunded" or "refunded" && evidence.State is "pending" or "authorized" or "failed" or "review"))
            {
                await native.ApplyAsync(order, current, evidence);
                await ledger.UpdateSettlementAsync(current.Id, evidence);
                if (operation is not null && OperationComplete(operation, evidence)) await ledger.SetOperationStateAsync(operation.Id, "applied");
            }
            if (delivery is not null) await ledger.CompleteInboxAsync(current.ProfileId, delivery.Id, delivery.BodyHash);
            return true;
        });
    }

    public async Task ReceiveAsync(string profileId, byte[] raw, IEnumerable<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetAsync(profileId);
        var delivery = gateways.Create(profile.Credentials).VerifyWebhook(raw, headers, profile.Credentials.WebhookSecret);
        var paymentId = PaymentEvidence.Text(delivery.Data, "paymentId") ?? PaymentEvidence.Text(delivery.Data, "intentId");
        if (paymentId is null || delivery.Data.ValueKind != JsonValueKind.Object || delivery.Data.TryGetProperty("splitLeg", out _) ||
            delivery.Data.TryGetProperty("paymentId", out _) && delivery.Data.TryGetProperty("intentId", out _) && PaymentEvidence.Text(delivery.Data, "paymentId") != PaymentEvidence.Text(delivery.Data, "intentId")) throw new GatewayFault("unmatched_payment", statusCode: 503);
        // Endpoint-secret rotation and signing-key rotation are independent. All endpoint profiles for
        // the same merchant may deliver an event; dedupe and query with the attempt's immutable profile.
        var attempt = await ledger.FindPaymentAsync(profile.Credentials.MerchantId, paymentId) ?? throw new GatewayFault("unmatched_payment", statusCode: 503);
        if ((await ledger.GetActiveAsync(attempt.NativeOrderId))?.Id != attempt.Id) throw new GatewayFault("inactive_attempt", statusCode: 503);
        await ReconcileAsync(attempt.NativeOrderId, cancellationToken, delivery);
    }

    public async Task<NativeMutationResult> MutateAsync(int orderId, string kind, long? requestedMinor, CancellationToken cancellationToken)
    {
        var attempt = await ledger.GetActiveAsync(orderId) ?? throw new PaymentReviewException();
        var before = await ReadSettlement(attempt, cancellationToken);
        var amount = kind == "void" ? 0 : requestedMinor ?? (kind == "refund" ? before.GrossMinor - before.RefundedMinor : before.GrossMinor);
        // The last native basket refund includes any remaining authenticated installment fee.
        if (kind == "refund" && requestedMinor == attempt.BaseMinor - Math.Min(attempt.BaseMinor, before.RefundedMinor))
            amount = before.GrossMinor - before.RefundedMinor;
        if (kind is not ("capture" or "refund" or "void") || amount < 0 || amount > MinorMoney.Maximum || kind != "void" && amount == 0) throw new PaymentReviewException();
        if (kind == "refund" && amount > before.GrossMinor - before.RefundedMinor || kind == "capture" && amount != before.GrossMinor || before.RefundedMinor < attempt.RefundedMinor) throw new PaymentReviewException();
        var operation = await ledger.WithOrderAsync(orderId, async () =>
        {
            var current = await ledger.GetActiveAsync(orderId) ?? throw new PaymentReviewException();
            if (current.Id != attempt.Id || current.PaymentId is null) throw new PaymentReviewException();
            var order = await native.GetAsync(orderId);
            if ((await Identity(order, current.MerchantId, current.Currency)).Fingerprint != current.Fingerprint || order.PaymentMethodSystemName != "Payments.Lunixi") throw new PaymentReviewException();
            if (kind == "capture" && order.OrderStatus == OrderStatus.Cancelled ||
                kind is "capture" or "void" && order.PaymentStatus != PaymentStatus.Authorized ||
                kind == "refund" && order.PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.PartiallyRefunded) ||
                MinorMoney.FromDecimal(order.RefundedAmount, current.Currency) > Math.Min(current.BaseMinor, before.RefundedMinor)) throw new PaymentReviewException();
            if (kind == "capture" && before.State != "authorized" || kind == "refund" && before.State is not ("paid" or "partially_refunded") || kind == "void" && before.State != "authorized") throw new PaymentReviewException();
            var pending = await ledger.GetPendingOperationAsync(attempt.Id);
            if (pending is not null && (DateTime.UtcNow - pending.CreatedUtc > TimeSpan.FromHours(23) || pending.CreatedUtc > DateTime.UtcNow.AddMinutes(5)))
                throw new GatewayFault("operation_reconciliation_required", statusCode: 409);
            if (pending is not null && (pending.State is "sending" or "native_pending" || pending.Kind != kind || pending.AmountMinor != amount || pending.BaselineRefunded != before.RefundedMinor))
                throw new GatewayFault("operation_pending", statusCode: 409);
            var value = pending ?? await ledger.AddOperationAsync(attempt.Id, kind, amount, before.RefundedMinor);
            await ledger.SetOperationStateAsync(value.Id, "sending"); return value;
        });
        var profile = await profiles.GetAsync(attempt.ProfileId);
        try
        {
            await gateways.Create(profile.Credentials).MutateAsync(attempt.PaymentId!, kind, kind == "void" ? null : amount, operation.Key, cancellationToken);
            var after = await ReadSettlement(attempt, cancellationToken);
            if (!OperationComplete(operation, after)) throw new GatewayFault("outcome_pending", outcomeUnknown: true);
            var nativeApply = await ledger.WithOrderAsync(orderId, async () =>
            {
                var current = await ledger.GetPendingOperationAsync(attempt.Id);
                if (current is null) return false;
                var order = await native.GetAsync(orderId);
                if (NativeAlreadyReflects(order, after, attempt.Currency)) { await ledger.SetOperationStateAsync(operation.Id, "applied"); return false; }
                await ledger.UpdateSettlementAsync(attempt.Id, after);
                await ledger.SetOperationStateAsync(operation.Id, "native_pending"); return true;
            });
            return new(nativeApply, after, attempt.PaymentId!);
        }
        catch
        {
            await ledger.WithOrderAsync(orderId, async () =>
            {
                var current = await ledger.GetPendingOperationAsync(attempt.Id);
                if (current?.Id == operation.Id && current.State == "sending") await ledger.SetOperationStateAsync(operation.Id, "unknown");
                return true;
            });
            throw;
        }
    }
    public async Task AcknowledgeNativeAsync(int orderId)
    {
        var attempt = await ledger.GetActiveAsync(orderId);
        if (attempt is null) return;
        var operation = await ledger.GetPendingOperationAsync(attempt.Id);
        if (operation?.State != "native_pending") return;
        var order = await native.GetAsync(orderId);
        var expected = new Settlement(attempt.State, attempt.GrossMinor, attempt.GrossMinor, attempt.RefundedMinor);
        if (NativeAlreadyReflects(order, expected, attempt.Currency)) await ledger.SetOperationStateAsync(operation.Id, "applied");
    }
    public async Task NativeRequestFinishedAsync(int orderId)
    {
        await ledger.WithOrderAsync(orderId, async () =>
        {
            var attempt = await ledger.GetActiveAsync(orderId);
            if (attempt is null) return false;
            var operation = await ledger.GetPendingOperationAsync(attempt.Id);
            if (operation?.State != "native_pending") return false;
            var expected = new Settlement(attempt.State, attempt.GrossMinor, attempt.GrossMinor, attempt.RefundedMinor);
            var order = await native.GetAsync(orderId);
            await ledger.SetOperationStateAsync(operation.Id, NativeAlreadyReflects(order, expected, attempt.Currency) ? "applied" : "unknown");
            return true;
        });
    }
    private static bool NativeAlreadyReflects(Order order, Settlement evidence, string currency) => evidence.State switch
    {
        "paid" => order.PaymentStatus == PaymentStatus.Paid,
        "refunded" => order.PaymentStatus == PaymentStatus.Refunded,
        "partially_refunded" => order.PaymentStatus == PaymentStatus.PartiallyRefunded && MinorMoney.FromDecimal(order.RefundedAmount, currency) >= Math.Min(MinorMoney.FromDecimal(order.OrderTotal, currency), evidence.RefundedMinor),
        "failed" => order.PaymentStatus == PaymentStatus.Voided,
        _ => false,
    };
    private static bool OperationComplete(PendingOperation operation, Settlement evidence) => operation.Kind switch
    {
        "capture" => evidence.State == "paid" && evidence.CapturedMinor >= operation.AmountMinor,
        "refund" => evidence.State is "refunded" or "partially_refunded" && evidence.RefundedMinor >= operation.BaselineRefunded + operation.AmountMinor,
        "void" => evidence.State == "failed" && evidence.CapturedMinor == 0,
        _ => false,
    };
}
