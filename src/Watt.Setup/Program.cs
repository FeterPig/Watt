using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Watt.Setup
{
    /// <summary>
    /// Per-user installer/uninstaller shipped in the release folder.
    /// It intentionally avoids PowerShell and any admin requirement:
    ///   Watt.Setup.exe install    [--install-dir DIR] [--no-start] [--no-autostart] [--no-shortcut]
    ///   Watt.Setup.exe uninstall  [--install-dir DIR] [--yes] [--keep-data]
    ///
    /// The installer copies the runtime payload that sits next to it (Watt.exe, config,
    /// Contracts/Support DLLs and plugins\) into %LOCALAPPDATA%\Programs\Watt, registers the
    /// HKCU Run value, creates the Start Menu shortcut and starts Watt.
    /// </summary>
    internal static class Program
    {
        private const string ProductName = "Watt";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "Watt";
        private const string ShortcutName = "Watt.lnk";

        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch
            {
                // Keep the default console encoding if UTF-8 is unavailable.
            }

            try
            {
                if (args == null || args.Length == 0)
                {
                    return RunInteractive();
                }

                switch (args[0].ToLowerInvariant())
                {
                    case "install":
                        return Install(args);
                    case "uninstall":
                        return Uninstall(args);
                    case "-h":
                    case "--help":
                    case "help":
                        PrintUsage();
                        return 0;
                    default:
                        Console.Error.WriteLine("Unknown command: " + args[0]);
                        PrintUsage();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        private static int RunInteractive()
        {
            PrintBanner();
            Console.WriteLine("  1. 安装 / Install");
            Console.WriteLine("  2. 卸载 / Uninstall");
            Console.WriteLine("  0. 退出 / Exit");
            Console.WriteLine();
            Console.Write("请选择 / Select: ");

            var choice = (Console.ReadLine() ?? string.Empty).Trim();
            int code;
            switch (choice)
            {
                case "1":
                    code = Install(new[] { "install" });
                    break;
                case "2":
                    code = Uninstall(new[] { "uninstall" });
                    break;
                case "":
                case "0":
                    return 0;
                default:
                    Console.Error.WriteLine("Invalid choice: " + choice);
                    Pause();
                    return 2;
            }

            Console.WriteLine();
            Console.WriteLine(code == 0 ? "Done." : "Failed with exit code " + code + ".");
            Pause();
            return code;
        }

        private static void Pause()
        {
            if (Console.IsInputRedirected) return;
            Console.Write("Press Enter to exit...");
            Console.ReadLine();
        }

        private static void PrintBanner()
        {
            Console.WriteLine("Watt.Setup 2.2.1 - per-user installer/uninstaller");
            Console.WriteLine("No administrator rights are required.");
            Console.WriteLine();
        }

        private static int Install(string[] args)
        {
            string installDir = null;
            var start = true;
            var autostart = true;
            var shortcut = true;
            var stop = true;
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--install-dir":
                        if (++i >= args.Length) throw new ArgumentException("--install-dir needs a value");
                        installDir = args[i];
                        break;
                    case "--no-start":
                        start = false;
                        break;
                    case "--no-autostart":
                        autostart = false;
                        break;
                    case "--no-shortcut":
                        shortcut = false;
                        break;
                    case "--no-stop":
                        stop = false;
                        break;
                    default:
                        throw new ArgumentException("Unknown install option: " + args[i]);
                }
            }

            // 2.2.1+ release layout: Watt.Setup.exe sits at the top level and the actual runtime
            // payload is in app\ so users do not accidentally launch Watt.exe directly.
            var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            var sourceDir = Path.Combine(baseDir, "app");
            if (!Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException(
                    "Runtime payload folder not found: " + sourceDir +
                    ". Start Watt.Setup.exe from the extracted release folder (the one containing app\\).");
            }
            ValidatePayload(sourceDir);

            installDir = string.IsNullOrEmpty(installDir)
                ? DefaultInstallDir()
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(installDir));

            if (SamePath(sourceDir, installDir))
            {
                throw new InvalidOperationException("The installer must not run from the installation directory.");
            }

            Console.WriteLine("Installing " + ProductName + "...");
            Console.WriteLine("  source : " + sourceDir);
            Console.WriteLine("  target : " + installDir);

            if (stop) StopWattProcesses();

            // Keep the installation directory itself: Explorer associates the notification-area
            // settings entry with this path, and deleting the folder during an in-place upgrade
            // can make Explorer forget/park the icon in the overflow. Overwrite runtime files in
            // place and replace only the plugins folder.
            Directory.CreateDirectory(installDir);
            var existingPlugins = Path.Combine(installDir, "plugins");
            if (Directory.Exists(existingPlugins))
            {
                Directory.Delete(existingPlugins, true);
            }

            CopyFile(sourceDir, installDir, "Watt.exe");
            CopyFileIfExists(sourceDir, installDir, "Watt.exe.config");
            CopyFile(sourceDir, installDir, "Watt.Plugin.Contracts.dll");
            CopyFile(sourceDir, installDir, "Watt.Plugin.Support.dll");

            var sourcePlugins = Path.Combine(sourceDir, "plugins");
            var targetPlugins = Path.Combine(installDir, "plugins");
            CopyDirectory(sourcePlugins, targetPlugins);

            var installedExe = Path.Combine(installDir, "Watt.exe");

            if (autostart)
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                    {
                        if (key == null) throw new InvalidOperationException("Cannot open HKCU Run key.");
                        key.SetValue(RunValueName, "\"" + installedExe + "\"", RegistryValueKind.String);
                    }
                    Console.WriteLine("  autostart: HKCU\\...\\Run\\" + RunValueName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("WARN: could not register autostart: " + ex.Message);
                }
            }

            if (shortcut)
            {
                CreateShortcut(installedExe, installDir);
            }

            if (start)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = installedExe,
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                });
                Console.WriteLine("  started : " + installedExe);
            }

            Console.WriteLine("OK - " + ProductName + " is installed for the current user.");
            return 0;
        }

        private static int Uninstall(string[] args)
        {
            string installDir = null;
            var assumeYes = false;
            var keepData = false;
            var autostart = true;
            var shortcut = true;
            var notify = true;
            var stop = true;
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--install-dir":
                        if (++i >= args.Length) throw new ArgumentException("--install-dir needs a value");
                        installDir = args[i];
                        break;
                    case "--yes":
                    case "-y":
                        assumeYes = true;
                        break;
                    case "--keep-data":
                        keepData = true;
                        break;
                    case "--no-autostart":
                        autostart = false;
                        break;
                    case "--no-shortcut":
                        shortcut = false;
                        break;
                    case "--no-notify":
                        notify = false;
                        break;
                    case "--no-stop":
                        stop = false;
                        break;
                    default:
                        throw new ArgumentException("Unknown uninstall option: " + args[i]);
                }
            }

            installDir = string.IsNullOrEmpty(installDir)
                ? ResolveInstalledDir()
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(installDir));
            var installedExe = Path.Combine(installDir, "Watt.exe");

            if (!assumeYes)
            {
                Console.Write("Uninstall " + ProductName + " from " + installDir + "? [y/N] ");
                var answer = Console.ReadLine();
                if (answer == null || (answer.Trim().ToLowerInvariant() != "y" &&
                                       answer.Trim().ToLowerInvariant() != "yes"))
                {
                    Console.WriteLine("Canceled.");
                    return 1;
                }
            }

            Console.WriteLine("Uninstalling " + ProductName + "...");
            Console.WriteLine("  target : " + installDir);

            if (stop) StopWattProcesses();

            if (autostart)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        if (key != null && key.GetValue(RunValueName) != null)
                        {
                            key.DeleteValue(RunValueName, false);
                            Console.WriteLine("  removed autostart");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("WARN: could not remove autostart: " + ex.Message);
                }
            }

            if (shortcut) RemoveShortcut();
            if (notify) RemoveNotifyIconEntries(installedExe);

            if (Directory.Exists(installDir))
            {
                Directory.Delete(installDir, true);
                Console.WriteLine("  removed install directory");
            }

            if (!keepData)
            {
                try
                {
                    var dataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Watt");
                    if (Directory.Exists(dataDir))
                    {
                        Directory.Delete(dataDir, true);
                        Console.WriteLine("  removed local data (cache and user plugins)");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("WARN: could not remove local data: " + ex.Message);
                }
            }

            Console.WriteLine("OK - " + ProductName + " is uninstalled for the current user.");
            return 0;
        }

        private static void ValidatePayload(string sourceDir)
        {
            foreach (var file in new[]
            {
                "Watt.exe",
                "Watt.Plugin.Contracts.dll",
                "Watt.Plugin.Support.dll"
            })
            {
                if (!File.Exists(Path.Combine(sourceDir, file)))
                {
                    throw new FileNotFoundException(
                        "Runtime payload is incomplete: app\\" + file +
                        ". Run build.cmd, then start Watt.Setup.exe from the release folder.");
                }
            }

            var plugins = Path.Combine(sourceDir, "plugins");
            if (!Directory.Exists(plugins) ||
                Directory.GetFiles(plugins, "*.dll").Length == 0)
            {
                throw new DirectoryNotFoundException(
                    "Runtime payload is incomplete: app\\plugins has no DLLs. " +
                    "Start Watt.Setup.exe from the built release folder.");
            }
        }

        private static string DefaultInstallDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", ProductName);
        }

        private static string ResolveInstalledDir()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    var value = key == null ? null : key.GetValue(RunValueName) as string;
                    if (!string.IsNullOrEmpty(value))
                    {
                        var exe = value.Trim('"');
                        var dir = Path.GetDirectoryName(exe);
                        if (!string.IsNullOrEmpty(dir) && File.Exists(exe)) return dir;
                    }
                }
            }
            catch
            {
                // Fall back to the default location.
            }
            return DefaultInstallDir();
        }

        private static void CreateShortcut(string installedExe, string installDir)
        {
            var shortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new InvalidOperationException("WScript.Shell is unavailable.");

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic link = shell.CreateShortcut(shortcutPath);
                link.TargetPath = installedExe;
                link.WorkingDirectory = installDir;
                link.IconLocation = installedExe + ",0";
                link.Description = "Watt - peripheral battery";
                link.Save();

                Console.WriteLine("  shortcut: " + shortcutPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: could not create shortcut: " + ex.Message);
            }
        }

        private static void RemoveShortcut()
        {
            var shortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
            try
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    Console.WriteLine("  removed shortcut");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: could not remove shortcut: " + ex.Message);
            }
        }

        private static void RemoveNotifyIconEntries(string installedExe)
        {
            try
            {
                using (var settings = Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\NotifyIconSettings", true))
                {
                    if (settings == null) return;

                    var names = settings.GetSubKeyNames();
                    foreach (var name in names)
                    {
                        using (var entry = settings.OpenSubKey(name))
                        {
                            if (entry == null) continue;
                            var path = entry.GetValue("ExecutablePath") as string;
                            if (path == null) continue;
                            if (!SamePath(path.Trim('"'), installedExe)) continue;

                            try
                            {
                                settings.DeleteSubKeyTree(name);
                                Console.WriteLine("  removed notification-area entry");
                            }
                            catch
                            {
                                // Explorer may briefly hold the key; not fatal.
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WARN: could not clean notification-area settings: " + ex.Message);
            }
        }

        private static void StopWattProcesses()
        {
            var processes = Process.GetProcessesByName("Watt");
            if (processes.Length == 0) return;

            Console.WriteLine("  stopping " + processes.Length + " running Watt process(es)");
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                catch
                {
                    // Keep going; a process may exit on its own.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void CopyFile(string sourceDir, string targetDir, string name)
        {
            var source = Path.Combine(sourceDir, name);
            if (!File.Exists(source)) throw new FileNotFoundException("Missing runtime file: " + source);
            File.Copy(source, Path.Combine(targetDir, name), true);
        }

        private static void CopyFileIfExists(string sourceDir, string targetDir, string name)
        {
            var source = Path.Combine(sourceDir, name);
            if (File.Exists(source)) File.Copy(source, Path.Combine(targetDir, name), true);
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException("Missing runtime directory: " + sourceDir);
            }

            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
            }
            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
            }
        }

        private static bool SamePath(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd('\\'),
                    Path.GetFullPath(right).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void PrintUsage()
        {
            PrintBanner();
            Console.WriteLine("Usage:");
            Console.WriteLine("  Watt.Setup.exe install   [--install-dir DIR] [--no-start] [--no-autostart] [--no-shortcut] [--no-stop]");
            Console.WriteLine("  Watt.Setup.exe uninstall [--install-dir DIR] [--yes] [--keep-data] [--no-autostart] [--no-shortcut] [--no-notify] [--no-stop]");
            Console.WriteLine();
            Console.WriteLine("Run without arguments for the interactive menu.");
            Console.WriteLine("Command line: Watt.Setup.exe install | uninstall [options]");
        }
    }
}
