using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nop.Plugin.Payments.Lunixi.Domain;

public sealed class PaymentReviewException : Exception
{
    public PaymentReviewException() : base("Authenticated payment evidence requires reconciliation.") { }
}

public static class MinorMoney
{
    public const long Maximum = long.MaxValue;
    private static readonly HashSet<string> Zero = ["BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"];
    private static readonly HashSet<string> Three = ["BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"];
    public static int Exponent(string currency)
    {
        if (currency.Length != 3 || currency.Any(c => !char.IsAsciiLetterUpper(c)) || currency is "CLF" or "UYW") throw new PaymentReviewException();
        return Zero.Contains(currency) ? 0 : Three.Contains(currency) ? 3 : 2;
    }
    public static long FromDecimal(decimal amount, string currency) => FromDecimalText(amount.ToString(CultureInfo.InvariantCulture), currency);
    public static long FromDecimalText(string amount, string currency)
    {
        var exponent = Exponent(currency);
        if (!System.Text.RegularExpressions.Regex.IsMatch(amount, @"\A(?:0|[1-9][0-9]*)(?:\.[0-9]+)?\z")) throw new PaymentReviewException();
        var parts = amount.Split('.'); var fraction = parts.Length == 2 ? parts[1] : "";
        if (fraction.Length > exponent && fraction[exponent..].Any(c => c != '0')) throw new PaymentReviewException();
        var text = (parts[0] + fraction[..Math.Min(exponent, fraction.Length)].PadRight(exponent, '0')).TrimStart('0');
        if (text == "") text = "0";
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var exact)) throw new PaymentReviewException();
        return exact;
    }
    public static decimal ToDecimal(long amount, string currency) => amount / (Exponent(currency) switch { 0 => 1m, 3 => 1000m, _ => 100m });
    public static long JsonInteger(JsonElement element)
    {
        long amount;
        if (element.ValueKind == JsonValueKind.String && System.Text.RegularExpressions.Regex.IsMatch(element.GetString()!, @"\A(?:0|[1-9][0-9]*)\z") && long.TryParse(element.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out amount) && amount is >= 0 and <= Maximum) return amount;
        throw new PaymentReviewException();
    }
}

public sealed record OrderIdentity(string MerchantId, string Reference, long AmountMinor, string Currency)
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    { merchantId = MerchantId, orderId = Reference, amount = AmountMinor.ToString(CultureInfo.InvariantCulture), currency = Currency })))).ToLowerInvariant();
}

public sealed record Settlement(string State, long GrossMinor, long CapturedMinor, long RefundedMinor, string? SplitId = null);

public static class PaymentEvidence
{
    public static Settlement Evaluate(OrderIdentity order, string paymentId, JsonElement payment, JsonElement? split = null)
    {
        var p = Data(payment);
        if (Text(p, "id") != paymentId || Text(p, "merchantId") != order.MerchantId || Text(p, "orderId") != order.Reference || Text(p, "currency") != order.Currency)
            throw new PaymentReviewException();
        var amount = Number(p, "amount");
        if (amount < order.AmountMinor) throw new PaymentReviewException();
        if (amount != order.AmountMinor)
        {
            var metadata = p.TryGetProperty("metadata", out var direct) ? direct : default;
            using var parsed = metadata.ValueKind == JsonValueKind.Undefined && Text(p, "metadataJson") is { } json ? ParseMetadata(json) : null;
            if (parsed is not null) metadata = parsed.RootElement;
            if (metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty("lunixiCommerce", out var commerce) || Text(commerce, "fingerprint") != order.Fingerprint)
                throw new PaymentReviewException();
        }
        var status = Text(p, "status");
        if (p.TryGetProperty("split", out var reference) && reference.ValueKind == JsonValueKind.Object || status == "SETTLED_BY_SPLIT")
        {
            if (reference.ValueKind != JsonValueKind.Object || Text(reference, "role") != "PARENT" || Text(reference, "splitPaymentId") is not { Length: > 0 } splitId || split is null)
                throw new PaymentReviewException();
            var s = Data(split.Value); var aggregate = Object(s, "aggregate");
            if (Text(s, "id") != splitId || Text(s, "orderId") != order.Reference || Text(s, "currency") != order.Currency ||
                !IsFalse(s, "needsReconciliation") || !IsTrue(aggregate, "amountsBalanced") || Number(aggregate, "totalAmount") != amount || Number(aggregate, "legsAmount") != amount)
                throw new PaymentReviewException();
            var captured = Number(aggregate, "capturedAmount"); var compensated = Number(aggregate, "compensatedAmount");
            var refunded = Number(Object(s, "refund"), "refundedAmount");
            if (captured > amount || refunded > captured || compensated > captured) throw new PaymentReviewException();
            var state = "pending";
            if (status == "SETTLED_BY_SPLIT" && Text(s, "status") == "COMPLETED" && captured == amount && compensated == 0)
                state = refunded == 0 ? "paid" : refunded == amount ? "refunded" : "partially_refunded";
            if (Text(s, "status") == "VOIDED" && captured == 0 && compensated == 0 && Number(aggregate, "authorizedAmount") == 0) state = "failed";
            return new(state, amount, captured, refunded, splitId);
        }
        var details = payment.TryGetProperty("details", out var outer) ? outer : Object(p, "details");
        var paid = Number(details, "capturedAmount"); var refunds = Number(details, "refundedAmount");
        if (paid > amount || refunds > paid) throw new PaymentReviewException();
        var result = status switch
        {
            "CAPTURED" when paid == amount && refunds == 0 => "paid",
            "AUTHORIZED" when paid == 0 && refunds == 0 => "authorized",
            "REFUNDED" when paid > 0 && paid == refunds => "refunded",
            "PARTIALLY_REFUNDED" when refunds > 0 && refunds < paid => "partially_refunded",
            "EXPIRED" or "VOIDED" when paid == 0 => "failed",
            "INITIATED" or "PROCESSING" or "AWAITING_3D" when paid == 0 => "pending",
            _ => "review",
        };
        // A promissory note can be CAPTURED at issuance without money movement. Fulfillment needs merchant policy.
        if (Text(p, "paymentMethod") == "PROMISSORY_NOTE" && result == "paid") result = "review";
        return new(result, amount, paid, refunds);
    }
    private static JsonDocument ParseMetadata(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { throw new PaymentReviewException(); }
    }
    public static JsonElement Data(JsonElement value) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("data", out var data) ? data : value;
    public static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    public static JsonElement Object(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.Object) throw new PaymentReviewException();
        return item;
    }
    public static long Number(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var item)) throw new PaymentReviewException();
        return MinorMoney.JsonInteger(item);
    }
    private static bool IsTrue(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.True;
    private static bool IsFalse(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.False;
}
