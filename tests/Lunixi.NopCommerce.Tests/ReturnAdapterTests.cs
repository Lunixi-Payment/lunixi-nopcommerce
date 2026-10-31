using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Payments.Lunixi.Controllers;

namespace Lunixi.NopCommerce.Tests;

public sealed class ReturnAdapterTests
{
    [Fact]
    public async Task AnonymousPostIgnoresFieldsAndRedirects303WithoutAnyDependencyAccess()
    {
        // Null dependencies make accidental order/session/gateway access fail this regression.
        var controller = new LunixiCheckoutController(null!, null!, null!, null!);
        var context = new DefaultHttpContext(); controller.ControllerContext = new() { HttpContext = context };
        context.Request.Scheme = "https"; context.Request.Method = "POST"; context.Request.PathBase = "/shop";
        context.Request.RouteValues["orderId"] = "123";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("orderId=999&status=success&amount=1&returnUrl=https://evil.invalid"));
        Assert.IsType<EmptyResult>(await controller.Callback());
        Assert.Equal(303, context.Response.StatusCode); Assert.Equal("/shop/lunixi/return/123", context.Response.Headers.Location);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task PostHasAnExactBodyBoundAndCannotRedirectToAnExternalPrefix()
    {
        var controller = new LunixiCheckoutController(null!, null!, null!, null!);
        var context = new DefaultHttpContext(); controller.ControllerContext = new() { HttpContext = context };
        context.Request.Scheme = "https"; context.Request.RouteValues["orderId"] = "123";
        context.Request.Body = new MemoryStream(new byte[65537]);
        Assert.Equal(413, Assert.IsType<StatusCodeResult>(await controller.Callback()).StatusCode);
        context.Request.PathBase = "//evil.invalid"; context.Request.Body = new MemoryStream();
        Assert.IsType<BadRequestResult>(await controller.Callback()); Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task ReturnGetRestoresNativeOwnerAndStoreChecksWithoutTrustingCallbackMoney()
    {
        var host = new CoordinatorHost(); await host.Initialize();
        var work = new Mock<IWorkContext>(); work.Setup(w => w.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 8 });
        var stores = new Mock<IStoreContext>(); stores.Setup(s => s.GetCurrentStoreAsync()).ReturnsAsync(new Store { Id = 1 });
        var controller = new LunixiCheckoutController(host.Coordinator, work.Object, stores.Object, null!);
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
        controller.Request.Scheme = "https";
        Assert.IsType<NotFoundResult>(await controller.Return(host.Order.Id));
        work.Setup(w => w.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 7 });
        Assert.IsType<ViewResult>(await controller.Return(host.Order.Id)); Assert.Empty(host.Gateway.CreateKeys);
    }

    [Fact]
    public void RealNativeMvcActionDiscoverySeparatesReadonlyPostAndOwnerCheckedGet()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddRouting();
        services.AddControllers().AddApplicationPart(typeof(LunixiCheckoutController).Assembly);
        using var provider = services.BuildServiceProvider();
        var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().Where(a => a.ControllerTypeInfo.AsType() == typeof(LunixiCheckoutController) && a.ActionName == "Return").ToArray();
        Assert.Equal(2, actions.Length);
        Assert.Single(actions, a => a.ActionConstraints!.OfType<HttpMethodActionConstraint>().Any(h => h.HttpMethods.Contains("POST")));
        Assert.Single(actions, a => a.ActionConstraints!.OfType<HttpMethodActionConstraint>().Any(h => h.HttpMethods.Contains("GET")));
        Assert.NotEmpty(typeof(LunixiCheckoutController).GetMethod(nameof(LunixiCheckoutController.Callback))!.GetCustomAttributes(typeof(IgnoreAntiforgeryTokenAttribute), true));
        Assert.Empty(typeof(LunixiCheckoutController).GetMethod(nameof(LunixiCheckoutController.Return))!.GetCustomAttributes(typeof(IgnoreAntiforgeryTokenAttribute), true));
    }
}
