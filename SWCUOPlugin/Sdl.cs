using System.Runtime.InteropServices;

/// <summary>
/// Minimal subset of SDL3 event definitions (SDL_events.h / SDL_scancode.h) that the plugin reads.
/// Defined here instead of referencing FNA's SDL3-CS, because FNA.dll is not available next to
/// ClassicUO's net472 Bootstrap on Windows.
/// </summary>
static class Sdl
{
    public const uint EVENT_KEY_UP = 0x301;

    public const int SCANCODE_A        = 4;
    public const int SCANCODE_Z        = 29;
    public const int SCANCODE_RETURN   = 40;
    public const int SCANCODE_KP_ENTER = 88;
}

/// <summary>SDL_KeyboardEvent. Every SDL_Event starts with the uint <c>type</c> field.</summary>
[StructLayout(LayoutKind.Sequential)]
struct SdlKeyboardEvent
{
    public uint   type;
    public uint   reserved;
    public ulong  timestamp;
    public uint   windowID;
    public uint   which;
    public int    scancode;  // SDL_Scancode
    public uint   key;       // SDL_Keycode
    public ushort mod;       // SDL_Keymod
    public ushort raw;
    public byte   down;
    public byte   repeat;
}
