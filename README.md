# Mod Settings

A Starship EVO mod that gives every mod's settings and tools one window, so mods never fight over hotkeys.
Open it with **F11** or the **Mod Settings** button in the game's Escape menu; Esc closes it.

> Early test release. Built and tested against Starship EVO early-access **26w45a**, single-player.

## What it does

- **Mods** tab: the loaded mods, the hotkey, and the Escape-menu button on or off.
- **All preferences**: edit every setting of every mod built on EvoCore. Settings are saved in
  `BepInEx\config\Evo\<mod>.cfg`; some mods only read them at start.
- Mods add their own tabs, such as [Cockpit Displays](https://github.com/JustNeutral0/StarshipEVO-CockpitDisplays)'
  **Camera screens** and [EvoCore](https://github.com/JustNeutral0/StarshipEVO-EvoCore)'s **Scripts**.
- While it's open, it frees the mouse and stops the camera and movement, as the game's own menus do.

## Install

Needs [EvoCore](https://github.com/JustNeutral0/StarshipEVO-EvoCore) (install it first) and
[Starship Uplink](https://github.com/MadTheDog/Starship-Uplink).

1. In Uplink, choose **Add from GitHub** and enter `JustNeutral0/StarshipEVO-ModSettings`.
2. Pick the `justneutral.modsettings-<version>.zip` asset, then install it into your profile.
3. Launch the game through Uplink.

## Adding a tab from your own mod

Your mod needs no reference to Mod Settings. Paste this attribute into it; the window finds it by name, so the
namespace doesn't matter:

```csharp
[System.AttributeUsage(System.AttributeTargets.Method)]
class ModSettingsTabAttribute : System.Attribute
{
    public ModSettingsTabAttribute(string title) { }
}
```

Then tag a static, parameterless method that draws the tab with `GUILayout`:

```csharp
[ModSettingsTab("My mod")]
static void DrawSettingsTab()
{
    if (GUILayout.Button("Do the thing")) DoTheThing();
}
```

It's called every GUI event while its tab is showing, so keep it light. If it throws, the error is shown in the tab
instead of breaking the window.

## Building from source

You need the .NET SDK and Starship EVO with BepInEx (Starship Uplink sets it up). This mod builds against EvoCore's source: clone [StarshipEVO-EvoCore](https://github.com/JustNeutral0/StarshipEVO-EvoCore) next to this repo (both folders side by side).

```
dotnet build ModSettings.csproj -c Release -p:Loader=BepInEx -p:Game="C:\path\to\Starship EVO"
```

`-p:Game` is your game folder (it defaults to the usual Steam location). The DLL lands in `bin\BepInEx\Release\`.

[MODDING.md](MODDING.md) has more on adding tabs.

## Licence

MIT, see [LICENSE](LICENSE). Starship EVO itself belongs to its developers.

Part of [Starship-EVO-Mods](https://github.com/JustNeutral0/Starship-EVO-Mods).
