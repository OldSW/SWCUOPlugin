# SWCUOPlugin

A managed (C#) plugin for the [ClassicUO](https://github.com/ClassicUO/ClassicUO) Ultima Online client.
Currently, it hooks SDL key events and sends a typing-indicator packet (`TypingIndicator`):
extended command `0xBF` with custom subcommand `0xEF`, which the server must handle.

## Layout

- `SWCUOPlugin/` – the plugin assembly (`SWCUOPlugin.dll`)
  - `Engine.cs` – entry point. ClassicUO finds `Assistant.Engine.Install(IntPtr header)` via reflection,
    passing a `PluginHeader*`. Host functions are bound from the header, plugin callbacks are written back into it.
  - `NativeTrampoline.cs` – native jump stubs used for every function pointer crossing the plugin/host boundary (see below).
  - `TypingIndicator.cs` – plugin feature logic.
- `NativeTrampolineTest/` – console app that verifies the trampoline (links `SWCUOPlugin/NativeTrampoline.cs`, exit code 0 = pass).
- `external/FNA` – git submodule (FNA-XNA), referenced for the `SDL3` bindings (`SDL.SDL_Event` etc.).
- `ConsoleApp1/`, `Test/` – local scratch projects, not part of the plugin.

## Build & test

- Requires .NET SDK 10 (`global.json`: `10.0.100`, `rollForward: latestMinor`). The plugin itself targets `net8.0`, x64 platform target, unsafe code enabled, C# 9.
- Clone with submodules: `git submodule update --init`
- Build: `dotnet build SWCUOPlugin/SWCUOPlugin.csproj`
- Test trampoline: `dotnet run --project NativeTrampolineTest`
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
