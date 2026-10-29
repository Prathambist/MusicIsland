using System.IO;

namespace MusicIsland
{
    /// <summary>
    /// "Launch at login" via the same Startup-folder shortcut install.ps1 creates.
    /// </summary>
    public static class StartupShortcut
    {
        static string ShortcutPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "MusicIsland.lnk");

        public static bool IsEnabled => File.Exists(ShortcutPath);

        public static void Set(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    File.Delete(ShortcutPath);
                    return;
                }

                string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MusicIsland.exe");
                var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(ShortcutPath);
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
                shortcut.Description = "MusicIsland Startup";
                shortcut.Save();
            }
            catch (Exception ex)
            {
                Log.Error("Could not update the startup shortcut", ex);
            }
        }
    }
}
