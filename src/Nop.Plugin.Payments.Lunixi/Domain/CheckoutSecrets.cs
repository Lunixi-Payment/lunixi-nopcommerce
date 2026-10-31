using System.Text.Json;

namespace Nop.Plugin.Payments.Lunixi.Domain;

/// <summary>Versioned plaintext stored only inside the existing authenticated token cipher.</summary>
public static class CheckoutSecrets
{
    // Legacy tokens reject all control characters; this prefix cannot be a legacy token.
    private const string Prefix = "\0lunixi-checkout-v2\0";

    public static string ValidatePageUrl(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        const string originPath = "https://pay.lunixi.com/payment/";
        if (!value.StartsWith(originPath, StringComparison.Ordinal) || value.Length < originPath.Length + 16 ||
            value.Length > originPath.Length + 4096 || value.Skip(originPath.Length).Any(c =>
                !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '~' or '-')))
            throw new PaymentReviewException();
        return value;
    }

    public static string RequirePageUrl(string value)
    {
        var url = ValidatePageUrl(value);
        return url.Length > 0 ? url : throw new PaymentReviewException();
    }

    public static string Encode(string token, string paymentPageUrl)
        => Prefix + JsonSerializer.Serialize(new { token, paymentPageUrl = ValidatePageUrl(paymentPageUrl) });

    public static (string Token, string PaymentPageUrl) Decode(string value)
    {
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return (value, "");
        try
        {
            using var json = JsonDocument.Parse(value[Prefix.Length..]);
            var token = json.RootElement.GetProperty("token").GetString();
            var url = json.RootElement.GetProperty("paymentPageUrl").GetString();
            if (string.IsNullOrEmpty(token) || token.Length > 16384 || token.Any(c => c < 0x21 || c > 0x7e))
                throw new PaymentReviewException();
            return (token, ValidatePageUrl(url));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new PaymentReviewException(); }
    }
}
