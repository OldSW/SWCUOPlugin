using System;
using System.Runtime.InteropServices;
using SDL3;

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
delegate bool PacketFn(ref byte[] data, ref int length);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool PacketNewFn(IntPtr data, ref int length);

// Plugin callbacks registered by the plugin
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void VoidFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool OnPacketFn(ref byte[] data, ref int length);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
delegate bool OnPacketNewFn(byte[] data, ref int length);

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
        private static PacketFn?            _sendToClient;
        private static PacketFn?            _sendToServer;
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
        private static OnPacketFn?    _onRecv;
        private static OnPacketFn?    _onSend;
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
        #endregion
        
        public static unsafe void Install(IntPtr header)
        {
            var h = (PluginHeader*)header;
            _clientVersion = h->ClientVersion;
            _typingIndicator = new TypingIndicator();

            // Capture host function pointers
            Bind(h->GetPlayerPosition, ref _getPlayerPosition);
            Bind(h->CastSpell,         ref _castSpell);
            Bind(h->RequestMove,       ref _requestMove);
            Bind(h->SetTitle,          ref _setTitle);
            Bind(h->GetUOFilePath,     ref _getUOFilePath);
            Bind(h->GetPacketLength,   ref _getPacketLength);
            Bind(h->Recv,              ref _sendToClient);
            Bind(h->Send,              ref _sendToServer);
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
            h->OnRecv                  = Pin(_onRecv             = HandleRecv);
            h->OnSend                  = Pin(_onSend             = HandleSend);
            h->OnRecv_new              = Pin(_onRecvNew          = HandleRecvNew);
            h->OnSend_new              = Pin(_onSendNew          = HandleSendNew);
            h->OnHotkeyPressed         = Pin(_onHotkey           = HandleHotkey);
            h->OnMouse                 = Pin(_onMouse            = HandleMouse);
            h->OnPlayerPositionChanged = Pin(_onPositionChanged  = HandlePositionChanged);
            h->OnWndProc               = Pin(_onWndProc          = HandleWndProc);
        }

        private static void Bind<T>(IntPtr ptr, ref T? field) where T : Delegate
            => field = ptr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(ptr) : null;

        private static IntPtr Pin(Delegate d)
            => Marshal.GetFunctionPointerForDelegate(d);

        // ── Event handlers ──────────────────────────────────────────────────

        private static void HandleInitialize()
        {
            _setTitle?.Invoke($"SWCUOPlugin (CUO {_clientVersion})");
        }

        private static void HandleConnected()   { }
        private static void HandleDisconnected(){ }
        private static void HandleClientClose() { }
        private static void HandleFocusGained() { }
        private static void HandleFocusLost()   { }

        private static void HandleTick()
        {
            _getPlayerPosition?.Invoke(out _playerX, out _playerY, out _playerZ);
        }

        private static void HandlePositionChanged(int x, int y, int z)
        {
            _playerX = x; _playerY = y; _playerZ = z;
        }

        // Return false to block the packet; return true to let it through.
        private static bool HandleRecv(ref byte[] data, ref int length) => true;
        private static bool HandleSend(ref byte[] data, ref int length) => true;
        private static bool HandleRecvNew(byte[] data, ref int length)  => true;
        private static bool HandleSendNew(byte[] data, ref int length)  => true;

        // Return false to consume the hotkey (prevent ClassicUO from processing it).
        private static bool HandleHotkey(int key, int mod, bool pressed) => true;

        private static void HandleMouse(int button, int wheel) { }

        // Return non-zero to indicate the event was handled and should not be processed further.
        private static unsafe int HandleWndProc(IntPtr e)
        {
            var sdlEvent = (SDL.SDL_Event*)e;
            var type = (SDL.SDL_EventType)sdlEvent->type;
            if (type == SDL.SDL_EventType.SDL_EVENT_KEY_UP)
            {
                _typingIndicator.Update();
                Console.WriteLine($"Key up event: {sdlEvent->key.key}");
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

        public static bool SendToClient(ref byte[] data, ref int length)
            => _sendToClient?.Invoke(ref data, ref length) ?? false;
        public static bool SendToServer(ref byte[] data, ref int length)
            => _sendToServer?.Invoke(ref data, ref length) ?? false;
        public static bool SendToClientNew(IntPtr data, ref int length)
            => _sendToClientNew?.Invoke(data, ref length) ?? false;
        public static bool SendToServerNew(IntPtr data, ref int length)
            => _sendToServerNew?.Invoke(data, ref length) ?? false;
    }
}
