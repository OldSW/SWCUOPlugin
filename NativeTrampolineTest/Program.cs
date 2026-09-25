using System;
using System.Runtime.InteropServices;

// Plugin-side delegate type (receives the raw pointer)
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool PluginPacketFn(IntPtr data, ref int length);

// Host-side delegate type, as ClassicUO declares OnPacketSendRecv_new
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool HostPacketFn(byte[] data, ref int length);

/// <summary>
/// Reproduces the InvalidCastException ClassicUO hits when converting a plugin
/// callback pointer, and checks that NativeTrampoline avoids it.
/// Exit code 0 = all checks passed.
/// </summary>
static class Program
{
    // Kept in a static so the GC doesn't collect it while native code holds the pointer
    private static readonly PluginPacketFn Callback = HandlePacket;

    private static bool HandlePacket(IntPtr data, ref int length)
    {
        Console.WriteLine($"  callback: first byte=0x{Marshal.ReadByte(data):X2}, length={length}");
        length = 42;
        return true;
    }

    private static int Main()
    {
        Console.WriteLine($"{RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture}");
        bool ok = true;

        IntPtr raw = Marshal.GetFunctionPointerForDelegate(Callback);

        // 1. Without a trampoline the runtime returns the cached PluginPacketFn and the cast fails
        try
        {
            Marshal.GetDelegateForFunctionPointer<HostPacketFn>(raw);
            Console.WriteLine("raw pointer:  no exception (cache behavior not reproduced)");
        }
        catch (InvalidCastException)
        {
            Console.WriteLine("raw pointer:  InvalidCastException (expected)");
        }

        // 2. Through a trampoline we get a fresh HostPacketFn that calls into our callback
        var host = Marshal.GetDelegateForFunctionPointer<HostPacketFn>(NativeTrampoline.Create(raw));
        int length = 3;
        bool result = host(new byte[] { 0xAB, 0x01, 0x02 }, ref length);

        bool passed = result && length == 42;
        ok &= passed;
        Console.WriteLine($"trampoline:   result={result}, length={length} -> {(passed ? "PASS" : "FAIL")}");

        return ok ? 0 : 1;
    }
}
