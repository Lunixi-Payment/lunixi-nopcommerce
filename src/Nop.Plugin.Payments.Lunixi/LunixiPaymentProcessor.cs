using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Plugin.Payments.Lunixi.Components;
using Nop.Plugin.Payments.Lunixi.Domain;
using Nop.Plugin.Payments.Lunixi.Infrastructure;
using Nop.Plugin.Payments.Lunixi.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Services.Security;

namespace Nop.Plugin.Payments.Lunixi;

public sealed class LunixiPaymentProcessor(IHttpContextAccessor http, IWorkContext work, IStoreContext stores, IWebHelper web,
    ISettingService settings, ILocalizationService localization, ILanguageService languages, IScheduleTaskService tasks,
    IPermissionService permissions, PaymentCoordinator payments) : BasePlugin, IPaymentMethod
{
    private Task<string> Text(string name) => localization.GetResourceAsync(LocaleResources.Prefix + name);
    private void ObserveNativeCompletion(int orderId)
    {
        http.HttpContext?.Response.OnCompleted(() => payments.NativeRequestFinishedAsync(orderId));
    }
    public bool SupportCapture => true;
    public bool SupportRefund => true;
    public bool SupportPartiallyRefund => true;
    public bool SupportVoid => true;
    public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;
    public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;
    public bool SkipPaymentInfo => false;
    public Type GetPublicViewComponent() => typeof(LunixiPaymentInfoViewComponent);
    public override string GetConfigurationPageUrl() => web.GetStoreLocation() + "Admin/PaymentLunixi/Configure";
    public Task<string> GetPaymentMethodDescriptionAsync() => Text("Description");
    public async Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
    {
        var store = await stores.GetCurrentStoreAsync(); var config = await settings.LoadSettingAsync<LunixiSettings>(store.Id);
        return !config.Enabled || config.ActiveProfileId.Length == 0 || !CredentialProfiles.ServerKeyConfigured;
    }
    public Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart) => Task.FromResult(0m);
    public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
    {
        var config = await settings.LoadSettingAsync<LunixiSettings>(processPaymentRequest.StoreId);
        if (!config.Enabled || config.ActiveProfileId.Length == 0 || !CredentialProfiles.ServerKeyConfigured)
            return new() { Errors = [await Text("Unavailable")] };
        return new() { NewPaymentStatus = PaymentStatus.Pending, AllowStoringCreditCardNumber = false };
    }
    public async Task PostProcessPaymentAsync(PostProcessPaymentRequest request)
    {
        var context = http.HttpContext ?? throw new PaymentReviewException();
        var customer = await work.GetCurrentCustomerAsync(); var store = await stores.GetCurrentStoreAsync();
        try
        {
            await payments.StartAsync(request.Order.Id, customer.Id, store.Id, web.GetStoreLocation(true) + "lunixi/return/" + request.Order.Id, context.RequestAborted);
        }
        catch (Exception error) when (error is GatewayFault or PaymentReviewException or OperationCanceledException)
        { /* The native order stays pending; an authenticated page provides status/reconciliation. */ }
        context.Response.Redirect(web.GetStoreLocation(true) + "lunixi/pay/" + request.Order.Id);
    }
    public Task<bool> CanRePostProcessPaymentAsync(Order order)
        => Task.FromResult(order.PaymentMethodSystemName == "Payments.Lunixi" && order.PaymentStatus == PaymentStatus.Pending && order.OrderStatus != OrderStatus.Cancelled);
    public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form) => Task.FromResult<IList<string>>([]);
    public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form) => Task.FromResult(new ProcessPaymentRequest());

    public async Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest request)
    {
        var result = new CapturePaymentResult();
        try
        {
            if (!await permissions.AuthorizeAsync(StandardPermission.Orders.ORDERS_CREATE_EDIT_DELETE)) throw new PaymentReviewException();
            var outcome = await payments.MutateAsync(request.Order.Id, "capture", null, http.HttpContext?.RequestAborted ?? default);
            if (!outcome.ApplyThroughNativeCaller) throw new PaymentReviewException();
            ObserveNativeCompletion(request.Order.Id);
            result.NewPaymentStatus = PaymentStatus.Paid; result.CaptureTransactionId = outcome.PaymentId;
        }
        catch (Exception error) when (error is GatewayFault or PaymentReviewException or OperationCanceledException)
        { result.AddError(await Text("OperationPending")); }
        return result;
    }
    public async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest request)
    {
        var result = new RefundPaymentResult();
        try
        {
            if (!await permissions.AuthorizeAsync(StandardPermission.Orders.ORDERS_CREATE_EDIT_DELETE)) throw new PaymentReviewException();
            // Native OrderTotal/AmountToRefund are denominated in the store's primary currency.
            var attemptCurrency = await payments.CurrencyAsync(request.Order.Id);
            var amount = request.IsPartialRefund ? MinorMoney.FromDecimal(request.AmountToRefund, attemptCurrency) : (long?)null;
            var outcome = await payments.MutateAsync(request.Order.Id, "refund", amount, http.HttpContext?.RequestAborted ?? default);
            if (!outcome.ApplyThroughNativeCaller) throw new PaymentReviewException();
            ObserveNativeCompletion(request.Order.Id);
            result.NewPaymentStatus = outcome.Settlement.State == "refunded" ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        }
        catch (Exception error) when (error is GatewayFault or PaymentReviewException or OperationCanceledException)
        { result.AddError(await Text("OperationPending")); }
        return result;
    }
    public async Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest request)
    {
        var result = new VoidPaymentResult();
        try
        {
            if (!await permissions.AuthorizeAsync(StandardPermission.Orders.ORDERS_CREATE_EDIT_DELETE)) throw new PaymentReviewException();
            var outcome = await payments.MutateAsync(request.Order.Id, "void", null, http.HttpContext?.RequestAborted ?? default);
            if (!outcome.ApplyThroughNativeCaller) throw new PaymentReviewException();
            ObserveNativeCompletion(request.Order.Id);
            result.NewPaymentStatus = PaymentStatus.Voided;
        }
        catch (Exception error) when (error is GatewayFault or PaymentReviewException or OperationCanceledException)
        { result.AddError(await Text("OperationPending")); }
        return result;
    }
    public async Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest request) => new() { Errors = [await Text("UnsupportedRecurring")] };
    public async Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest request) => new() { Errors = [await Text("UnsupportedRecurring")] };
    public override async Task InstallAsync()
    {
        if (!(await settings.LoadSettingAsync<LunixiSettings>()).Enabled) await settings.SaveSettingAsync(new LunixiSettings());
        foreach (var language in await languages.GetAllLanguagesAsync(showHidden: true))
            await localization.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>(language.LanguageCulture.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? LocaleResources.Turkish : LocaleResources.English), language.Id);
        var type = typeof(ReconciliationTask).FullName + ", " + typeof(ReconciliationTask).Assembly.GetName().Name;
        if (await tasks.GetTaskByTypeAsync(type) is null) await tasks.InsertTaskAsync(new ScheduleTask { Name = await Text("TaskName"), Type = type, Enabled = true, Seconds = 60, StopOnError = false });
        await base.InstallAsync();
    }
    public override async Task UninstallAsync()
    {
        var current = await settings.LoadSettingAsync<LunixiSettings>(); current.Enabled = false; await settings.SaveSettingAsync(current);
        var type = typeof(ReconciliationTask).FullName + ", " + typeof(ReconciliationTask).Assembly.GetName().Name;
        if (await tasks.GetTaskByTypeAsync(type) is { } task) { task.Enabled = false; await tasks.UpdateTaskAsync(task); }
        // Retain profiles, settings, operations, attempts and inbox for financial retention/reinstall.
        await base.UninstallAsync();
    }
}
