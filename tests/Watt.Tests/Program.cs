using System;
using System.Windows;

namespace Watt.Tests
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Console.WriteLine("tests running as " + Environment.UserName + " / " +
                              System.Security.Principal.WindowsIdentity.GetCurrent().Name +
                              ", temp=" + System.IO.Path.GetTempPath());
            TestEnv.Initialize();

            // WPF resources used by PanelView (and by --snapshot rendering) need one
            // Application instance. A console process can own it as long as the thread is STA.
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            var runner = new TestRunner(args.Length > 0 ? args[0] : null);

            BatteryServiceTests.Register(runner);
            StoreTests.Register(runner);
            PreferenceTests.Register(runner);
            ClassifierTests.Register(runner);
            SupportTests.Register(runner);
            StartupServiceTests.Register(runner);
            ThemeTests.Register(runner);
            BleGuardTests.Register(runner);
            PluginHostTests.Register(runner);
            UiTests.Register(runner);
            DiagnosticsTests.Register(runner);
            MiscTests.Register(runner);

            var exitCode = runner.Finish();
            if (exitCode == 0) TestEnv.Cleanup();

            // Do not call application.Shutdown(): this is a console process and the dispatcher
            // never started. Returning from Main ends the process.
            GC.KeepAlive(application);
            return exitCode;
        }
    }
}
