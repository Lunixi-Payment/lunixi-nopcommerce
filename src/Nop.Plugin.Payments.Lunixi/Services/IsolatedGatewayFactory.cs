using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Nop.Core.Infrastructure;
using Nop.Plugin.Payments.Lunixi.Domain;

namespace Nop.Plugin.Payments.Lunixi.Services;

public sealed class IsolatedGatewayFactory(INopFileProvider files) : ILunixiGatewayFactory, IDisposable
{
    private static readonly object Sync = new();
    private static Type? gatewayType;
    private readonly Dictionary<string, ILunixiGateway> clients = new(StringComparer.Ordinal);
    public ILunixiGateway Create(GatewayCredentials credentials)
    {
        lock (Sync)
        {
            if (clients.TryGetValue(credentials.ProfileId, out var existing)) return existing;
            if (gatewayType is null)
            {
                var directory = files.MapPath("~/Plugins/Payments.Lunixi/Vendor");
                var context = new VendorContext(directory);
                gatewayType = context.LoadFromAssemblyPath(Path.Combine(directory, "Lunixi.NopCommerce.Gateway.dll")).GetType("Lunixi.NopCommerce.Gateway.GatewayClient", true)!;
            }
            try
            {
                var client = (ILunixiGateway)Activator.CreateInstance(gatewayType, credentials, null, null)!;
                clients.Add(credentials.ProfileId, client); return client;
            }
            catch (TargetInvocationException) { throw new GatewayFault("configuration_invalid"); }
        }
    }
    public void Dispose() { lock (Sync) { foreach (var client in clients.Values) client.Dispose(); clients.Clear(); } }
}

internal sealed class VendorContext(string directory) : AssemblyLoadContext("Lunixi.NopCommerce.Vendor", false)
{
    private readonly AssemblyDependencyResolver resolver = new(Path.Combine(directory, "Lunixi.NopCommerce.Gateway.dll"));
    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == typeof(ILunixiGateway).Assembly.GetName().Name) return typeof(ILunixiGateway).Assembly;
        var path = resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path is null && unmanagedDllName is "libsodium" or "sodium")
        {
            var rid = RuntimeInformation.RuntimeIdentifier;
            var file = OperatingSystem.IsWindows() ? "libsodium.dll" : OperatingSystem.IsMacOS() ? "libsodium.dylib" : "libsodium.so";
            var candidate = Path.Combine(directory, "runtimes", rid, "native", file);
            if (File.Exists(candidate)) path = candidate;
        }
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
