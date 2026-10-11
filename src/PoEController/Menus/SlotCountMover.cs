using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// 0.36.0: the game's gauntlet cursor sits on the slot the controller has chosen and covers the
    /// stack count in its bottom-right corner. While the cursor is on an item slot (inventory, stash, loot, shops…),
    /// that slot's count label is mirrored to the top-left corner — same distance from the edges — and put back as
    /// soon as the cursor leaves, the menu closes, the mouse takes over or the feature turns off. Only the label's
    /// position changes; the game rewrites its text and colour as usual.
    /// </summary>
    internal static class SlotCountMover
    {
        private static UILabel s_Label;
        private static Transform s_Parent;
        private static Vector3 s_Original;

        /// <summary>Call every frame; <paramref name="active"/> = Console menu on the pad.</summary>
        public static void Update(bool active)
        {
            UILabel target = active ? LabelUnderCursor() : null;
            if (target == s_Label)
            {
                return;
            }
            Restore();
            if (target != null)
            {
                Move(target);
            }
        }

        /// <summary>Puts a moved count back where the game had it. Safe to call any time.</summary>
        public static void Restore()
        {
            if (s_Label != null && s_Label.cachedTransform.parent == s_Parent)
            {
                s_Label.cachedTransform.localPosition = s_Original;
            }
            s_Label = null;
            s_Parent = null;
        }

        private static UILabel LabelUnderCursor()
        {
            Collider c = FocusNavigator.ColliderAt(MouseEmulator.Position);
            UIInventoryGridItem slot = c != null ? NGUITools.FindInParents<UIInventoryGridItem>(c.gameObject) : null;
            if (slot == null || slot.Empty || slot.QuantityLabel == null || slot.Background == null)
            {
                return null;
            }
            UILabel label = slot.QuantityLabel;
            return label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.text) ? label : null;
        }

        private static void Move(UILabel label)
        {
            UIInventoryGridItem slot = NGUITools.FindInParents<UIInventoryGridItem>(label.gameObject);
            Transform parent = label.cachedTransform.parent;
            if (slot == null || slot.Background == null || parent == null)
            {
                return;
            }
            Bounds tile = NGUIMath.CalculateRelativeWidgetBounds(parent, slot.Background.transform);
            Bounds text = NGUIMath.CalculateRelativeWidgetBounds(parent, label.cachedTransform);
            if (tile.size.x <= 0f || tile.size.y <= 0f || text.size.x <= 0f)
            {
                return;
            }
            // The game places it bottom-right; keep its gaps to the right / bottom edge for the left / top edge.
            float gapX = Mathf.Max(0f, tile.max.x - text.max.x);
            float gapY = Mathf.Max(0f, text.min.y - tile.min.y);
            float dx = tile.min.x + gapX - text.min.x;
            float dy = tile.max.y - gapY - text.max.y;
            s_Label = label;
            s_Parent = parent;
            s_Original = label.cachedTransform.localPosition;
            label.cachedTransform.localPosition = s_Original + new Vector3(dx, dy, 0f);
        }
    }
}
