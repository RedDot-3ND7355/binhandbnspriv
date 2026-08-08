using System;
using System.IO;
using Avalonia;

namespace LegacyBin.Ava
{
    internal static class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called.
        [STAThread]
        public static int Main(string[] args)
        {
            // CLI mode (headless, for smoke tests / scripting on any OS).
            if (args != null && args.Length >= 2)
            {
                if (string.Equals(args[0], "unpack", StringComparison.OrdinalIgnoreCase))
                {
                    return Cli.RunUnpack(args[1], args.Length >= 3 ? args[2] : null);
                }
                if (args.Length >= 3 && string.Equals(args[0], "repack", StringComparison.OrdinalIgnoreCase))
                {
                    return Cli.RunRepack(args[1], args[2]);
                }
                if (args.Length >= 4 && string.Equals(args[0], "merge", StringComparison.OrdinalIgnoreCase))
                {
                    bool renames = args.Length >= 5
                        && (string.Equals(args[4], "--renames", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(args[4], "-r", StringComparison.OrdinalIgnoreCase));
                    return MergeCli.Run(args[1], args[2], args[3], renames);
                }
                if (args.Length >= 4 && string.Equals(args[0], "translate-apply", StringComparison.OrdinalIgnoreCase))
                {
                    return TranslateApplyCli.Run(args[1], args[2], args[3]);
                }
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
