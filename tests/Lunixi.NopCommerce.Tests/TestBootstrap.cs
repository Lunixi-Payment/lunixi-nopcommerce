using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Lunixi.NopCommerce.Tests;

internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void ResolvePinnedPlatform()
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var directory = Environment.GetEnvironmentVariable("NOP_RUNTIME_PATH") ?? "";
            var path = Path.Combine(directory, name.Name + ".dll");
            return Path.IsPathFullyQualified(path) && File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
    }
}
