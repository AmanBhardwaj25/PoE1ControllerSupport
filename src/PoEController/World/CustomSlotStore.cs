using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PoEController.World
{
    /// <summary>
    /// What each character has pinned to the Combat Radial's 7 custom slots. Kept in our own text file next to the
    /// mod's config (<c>BepInEx/config/poe.controller.radialslots.txt</c>), never in save games. One line per slot:
    /// character key, slot, entry key, display name (tab-separated). Keys are object names, so they survive restarts
    /// and stay readable; an entry that can't be found any more (spell forgotten, item used up) shows as unavailable.
    /// </summary>
    internal static class CustomSlotStore
    {
        public const int Count = 7;

        public struct Pin
        {
            public string Key;
            public string Name;
        }

        private static readonly Dictionary<string, Pin[]> s_Pins = new Dictionary<string, Pin[]>();
        private static bool s_Loaded;

        private static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "poe.controller.radialslots.txt");

        /// <summary>"Edér", "Aloth"… — display names are stable per character within a playthrough.</summary>
        public static string CharacterKey(CharacterStats stats) => stats != null ? stats.Name() : null;

        public static Pin Get(CharacterStats stats, int slot)
        {
            Load();
            string key = CharacterKey(stats);
            return key != null && s_Pins.TryGetValue(key, out Pin[] pins) && slot >= 0 && slot < Count ? pins[slot] : default;
        }

        public static void Set(CharacterStats stats, int slot, string entryKey, string name)
        {
            Load();
            string key = CharacterKey(stats);
            if (key == null || slot < 0 || slot >= Count)
            {
                return;
            }
            if (!s_Pins.TryGetValue(key, out Pin[] pins))
            {
                pins = new Pin[Count];
                s_Pins[key] = pins;
            }
            pins[slot] = new Pin { Key = entryKey, Name = name };
            Save();
        }

        private static void Load()
        {
            if (s_Loaded)
            {
                return;
            }
            s_Loaded = true;
            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string[] f = line.Split('\t');
                    if (f.Length < 4 || !int.TryParse(f[1], out int slot) || slot < 0 || slot >= Count)
                    {
                        continue;
                    }
                    if (!s_Pins.TryGetValue(f[0], out Pin[] pins))
                    {
                        pins = new Pin[Count];
                        s_Pins[f[0]] = pins;
                    }
                    pins[slot] = new Pin { Key = f[2], Name = f[3] };
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Custom radial slots: could not read {FilePath}: {e.Message}");
            }
        }

        private static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (KeyValuePair<string, Pin[]> kv in s_Pins)
                {
                    for (int i = 0; i < Count; i++)
                    {
                        Pin p = kv.Value[i];
                        if (!string.IsNullOrEmpty(p.Key))
                        {
                            sb.Append(Clean(kv.Key)).Append('\t').Append(i).Append('\t').Append(Clean(p.Key)).Append('\t').Append(Clean(p.Name)).Append('\n');
                        }
                    }
                }
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Custom radial slots: could not write {FilePath}: {e.Message}");
            }
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }
}
