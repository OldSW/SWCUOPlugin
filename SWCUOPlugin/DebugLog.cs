using System.Diagnostics;
#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
#endif

/// <summary>
/// Debug output for the plugin. On Windows it opens a console window (ClassicUO has none);
/// on every OS it writes to the console and to SWCUOPlugin.log next to the plugin DLL.
///
/// Debug builds only: the methods are [Conditional("DEBUG")], so in Release builds the compiler
/// removes every call (including evaluation of the message arguments), and the implementation
/// below is not compiled at all.
/// </summary>
static class DebugLog
{
    [Conditional("DEBUG")]
    public static void Init()
    {
#if DEBUG
        if (_initialized)
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

        // Fires when the runtime can't find a referenced assembly.
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            Write($"AssemblyResolve: could not find '{e.Name}' (requested by {e.RequestingAssembly?.FullName ?? "?"})");
            return null;
        };
#endif
    }

    [Conditional("DEBUG")]
    public static void Write(string message)
    {
#if DEBUG
        string line = $"{DateTime.Now:HH:mm:ss.fff} [T{Thread.CurrentThread.ManagedThreadId}] {message}";
        lock (Lock)
        {
            try { Console.WriteLine(line); } catch { }
            try { _file?.WriteLine(line); } catch { }
        }
#endif
    }

    /// <summary>Writes the message only the first time <paramref name="key"/> is seen.</summary>
    [Conditional("DEBUG")]
    public static void Once(string key, string message)
    {
#if DEBUG
        lock (Lock)
        {
            if (!Seen.Add(key))
                return;
        }
        Write(message);
#endif
    }

#if DEBUG
    private static readonly object Lock = new object();
    private static readonly HashSet<string> Seen = new HashSet<string>();
    private static StreamWriter? _file;
    private static bool _initialized;

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
#endif
}
