using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Plugin.Payments.Lunixi.Infrastructure;
using Nop.Plugin.Payments.Lunixi.Models;
using Nop.Plugin.Payments.Lunixi.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using System.Security.Cryptography;
using System.Text;

namespace Nop.Plugin.Payments.Lunixi.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public sealed class PaymentLunixiController(IStoreContext stores, IWebHelper web, ISettingService settings,
    CredentialProfiles profiles, PaymentCoordinator coordinator, INotificationService notifications, ILocalizationService localization) : BasePaymentController
{
    [HttpGet]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure()
    {
        var scope = await stores.GetActiveStoreScopeConfigurationAsync();
        var config = await settings.LoadSettingAsync<LunixiSettings>(scope);
        var model = new ConfigurationModel { Enabled = config.Enabled, Hosted = config.Hosted, Preauthorize = config.Preauthorize, ServerKeyConfigured = CredentialProfiles.ServerKeyConfigured };
        if (config.ActiveProfileId.Length > 0 && model.ServerKeyConfigured)
        {
            try
            {
                var profile = await profiles.GetAsync(config.ActiveProfileId);
                model.MerchantId = profile.Credentials.MerchantId; model.KeyId = profile.Credentials.KeyId;
                model.WebhookUrl = web.GetStoreLocation(true) + "lunixi/webhook/" + config.ActiveProfileId;
            }
            catch (InvalidOperationException) { model.ServerKeyConfigured = false; }
        }
        return View("~/Plugins/Payments.Lunixi/Views/Configure.cshtml", model);
    }
    [HttpPost]
    [RequestSizeLimit(65536)]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        try
        {
            if ((model.PrivateKeyPem?.Length ?? 0) > 16384 || (model.WebhookSecret?.Length ?? 0) > 4096) throw new PaymentReviewException();
            var scope = await stores.GetActiveStoreScopeConfigurationAsync();
            var config = await settings.LoadSettingAsync<LunixiSettings>(scope);
            GatewayCredentials? previous = config.ActiveProfileId.Length > 0 ? (await profiles.GetAsync(config.ActiveProfileId)).Credentials : null;
            var next = new GatewayCredentials
            {
                MerchantId = model.MerchantId,
                KeyId = model.KeyId,
                PrivateKeyPem = string.IsNullOrWhiteSpace(model.PrivateKeyPem) ? previous?.PrivateKeyPem ?? "" : model.PrivateKeyPem,
                WebhookSecret = string.IsNullOrWhiteSpace(model.WebhookSecret) ? previous?.WebhookSecret ?? "" : model.WebhookSecret,
            };
            static bool SecretEquals(string first, string second) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(first)), SHA256.HashData(Encoding.UTF8.GetBytes(second)));
            var same = previous is not null && previous.MerchantId == next.MerchantId && previous.KeyId == next.KeyId &&
                SecretEquals(previous.PrivateKeyPem, next.PrivateKeyPem) && SecretEquals(previous.WebhookSecret, next.WebhookSecret);
            var profileId = same ? config.ActiveProfileId : await profiles.SaveAsync(scope, next);
            config.ActiveProfileId = profileId; config.Enabled = model.Enabled; config.Hosted = model.Hosted; config.Preauthorize = model.Preauthorize;
            await settings.SaveSettingAsync(config, scope);
            notifications.SuccessNotification(await localization.GetResourceAsync(LocaleResources.Prefix + "Saved"));
        }
        catch (Exception error) when (error is PaymentReviewException or GatewayFault or InvalidOperationException or ArgumentException)
        { notifications.ErrorNotification(await localization.GetResourceAsync(LocaleResources.Prefix + "InvalidSettings")); }
        // PRG discards posted secret fields, including model-state attempted values.
        return RedirectToAction(nameof(Configure));
    }
    [HttpPost]
    [CheckPermission(StandardPermission.Orders.ORDERS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> Reconcile(int orderId)
    {
        try { await coordinator.ReconcileAsync(orderId, HttpContext.RequestAborted); }
        catch (Exception error) when (error is PaymentReviewException or GatewayFault or OperationCanceledException)
        { notifications.ErrorNotification(await localization.GetResourceAsync(LocaleResources.Prefix + "OperationPending")); }
        return RedirectToAction("Edit", "Order", new { id = orderId, area = AreaNames.ADMIN });
    }
}
