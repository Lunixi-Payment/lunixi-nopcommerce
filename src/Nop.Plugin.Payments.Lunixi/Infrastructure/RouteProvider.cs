using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.Lunixi.Infrastructure;

public sealed class RouteProvider : IRouteProvider
{
    public void RegisterRoutes(IEndpointRouteBuilder routes)
    {
        routes.MapControllerRoute("Lunixi.Configure", "Admin/PaymentLunixi/Configure", new { controller = "PaymentLunixi", action = "Configure", area = AreaNames.ADMIN });
        routes.MapControllerRoute("Lunixi.Reconcile", "Admin/PaymentLunixi/Reconcile", new { controller = "PaymentLunixi", action = "Reconcile", area = AreaNames.ADMIN });
        routes.MapControllerRoute("Lunixi.Pay", "lunixi/pay/{orderId:int}", new { controller = "LunixiCheckout", action = "Pay" });
        routes.MapControllerRoute("Lunixi.Begin", "lunixi/begin/{orderId:int}", new { controller = "LunixiCheckout", action = "Begin" });
        routes.MapControllerRoute("Lunixi.Return", "lunixi/return/{orderId:int}", new { controller = "LunixiCheckout", action = "Return" });
        routes.MapControllerRoute("Lunixi.Webhook", "lunixi/webhook/{profileId}", new { controller = "LunixiCheckout", action = "Webhook" });
    }
    public int Priority => 10;
}
