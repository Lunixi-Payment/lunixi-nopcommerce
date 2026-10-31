using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.Lunixi;

public sealed class LunixiSettings : ISettings
{
    public bool Enabled { get; set; }
    public string ActiveProfileId { get; set; } = "";
    public bool Hosted { get; set; }
    public bool Preauthorize { get; set; }
}
