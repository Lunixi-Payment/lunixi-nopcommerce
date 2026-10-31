using System.Text.Json.Serialization;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.Lunixi.Models;

public sealed record ConfigurationModel : BaseNopModel
{
    public bool Enabled { get; set; }
    public bool Hosted { get; set; }
    public bool Preauthorize { get; set; }
    public string MerchantId { get; set; } = "";
    public string KeyId { get; set; } = "";
    [JsonIgnore] public string PrivateKeyPem { get; set; } = "";
    [JsonIgnore] public string WebhookSecret { get; set; } = "";
    public bool ServerKeyConfigured { get; set; }
    public string WebhookUrl { get; set; } = "";
    public override string ToString() => "LunixiConfigurationModel { Credentials = [REDACTED] }";
}

public sealed record CheckoutModel(int OrderId, [property: JsonIgnore] string? Token = null, bool Hosted = false)
{
    public override string ToString() => "LunixiCheckoutModel { Token = [REDACTED] }";
}
