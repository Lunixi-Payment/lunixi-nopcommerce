using System.Text.Json;

namespace Nop.Plugin.Payments.Lunixi.Domain;

// Keep the SDK boundary free of SDK or NSec types: the vendor context is private to this plugin.
public sealed class GatewayCredentials
{
    public string ProfileId { get; init; } = "";
    public string BaseUrl { get; init; } = "https://api-gateway.lunixi.com";
    public required string MerchantId { get; init; }
    public required string KeyId { get; init; }
    public string PublishableKey { get; init; } = "";
    public string PrivateKeyPem { get; init; } = "";
    public string WebhookSecret { get; init; } = "";
    public override string ToString() => "GatewayCredentials { Credentials = [REDACTED] }";
}

public sealed record GatewayReply(int StatusCode, JsonElement Envelope, string? RequestId)
{
    public JsonElement Data => Envelope.ValueKind == JsonValueKind.Object && Envelope.TryGetProperty("data", out var data) ? data : Envelope;
    public override string ToString() => "GatewayReply { Data = [REDACTED] }";
}

public sealed record VerifiedDelivery(string Id, string Type, string BodyHash, JsonElement Envelope)
{
    public JsonElement Data => Envelope.ValueKind == JsonValueKind.Object && Envelope.TryGetProperty("data", out var data) ? data : Envelope;
    public override string ToString() => $"VerifiedDelivery {{ Type = {Type}, Data = [REDACTED] }}";
}

public sealed class GatewayFault(string? code = null, string? requestId = null, int? statusCode = null, bool outcomeUnknown = false)
    : Exception("Lunixi request could not establish a valid final result.")
{
    public string? Code { get; } = code;
    public string? RequestId { get; } = requestId;
    public int? StatusCode { get; } = statusCode;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}

public interface ILunixiGateway : IDisposable
{
    Task<GatewayReply> CreateIntentAsync(JsonElement body, string operationKey, CancellationToken cancellationToken);
    Task<GatewayReply> GetPaymentAsync(string paymentId, CancellationToken cancellationToken);
    Task<GatewayReply> GetSplitAsync(string splitId, CancellationToken cancellationToken);
    Task<GatewayReply> MutateAsync(string paymentId, string operation, long? amount, string operationKey, CancellationToken cancellationToken);
    VerifiedDelivery VerifyWebhook(byte[] body, IEnumerable<KeyValuePair<string, string>> headers, string secret);
}

public interface ILunixiGatewayFactory
{
    ILunixiGateway Create(GatewayCredentials credentials);
}
