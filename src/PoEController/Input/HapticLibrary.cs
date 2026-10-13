using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PoEController.Input
{
    /// <summary>One vibration layer: a shape on one motor (or both).</summary>
    [Serializable]
    public sealed class HapticLayer
    {
        /// <summary>"low" (the big, heavy motor), "high" (the small, fast one) or "both".</summary>
        public string motor = "both";
        /// <summary>Peak strength 0..1 (scaled by the hit's strength and the user's multiplier).</summary>
        public float amp = 1f;
        /// <summary>Seconds to ramp up from 0 to the peak.</summary>
        public float attack = 0f;
        /// <summary>Fade time constant in seconds after the peak (large = nearly steady).</summary>
        public float fade = 0.1f;
        /// <summary>Total length in seconds.</summary>
        public float length = 0.2f;
        /// <summary>Seconds after the phase starts before this layer begins.</summary>
        public float delay = 0f;
        /// <summary>Pulses per second (0 = no pulsing).</summary>
        public float pulse = 0f;
        /// <summary>Share of each pulse that is "on" (0..1).</summary>
        public float duty = 0.5f;
        /// <summary>0..1: how much uneven noise takes off the strength (crackle, flicker).</summary>
        public float flicker = 0f;
        /// <summary>Repeat the layer this many extra times, each starting <see cref="every"/> seconds later.</summary>
        public int repeat = 0;
        public float every = 0.1f;
        /// <summary>Each repeat is this much weaker (0.8 = 80 % of the one before).</summary>
        public float falloff = 1f;
    }

    /// <summary>One controller group's version of a pattern, by phase.</summary>
    [Serializable]
    public sealed class HapticVersion
    {
        /// <summary>From the moment the main character's attack starts until it lands (the build-up).</summary>
        public HapticLayer[] cast;
        /// <summary>The moment the attack is released.</summary>
        public HapticLayer[] launch;
        /// <summary>A hit lands.</summary>
        public HapticLayer[] impact;
        /// <summary>Each later hit of a lingering / pulsing spell (falls back to impact).</summary>
        public HapticLayer[] linger;
        /// <summary>Turn-based long casts: loops while the spell is being prepared, until the caster's turn ends (length 0 = until then).</summary>
        public HapticLayer[] prepare;

        public bool IsEmpty => Empty(cast) && Empty(launch) && Empty(impact) && Empty(linger) && Empty(prepare);

        private static bool Empty(HapticLayer[] l) => l == null || l.Length == 0;
    }

    /// <summary>A pattern file (haptics/buckets/*.json, haptics/spells/*.json, haptics/custom/*.json).</summary>
    [Serializable]
    public sealed class HapticPattern
    {
        public string name = "";
        /// <summary>What it is meant to feel like (for people editing the file).</summary>
        public string note = "";
        /// <summary>Bucket to fall back on for anything this file leaves out.</summary>
        public string bucket = "";
        /// <summary>Group 1: classic weighted motors (Xbox, DualShock 4, most pads).</summary>
        public HapticVersion motors;
        /// <summary>Group 2: precise actuators (DualSense simple mode, Switch, Steam Deck).</summary>
        public HapticVersion haptic;
        /// <summary>Group 3: the DualSense with all its extras (base: haptic).</summary>
        public HapticVersion dualsense;
        /// <summary>Light colours ("lights": { "colors": ["#rrggbb", ...] }), from the spell's icon art; empty = not set here.</summary>
        public Color[] colors = new Color[0];

        /// <summary>Reads a pattern file (see haptics/README.md). Unknown fields are ignored; missing ones keep their defaults.</summary>
        public static HapticPattern FromJson(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null)
            {
                throw new FormatException("the file is not a { ... } object");
            }
            return new HapticPattern
            {
                name = MiniJson.Text(root, "name", ""),
                note = MiniJson.Text(root, "note", ""),
                bucket = MiniJson.Text(root, "bucket", ""),
                motors = Version(MiniJson.Child(root, "motors")),
                haptic = Version(MiniJson.Child(root, "haptic")),
                dualsense = Version(MiniJson.Child(root, "dualsense")),
                colors = Colors(MiniJson.List(MiniJson.Child(root, "lights"), "colors")),
            };
        }

        /// <summary>"#rrggbb" (or "rrggbb") strings to colours; anything that doesn't parse is left out.</summary>
        public static Color[] Colors(List<object> list)
        {
            var result = new List<Color>();
            foreach (object o in list ?? new List<object>())
            {
                if (o is string s && TryColor(s, out Color c))
                {
                    result.Add(c);
                }
            }
            return result.ToArray();
        }

        public static bool TryColor(string s, out Color c)
        {
            c = Color.black;
            s = (s ?? "").Trim().TrimStart('#');
            if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int v))
            {
                return false;
            }
            c = new Color(((v >> 16) & 0xff) / 255f, ((v >> 8) & 0xff) / 255f, (v & 0xff) / 255f);
            return true;
        }

        private static HapticVersion Version(Dictionary<string, object> d)
        {
            if (d == null)
            {
                return null;
            }
            return new HapticVersion
            {
                cast = Layers(d, "cast"),
                launch = Layers(d, "launch"),
                impact = Layers(d, "impact"),
                linger = Layers(d, "linger"),
                prepare = Layers(d, "prepare"),
            };
        }

        private static HapticLayer[] Layers(Dictionary<string, object> d, string phase)
        {
            List<object> list = MiniJson.List(d, phase);
            if (list == null)
            {
                return null;
            }
            var layers = new List<HapticLayer>();
            foreach (object o in list)
            {
                if (!(o is Dictionary<string, object> l))
                {
                    continue;
                }
                var x = new HapticLayer();
                x.motor = MiniJson.Text(l, "motor", x.motor);
                x.amp = MiniJson.Num(l, "amp", x.amp);
                x.attack = MiniJson.Num(l, "attack", x.attack);
                x.fade = MiniJson.Num(l, "fade", x.fade);
                x.length = MiniJson.Num(l, "length", x.length);
                x.delay = MiniJson.Num(l, "delay", x.delay);
                x.pulse = MiniJson.Num(l, "pulse", x.pulse);
                x.duty = MiniJson.Num(l, "duty", x.duty);
                x.flicker = MiniJson.Num(l, "flicker", x.flicker);
                x.repeat = (int)MiniJson.Num(l, "repeat", x.repeat);
                x.every = MiniJson.Num(l, "every", x.every);
                x.falloff = MiniJson.Num(l, "falloff", x.falloff);
                layers.Add(x);
            }
            return layers.ToArray();
        }
    }

    /// <summary>The controller's vibration hardware group.</summary>
    public enum HapticGroup
    {
        Motors,
        Haptic,
        DualSense,
    }

    /// <summary>
    /// Loads the vibration pattern files and finds the right one for a spell, a bucket or a built-in event. Lookup
    /// order for a name: haptics/custom/&lt;name&gt;.json, haptics/spells/&lt;name&gt;.json, then (for buckets)
    /// haptics/buckets/&lt;name&gt;.json. A file is re-read about a second after it changes on disk, so people can
    /// edit a pattern while the game runs. A file that doesn't parse is logged once and skipped (the next fallback
    /// is used). Per controller group a missing version falls back: dualsense -> haptic -> motors, haptic -> motors,
    /// motors -> haptic.
    /// </summary>
    public static class HapticLibrary
    {
        private sealed class Cached
        {
            public DateTime Stamp;
            public HapticPattern Pattern;
            public float NextCheck;
        }

        private const float CheckSeconds = 1f;
        private static string s_Root;
        private static readonly Dictionary<string, Cached> s_Cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_Reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string Root => s_Root;

        public static void Init(string pluginDir)
        {
            s_Root = Path.Combine(pluginDir, "haptics");
            foreach (string sub in new[] { "custom", "spells", "buckets" })
            {
                try
                {
                    Directory.CreateDirectory(Path.Combine(s_Root, sub));
                }
                catch (Exception)
                {
                    // Read-only install: shipped files still load; custom ones simply aren't there.
                }
            }
        }

        /// <summary>A spell's own file (custom, then shipped), or null.</summary>
        public static HapticPattern Spell(string slug) => Merge(Load("custom", slug), Load("spells", slug));

        /// <summary>A bucket's file (custom overrides allowed), or null.</summary>
        public static HapticPattern Bucket(string bucket) => Merge(Load("custom", bucket), Load("buckets", bucket));

        /// <summary>
        /// A custom file wins, but what it leaves out comes from the shipped one: a file with only "lights" keeps the
        /// shipped vibration, a file with only a vibration keeps the shipped colours.
        /// </summary>
        private static HapticPattern Merge(HapticPattern custom, HapticPattern shipped)
        {
            if (custom == null || shipped == null)
            {
                return custom ?? shipped;
            }
            bool vibration = !Empty(custom.motors) || !Empty(custom.haptic) || !Empty(custom.dualsense) || !string.IsNullOrEmpty(custom.bucket);
            if (vibration && custom.colors.Length > 0)
            {
                return custom;
            }
            return new HapticPattern
            {
                name = custom.name,
                note = custom.note,
                bucket = vibration ? custom.bucket : shipped.bucket,
                motors = vibration ? custom.motors : shipped.motors,
                haptic = vibration ? custom.haptic : shipped.haptic,
                dualsense = vibration ? custom.dualsense : shipped.dualsense,
                colors = custom.colors.Length > 0 ? custom.colors : shipped.colors,
            };
        }

        private static bool Empty(HapticVersion v) => v == null || v.IsEmpty;

        /// <summary>The layers of <paramref name="phase"/> for <paramref name="group"/>, following the version and bucket fallbacks.</summary>
        public static HapticLayer[] Layers(HapticPattern pattern, HapticGroup group, string phase)
        {
            for (int depth = 0; pattern != null && depth < 3; depth++)
            {
                HapticLayer[] l = Phase(Version(pattern, group), phase);
                if (l != null && l.Length > 0)
                {
                    return l;
                }
                pattern = string.IsNullOrEmpty(pattern.bucket) ? null : Bucket(pattern.bucket);
            }
            return null;
        }

        private static HapticVersion Version(HapticPattern p, HapticGroup group)
        {
            HapticVersion[] order;
            switch (group)
            {
                case HapticGroup.DualSense: order = new[] { p.dualsense, p.haptic, p.motors }; break;
                case HapticGroup.Haptic: order = new[] { p.haptic, p.motors }; break;
                default: order = new[] { p.motors, p.haptic }; break;
            }
            foreach (HapticVersion v in order)
            {
                if (v != null && !v.IsEmpty)
                {
                    return v;
                }
            }
            return null;
        }

        private static HapticLayer[] Phase(HapticVersion v, string phase)
        {
            if (v == null)
            {
                return null;
            }
            switch (phase)
            {
                case "cast": return v.cast;
                case "launch": return v.launch;
                case "prepare": return v.prepare;
                case "linger": return v.linger != null && v.linger.Length > 0 ? v.linger : v.impact;
                default: return v.impact;
            }
        }

        /// <summary>Every pattern name in a folder (for the test bench).</summary>
        public static List<string> List(string sub)
        {
            var names = new List<string>();
            try
            {
                string dir = Path.Combine(s_Root ?? "", sub);
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "*.json"))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(f));
                    }
                }
            }
            catch (Exception)
            {
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static HapticPattern Load(string sub, string name)
        {
            if (s_Root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            string path = Path.Combine(Path.Combine(s_Root, sub), name + ".json");
            float now = Time.unscaledTime;
            if (s_Cache.TryGetValue(path, out Cached c) && now < c.NextCheck)
            {
                return c.Pattern;
            }
            if (c == null)
            {
                c = new Cached();
                s_Cache[path] = c;
            }
            c.NextCheck = now + CheckSeconds;
            try
            {
                if (!File.Exists(path))
                {
                    c.Pattern = null;
                    c.Stamp = DateTime.MinValue;
                    return null;
                }
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (stamp == c.Stamp && c.Pattern != null)
                {
                    return c.Pattern;
                }
                HapticPattern p = HapticPattern.FromJson(File.ReadAllText(path));
                c.Pattern = p;
                c.Stamp = stamp;
                if (s_Reported.Remove(path))
                {
                    Plugin.Log.LogInfo($"Haptics: {sub}/{name}.json reloaded");
                }
                return p;
            }
            catch (Exception e)
            {
                c.Pattern = null;
                if (s_Reported.Add(path))
                {
                    Plugin.Log.LogWarning($"Haptics: {sub}/{name}.json can't be read ({e.Message}); using the next fallback");
                }
                return null;
            }
        }
    }
}
