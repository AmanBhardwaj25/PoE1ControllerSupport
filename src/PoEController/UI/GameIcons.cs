using System.Collections.Generic;
using UnityEngine;

namespace PoEController.UI
{
    /// <summary>
    /// The radials' icons, taken from the running game (0.39.0; replaces the Switch port's icon files, which could
    /// not be shipped): the HUD's own action-bar atlas ("ActionBar": character sheet, map, rest, journal, formations…)
    /// and the game's standalone icon textures (class ability rings, AI on / off, talk). Nothing is shipped or written;
    /// an icon that can't be found returns false and the radial shows the entry's name instead.
    /// </summary>
    internal static class GameIcons
    {
        /// <summary>Our icon name → the game's sprite / texture name, where they differ (none = same name).</summary>
        private static readonly Dictionary<string, string> s_GameName = new Dictionary<string, string>
        {
            { "ICO_levelup", "ICO_levelUp" },
            // No PC icon for these Switch ones: the nearest of the game's own.
            { "ICO_log", "TAB_combat" },
            { "ICO_status", "ICO_phrases" },
            { "quickItems_S", "ICO_inventory" },
            { "unarmed_L", "ICO_attack" },
        };

        private const string HudAtlas = "ActionBar";
        private const float RescanSeconds = 2f;

        private static UIAtlas s_Atlas;
        private static readonly Dictionary<string, Texture> s_Textures = new Dictionary<string, Texture>();
        private static float s_NextScan;
        private static readonly HashSet<string> s_Logged = new HashSet<string>();

        /// <summary>Texture and UV rect (bottom-left origin, for GUI.DrawTextureWithTexCoords) of <paramref name="name"/>.</summary>
        public static bool TryGet(string name, out Texture tex, out Rect uv)
        {
            try
            {
                return Find(name, out tex, out uv);
            }
            catch (System.Exception e)
            {
                // Never take a radial down over an icon: show the name instead.
                if (s_Logged.Add("!" + name))
                {
                    Plugin.Log.LogWarning($"Radial icon '{name}' failed: {e.Message}");
                }
                tex = null;
                uv = new Rect(0f, 0f, 1f, 1f);
                return false;
            }
        }

        private static bool Find(string name, out Texture tex, out Rect uv)
        {
            tex = null;
            uv = new Rect(0f, 0f, 1f, 1f);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            string game = s_GameName.TryGetValue(name, out string mapped) ? mapped : name;

            if (s_Textures.TryGetValue(game, out tex) && tex != null)
            {
                return true;
            }
            UIAtlas atlas = Atlas();
            if (atlas != null && atlas.texture != null)
            {
                UIAtlas.Sprite sprite = atlas.GetSprite(game);
                if (sprite != null)
                {
                    tex = atlas.texture;
                    uv = atlas.coordinates == UIAtlas.Coordinates.Pixels
                        ? NGUIMath.ConvertToTexCoords(sprite.outer, tex.width, tex.height)
                        : sprite.outer;
                    return true;
                }
            }
            tex = FindTexture(game);
            if (tex != null)
            {
                return true;
            }
            if (s_Logged.Add(name))
            {
                Plugin.Log.LogInfo($"Radial icon '{name}' (game '{game}') not found; showing the name instead");
            }
            return false;
        }

        private static UIAtlas Atlas()
        {
            if (s_Atlas != null)
            {
                return s_Atlas;
            }
            if (Time.unscaledTime < s_NextScan)
            {
                return null;
            }
            s_NextScan = Time.unscaledTime + RescanSeconds;
            foreach (UIAtlas a in Resources.FindObjectsOfTypeAll<UIAtlas>())
            {
                if (a != null && a.name == HudAtlas)
                {
                    s_Atlas = a;
                    Plugin.Log.LogInfo($"Radial icons: using the game's '{HudAtlas}' atlas");
                    break;
                }
            }
            return s_Atlas;
        }

        private static float s_NextTextureScan;

        /// <summary>
        /// A loaded Texture2D by name (the HUD's standalone icons). One pass over the loaded textures caches every
        /// "ICO_" one; a name still missing triggers a new pass at most every couple of seconds (scenes load more).
        /// </summary>
        private static Texture FindTexture(string name)
        {
            if (s_Textures.TryGetValue(name, out Texture cached) && cached != null)
            {
                return cached;
            }
            if (Time.unscaledTime < s_NextTextureScan)
            {
                return null;
            }
            s_NextTextureScan = Time.unscaledTime + RescanSeconds;
            foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (t != null && t.name.StartsWith("ICO_", System.StringComparison.Ordinal))
                {
                    s_Textures[t.name] = t;
                }
            }
            return s_Textures.TryGetValue(name, out cached) && cached != null ? cached : null;
        }
    }
}
