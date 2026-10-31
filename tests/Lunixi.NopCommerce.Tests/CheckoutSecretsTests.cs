using Nop.Plugin.Payments.Lunixi.Domain;

namespace Lunixi.NopCommerce.Tests;

public sealed class CheckoutSecretsTests
{
    [Theory]
    [InlineData("http://pay.lunixi.com/payment/opaque-token-123456")]
    [InlineData("https://evil.example/payment/opaque-token-123456")]
    [InlineData("https://pay.lunixi.com:443/payment/opaque-token-123456")]
    [InlineData("https://user@pay.lunixi.com/payment/opaque-token-123456")]
    [InlineData("https://pay.lunixi.com/payment/opaque-token-123456?x=1")]
    [InlineData("https://pay.lunixi.com/payment/opaque-token-123456#x")]
    [InlineData("https://pay.lunixi.com/payment/opaque-token-123456\n")]
    [InlineData("https://pay.lunixi.com/payment/short")]
    [InlineData("https://pay.lunixi.com/payment/opaque%2Ftoken-123456")]
    [InlineData("https://pay.lunixi.com/payment/../opaque-token-123456")]
    public void RejectsUntrustedBackendRedirects(string value)
        => Assert.Throws<PaymentReviewException>(() => CheckoutSecrets.ValidatePageUrl(value));

    [Fact]
    public void VersionedStateKeepsIndependentTokensAndReadsLegacyWithoutFallback()
    {
        const string url = "https://pay.lunixi.com/payment/backend-opaque-token-123456";
        var data = CheckoutSecrets.Decode(CheckoutSecrets.Encode("different-browser-token", url));
        Assert.Equal("different-browser-token", data.Token); Assert.Equal(url, data.PaymentPageUrl);
        var legacy = CheckoutSecrets.Decode("legacy-browser-token");
        Assert.Equal("legacy-browser-token", legacy.Token); Assert.Equal("", legacy.PaymentPageUrl);
        Assert.Throws<PaymentReviewException>(() => CheckoutSecrets.RequirePageUrl(legacy.PaymentPageUrl));
    }
}
