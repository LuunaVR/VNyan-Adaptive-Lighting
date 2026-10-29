using System.Reflection;

namespace VNyanAdaptiveLighting.Plugin
{
    public static class PluginInfo
    {
        // Single source of truth is <Version> in VNyanAdaptiveLighting.csproj.
        public static readonly string Version = Format(Assembly.GetExecutingAssembly().GetName().Version);

        private static string Format(System.Version v) => v == null ? "0.0.0" : v.Major + "." + v.Minor + "." + v.Build;
    }
}
