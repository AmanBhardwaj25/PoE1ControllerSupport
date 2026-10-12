using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace PoEController.Settings
{
    /// <summary>
    /// The mod's own settings panel (View + Start in the world; no changes to the game's menus). Built from the
    /// plugin's BepInEx config entries, so every setting appears automatically: on/off, choices (enums) and numbers
    /// (stepped inside their allowed range). Changing a value sets the config entry, which BepInEx saves at once;
    /// the plugin reads the values live. Pauses the game while open (not in turn-based combat).
    /// </summary>
    internal static class SettingsPanel
    {
        public sealed class Row
        {
            /// <summary>Null for a section header.</summary>
            public ConfigEntryBase Entry;
            public string Label;
        }

        /// <summary>Readable names; anything not listed gets its key split into words.</summary>
        private static readonly Dictionary<string, string> s_Labels = new Dictionary<string, string>
        {
            { "Controls.Style", "Control style" },
            { "Controls.ConsoleHideActionBar", "Hide the action bar (Console)" },
            { "Controls.ConsoleCombatPointer", "Pointer in real-time combat (Console)" },
            { "Prompts.Enabled", "Show button prompts" },
            { "Prompts.Scale", "Prompt size" },
            { "Prompts.TargetLabel", "Word on the A prompt" },
            { "Prompts.ConsoleShortBar", "Short prompt bar (Console)" },
            { "Prompts.WorldPosition", "Prompt bar in the world" },
            { "Prompts.MenuPosition", "Prompt bar in menus" },
            { "Glyphs.Style", "Button glyph style" },
            { "Input.FollowCamera", "Follow camera (R3)" },
            { "Input.CameraSpeed", "Camera speed" },
            { "Input.StickDeadzone", "Stick dead zone" },
            { "Input.AnalogMovementSpeed", "Analog movement speed" },
            { "Input.PauseOnDisconnect", "Pause when the controller disconnects" },
            { "Menus.QuickTooltips", "Quick tooltips" },
            { "Menus.BrightTooltipWords", "Brighter tooltip words" },
            { "Menus.TooltipDelay", "Tooltip delay (seconds)" },
            { "Tutorials.Timed", "Timed tutorials" },
            { "Tutorials.ReadingWordsPerMinute", "Tutorial reading speed (words/min)" },
            { "Tutorials.RecallHoldSeconds", "Hold L3 to recall a tutorial (seconds)" },
        };

        /// <summary>Choice names that word-splitting would get wrong.</summary>
        private static readonly Dictionary<string, string> s_ValueNames = new Dictionary<string, string>
        {
            { "PlayStation", "PlayStation" },
            { "SteamDeck", "Steam Deck" },
            { "GlyphOnly", "Glyph only" },
            { "PcLayout", "PC layout" },
        };

        private static readonly string[] s_SectionOrder = { "Controls", "Prompts", "Input", "Menus", "Tutorials", "Glyphs" };

        private static readonly Dictionary<string, string> s_SectionNames = new Dictionary<string, string>
        {
            { "Controls", "Controls" },
            { "Prompts", "Prompts" },
            { "Input", "Camera & controller" },
            { "Menus", "Menus" },
            { "Tutorials", "Tutorials" },
            { "Glyphs", "Glyphs" },
        };

        private static readonly double[] s_NiceSteps = { 0.01, 0.02, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 20, 50, 100 };

        public static readonly List<Row> Rows = new List<Row>();
        public static int Focus { get; private set; }
        public static bool IsOpen { get; private set; }

        private static bool s_PausedByUs;

        public static void Open()
        {
            Build();
            if (Rows.Count == 0)
            {
                return;
            }
            Focus = NextSelectable(-1, 1);
            IsOpen = true;
            TimeController tc = TimeController.Instance;
            if (tc != null && !tc.Paused && !TacticalModeManager.IsInTacticalCombat())
            {
                tc.SafePaused = true;
                s_PausedByUs = tc.Paused;
            }
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo("Controller settings opened");
        }

        public static void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            if (s_PausedByUs && TimeController.Instance != null && TimeController.Instance.Paused)
            {
                TimeController.Instance.Paused = false;
            }
            s_PausedByUs = false;
            Plugin.Log.LogInfo("Controller settings closed");
        }

        public static void MoveFocus(int dir)
        {
            int next = NextSelectable(Focus, dir);
            if (next >= 0)
            {
                Focus = next;
            }
        }

        public static ConfigEntryBase FocusedEntry => Focus >= 0 && Focus < Rows.Count ? Rows[Focus].Entry : null;

        /// <summary>A on the focused row: flips on/off, next choice; numbers do nothing (use ←/→).</summary>
        public static bool Activate()
        {
            ConfigEntryBase e = FocusedEntry;
            if (e == null || !(e.SettingType == typeof(bool) || e.SettingType.IsEnum))
            {
                return false;
            }
            Change(1);
            return true;
        }

        public static bool CanActivate => FocusedEntry != null && (FocusedEntry.SettingType == typeof(bool) || FocusedEntry.SettingType.IsEnum);

        /// <summary>←/→: previous / next value (wraps for choices, clamps for numbers).</summary>
        public static void Change(int dir)
        {
            ConfigEntryBase e = FocusedEntry;
            if (e == null)
            {
                return;
            }
            Type t = e.SettingType;
            if (t == typeof(bool))
            {
                e.BoxedValue = !(bool)e.BoxedValue;
            }
            else if (t.IsEnum)
            {
                Array values = Enum.GetValues(t);
                int i = Array.IndexOf(values, e.BoxedValue);
                e.BoxedValue = values.GetValue(((i + dir) % values.Length + values.Length) % values.Length);
            }
            else if (t == typeof(float))
            {
                GetRange(e, out double min, out double max);
                double step = Step(min, max);
                double v = Math.Round(((float)e.BoxedValue + dir * step) / step) * step;
                e.BoxedValue = (float)Clamp(v, min, max);
            }
            else if (t == typeof(int))
            {
                GetRange(e, out double min, out double max);
                int step = Math.Max(1, (int)Step(min, max));
                e.BoxedValue = (int)Clamp((int)e.BoxedValue + dir * step, min, max);
            }
            else
            {
                return;
            }
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        public static string ValueText(ConfigEntryBase e)
        {
            object v = e.BoxedValue;
            if (v is bool b)
            {
                return b ? "On" : "Off";
            }
            if (v is float f)
            {
                return f.ToString("0.##", CultureInfo.InvariantCulture);
            }
            if (v is Enum)
            {
                string name = v.ToString();
                return s_ValueNames.TryGetValue(name, out string nice) ? nice : Words(name);
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static string Description(ConfigEntryBase e) => e?.Description?.Description ?? "";

        private static void Build()
        {
            Rows.Clear();
            ConfigFile config = Plugin.Instance != null ? Plugin.Instance.Config : null;
            if (config == null)
            {
                return;
            }
            var bySection = new Dictionary<string, List<ConfigEntryBase>>();
            var sections = new List<string>(s_SectionOrder);
            foreach (ConfigDefinition def in config.Keys)
            {
                ConfigEntryBase e = config[def];
                if (!Supported(e.SettingType))
                {
                    continue;
                }
                if (!bySection.TryGetValue(def.Section, out List<ConfigEntryBase> list))
                {
                    list = new List<ConfigEntryBase>();
                    bySection[def.Section] = list;
                    if (!sections.Contains(def.Section))
                    {
                        sections.Add(def.Section);
                    }
                }
                list.Add(e);
            }
            foreach (string section in sections)
            {
                if (!bySection.TryGetValue(section, out List<ConfigEntryBase> list))
                {
                    continue;
                }
                Rows.Add(new Row { Label = s_SectionNames.TryGetValue(section, out string name) ? name : section });
                foreach (ConfigEntryBase e in list)
                {
                    string key = e.Definition.Section + "." + e.Definition.Key;
                    Rows.Add(new Row { Entry = e, Label = s_Labels.TryGetValue(key, out string label) ? label : Words(e.Definition.Key) });
                }
            }
        }

        private static bool Supported(Type t) => t == typeof(bool) || t == typeof(float) || t == typeof(int) || t.IsEnum;

        private static int NextSelectable(int from, int dir)
        {
            for (int i = 1; i <= Rows.Count; i++)
            {
                int j = ((from + dir * i) % Rows.Count + Rows.Count) % Rows.Count;
                if (Rows[j].Entry != null)
                {
                    return j;
                }
            }
            return -1;
        }

        private static void GetRange(ConfigEntryBase e, out double min, out double max)
        {
            min = double.NaN;
            max = double.NaN;
            AcceptableValueBase acc = e.Description?.AcceptableValues;
            if (acc != null)
            {
                // AcceptableValueRange<T> (generic): read MinValue / MaxValue by name.
                object lo = acc.GetType().GetProperty("MinValue")?.GetValue(acc, null);
                object hi = acc.GetType().GetProperty("MaxValue")?.GetValue(acc, null);
                if (lo != null && hi != null)
                {
                    min = Convert.ToDouble(lo, CultureInfo.InvariantCulture);
                    max = Convert.ToDouble(hi, CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>About 20 steps across the range, rounded to a friendly number (0.1 when there is no range).</summary>
        private static double Step(double min, double max)
        {
            if (double.IsNaN(min) || double.IsNaN(max) || max <= min)
            {
                return 0.1;
            }
            double raw = (max - min) / 20.0;
            foreach (double s in s_NiceSteps)
            {
                if (s >= raw)
                {
                    return s;
                }
            }
            return s_NiceSteps[s_NiceSteps.Length - 1];
        }

        private static double Clamp(double v, double min, double max)
        {
            if (!double.IsNaN(min))
            {
                v = Math.Max(min, v);
            }
            if (!double.IsNaN(max))
            {
                v = Math.Min(max, v);
            }
            return v;
        }

        /// <summary>"GlyphOnly" → "Glyph only".</summary>
        private static string Words(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]))
                {
                    sb.Append(' ');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
