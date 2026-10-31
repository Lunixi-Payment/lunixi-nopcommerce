using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.Lunixi.Components;

public sealed class LunixiPaymentInfoViewComponent : NopViewComponent
{
    public IViewComponentResult Invoke() => View("~/Plugins/Payments.Lunixi/Views/PaymentInfo.cshtml");
}
