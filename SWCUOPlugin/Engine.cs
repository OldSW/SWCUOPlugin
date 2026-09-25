using System;
using System.Runtime.InteropServices;

// ─── Delegate types ────────────────────────────────────────────────────────

// Host functions provided by ClassicUO
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool GetPlayerPositionFn(out int x, out int y, out int z);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void CastSpellFn(int index);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool RequestMoveFn(int dir, bool run);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void SetTitleFn(string title);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate string GetUOFilePathFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate short GetPacketLengthFn(int packetId);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool GetClilocFn(int cliloc, [MarshalAs(UnmanagedType.LPStr)] string args, bool capitalize,
                          [MarshalAs(UnmanagedType.LPStr)] out string buffer);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool PacketNewFn(IntPtr data, ref int length);

// Plugin callbacks registered by the plugin
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void VoidFn();

// CUO passes a pinned byte[] -> we receive the raw pointer. Declaring byte[] here
// would make the reverse marshaler build a 1-element array, since it doesn't know the length.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool OnPacketNewFn(IntPtr data, ref int length);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool OnHotkeyFn(int key, int mod, bool pressed);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void OnMouseFn(int button, int wheel);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void OnPositionFn(int x, int y, int z);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int OnWndProcFn(IntPtr sdlEvent);

// ─── PluginHeader ──────────────────────────────────────────────────────────

/// <summary>
/// Mirrors PluginHeader in ClassicUO.Bootstrap/src/Plugin.cs.
/// Field order must stay in sync with the ClassicUO source.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
struct PluginHeader
{
    public int    ClientVersion;  // filled by ClassicUO
    public IntPtr HWND;

    // Written by plugin during Install, read by ClassicUO after Install returns
    public IntPtr OnRecv;
    public IntPtr OnSend;
    public IntPtr OnHotkeyPressed;
    public IntPtr OnMouse;
    public IntPtr OnPlayerPositionChanged;
    public IntPtr OnClientClosing;
    public IntPtr OnInitialize;
    public IntPtr OnConnected;
    public IntPtr OnDisconnected;
    public IntPtr OnFocusGained;
    public IntPtr OnFocusLost;

    // Filled by ClassicUO before Install is called
    public IntPtr GetUOFilePath;
    public IntPtr Recv;
    public IntPtr Send;
    public IntPtr GetPacketLength;
    public IntPtr GetPlayerPosition;
    public IntPtr CastSpell;
    public IntPtr GetStaticImage;

    public IntPtr Tick;           // plugin callback

    public IntPtr RequestMove;
    public IntPtr SetTitle;

    // Newer packet API
    public IntPtr OnRecv_new;
    public IntPtr OnSend_new;
    public IntPtr Recv_new;
    public IntPtr Send_new;

    public IntPtr OnDrawCmdList;
    public IntPtr SDL_Window;
    public IntPtr OnWndProc;
    public IntPtr GetStaticData;
    public IntPtr GetTileData;
    public IntPtr GetCliloc;
}

// ─── Plugin entry point ────────────────────────────────────────────────────

namespace Assistant
{
    /// <summary>
    /// ClassicUO plugin entry point.
    /// ClassicUO looks for the type "Assistant.Engine" and calls
    /// <c>Install(IntPtr header)</c> via reflection.
    /// </summary>
    public static class Engine
    {
        // ── Host functions (provided by ClassicUO) ──────────────────────────
        private static GetPlayerPositionFn? _getPlayerPosition;
        private static CastSpellFn?         _castSpell;
        private static RequestMoveFn?       _requestMove;
        private static SetTitleFn?          _setTitle;
        private static GetUOFilePathFn?     _getUOFilePath;
        private static GetPacketLengthFn?   _getPacketLength;
        private static GetClilocFn?         _getCliloc;
        private static PacketNewFn?         _sendToClientNew;
        private static PacketNewFn?         _sendToServerNew;

        // ── Plugin callbacks (kept in statics to prevent GC collection) ─────
        private static VoidFn?        _onInitialize;
        private static VoidFn?        _onConnected;
        private static VoidFn?        _onDisconnected;
        private static VoidFn?        _onClientClose;
        private static VoidFn?        _onFocusGained;
        private static VoidFn?        _onFocusLost;
        private static VoidFn?        _tick;
        private static OnPacketNewFn? _onRecvNew;
        private static OnPacketNewFn? _onSendNew;
        private static OnHotkeyFn?    _onHotkey;
        private static OnMouseFn?     _onMouse;
        private static OnPositionFn?  _onPositionChanged;
        private static OnWndProcFn?   _onWndProc;

        // ── Cached state ────────────────────────────────────────────────────
        private static int _playerX, _playerY, _playerZ;
        private static int _clientVersion;

        public static int PlayerX       => _playerX;
        public static int PlayerY       => _playerY;
        public static int PlayerZ       => _playerZ;
        public static int ClientVersion => _clientVersion;

        // ── Entry point ─────────────────────────────────────────────────────

        #region PluginState
        private static TypingIndicator _typingIndicator = new TypingIndicator();
        private static Journal? _journal;
        #endregion
        
        public static unsafe void Install(IntPtr header)
        {
            DebugLog.Init();
            DebugLog.Write($"Install called, header=0x{header.ToInt64():X}");
            DebugLog.Write($"Runtime: {RuntimeInformation.FrameworkDescription}, OS: {RuntimeInformation.OSDescription}, " +
                           $"arch: {RuntimeInformation.ProcessArchitecture}, plugin: {typeof(Engine).Assembly.Location}");

            try
            {
                InstallCore((PluginHeader*)header);
                DebugLog.Write("Install finished");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"Install failed: {ex}");
                throw;
            }
        }

        private static unsafe void InstallCore(PluginHeader* h)
        {
            _clientVersion = h->ClientVersion;
            DebugLog.Write($"ClientVersion=0x{h->ClientVersion:X}, SDL_Window=0x{h->SDL_Window.ToInt64():X}, HWND=0x{h->HWND.ToInt64():X}");
            _typingIndicator = new TypingIndicator();

            var config = Config.Load();
            _journal = config.JournalEnabled ? new Journal(config.JournalFolder) : null;

            // Capture host function pointers
            Bind(h->GetPlayerPosition, ref _getPlayerPosition);
            Bind(h->CastSpell,         ref _castSpell);
            Bind(h->RequestMove,       ref _requestMove);
            Bind(h->SetTitle,          ref _setTitle);
            Bind(h->GetUOFilePath,     ref _getUOFilePath);
            Bind(h->GetPacketLength,   ref _getPacketLength);
            Bind(h->GetCliloc,         ref _getCliloc);
            Bind(h->Recv_new,          ref _sendToClientNew);
            Bind(h->Send_new,          ref _sendToServerNew);

            // Register our callbacks
            h->OnInitialize            = Pin(_onInitialize       = HandleInitialize);
            h->OnConnected             = Pin(_onConnected        = HandleConnected);
            h->OnDisconnected          = Pin(_onDisconnected     = HandleDisconnected);
            h->OnClientClosing         = Pin(_onClientClose      = HandleClientClose);
            h->OnFocusGained           = Pin(_onFocusGained      = HandleFocusGained);
            h->OnFocusLost             = Pin(_onFocusLost        = HandleFocusLost);
            h->Tick                    = Pin(_tick               = HandleTick);
            // Legacy OnRecv/OnSend (ref byte[]) can't survive real native marshaling;
            // CUO prefers the _new callbacks anyway, so only register those.
            h->OnRecv_new             = Pin(_onRecvNew          = HandleRecvNew);
            h->OnSend_new              = Pin(_onSendNew          = HandleSendNew);
            h->OnHotkeyPressed         = Pin(_onHotkey           = HandleHotkey);
            h->OnMouse                 = Pin(_onMouse            = HandleMouse);
            h->OnPlayerPositionChanged = Pin(_onPositionChanged  = HandlePositionChanged);
            h->OnWndProc               = Pin(_onWndProc          = HandleWndProc);
        }

        // Both directions go through a NativeTrampoline so the runtime never finds the
        // pointer in its delegate cache (which would hand back the other side's delegate
        // type and throw InvalidCastException).
        private static void Bind<T>(IntPtr ptr, ref T? field) where T : Delegate
        {
            field = ptr != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<T>(NativeTrampoline.Create(ptr))
                : null;
            DebugLog.Write($"Bind {typeof(T).Name}: host=0x{ptr.ToInt64():X}{(field == null ? " (not provided)" : "")}");
        }

        private static IntPtr Pin(Delegate d)
        {
            IntPtr raw = Marshal.GetFunctionPointerForDelegate(d);
            IntPtr trampoline = NativeTrampoline.Create(raw);
            DebugLog.Write($"Pin {d.Method.Name}: delegate=0x{raw.ToInt64():X} -> trampoline=0x{trampoline.ToInt64():X}");
            return trampoline;
        }

        // ── Event handlers ──────────────────────────────────────────────────

        private static void HandleInitialize()
        {
            DebugLog.Write("OnInitialize called");
            _setTitle?.Invoke($"SWCUOPlugin (CUO {_clientVersion})");
        }

        private static void HandleConnected()    => DebugLog.Write("OnConnected called");

        private static void HandleDisconnected()
        {
            DebugLog.Write("OnDisconnected called");
            _journal?.Close();
        }

        private static void HandleClientClose()
        {
            DebugLog.Write("OnClientClosing called");
            _journal?.Close();
        }

        private static void HandleFocusGained()  => DebugLog.Once(nameof(HandleFocusGained), "OnFocusGained first call");
        private static void HandleFocusLost()    => DebugLog.Once(nameof(HandleFocusLost), "OnFocusLost first call");

        private static void HandleTick()
        {
            DebugLog.Once(nameof(HandleTick), "Tick first call");
            _getPlayerPosition?.Invoke(out _playerX, out _playerY, out _playerZ);
        }

        private static void HandlePositionChanged(int x, int y, int z)
        {
            DebugLog.Once(nameof(HandlePositionChanged), $"OnPlayerPositionChanged first call ({x}, {y}, {z})");
            _playerX = x; _playerY = y; _playerZ = z;
        }

        // Return false to block the packet; return true to let it through.
        private static bool HandleRecvNew(IntPtr data, ref int length)
        {
            DebugLog.Once(nameof(HandleRecvNew), $"OnRecv_new first call, length={length}");
            try
            {
                _journal?.OnRecv(data, length);
            }
            catch (Exception ex)
            {
                DebugLog.Once("Journal OnRecv failed", $"Journal OnRecv failed (further errors suppressed): {ex}");
            }
            return true;
        }

        private static bool HandleSendNew(IntPtr data, ref int length)
        {
            DebugLog.Once(nameof(HandleSendNew), $"OnSend_new first call, length={length}");
            try
            {
                _journal?.OnSend(data, length);
            }
            catch (Exception ex)
            {
                DebugLog.Once("Journal OnSend failed", $"Journal OnSend failed (further errors suppressed): {ex}");
            }
            return true;
        }

        // Return false to consume the hotkey (prevent ClassicUO from processing it).
        private static bool HandleHotkey(int key, int mod, bool pressed)
        {
            DebugLog.Once(nameof(HandleHotkey), $"OnHotkeyPressed first call, key={key}");
            return true;
        }

        private static void HandleMouse(int button, int wheel)
            => DebugLog.Once(nameof(HandleMouse), "OnMouse first call");

        // Return non-zero to indicate the event was handled and should not be processed further.
        private static unsafe int HandleWndProc(IntPtr e)
        {
            DebugLog.Once(nameof(HandleWndProc), "OnWndProc first call");
            try
            {
                var key = (SdlKeyboardEvent*)e;
                if (key->type == Sdl.EVENT_KEY_UP)
                {
                    int scancode = key->scancode;
                    DebugLog.Write($"Key up: scancode {scancode}");
                    if (scancode >= Sdl.SCANCODE_A && scancode <= Sdl.SCANCODE_Z)
                        _typingIndicator.Update();
                    else if (scancode == Sdl.SCANCODE_RETURN || scancode == Sdl.SCANCODE_KP_ENTER)
                        _typingIndicator.Reset();
                }
            }
            catch (Exception ex)
            {
                DebugLog.Once("HandleWndProc failed", $"OnWndProc failed (further errors suppressed): {ex}");
            }
            return 0;
        }

        // ── Public API ──────────────────────────────────────────────────────

        public static bool GetPlayerPosition(out int x, out int y, out int z)
        {
            if (_getPlayerPosition != null)
                return _getPlayerPosition(out x, out y, out z);
            x = _playerX; y = _playerY; z = _playerZ;
            return true;
        }

        public static void  CastSpell(int spellIndex)          => _castSpell?.Invoke(spellIndex);
        public static bool  RequestMove(int dir, bool run)     => _requestMove?.Invoke(dir, run) ?? false;
        public static void  SetTitle(string title)             => _setTitle?.Invoke(title);
        public static string GetUOFilePath()                   => _getUOFilePath?.Invoke() ?? string.Empty;
        public static short GetPacketLength(int id)            => _getPacketLength?.Invoke(id) ?? -1;

        /// <summary>Translates a cliloc with tab-separated arguments; null if unknown or the host doesn't provide it.</summary>
        public static string? GetCliloc(int cliloc, string args, bool capitalize)
            => _getCliloc != null && _getCliloc(cliloc, args, capitalize, out string buffer) ? buffer : null;

        // The legacy ref byte[] host functions don't marshal correctly across a real
        // native boundary, so pin the array and use the pointer-based API instead.
        public static unsafe bool SendToClient(ref byte[] data, ref int length)
        {
            fixed (byte* p = data)
                return SendToClientNew((IntPtr)p, ref length);
        }

        public static unsafe bool SendToServer(ref byte[] data, ref int length)
        {
            fixed (byte* p = data)
                return SendToServerNew((IntPtr)p, ref length);
        }
        public static bool SendToClientNew(IntPtr data, ref int length)
            => _sendToClientNew?.Invoke(data, ref length) ?? false;
        public static bool SendToServerNew(IntPtr data, ref int length)
            => _sendToServerNew?.Invoke(data, ref length) ?? false;
    }
}
