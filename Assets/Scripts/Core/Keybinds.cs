using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Metro
{
    public enum BindAction
    {
        Pause, Speed1, Speed2, Speed3, Overview, Inspect, Line, Insert, Depot, Demolish,
        Population, Jobs, Coverage, Finance, Stats, City, CityHall, HideUI, Screenshot, Help,
        Plan    // new actions go at the end so saved bindings keep their meaning
    }

    [Serializable]
    public sealed class KeyBindSave
    {
        public string action;
        public int key;
    }

    /// <summary>Rebindable hotkeys (Settings → Controls). Esc, Ctrl+Z and the line tool's own keys stay fixed.</summary>
    public static class Keybinds
    {
        static readonly Dictionary<BindAction, Key> Defaults = new Dictionary<BindAction, Key>
        {
            { BindAction.Pause, Key.Space }, { BindAction.Speed1, Key.Digit1 }, { BindAction.Speed2, Key.Digit2 }, { BindAction.Speed3, Key.Digit3 },
            { BindAction.Overview, Key.Tab }, { BindAction.Inspect, Key.V }, { BindAction.Line, Key.L }, { BindAction.Insert, Key.I },
            { BindAction.Depot, Key.B }, { BindAction.Demolish, Key.X }, { BindAction.Population, Key.P }, { BindAction.Jobs, Key.J },
            { BindAction.Coverage, Key.C }, { BindAction.Finance, Key.F }, { BindAction.Stats, Key.T }, { BindAction.City, Key.G },
            { BindAction.CityHall, Key.M }, { BindAction.HideUI, Key.H }, { BindAction.Screenshot, Key.F12 }, { BindAction.Help, Key.F1 },
            { BindAction.Plan, Key.K }
        };

        static readonly Dictionary<BindAction, Key> current = new Dictionary<BindAction, Key>(Defaults);

        /// <summary>Keys that can't be bound: they have fixed meanings everywhere (menus, line drawing, the camera).</summary>
        public static bool Reserved(Key k) => k == Key.Escape || k == Key.Enter || k == Key.NumpadEnter || k == Key.Backspace || k == Key.None
                                              || k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt
                                              || k == Key.LeftShift || k == Key.RightShift || k == Key.LeftWindows || k == Key.RightWindows
                                              || k == Key.ContextMenu || k == Key.W || k == Key.A || k == Key.S || k == Key.D
                                              || k == Key.UpArrow || k == Key.DownArrow || k == Key.LeftArrow || k == Key.RightArrow
                                              || k == Key.Equals || k == Key.Minus || k == Key.NumpadPlus || k == Key.NumpadMinus;

        public static Key For(BindAction a) => current.TryGetValue(a, out var k) ? k : Key.None;

        public static bool Down(BindAction a)
        {
            var k = For(a);
            return k != Key.None && InputX.KeyDown(k);
        }

        public static string Label(BindAction a) => KeyLabel(For(a));

        public static string KeyLabel(Key k)
        {
            switch (k)
            {
                case Key.None: return "—";
                case Key.Space: return "Space";
                case Key.Tab: return "Tab";
                // Digit1..Digit9 come before Digit0 in the Input System's enum.
                case Key.Digit0: return "0";
                case Key.Digit1: case Key.Digit2: case Key.Digit3: case Key.Digit4: case Key.Digit5:
                case Key.Digit6: case Key.Digit7: case Key.Digit8: case Key.Digit9:
                    return ((int)(k - Key.Digit1) + 1).ToString();
            }
            // Letters and symbols as printed on the player's keyboard layout (bindings are physical keys).
            try
            {
                var kb = Keyboard.current;
                string shown = kb != null ? kb[k].displayName : null;
                if (!string.IsNullOrWhiteSpace(shown)) return shown.Length == 1 ? shown.ToUpperInvariant() : shown;
            }
            catch (Exception) { }
            return k.ToString();
        }

        public static string Title(BindAction a)
        {
            switch (a)
            {
                case BindAction.Pause: return "Pause / resume";
                case BindAction.Speed1: return "Normal speed";
                case BindAction.Speed2: return "Fast";
                case BindAction.Speed3: return "Fastest";
                case BindAction.Overview: return "Whole-city overview";
                case BindAction.Inspect: return "Inspect tool";
                case BindAction.Line: return "Build line";
                case BindAction.Insert: return "Add a station to a track";
                case BindAction.Depot: return "Build depot";
                case BindAction.Demolish: return "Demolish";
                case BindAction.Population: return "Population map";
                case BindAction.Jobs: return "Jobs map";
                case BindAction.Coverage: return "Coverage map";
                case BindAction.Finance: return "Finances";
                case BindAction.Stats: return "Statistics";
                case BindAction.City: return "City overview";
                case BindAction.CityHall: return "City Hall";
                case BindAction.HideUI: return "Hide the interface";
                case BindAction.Screenshot: return "Screenshot";
                case BindAction.Plan: return "Plan in pencil";
                default: return "Help";
            }
        }

        /// <summary>Binds a key; if another action had it, that action gets this one's old key (a swap).</summary>
        public static void Bind(BindAction a, Key k)
        {
            if (Reserved(k)) return;
            var old = For(a);
            foreach (var other in new List<BindAction>(current.Keys))
                if (other != a && current[other] == k) current[other] = old;
            current[a] = k;
        }

        public static void ResetDefaults()
        {
            current.Clear();
            foreach (var kv in Defaults) current[kv.Key] = kv.Value;
        }

        public static List<KeyBindSave> Save()
        {
            var list = new List<KeyBindSave>();
            foreach (var kv in current) list.Add(new KeyBindSave { action = kv.Key.ToString(), key = (int)kv.Value });
            return list;
        }

        public static void Load(List<KeyBindSave> saved)
        {
            ResetDefaults();
            if (saved == null) return;
            foreach (var s in saved)
                if (Enum.TryParse(s.action, out BindAction a) && Enum.IsDefined(typeof(Key), s.key) && !Reserved((Key)s.key)) current[a] = (Key)s.key;
        }
    }
}
