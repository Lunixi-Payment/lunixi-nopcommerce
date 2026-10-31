using System.Text.Json;
using Nop.Plugin.Payments.Lunixi.Data;
using Nop.Plugin.Payments.Lunixi.Domain;

namespace Nop.Plugin.Payments.Lunixi.Services;

public sealed class CredentialProfiles(PaymentLedger ledger, ILunixiGatewayFactory gateways)
{
    public static bool ServerKeyConfigured
    {
        get
        {
            try { return Convert.FromBase64String(Environment.GetEnvironmentVariable("LUNIXI_NOP_ENCRYPTION_KEY") ?? "").Length == 32; }
            catch (FormatException) { return false; }
        }
    }
    private static SecretProtection Protector => new(Environment.GetEnvironmentVariable("LUNIXI_NOP_ENCRYPTION_KEY") ?? "");
    public async Task<(int StoreId, GatewayCredentials Credentials)> GetAsync(string profileId)
    {
        if (!Guid.TryParseExact(profileId, "N", out _)) throw new PaymentReviewException();
        var row = await ledger.GetProfileAsync(profileId) ?? throw new PaymentReviewException();
        var credentials = JsonSerializer.Deserialize<GatewayCredentials>(Protector.Unprotect(row.Cipher, "profile:" + profileId)) ?? throw new PaymentReviewException();
        if (credentials.ProfileId != profileId) throw new PaymentReviewException();
        return (row.StoreId, credentials);
    }
    public async Task<string> SaveAsync(int storeId, GatewayCredentials input)
    {
        if (input.BaseUrl != "https://api-gateway.lunixi.com" || !Safe(input.MerchantId, 256) || !Safe(input.KeyId, 512) ||
            string.IsNullOrWhiteSpace(input.PrivateKeyPem) || string.IsNullOrWhiteSpace(input.WebhookSecret) || input.WebhookSecret.Length > 4096)
            throw new PaymentReviewException();
        var id = Guid.NewGuid().ToString("N");
        var credentials = new GatewayCredentials { ProfileId = id, MerchantId = input.MerchantId, KeyId = input.KeyId, PrivateKeyPem = input.PrivateKeyPem, WebhookSecret = input.WebhookSecret, PublishableKey = input.PublishableKey };
        _ = gateways.Create(credentials); // Validate the actual SDK key import before storing credentials.
        await ledger.SaveProfileAsync(id, storeId, Protector.Protect(JsonSerializer.Serialize(credentials), "profile:" + id));
        return id;
    }
    public string Encrypt(string value, string purpose) => Protector.Protect(value, purpose);
    public string Decrypt(string value, string purpose) => Protector.Unprotect(value, purpose);
    private static bool Safe(string? value, int max) => value is { Length: > 0 } && value.Length <= max && value.All(c => c >= 0x21 && c <= 0x7e);
}
