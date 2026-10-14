using System;
using HarmonyLib;
using UnityEngine;

namespace PoEController.Patches
{
    /// <summary>
    /// The game sets every cursor (the gauntlet and its variants, in GameCursor) with <c>CursorMode.Auto</c>: the system
    /// draws it. On the Steam Deck the system is gamescope, which hides a mouse pointer that hasn't moved for a while;
    /// the locked menu cursor jumps to an element and then stands still, so the gauntlet vanished there. With
    /// <see cref="Software"/> on, the cursor goes in as <c>CursorMode.ForceSoftware</c>: Unity draws it into the game
    /// image, which gamescope can't hide. Config Input.CursorDrawnByGame (Auto = on SteamOS only). The last cursor is
    /// remembered so a change of the setting applies at once.
    /// </summary>
    [Compat.Feature(Compat.Features.SoftwareCursor)]
    [HarmonyPatch(typeof(Cursor), nameof(Cursor.SetCursor), typeof(Texture2D), typeof(Vector2), typeof(CursorMode))]
    internal static class CursorPatches
    {
        private static bool s_Software;
        private static bool s_HaveLast;
        private static Texture2D s_LastTexture;
        private static Vector2 s_LastHotspot;
        private static CursorMode s_LastMode;
        private static bool s_Reapplying;

        /// <summary>Draw the cursor into the game image (set every frame from the config).</summary>
        public static bool Software
        {
            get => s_Software;
            set
            {
                if (s_Software == value)
                {
                    return;
                }
                s_Software = value;
                Plugin.Log.LogInfo(value ? "Cursor: drawn by the game (software cursor)" : "Cursor: drawn by the system");
                if (s_HaveLast)
                {
                    s_Reapplying = true;
                    try
                    {
                        Cursor.SetCursor(s_LastTexture, s_LastHotspot, s_LastMode);
                    }
                    finally
                    {
                        s_Reapplying = false;
                    }
                }
            }
        }

        private static void Prefix(Texture2D __0, Vector2 __1, ref CursorMode __2)
        {
            try
            {
                if (!s_Reapplying)
                {
                    s_LastTexture = __0;
                    s_LastHotspot = __1;
                    s_LastMode = __2;
                    s_HaveLast = true;
                }
                if (s_Software)
                {
                    __2 = CursorMode.ForceSoftware;
                }
            }
            catch (Exception e)
            {
                Compat.Features.Off(Compat.Features.SoftwareCursor, e.Message);
            }
        }
    }

    /// <summary>When the game's cursor is drawn (Input.CursorDrawnByGame).</summary>
    public enum CursorDrawing
    {
        Auto,
        Always,
        Never,
    }
}
