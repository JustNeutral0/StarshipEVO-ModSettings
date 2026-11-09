using EvoCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UI;
using UnityEngine;
using UnityEngine.UI;

#if !BEPINEX
[assembly: MelonLoader.MelonInfo(typeof(ModSettings.Mod), "Mod Settings", "0.5.1", "Claude")]
[assembly: MelonLoader.MelonGame("Moonfire Entertainment", "Starship EVO")]
[assembly: MelonLoader.MelonAdditionalDependencies("EvoCore")]
#endif

namespace ModSettings
{
    // One window for every mod's settings and test tools, opened from a hotkey or a button in the game's
    // Escape menu, so mods never fight over keys.
    //
    // A mod adds a tab with no reference to this DLL: declare any attribute class named ModSettingsTabAttribute
    // (or the older TestPanelTabAttribute) whose constructor takes the tab title, and put it on a static,
    // parameterless method that draws the tab with GUILayout. Mods that do reference this DLL can call
    // Mod.AddTab instead. Every mod's setting is also editable in the "All preferences" tab.
#if BEPINEX
    [BepInEx.BepInPlugin("evo.modsettings", "Mod Settings", "0.5.1")]
    [BepInEx.BepInDependency("evo.core")]
#endif
    public class Mod : EvoMod
    {
        class Tab
        {
            public string title, source;
            public Action draw;
        }

        const string ButtonName = "ModSettingsButton";
        static readonly string[] TabAttributes = { "ModSettingsTabAttribute", "TestPanelTabAttribute" };
        static readonly List<Tab> tabs = new List<Tab>();
        static readonly PropertyInfo InMenu = AccessTools.Property(typeof(PlayerState), nameof(PlayerState.InMenu));
        static readonly FieldInfo MultiplayerButton = AccessTools.Field(typeof(UI_MainMenu), "openToMultiButton");
        static readonly FieldInfo SkipTranslation = AccessTools.Field(typeof(UITemplateButton), "skipTranslation");
        static Mod instance;

        Setting<string> hotkeyPref;
        Setting<bool> menuButtonPref;
        KeyCode hotkey = KeyCode.F11;
        bool open, scanned, blockingGame, rebinding, prefsDirty;
        UI_MainMenu openedFrom;
        int selected;
        Rect window = new Rect(60, 60, 760, 560);
        Vector2 scroll;
        string error;
        readonly Dictionary<Setting, string> edits = new Dictionary<Setting, string>();

        public static bool IsOpen => instance != null && instance.open;

        public static void Close()
        {
            if (instance != null && instance.open) instance.SetOpen(false);
        }

        // The window's place and size, for tabs that fit their content to it (the graph canvas). Read by reflection:
        // ModSettings.Mod.WindowRect.
        public static Rect WindowRect => instance != null ? instance.window : default;

        // Grows the window to at least this size (kept on screen); never shrinks it. By reflection:
        // ModSettings.Mod.EnsureSize(1100f, 820f).
        public static void EnsureSize(float width, float height)
        {
            if (instance == null) return;
            var w = instance.window;
            w.width = Mathf.Min(Mathf.Max(w.width, width), Screen.width - 20);
            w.height = Mathf.Min(Mathf.Max(w.height, height), Screen.height - 20);
            w.x = Mathf.Clamp(w.x, 10, Mathf.Max(10, Screen.width - w.width - 10));
            w.y = Mathf.Clamp(w.y, 10, Mathf.Max(10, Screen.height - w.height - 10));
            instance.window = w;
        }

        // Opens the window (if it isn't already) on the tab with this title. Other mods call it by reflection:
        // ModSettings.Mod.Open("Scripts"). False when there's no such tab, or the window isn't loaded.
        public static bool Open(string tab)
        {
            if (instance == null) return false;
            if (!instance.open) instance.SetOpen(true);
            var index = tabs.FindIndex(t => t.title == tab);
            if (index < 0) return false;
            if (index != instance.selected)
            {
                instance.selected = index;
                instance.scroll = Vector2.zero;
                instance.error = null;
                instance.edits.Clear();
            }
            return true;
        }

        public static void AddTab(string title, Action draw, string source = null)
        {
            if (string.IsNullOrEmpty(title) || draw == null) return;
            tabs.RemoveAll(t => t.title == title);
            tabs.Add(new Tab { title = title, draw = draw, source = source ?? draw.Method.DeclaringType?.Assembly.GetName().Name });
        }

        // F1 to F10 are taken by the game, so the key can be changed here or in UserData\MelonPreferences.cfg.
        protected override void Init()
        {
            instance = this;
            var category = Settings.For("ModSettings", "Mod Settings");
            hotkeyPref = category.Add("Hotkey", "F11", description: "Key that opens the Mod Settings window");
            menuButtonPref = category.Add("PauseMenuButton", true, description: "Show a Mod Settings button in the game's Escape menu");
            if (Enum.TryParse(hotkeyPref.Value, true, out KeyCode key)) hotkey = key;
            else Log.Warning($"Unknown hotkey '{hotkeyPref.Value}', using F11");
            Log.Msg($"Press {hotkey} or use the Escape menu to open Mod Settings");
        }

        protected override void Tick()
        {
            if (!rebinding && Input.GetKeyDown(hotkey)) SetOpen(!open);
            if (!open) return;
            // The game re-locks the cursor for play, so keep it free while the window is up.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Escape is handled after the game's Update, which ignores it while we hold the in-menu flag, so one
        // press doesn't both close this window and toggle the game's menu.
        protected override void LateTick()
        {
            if (open && !rebinding && Input.GetKeyDown(KeyCode.Escape)) Back();
        }

        // Leaves for the Escape menu when it was opened from there, otherwise back to the game.
        void Back()
        {
            var menu = openedFrom;
            SetOpen(false);
            if (menu != null) menu.SetActive(true);
        }

        void SetOpen(bool value, UI_MainMenu from = null)
        {
            if (value && !scanned) Scan();
            if (!value && prefsDirty) SavePrefs();
            open = value;
            rebinding = false;
            openedFrom = value ? from : null;
            if (open)
            {
                // Setting the game's own in-menu flag stops the camera and movement, as its menus do.
                // Leave it alone if a real game menu already holds it.
                blockingGame = !PlayerState.InMenu && InMenu?.SetMethod != null;
                if (blockingGame) InMenu.SetValue(null, true);
            }
            else
            {
                if (blockingGame && PlayerState.InMenu) InMenu.SetValue(null, false);
                blockingGame = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void SavePrefs()
        {
            prefsDirty = false;
            Settings.SaveAll();
        }

        // Finds tab methods in every mod by attribute name, so mods need no reference to this DLL.
        void Scan()
        {
            scanned = true;
            var modsFolder = EvoCore.Paths.Mods;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string location;
                try { location = assembly.Location; } catch { continue; }
                if (string.IsNullOrEmpty(location) || !Path.GetFullPath(location).StartsWith(modsFolder, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var type in Types(assembly))
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.GetParameters().Length != 0) continue;
                    var tag = method.GetCustomAttributesData().FirstOrDefault(a => TabAttributes.Contains(a.AttributeType.Name));
                    if (tag == null) continue;
                    var title = tag.ConstructorArguments.Count > 0 ? tag.ConstructorArguments[0].Value as string : null;
                    try { AddTab(title ?? method.Name, (Action)Delegate.CreateDelegate(typeof(Action), method), assembly.GetName().Name); }
                    catch (Exception e) { Log.Warning($"Skipped tab {type.Name}.{method.Name}: {e.Message}"); }
                }
            }
            tabs.Sort((a, b) => string.Compare(a.title, b.title, StringComparison.OrdinalIgnoreCase));
            tabs.Insert(0, new Tab { title = "Mods", draw = DrawMods, source = "ModSettings" });
            tabs.Add(new Tab { title = "All preferences", draw = DrawPreferences, source = "ModSettings" });
            Log.Msg($"Mod Settings tabs: {string.Join(", ", tabs.Select(t => $"{t.title} ({t.source})"))}");
        }

        static IEnumerable<Type> Types(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        protected override void Gui()
        {
            if (!open) return;
            if (rebinding) CatchKey();
            var drawn = GUILayout.Window(0x7E57, window, DrawWindow, $"Mod Settings   ({hotkey} or Esc to close)");
            // Resizing changes window inside DrawWindow; GUILayout.Window would hand back the old size.
            window = resizing ? new Rect(drawn.x, drawn.y, window.width, window.height) : drawn;
        }

        // The grip in the bottom-right corner: drag it to resize the window.
        bool resizing;
        int gripId;

        void DrawGrip()
        {
            var grip = new Rect(window.width - 18, window.height - 18, 16, 16);
            GUI.Label(grip, "//");
            var e = Event.current;
            gripId = GUIUtility.GetControlID(FocusType.Passive);   // asked for every event, as IMGUI expects
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && grip.Contains(e.mousePosition):
                    resizing = true;
                    GUIUtility.hotControl = gripId;
                    e.Use();
                    break;
                case EventType.MouseDrag when resizing:
                    window.width = Mathf.Clamp(window.width + e.delta.x, 600, Screen.width - window.x - 5);
                    window.height = Mathf.Clamp(window.height + e.delta.y, 400, Screen.height - window.y - 5);
                    e.Use();
                    break;
                case EventType.MouseUp when resizing:
                    resizing = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        void DrawWindow(int id)
        {
            selected = Mathf.Clamp(selected, 0, tabs.Count - 1);
            var picked = GUILayout.SelectionGrid(selected, tabs.Select(t => t.title).ToArray(), Mathf.Min(tabs.Count, 5));
            if (picked != selected) { selected = picked; scroll = Vector2.zero; error = null; edits.Clear(); }
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll);
            // One broken tab must not take the window down with it.
            try { tabs[selected].draw(); }
            catch (Exception e)
            {
                if (error == null) { error = e.Message; Log.Error($"Tab '{tabs[selected].title}' failed: {e}"); }
            }
            if (error != null) GUILayout.Label("This tab threw an error (see the log): " + error);
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(openedFrom != null ? "Back to menu" : "Close", GUILayout.Width(140))) Back();
            GUILayout.Space(16);
            GUILayout.EndHorizontal();
            DrawGrip();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        void DrawMods()
        {
            GUILayout.Label("<b>This window</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label(rebinding ? "Press a key (Esc cancels)..." : $"Opens with {hotkey}", GUILayout.Width(260));
            if (!rebinding && GUILayout.Button("Change key", GUILayout.Width(120))) rebinding = true;
            GUILayout.EndHorizontal();
            if (hotkey >= KeyCode.F1 && hotkey <= KeyCode.F10) GUILayout.Label("The game also uses F1 to F10, so this key does both.");
            var button = GUILayout.Toggle(menuButtonPref.Value, " Mod Settings button in the Escape menu");
            if (button != menuButtonPref.Value) { menuButtonPref.Value = button; prefsDirty = true; }

            GUILayout.Space(10);
            GUILayout.Label("<b>Loaded mods</b>");
            foreach (var info in Mods.Loaded)
            {
                var tabCount = tabs.Count(t => t.source == info.Assembly?.GetName().Name && t.source != "ModSettings");
                GUILayout.Label($"{info.Name} {info.Version}   by {info.Author}" + (tabCount > 0 ? $"   ({tabCount} tab{(tabCount > 1 ? "s" : "")})" : ""));
            }
        }

        // Takes the next key press for the hotkey. Escape cancels; the mouse buttons are never taken.
        void CatchKey()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
            if (e.keyCode != KeyCode.Escape)
            {
                hotkey = e.keyCode;
                hotkeyPref.Value = hotkey.ToString();
                SavePrefs();
            }
            rebinding = false;
            e.Use();
        }

        // Every visible setting of every mod. Mods that read their settings once at start
        // only see a change after a restart.
        void DrawPreferences()
        {
            GUILayout.Label("Changes save when this window closes. Some mods only read their settings when the game starts.");
            foreach (var category in Settings.All)
            {
                if (category.IsHidden || category.Entries.All(e => e.IsHidden)) continue;
                GUILayout.Space(8);
                GUILayout.Label($"<b>{category.DisplayName}</b>");
                foreach (var entry in category.Entries)
                    if (!entry.IsHidden) DrawEntry(entry);
            }
        }

        void DrawEntry(Setting entry)
        {
            var type = entry.Type;
            var name = entry.DisplayName;
            GUILayout.BeginHorizontal();
            if (type == typeof(bool))
            {
                var value = (bool)entry.BoxedValue;
                if (GUILayout.Toggle(value, " " + name) != value) Set(entry, !value);
            }
            else
            {
                GUILayout.Label(name, GUILayout.Width(220));
                if (type == typeof(string) || IsNumber(type))
                {
                    if (!edits.TryGetValue(entry, out var text)) text = Format(entry.BoxedValue);
                    var changed = GUILayout.TextField(text, GUILayout.Width(260));
                    if (changed != text)
                    {
                        edits[entry] = changed;
                        if (TryParse(changed, type, out var parsed)) Set(entry, parsed);
                    }
                }
                else if (type.IsEnum)
                {
                    if (GUILayout.Button(entry.BoxedValue.ToString(), GUILayout.Width(260)))
                    {
                        var values = Enum.GetValues(type);
                        Set(entry, values.GetValue((Array.IndexOf(values, entry.BoxedValue) + 1) % values.Length));
                    }
                }
                else GUILayout.Label(entry.ValueText);
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(entry.Description)) GUILayout.Label("    " + entry.Description);
        }

        void Set(Setting entry, object value)
        {
            entry.BoxedValue = value;
            prefsDirty = true;
        }

        static bool IsNumber(Type type) =>
            type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double);

        static string Format(object value) =>
            value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";

        static bool TryParse(string text, Type type, out object value)
        {
            value = null;
            var style = NumberStyles.Float;
            var culture = CultureInfo.InvariantCulture;
            if (type == typeof(string)) value = text;
            else if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, culture, out var i)) value = i;
            else if (type == typeof(long) && long.TryParse(text, NumberStyles.Integer, culture, out var l)) value = l;
            else if (type == typeof(float) && float.TryParse(text, style, culture, out var f)) value = f;
            else if (type == typeof(double) && double.TryParse(text, style, culture, out var d)) value = d;
            return value != null;
        }

        // Adds a Mod Settings button to the game's Escape menu, copied from its multiplayer button so it
        // looks the same, the first time the menu opens.
        [HarmonyPatch(typeof(UI_MainMenu), nameof(UI_MainMenu.SetActive))]
        static class MenuButton
        {
            static void Postfix(UI_MainMenu __instance, bool active)
            {
                if (!active || instance == null) return;
                try { Add(__instance); }
                catch (Exception e) { instance.Log.Error("Couldn't add the Escape menu button: " + e); }
            }

            static void Add(UI_MainMenu menu)
            {
                var template = MultiplayerButton?.GetValue(menu) as UITemplateButton;
                if (template == null) return;
                var parent = template.transform.parent;
                var existing = parent.Find(ButtonName);
                if (existing != null)
                {
                    existing.gameObject.SetActive(instance.menuButtonPref.Value);
                    var click = existing.GetComponent<Selectable>();
                    if (click != null) click.enabled = true; // the game's own handler disables a pressed button
                    return;
                }
                if (!instance.menuButtonPref.Value) return;

                var go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                go.name = ButtonName;
                go.SetActive(true);
                go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
                var button = go.GetComponent<UITemplateButton>();
                SkipTranslation?.SetValue(button, true);
                button.SetButtonLabel("Mod Settings", "MainMenu_ModSettings_Button");
                button.SetTooltip("Settings and test tools for installed mods");
                var unityButton = go.GetComponent<Button>();
                if (unityButton != null)
                {
                    // A fresh event drops the multiplayer call copied with the button.
                    unityButton.onClick = new Button.ButtonClickedEvent();
                    unityButton.onClick.AddListener(() =>
                    {
                        menu.SetActive(false);
                        instance.SetOpen(true, menu);
                    });
                }
                if (parent.GetComponent<LayoutGroup>() == null) MakeRoom(go.GetComponent<RectTransform>(), template.GetComponent<RectTransform>());
                instance.Log.Msg($"Added the Escape menu button under {Path(parent)} ({parent.childCount} children, layout {(parent.GetComponent<LayoutGroup>()?.GetType().Name ?? "none")})");
            }

            // Without a layout group the copy would sit on top of the original, so put it one row lower and
            // move every button below it down by a row too.
            static void MakeRoom(RectTransform added, RectTransform template)
            {
                if (added == null || template == null) return;
                var rows = template.parent.Cast<Transform>().OfType<RectTransform>()
                    .Where(r => r != added && r.GetComponent<UITemplateButton>() != null).ToList();
                var gaps = rows.Select(r => Mathf.Abs(r.anchoredPosition.y - template.anchoredPosition.y)).Where(g => g > 1f).ToList();
                var step = gaps.Count > 0 ? gaps.Min() : template.rect.height + 4f;
                GrowDown(template.parent as RectTransform, step);
                foreach (var row in rows)
                    if (row.anchoredPosition.y < template.anchoredPosition.y - 1f)
                        row.anchoredPosition -= new Vector2(0f, step);
                added.anchoredPosition = template.anchoredPosition - new Vector2(0f, step);
            }

            // Makes the menu's box one row taller at the bottom, so the last button stays inside it. Children
            // pinned to a point keep their place on screen; children stretched with the box grow with it.
            static void GrowDown(RectTransform box, float step)
            {
                if (box == null) return;
                var pinned = box.Cast<Transform>().OfType<RectTransform>()
                    .Where(r => r.anchorMin.y == r.anchorMax.y).ToDictionary(r => r, r => r.position);
                box.sizeDelta += new Vector2(0f, step);
                box.anchoredPosition -= new Vector2(0f, step * (1f - box.pivot.y));
                foreach (var pair in pinned) pair.Key.position = pair.Value;
                instance.Log.Msg($"Grew {box.name} by {step:F0} to {box.rect.size}, pivot {box.pivot}, anchors {box.anchorMin}-{box.anchorMax}; " +
                    string.Join(", ", box.Cast<Transform>().OfType<RectTransform>().Select(r => $"{r.name} {r.anchorMin.y:F2}-{r.anchorMax.y:F2} y {r.anchoredPosition.y:F0}")));
            }

            static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
        }
    }
}
