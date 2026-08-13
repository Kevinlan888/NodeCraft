using System;

namespace NodeCraft
{
    internal static class StartupGraphPathResolver
    {
        public static string TryResolve(string[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
                return null;

            return args[0].EndsWith(".flow.xml", StringComparison.OrdinalIgnoreCase)
                ? args[0]
                : null;
        }
    }
}
