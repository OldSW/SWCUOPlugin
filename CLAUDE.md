# SWCUOPlugin

A managed (C#) plugin for the [ClassicUO](https://github.com/ClassicUO/ClassicUO) Ultima Online client.
It hooks SDL key events and sends a typing-indicator packet (`TypingIndicator`):
extended command `0xBF` with custom subcommand `0xEF`, which the server must handle.
It also writes received text and emotes to a journal file (`Journal`).

## Layout

- `SWCUOPlugin/` – the plugin assembly (`SWCUOPlugin.dll`)
  - `Engine.cs` – entry point. ClassicUO finds `Assistant.Engine.Install(IntPtr header)` via reflection,
    passing a `PluginHeader*`. Host functions are bound from the header, plugin callbacks are written back into it.
  - `NativeTrampoline.cs` – native jump stubs used for every function pointer crossing the plugin/host boundary (see below).
  - `TypingIndicator.cs` – typing indicator feature.
  - `Journal.cs` – journal feature: parses received speech/cliloc packets (0x1C, 0xAE, 0xC1, 0xCC) and appends them
    to `<folder>/<character>_<date>.txt`. The character name comes from the outgoing 0x5D/0x00 packet.
  - `Config.cs` – reads `SWCUOPlugin.ini` (key=value) next to the DLL, creating it with defaults if missing.
  - `Sdl.cs` – the few SDL3 event structs/constants the plugin reads (hand-written, no FNA dependency).
  - `DebugLog.cs` – debug output: console window on Windows, `SWCUOPlugin.log` next to the DLL on all OSes.
    Debug builds only: methods are `[Conditional("DEBUG")]`, so Release builds strip all calls and the implementation.
    No log file in a Debug build means `Install` was never called.
- `NativeTrampolineTest/` – console app that verifies the trampoline (links `SWCUOPlugin/NativeTrampoline.cs`, exit code 0 = pass).
- `external/FNA` – git submodule (FNA-XNA). No longer referenced by the plugin; only useful as a reference for SDL3 definitions.
- `ConsoleApp1/`, `Test/` – local scratch projects, not part of the plugin.


## Build & test

- Requires .NET SDK 10 (`global.json`: `10.0.100`, `rollForward: latestMinor`). The plugin itself targets `net472`, x64 platform target, unsafe code enabled, C# 9.
- **Keep the plugin on `net472` with no dependencies beyond the .NET Framework.** On Windows, `ClassicUO.exe` is the
  Bootstrap running on .NET Framework 4.7.2 and loads the plugin via `Assembly.LoadFile`; a `net8.0` assembly or a missing
  dependency makes it silently skip the plugin (ClassicUO still logs "Plugin … loaded."). Only APIs available in
  .NET Framework 4.7.2 may be used (e.g. no `Environment.TickCount64`, `Span<T>` without a package).
- Submodules (optional, only for the FNA reference sources): `git submodule update --init`
- Build: `dotnet build SWCUOPlugin/SWCUOPlugin.csproj` → `SWCUOPlugin/bin/Debug/net472/SWCUOPlugin.dll`
- Test trampoline: `dotnet run --project NativeTrampolineTest`
- CI (`.github/workflows/`): `build.yml` builds Release + runs the trampoline test on every push/PR.
  `release.yaml` runs on tags `*.*.*` (e.g. `1.0.0`), stamps the tag as assembly version and attaches
  `SWCUOPlugin-<tag>.zip` (Release DLL, no debug logging) to a GitHub release.
- The csproj references `external/cuoapi.dll` via HintPath; the file is not in the repo and nothing currently uses it.

## ClassicUO interop – important

- A local checkout of [ClassicUO](https://github.com/ClassicUO/ClassicUO) is very helpful: it lets you read how
  plugins are loaded and run a debug build of the client against the plugin. Ask the user where it lives if you need it.
  Plugin loading code: `src/ClassicUO.Client/Network/Plugin.cs` and `src/ClassicUO.Bootstrap/src/Plugin.cs`.
  `PluginHeader` in `Engine.cs` must mirror the field order there exactly.
- **Never pass raw `Marshal.GetFunctionPointerForDelegate` pointers to CUO, and never call `GetDelegateForFunctionPointer`
  on CUO's pointers directly.** Plugin and host share one runtime; the runtime returns the cached original delegate
  (of the other side's, often private, delegate type) and the cast throws `InvalidCastException`.
  Always go through `Engine.Pin` / `Engine.Bind`, which wrap pointers in a `NativeTrampoline`.
- Trampolines support Windows (`VirtualAlloc`), Linux and macOS (`mmap` RW → `mprotect` RX) on x64 and arm64.
- Because calls now go through real native marshaling, array parameters don't round-trip:
  - Use the `_new` packet API (`OnRecv_new`/`OnSend_new`, `Recv_new`/`Send_new`) with `IntPtr data, ref int length`.
  - The legacy `ref byte[]` callbacks/host functions are intentionally not registered/bound.
  - `Engine.SendToClient/SendToServer(ref byte[] …)` pin the array and forward to the `_new` host functions.
- Keep every delegate passed to CUO in a static field so it isn't garbage-collected.
- Handler return conventions: packet/hotkey handlers return `true` to pass through, `false` to block;
  `OnWndProc` returns non-zero to consume the SDL event.
