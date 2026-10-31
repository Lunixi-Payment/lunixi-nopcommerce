using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Plugin.Payments.Lunixi.Models;
using Nop.Plugin.Payments.Lunixi.Services;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Lunixi.Controllers;

[AutoValidateAntiforgeryToken]
public sealed class LunixiCheckoutController(PaymentCoordinator coordinator, IWorkContext work, IStoreContext stores, IWebHelper web) : BasePaymentController
{
    private async Task<(int Customer, int Store)> Current()
    {
        var customer = await work.GetCurrentCustomerAsync(); var store = await stores.GetCurrentStoreAsync(); return (customer.Id, store.Id);
    }
    [HttpGet]
    public async Task<IActionResult> Pay(int orderId)
    {
        if (!Request.IsHttps) return Forbid();
        Response.Headers.CacheControl = "no-store"; Response.Headers["Referrer-Policy"] = "no-referrer";
        var current = await Current();
        try { await coordinator.EnsureOwnerAsync(orderId, current.Customer, current.Store); }
        catch (PaymentReviewException) { return NotFound(); }
        try
        {
            var page = await coordinator.PageAsync(orderId, current.Customer, current.Store);
            if (page.Hosted) return Redirect(page.HostedUrl);
            return View("~/Plugins/Payments.Lunixi/Views/Checkout.cshtml", new CheckoutModel(orderId, page.Token, false));
        }
        catch (PaymentReviewException) { return View("~/Plugins/Payments.Lunixi/Views/Checkout.cshtml", new CheckoutModel(orderId)); }
    }
    [HttpPost]
    public async Task<IActionResult> Begin(int orderId)
    {
        if (!Request.IsHttps) return Forbid();
        Response.Headers.CacheControl = "no-store"; var current = await Current();
        try { await coordinator.StartAsync(orderId, current.Customer, current.Store, web.GetStoreLocation(true) + "lunixi/return/" + orderId, HttpContext.RequestAborted); }
        catch (Exception error) when (error is PaymentReviewException or GatewayFault or OperationCanceledException) { }
        return RedirectToRoute("Lunixi.Pay", new { orderId });
    }
    [AcceptVerbs("GET", "POST")]
    [IgnoreAntiforgeryToken]
    public IActionResult Return(int orderId)
    {
        Response.Headers.CacheControl = "no-store";
        // No amount/status/order mutation from callback fields and no credential-bearing redirect URL.
        return View("~/Plugins/Payments.Lunixi/Views/Pending.cshtml", new CheckoutModel(orderId));
    }
    [HttpPost]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Webhook(string profileId)
    {
        try
        {
            using var body = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await Request.Body.ReadAsync(buffer, HttpContext.RequestAborted)) != 0)
            {
                if (body.Length + read > 1024 * 1024) return StatusCode(413);
                body.Write(buffer, 0, read);
            }
            var headers = Request.Headers.SelectMany(h => h.Value.Select(value => new KeyValuePair<string, string>(h.Key, value ?? "")));
            await coordinator.ReceiveAsync(profileId, body.ToArray(), headers, HttpContext.RequestAborted); return Ok();
        }
        catch (GatewayFault error) { return StatusCode(error.StatusCode == 400 ? 400 : 503); }
        catch (Exception error) when (error is PaymentReviewException or InvalidOperationException or OperationCanceledException or System.Data.Common.DbException)
        { return StatusCode(503); }
    }
}
