using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Payments.Lunixi.Data;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Plugin.Payments.Lunixi.Services;

namespace Nop.Plugin.Payments.Lunixi.Infrastructure;

public sealed class NopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ILunixiGatewayFactory, IsolatedGatewayFactory>();
        services.AddScoped<NativeConnectionFactory>(); services.AddScoped<PaymentLedger>();
        services.AddScoped<CredentialProfiles>(); services.AddScoped<NativeOrders>(); services.AddScoped<PaymentCoordinator>();
    }
    public void Configure(IApplicationBuilder application) { }
    public int Order => 20;
}
