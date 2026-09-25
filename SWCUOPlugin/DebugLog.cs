using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Debug output for the plugin. On Windows it opens a console window (ClassicUO has none);
/// on every OS it writes to the console and to SWCUOPlugin.log next to the plugin DLL.
/// Must not reference FNA/SDL types, so it keeps working even if those fail to load.
/// </summary>
static class DebugLog
{
    // Set to false to disable all debug output.
    public static readonly bool Enabled = true;

    private static readonly object Lock = new object();
    private static readonly HashSet<string> Seen = new HashSet<string>();
    private static StreamWriter? _file;
    private static bool _initialized;

    public static void Init()
    {
        if (!Enabled || _initialized)
            return;
        _initialized = true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            OpenConsoleWindow();

        try
        {
            string dir = Path.GetDirectoryName(typeof(DebugLog).Assembly.Location) ?? ".";
            string path = Path.Combine(dir, "SWCUOPlugin.log");
            _file = new StreamWriter(path, append: false) { AutoFlush = true };
            Write($"Log file: {path}");
        }
        catch (Exception ex)
        {
            Write($"Could not open log file: {ex.Message}");
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write($"UNHANDLED EXCEPTION: {e.ExceptionObject}");

        // Fires when the runtime can't find a referenced assembly (e.g. FNA under the net472 Bootstrap).
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            Write($"AssemblyResolve: could not find '{e.Name}' (requested by {e.RequestingAssembly?.FullName ?? "?"})");
            return null;
        };
    }

    public static void Write(string message)
    {
        if (!Enabled)
            return;

        string line = $"{DateTime.Now:HH:mm:ss.fff} [T{Thread.CurrentThread.ManagedThreadId}] {message}";
        lock (Lock)
        {
            try { Console.WriteLine(line); } catch { }
            try { _file?.WriteLine(line); } catch { }
        }
    }

    /// <summary>Writes the message only the first time <paramref name="key"/> is seen.</summary>
    public static void Once(string key, string message)
    {
        if (!Enabled)
            return;

        lock (Lock)
        {
            if (!Seen.Add(key))
                return;
        }
        Write(message);
    }

    private static void OpenConsoleWindow()
    {
        try
        {
            if (GetConsoleWindow() == IntPtr.Zero)
                AllocConsole();
            SetConsoleTitle("SWCUOPlugin debug");

            // FileStream refuses "CONOUT$" on .NET Framework, so open the handle natively.
            var handle = CreateFile("CONOUT$", GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (!handle.IsInvalid)
                Console.SetOut(new StreamWriter(new FileStream(handle, FileAccess.Write)) { AutoFlush = true });
        }
        catch (Exception ex)
        {
            Write($"Could not open console window: {ex.Message}");
        }
    }

    private const uint GENERIC_WRITE     = 0x40000000;
    private const uint FILE_SHARE_WRITE  = 0x2;
    private const uint OPEN_EXISTING     = 3;

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern bool SetConsoleTitle(string title);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
                                                    uint creation, uint flags, IntPtr template);
}
