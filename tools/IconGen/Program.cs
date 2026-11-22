using System;
using System.IO;

namespace IconGen
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string workspace = FindWorkspaceRoot();
                string assets = Path.Combine(workspace, "watt", "assets");
                string previews = Path.Combine(assets, "previews");
                Directory.CreateDirectory(previews);

                string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
                Console.WriteLine("IconGen mode: " + mode);
                Console.WriteLine("Workspace:  " + workspace);
                Console.WriteLine("Fluent font available: " + Fonts.FontAvailable);

                if (mode == "all" || mode == "glyphs")
                {
                    GlyphSheet.Write(previews);
                    Console.WriteLine("glyph sheets written");
                }
                if (mode == "all" || mode == "candidates")
                {
                    CandidateSheet.Write(previews);
                    Console.WriteLine("candidate sheets written");
                }
                if (mode == "all" || mode == "icons")
                {
                    IconBuilder.Write(assets, previews);
                    Console.WriteLine("icons written");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static string FindWorkspaceRoot()
        {
            string[] starts = { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory };
            foreach (string start in starts)
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "watt", "src", "Watt", "Watt.csproj")))
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
            }
            throw new InvalidOperationException("Could not find the workspace root (watt/src/Watt/Watt.csproj).");
        }
    }
}
