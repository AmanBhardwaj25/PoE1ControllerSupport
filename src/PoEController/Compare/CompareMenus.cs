using System.Collections.Generic;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Compare
{
    /// <summary>The compare popup ("with equipped / with an item from inventory") and window placement.</summary>
    internal static class CompareMenus
    {
        /// <summary>Compare was chosen in <paramref name="source"/>: ask what to compare with.</summary>
        public static void OpenChoice(UIItemInspectManager source, Equippable item)
        {
            var entries = new List<PopupMenu.Entry>
            {
                new PopupMenu.Entry { Label = "Compare with equipped item", Run = () => CompareSession.StartEquipped(item, source) },
                // The card is put away and the item is picked in the window behind it.
                new PopupMenu.Entry { Label = "Compare with item from inventory", Run = () => CompareSession.StartChoosing(item, source) },
            };
            PopupMenu.Open("Compare " + item.Name, WindowRect(source), entries, null, Compat.GameRef.Get<UIWidget>(source, "TitleLabel"));
        }

        /// <summary>Screen rect (GUI coordinates) of an inspect window, for placing popups beside it.</summary>
        public static Rect WindowRect(UIHudWindow w)
        {
            Camera ui = InGameUILayout.NGUICamera;
            GameObject window = w != null ? Compat.GameRef.Object(w, "Window") : null;
            GameObject root = window != null ? window : w != null ? w.gameObject : null;
            if (root == null || ui == null)
            {
                return new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 1f, 1f);
            }
            Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(root.transform);
            Vector3 min = ui.WorldToScreenPoint(b.min);
            Vector3 max = ui.WorldToScreenPoint(b.max);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Screen.height - Mathf.Max(min.y, max.y),
                Mathf.Max(min.x, max.x), Screen.height - Mathf.Min(min.y, max.y));
        }
    }
}
