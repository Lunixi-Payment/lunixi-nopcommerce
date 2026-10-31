namespace Nop.Plugin.Payments.Lunixi.Domain;

public sealed class CheckoutAttempt
{
    public int Id { get; set; }
    public int NativeOrderId { get; set; }
    public string ProfileId { get; set; } = "";
    public string OrderReference { get; set; } = "";
    public string MerchantId { get; set; } = "";
    public string Currency { get; set; } = "";
    public long BaseMinor { get; set; }
    public string Fingerprint { get; set; } = "";
    public string OperationKey { get; set; } = "";
    public string PayloadCipher { get; set; } = "";
    public string? PaymentId { get; set; }
    public string? TokenCipher { get; set; }
    public string State { get; set; } = "creating";
    public long GrossMinor { get; set; }
    public long RefundedMinor { get; set; }
    public DateTime CreatedUtc { get; set; }
    public OrderIdentity Identity => new(MerchantId, OrderReference, BaseMinor, Currency);
    public override string ToString() => "CheckoutAttempt { Data = [REDACTED] }";
}

public sealed record PendingOperation(int Id, int AttemptId, string Kind, long AmountMinor, long BaselineRefunded, string Key, string State, DateTime CreatedUtc);
public sealed record CheckoutPage(string Token, string PublishableKey, bool Hosted, int OrderId)
{
    public string HostedUrl => "https://pay.lunixi.com/payment/" + Uri.EscapeDataString(Token);
    public override string ToString() => "CheckoutPage { Token = [REDACTED] }";
}
