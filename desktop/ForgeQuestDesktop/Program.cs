using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ForgeQuestDesktop;

static class Program
{
    // Single-instance guard. Without this, a second launch (an impatient
    // double-click on the desktop shortcut, or opening it again without
    // noticing a window is already open) silently starts a second local
    // server — it loses the port race for 47811 (see MainForm.StartServer)
    // and falls back to 47812, and since localStorage is scoped per-origin
    // *including the port*, that second window looks completely empty/fresh
    // even though the real save is sitting untouched in the first window.
    // Closing the "duplicate" (which might actually be the original) and
    // playing in the new one strands that progress on a port the player will
    // probably never land on again. A named Mutex — process-local by name,
    // no "Global\" prefix needed since this only ever runs per-user — lets a
    // second launch detect the first and just focus it instead.
    private const string MutexName = "ForgeQuest-SingleInstance-Mutex";

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            FocusExistingInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    // Best-effort UX only — the Mutex check above is what actually prevents
    // the second, empty-save instance; this just brings the real one forward
    // instead of leaving the player to go hunt for it themselves.
    private static void FocusExistingInstance()
    {
        var current = Process.GetCurrentProcess();
        foreach (var proc in Process.GetProcessesByName(current.ProcessName))
        {
            if (proc.Id == current.Id) continue;
            var handle = proc.MainWindowHandle;
            if (handle == IntPtr.Zero) continue;
            if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
            SetForegroundWindow(handle);
            break;
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
}
