using System.Net;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Nop.Core.Infrastructure;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Plugin.Payments.Lunixi.Services;

namespace Lunixi.NopCommerce.Tests;

public sealed class IsolatedBridgeTests
{
    [Fact]
    public void InstallableArtifactLoadsRealSdkAndNativeCryptoOnlyInPrivateContext()
    {
        var root = Environment.GetEnvironmentVariable("LUNIXI_PLUGIN_STAGING") ?? throw new InvalidOperationException("Build the installable artifact and set LUNIXI_PLUGIN_STAGING for private SDK load verification.");
        var environment = new Mock<IWebHostEnvironment>(); environment.Setup(e => e.ContentRootPath).Returns(root); environment.Setup(e => e.WebRootPath).Returns(root);
        using var files = new NopFileProvider(environment.Object); using var factory = new IsolatedGatewayFactory(files);
        var gateway = factory.Create(new GatewayCredentials { ProfileId = "00000000000000000000000000000001", MerchantId = "synthetic-merchant", KeyId = "mk_test_synthetic", PrivateKeyPem = CoordinatorHost.SyntheticKey });
        var context = AssemblyLoadContext.GetLoadContext(gateway.GetType().Assembly);
        Assert.Equal("Lunixi.NopCommerce.Vendor", context!.Name);
        var sdk = context.Assemblies.Single(a => a.GetName().Name == "Lunixi.Sdk");
        var crypto = context.Assemblies.Single(a => a.GetName().Name == "NSec.Cryptography");
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(sdk));
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(crypto));
        Assert.Contains("/Vendor/", sdk.Location.Replace('\\', '/'));
        Assert.Same(gateway, factory.Create(new GatewayCredentials { ProfileId = "00000000000000000000000000000001", MerchantId = "synthetic-merchant", KeyId = "mk_test_synthetic", PrivateKeyPem = CoordinatorHost.SyntheticKey }));
    }

    [Fact]
    public async Task RealSdkBridgeSignsBodylessAuthAndFinancialRequestAndCachesToken()
    {
        var authCount = 0; var writes = 0;
        using var handler = new DelegateHandler(async (request, token) =>
        {
            Assert.Equal("api-gateway.lunixi.com", request.RequestUri!.Host); Assert.True(request.Headers.Contains("X-Signature"));
            if (request.RequestUri.AbsolutePath.EndsWith("/auth/token", StringComparison.Ordinal))
            {
                authCount++; Assert.Null(request.Content); Assert.False(request.Headers.Contains("Digest"));
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"synthetic-token\",\"expires_in\":1800}") };
            }
            writes++; Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal("nop:checkout:persisted", request.Headers.GetValues("Idempotency-Key").Single());
            var body = await request.Content!.ReadAsByteArrayAsync(token);
            Assert.Equal("SHA-256=" + Convert.ToBase64String(SHA256.HashData(body)), request.Headers.GetValues("Digest").Single());
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":{\"paymentId\":\"pay-bridge\",\"token\":\"synthetic-checkout\"}}") };
        });
        using var bridge = new global::Lunixi.NopCommerce.Gateway.GatewayClient(new GatewayCredentials { MerchantId = "synthetic", KeyId = "mk_test_synthetic", PrivateKeyPem = CoordinatorHost.SyntheticKey }, handler);
        var payload = JsonSerializer.SerializeToElement(new { amount = "10050", currency = "TRY", orderId = "order-bridge" });
        var first = await bridge.CreateIntentAsync(payload, "nop:checkout:persisted", default);
        await bridge.CreateIntentAsync(payload, "nop:checkout:persisted", default);
        Assert.Equal("pay-bridge", first.Data.GetProperty("paymentId").GetString()); Assert.Equal(1, authCount); Assert.Equal(2, writes);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("refund")]
    public async Task NativeMutationsRequireResolvedAmountsAndNeverTreatNullAsRemaining(string operation)
    {
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            calls++;
            throw new InvalidOperationException("Invalid amount must not reach authentication or transport.");
        });
        using var bridge = new global::Lunixi.NopCommerce.Gateway.GatewayClient(new GatewayCredentials { MerchantId = "synthetic", KeyId = "mk_test_synthetic", PrivateKeyPem = CoordinatorHost.SyntheticKey }, handler);
        foreach (var amount in new string?[] { null, "0" })
        {
            var error = await Assert.ThrowsAsync<GatewayFault>(() => bridge.MutateAsync("pay-bridge", operation, amount, "nop:mutation:persisted", default));
            Assert.Equal("configuration_invalid", error.Code);
            Assert.Equal(400, error.StatusCode);
            Assert.False(error.OutcomeUnknown);
        }
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("refund")]
    public async Task NativeMutationPreservesResolvedLargeAmountOnTheWire(string operation)
    {
        const string amount = "9007199254740993";
        var writes = 0;
        using var handler = new DelegateHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"synthetic-token\",\"expires_in\":1800}") };
            writes++;
            Assert.EndsWith("/" + operation, request.RequestUri.AbsolutePath);
            Assert.Equal("nop:mutation:persisted", request.Headers.GetValues("Idempotency-Key").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(amount, body.RootElement.GetProperty("amount").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":{}}") };
        });
        using var bridge = new global::Lunixi.NopCommerce.Gateway.GatewayClient(new GatewayCredentials { MerchantId = "synthetic", KeyId = "mk_test_synthetic", PrivateKeyPem = CoordinatorHost.SyntheticKey }, handler);
        await bridge.MutateAsync("pay-bridge", operation, amount, "nop:mutation:persisted", default);
        Assert.Equal(1, writes);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
