using System;
using HarmonyLib;
using PoEController.Glyphs;

namespace PoEController.Patches
{
    /// <summary>
    /// Hooks for controller glyphs in the game's text (<see cref="TextGlyphs"/>): strings are swapped for their
    /// controller versions where the game looks them up, and turned into glyph gaps where they enter a label.
    /// Only on the controller and only in English; any failure turns the feature off and the game's text returns.
    /// </summary>
    [Compat.Feature(Compat.Features.TextGlyphs)]
    internal static class TextGlyphPatches
    {
        private static bool English
        {
            get
            {
                Language lang = StringTableManager.CurrentLanguage;
                return lang != null && string.Equals(lang.Folder, "en", StringComparison.OrdinalIgnoreCase);
            }
        }

        [HarmonyPatch(typeof(UILabel), nameof(UILabel.text), MethodType.Setter)]
        private static class LabelText
        {
            private static void Prefix(UILabel __instance, ref string value)
            {
                try
                {
                    if (TextGlyphs.Concerns(__instance, value))
                    {
                        TextGlyphs.OnSetText(__instance, ref value);
                    }
                }
                catch (Exception e)
                {
                    TextGlyphs.Fail(e);
                }
            }
        }

        [HarmonyPatch(typeof(StringTableManager), nameof(StringTableManager.GetText),
            typeof(DatabaseString.StringTableType), typeof(int), typeof(Gender))]
        private static class StringLookup
        {
            private static void Postfix(DatabaseString.StringTableType stringTable, int stringID, ref string __result)
            {
                try
                {
                    if (!TextGlyphs.Broken && TextGlyphs.OnGamepad && !string.IsNullOrEmpty(__result) && English
                        && ControllerText.TryGet(stringTable, stringID, __result, out string text))
                    {
                        __result = text;
                    }
                }
                catch (Exception e)
                {
                    TextGlyphs.Fail(e);
                }
            }
        }

        /// <summary>"Inventory [I]"-style hotkey hints: the key name becomes the pad button when the mod drives that action.</summary>
        [HarmonyPatch(typeof(ControlMapping), nameof(ControlMapping.GetControlString))]
        private static class HotkeyName
        {
            private static void Postfix(MappedControl control, ref string __result)
            {
                try
                {
                    if (!TextGlyphs.Broken && TextGlyphs.OnGamepad)
                    {
                        string glyph = ControllerText.GlyphFor(control);
                        if (glyph != null)
                        {
                            __result = TextGlyphs.Token(glyph, __result);
                        }
                    }
                }
                catch (Exception e)
                {
                    TextGlyphs.Fail(e);
                }
            }
        }
    }
}
