using System;
using System.Runtime.InteropServices;

/// <summary>
/// Creates tiny native stubs that just jump to a target function pointer.
///
/// Why: ClassicUO and the plugin run in the same .NET runtime. When one side
/// calls <c>Marshal.GetDelegateForFunctionPointer&lt;T&gt;</c> on a pointer that
/// came from <c>Marshal.GetFunctionPointerForDelegate</c>, the runtime finds the
/// pointer in its delegate cache and returns the *original* delegate object
/// (of the other side's delegate type), which then fails the cast to T with an
/// InvalidCastException. A trampoline is a fresh native address the runtime has
/// never seen, so it builds a new delegate of the requested type instead.
/// </summary>
static unsafe class NativeTrampoline
{
    public static IntPtr Create(IntPtr target)
    {
        if (target == IntPtr.Zero)
            return IntPtr.Zero;

        byte[] code = BuildCode(target);
        int size = Environment.SystemPageSize;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            IntPtr mem = VirtualAlloc(IntPtr.Zero, (UIntPtr)size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
            if (mem == IntPtr.Zero)
                throw new OutOfMemoryException("VirtualAlloc failed for trampoline");

            Marshal.Copy(code, 0, mem, code.Length);
            FlushInstructionCache(GetCurrentProcess(), mem, (UIntPtr)code.Length);
            return mem;
        }
        else
        {
            // W^X: map writable, copy, then flip to executable (required on Apple Silicon).
            int mapAnon = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 0x1000 : 0x20;
            IntPtr mem = mmap(IntPtr.Zero, (UIntPtr)size, PROT_READ | PROT_WRITE, MAP_PRIVATE | mapAnon, -1, IntPtr.Zero);
            if (mem == new IntPtr(-1))
                throw new OutOfMemoryException("mmap failed for trampoline");

            Marshal.Copy(code, 0, mem, code.Length);

            if (mprotect(mem, (UIntPtr)size, PROT_READ | PROT_EXEC) != 0)
                throw new InvalidOperationException("mprotect failed for trampoline");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                sys_icache_invalidate(mem, (UIntPtr)code.Length);

            return mem;
        }
    }

    private static byte[] BuildCode(IntPtr target)
    {
        ulong addr = (ulong)target.ToInt64();
        byte[] code;

        switch (RuntimeInformation.ProcessArchitecture)
        {
            case Architecture.X64:
                // mov rax, imm64 ; jmp rax
                code = new byte[12];
                code[0] = 0x48; code[1] = 0xB8;
                BitConverter.GetBytes(addr).CopyTo(code, 2);
                code[10] = 0xFF; code[11] = 0xE0;
                return code;

            case Architecture.Arm64:
                // ldr x16, #8 ; br x16 ; .quad target
                code = new byte[16];
                BitConverter.GetBytes(0x58000050u).CopyTo(code, 0);
                BitConverter.GetBytes(0xD61F0200u).CopyTo(code, 4);
                BitConverter.GetBytes(addr).CopyTo(code, 8);
                return code;

            default:
                throw new PlatformNotSupportedException($"No trampoline for {RuntimeInformation.ProcessArchitecture}");
        }
    }

    // ── Windows ─────────────────────────────────────────────────────────────
    private const uint MEM_COMMIT             = 0x1000;
    private const uint MEM_RESERVE            = 0x2000;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protect);

    [DllImport("kernel32")]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

    [DllImport("kernel32")]
    private static extern IntPtr GetCurrentProcess();

    // ── Unix / macOS ────────────────────────────────────────────────────────
    private const int PROT_READ   = 1;
    private const int PROT_WRITE  = 2;
    private const int PROT_EXEC   = 4;
    private const int MAP_PRIVATE = 2;

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr addr, UIntPtr length, int prot, int flags, int fd, IntPtr offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int mprotect(IntPtr addr, UIntPtr length, int prot);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern void sys_icache_invalidate(IntPtr start, UIntPtr length);
}