using System.Text.Json;
using Lunixi;
using Nop.Plugin.Payments.Lunixi.Domain;

namespace Lunixi.NopCommerce.Gateway;

public sealed class GatewayClient : ILunixiGateway
{
    private readonly LunixiClient client;
    private readonly TimeProvider clock;
    public GatewayClient(GatewayCredentials credentials, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        var options = new LunixiOptions { BaseUri = new(credentials.BaseUrl), KeyId = credentials.KeyId, PrivateKeyPem = credentials.PrivateKeyPem, TimeProvider = this.clock };
        client = handler is null ? new(options) : new(options, handler);
    }
    public Task<GatewayReply> CreateIntentAsync(JsonElement body, string operationKey, CancellationToken cancellationToken)
        => Call(() => client.SendAsync(HttpMethod.Post, "/api/v1/payments/intents", body, operationKey, cancellationToken: cancellationToken));
    public Task<GatewayReply> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
        => Call(() => client.Payments.GetAsync(paymentId, cancellationToken));
    public Task<GatewayReply> GetSplitAsync(string splitId, CancellationToken cancellationToken)
        => Call(() => client.Payments.GetSplitAsync(splitId, cancellationToken));
    public Task<GatewayReply> MutateAsync(string paymentId, string operation, long? amount, string operationKey, CancellationToken cancellationToken)
        => operation switch
        {
            "capture" => Call(() => client.Payments.CaptureAsync(paymentId, operationKey, amount, cancellationToken)),
            "refund" => Call(() => client.Payments.RefundAsync(paymentId, operationKey, amount, cancellationToken: cancellationToken)),
            "void" => Call(() => client.Payments.VoidAsync(paymentId, operationKey, cancellationToken)),
            _ => throw new InvalidOperationException("Unsupported financial operation."),
        };
    public VerifiedDelivery VerifyWebhook(byte[] body, IEnumerable<KeyValuePair<string, string>> headers, string secret)
    {
        try
        {
            var delivery = new WebhookVerifier(secret, clock).Verify(body, headers);
            return new(delivery.Id, delivery.Type, delivery.BodySha256, delivery.Envelope);
        }
        catch (LunixiWebhookException) { throw new GatewayFault("invalid_webhook", statusCode: 400); }
    }
    private static async Task<GatewayReply> Call(Func<Task<LunixiResponse>> action)
    {
        try { var reply = await action(); return new(reply.StatusCode, reply.Envelope, reply.RequestId); }
        catch (LunixiApiException error) { throw new GatewayFault(error.Code, error.RequestId, error.StatusCode, error.StatusCode >= 500); }
        catch (LunixiTransportException error) { throw new GatewayFault(outcomeUnknown: error.OutcomeMayBeUnknown); }
        catch (LunixiConfigurationException) { throw new GatewayFault("configuration_invalid", statusCode: 400); }
    }
    public void Dispose() => client.Dispose();
}
