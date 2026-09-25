# SWCUOPlugin

[ClassicUO](https://github.com/ClassicUO/ClassicUO) plugin for the freeshard [Schattenwelt](https://alte-schattenwelt.de), written in C#.
It is the managed counterpart of [swcuors](https://github.com/OldSW/swcuors) (Rust).

## Features

### Typing Indicator

While you type a chat message, the plugin tells the server so it can show a typing animation above your character.

- Only letter keys (A–Z) count. After more than 10 letters, the plugin sends a packet, at most once every 5 seconds.
- Pressing Enter (sending the message) resets the counter.
- The packet is the extended command `0xBF` with the custom subcommand `0xEF` (5 bytes: `BF 00 05 00 EF`).
  The server needs a handler for it, see [Server setup](#server-setup).

### Journal

The plugin writes all text the client receives – speech, emotes, whispers, yells, spells, guild/alliance chat and
system messages – to a text file per character and day, e.g. `Journal/Jörg_2026-09-25.txt`:

```
[14:03:12] Bob: Grüß dich
[14:03:15] [Emote] Alice: *winkt*
[14:03:20] [System] You see: a chair
```

Object labels (single-clicking items/mobiles) are skipped. Files are UTF-8 and appended to, so restarting the client keeps the day's log.

## Configuration

On first start the plugin creates `SWCUOPlugin.ini` next to `SWCUOPlugin.dll`:

```ini
# Write received speech, emotes and system messages to a text file per character and day.
JournalEnabled=true
# Absolute path, or relative to the plugin folder. Environment variables like %USERPROFILE% are expanded.
JournalFolder=Journal
```

Changes take effect after restarting the client.

## Installation

1. Download `SWCUOPlugin-<version>.zip` from the [releases](https://github.com/OldSW/SWCUOPlugin/releases).
2. Extract `SWCUOPlugin.dll` into a folder below ClassicUO's plugin directory, e.g.
   `ClassicUO/Data/Plugins/swplugin/SWCUOPlugin.dll`.
3. Add the plugin to ClassicUO's `settings.json`:
   ```json
   "plugins": [ "swplugin/SWCUOPlugin.dll" ]
   ```

## Server setup

Add a new PacketHandler script:

```csharp
using Server.Mobiles;

namespace Server.Network;

public class TypingIndicator : IBootInitialize
{
    [Boot]
    public static void Initialize()
    {
        PacketHandlers.RegisterExtended(0xEF, true, TypingIndicatorRequest);
    }

    public static void TypingIndicatorRequest(NetState state, PacketReader pvSrc)
    {
        if (state.Mobile is PlayerMobile pm)
        {
            pm.StartTypingAnimationTimer();
        }
    }
}
```

And add this to your `PlayerMobile.cs` for the animation:

```csharp
private Timer m_LastPlayedTypingAnimationTimer = null;

public void StartTypingAnimationTimer()
{
    m_LastPlayedTypingAnimationTimer?.Stop();
    m_LastPlayedTypingAnimationTimer = Timer.DelayCall(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(200), 20,
        new TimerStateCallback(TypeAnimation), new TypingAnimation(this));
    m_LastPlayedTypingAnimationTimer.Start();
}

private class TypingAnimation(Mobile mobile)
{
    public Mobile Mobile { get; set; } = mobile;
    public int Count { get; set; } = 0;
}

private static void TypeAnimation(object o)
{
    if (o is TypingAnimation animInfo && animInfo.Mobile != null && !animInfo.Mobile.Hidden)
    {
        int height = animInfo.Mobile.Z + 18;
        Entity ent = new Entity(Serial.Zero, new Point3D(animInfo.Mobile.X, animInfo.Mobile.Y, height), animInfo.Mobile.Map);
        Effects.SendTargetParticles(ent, 0x1810 + animInfo.Count, 1, 5, animInfo.Mobile.SpeechHue, EffectLayer.Head, 0);
        animInfo.Count++;
        if (animInfo.Count > 12) animInfo.Count = 0;
    }
}
```

## Building

Requires the .NET SDK 10 (see `global.json`). The plugin targets .NET Framework 4.7.2, because on Windows
ClassicUO loads managed plugins through its .NET Framework launcher. The reference assemblies come from NuGet,
so it builds on Windows, macOS and Linux.

```sh
dotnet build SWCUOPlugin/SWCUOPlugin.csproj -c Release   # -> SWCUOPlugin/bin/Release/net472/SWCUOPlugin.dll
dotnet run --project NativeTrampolineTest                # trampoline self-test
```

A **Debug** build (`dotnet build SWCUOPlugin/SWCUOPlugin.csproj`) additionally writes debug output to
`SWCUOPlugin.log` next to the DLL and, on Windows, opens a console window. Release builds contain no debug code.

## Releases

Pushing a version tag builds the plugin and publishes a GitHub release with `SWCUOPlugin-<tag>.zip`:

```sh
git tag 1.0.0
git push origin 1.0.0
```

## License

[BSD 2-Clause](LICENSE.md)
