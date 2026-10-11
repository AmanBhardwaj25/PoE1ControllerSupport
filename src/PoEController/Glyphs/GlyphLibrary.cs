using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using PoEController.Input;
using PoEController.UI;
using UnityEngine;

namespace PoEController.Glyphs
{
    /// <summary>
    /// Loads button glyph textures from glyphs/&lt;family&gt;/&lt;name&gt;.png next to the plugin.
    /// Names are positional: south/east/west/north, lb/rb/lt/rt, lstick/rstick, dpad_*, start/back.
    /// </summary>
    public sealed class GlyphLibrary
    {
        private static readonly Dictionary<string, string> s_Fallbacks = new Dictionary<string, string>
        {
            { "lstick_click", "lstick" },
            { "rstick_click", "rstick" },
            { "back", "start" },
        };

        private readonly string m_Root;
        private readonly ModLog m_Log;
        private readonly Dictionary<string, Texture2D> m_Cache = new Dictionary<string, Texture2D>();

        public GlyphLibrary(string root, ModLog log)
        {
            m_Root = root;
            m_Log = log;
        }

        public static string NameFor(PadButton b)
        {
            switch (b)
            {
                case PadButton.South: return "south";
                case PadButton.East: return "east";
                case PadButton.West: return "west";
                case PadButton.North: return "north";
                case PadButton.Back: return "back";
                case PadButton.Start: return "start";
                case PadButton.LeftStick: return "lstick_click";
                case PadButton.RightStick: return "rstick_click";
                case PadButton.LeftShoulder: return "lb";
                case PadButton.RightShoulder: return "rb";
                case PadButton.LeftTrigger: return "lt";
                case PadButton.RightTrigger: return "rt";
                case PadButton.DpadUp: return "dpad_up";
                case PadButton.DpadDown: return "dpad_down";
                case PadButton.DpadLeft: return "dpad_left";
                case PadButton.DpadRight: return "dpad_right";
                default: return "start";
            }
        }

        public Texture2D Get(GlyphFamily family, PadButton button) => Get(family, NameFor(button));

        public Texture2D Get(GlyphFamily family, string name)
        {
            string key = family + "/" + name;
            if (m_Cache.TryGetValue(key, out Texture2D tex))
            {
                return tex;
            }
            tex = Load(family, name);
            if (tex == null && s_Fallbacks.TryGetValue(name, out string fallback))
            {
                tex = Get(family, fallback);
            }
            if (tex == null)
            {
                m_Log.LogWarning($"No glyph '{name}' for {family}");
            }
            m_Cache[key] = tex;
            return tex;
        }

        private Texture2D Load(GlyphFamily family, string name)
        {
            return TextureLoader.LoadPng(Path.Combine(Path.Combine(m_Root, family.ToString().ToLowerInvariant()), name + ".png"));
        }
    }
}
