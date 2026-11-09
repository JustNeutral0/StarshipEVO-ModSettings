# Mod Settings

One window for every mod's settings and test tools, so mods never fight over hotkeys. Open it with F11 or the **Mod Settings** button in the game's Escape menu. Esc closes it, going back to the Escape menu if you came from there.

The **Mods** tab lists the loaded mods, changes the key, and turns the Escape-menu button on or off. **All preferences** edits every MelonPreferences entry of every mod; changes save when the window closes, and some mods only read them at start. Both are also under `[ModSettings]` in `UserData\MelonPreferences.cfg`.

While the window is open, it frees the mouse and sets the game's own in-menu flag, so the camera and movement stop as in the game's menus.

## Adding a tab to any mod

Your mod needs no reference to `ModSettings.dll`. Paste this attribute into the mod; the window matches it by name, so the namespace doesn't matter (the older name `TestPanelTabAttribute` also works):

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

The method is called every GUI event while its tab is showing, so keep it light. If it throws, the window logs the error and shows it in the tab instead of breaking. Keep settings in MelonPreferences so they last between runs.

A mod that references `ModSettings.dll` can call `ModSettings.Mod.AddTab("Title", DrawMethod)`, `Mod.Close()`, `Mod.Open("Tab title")` (0.3.0: opens the window on that tab, false if there's no such tab) and `Mod.IsOpen`. The UCS loader calls `Close` by reflection before a screenshot; EvoCore calls `Open("Scripts")` the same way.

## Escape-menu button

The first time the Escape menu opens, a copy of its multiplayer button is added right below it, with its own label and click. If the menu has no layout group, the buttons below are moved down a row. The log notes where it went.

## Tabs so far

| Tab | Mod | What it does |
|---|---|---|
| Mods | ModSettings | Loaded mods, the hotkey, the Escape-menu button |
| NPC ships | EvoFixes (NpcDebug) | Collision boxes on or off, an on-screen ship list, and a spin log |
| NPC avoidance fix | EvoFixes (NpcAvoidFix) | Turns the fix on or off for comparison, and counts the ships it frees |
| Repair Bay | EvoFixes (RepairBay) | Each loaded bay's ship, size, state and current status |
| Pipe Thin fix | EvoFixes (PipeThinFix) | What it measured for Pipe Thin, and turns the fix on or off |
| UCS loader | UCS_StarshipEvoLoader | Pick a character, size, grip, shader, draw mode, screenshot |
| All preferences | ModSettings | Every MelonPreferences entry |
