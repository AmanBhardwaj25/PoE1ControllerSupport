using System.Collections.Generic;
using PoEController.Compare;
using PoEController.Context;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// "Compare with item from inventory" (<see cref="CompareSession.Stage.Choosing"/>): the item's card is put away
    /// and the player picks the other item in the window behind it (inventory, stash + party rows, shop…). Confined
    /// there: the d-pad only stops on items that can be compared, everything else holding an item is greyed out and
    /// A refuses it (error sound); A on a comparable item brings the card back with the comparison; B = back to the
    /// card. No window bindings (B doesn't close the window, LB/RB don't switch screens).
    /// </summary>
    public sealed class CompareChooseScheme : MenuScheme
    {
        private readonly List<Vector2> m_Points = new List<Vector2>();
        private readonly List<Rect> m_Greyed = new List<Rect>();
        private readonly List<UIInventoryGridItem> m_Slots = new List<UIInventoryGridItem>();
        private UIInventoryGridItem m_Hovered;

        public CompareChooseScheme()
            : base("Choose item to compare")
        {
        }

        protected override bool ClickOnA => false;

        protected override bool RightClickOnX => false;

        /// <summary>The stash opens over the inventory: its party rows stay pickable.</summary>
        protected override Transform ExtraNavigationRoot
        {
            get
            {
                UIInventoryManager inv = UIInventoryManager.Instance;
                return inv != null && inv.WindowActive() && ScreenTracker.Top != inv ? inv.transform : null;
            }
        }

        public override void Activate()
        {
            base.Activate();
            SubmitGuard.Mute();
            m_Hovered = null;
        }

        public override void Deactivate()
        {
            SubmitGuard.Restore(force: true);
            m_Greyed.Clear();
            base.Deactivate();
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            SubmitGuard.Mute();
            if (pad.GetButtonDown(PadButton.East))
            {
                CompareSession.Safe(CompareSession.CancelChoosing, "Compare: back to the card");
                pad.SuppressHeldButtons();
                return;
            }
            base.HandleInput(ctx, pad, dt);
            Compat.Features.Run(Compat.Features.Compare, () => HandleChoice(pad));
        }

        private void HandleChoice(Gamepad pad)
        {
            UpdateHovered();
            if (pad.GetButtonDown(PadButton.South))
            {
                if (m_Hovered != null && CompareSession.CanChoose(m_Hovered))
                {
                    UIInventoryGridItem slot = m_Hovered;
                    Plugin.Log.LogInfo($"Compare with {slot.InvItem.baseItem.name}");
                    CompareSession.Safe(() => CompareSession.Choose(slot), "Compare: choose item");
                    pad.SuppressHeldButtons();
                }
                else
                {
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.Error);
                }
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            into.Add(new Prompt("dpad", "Navigate"));
            into.Add(new Prompt("lstick", "Cursor"));
            if (Compat.Features.Get(Compat.Features.Compare, () => m_Hovered != null && CompareSession.CanChoose(m_Hovered), false))
            {
                into.Add(new Prompt("south", "Compare with this"));
            }
            into.Add(new Prompt("rstick_ud", "Scroll"));
            into.Add(new Prompt("east", "Back"));
            UIHudWindow top = ctx.TopWindow;
            Compat.Features.Run(Compat.Features.Compare, () => UpdateGreyed(top));
        }

        /// <summary>Dark boxes over the items that can't be compared (IMGUI, from Plugin.OnGUI).</summary>
        public void DrawGreyed()
        {
            if (m_Greyed.Count == 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            foreach (Rect r in m_Greyed)
            {
                GUI.DrawTexture(r, Texture2D.whiteTexture);
            }
            GUI.color = old;
        }

        /// <summary>Only comparable items are d-pad stops.</summary>
        protected override List<Vector2> CollectPoints(Transform root, Transform extraRoot)
        {
            m_Points.Clear();
            Compat.Features.Run(Compat.Features.Compare, () => CollectComparable(root, extraRoot));
            return m_Points;
        }

        private void CollectComparable(Transform root, Transform extraRoot)
        {
            GatherSlots(root, extraRoot);
            foreach (UIInventoryGridItem slot in m_Slots)
            {
                if (!CompareSession.CanChoose(slot))
                {
                    continue;
                }
                Collider c = SlotCollider(slot);
                if (c == null)
                {
                    continue;
                }
                UICamera uiCam = UICamera.FindCameraForLayer(c.gameObject.layer);
                if (uiCam == null || uiCam.cachedCamera == null)
                {
                    continue;
                }
                Vector3 p = uiCam.cachedCamera.WorldToScreenPoint(c.bounds.center);
                Collider hit = FocusNavigator.ColliderAt(p);
                // Reachable the way a click would be (not scrolled away, not covered).
                if (hit != null && hit.GetComponentInParent<UIInventoryGridItem>() == slot)
                {
                    m_Points.Add(new Vector2(p.x, p.y));
                }
            }
        }

        private void UpdateHovered()
        {
            Collider c = FocusNavigator.ColliderAt(MouseEmulator.Position);
            m_Hovered = c != null ? c.GetComponentInParent<UIInventoryGridItem>() : null;
        }

        private void UpdateGreyed(UIHudWindow top)
        {
            m_Greyed.Clear();
            GatherSlots(top != null ? top.transform : null, ExtraNavigationRoot);
            foreach (UIInventoryGridItem slot in m_Slots)
            {
                if (slot.InvItem == null || slot.InvItem.baseItem == null || CompareSession.CanChoose(slot))
                {
                    continue;
                }
                Collider c = SlotCollider(slot);
                UICamera uiCam = c != null ? UICamera.FindCameraForLayer(c.gameObject.layer) : null;
                Camera cam = uiCam != null ? uiCam.cachedCamera : null;
                if (cam == null)
                {
                    continue;
                }
                Vector3 a = cam.WorldToScreenPoint(c.bounds.min);
                Vector3 b = cam.WorldToScreenPoint(c.bounds.max);
                Rect screen = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                Rect visible = LabelLayout.VisibleArea(c.transform, cam);
                float xMin = Mathf.Max(screen.xMin, visible.xMin);
                float yMin = Mathf.Max(screen.yMin, visible.yMin);
                float xMax = Mathf.Min(screen.xMax, visible.xMax);
                float yMax = Mathf.Min(screen.yMax, visible.yMax);
                if (xMax - xMin < 2f || yMax - yMin < 2f)
                {
                    continue;
                }
                m_Greyed.Add(Rect.MinMaxRect(xMin, Screen.height - yMax, xMax, Screen.height - yMin));
            }
        }

        private void GatherSlots(Transform root, Transform extraRoot)
        {
            m_Slots.Clear();
            if (root != null)
            {
                m_Slots.AddRange(root.GetComponentsInChildren<UIInventoryGridItem>(false));
            }
            if (extraRoot != null)
            {
                m_Slots.AddRange(extraRoot.GetComponentsInChildren<UIInventoryGridItem>(false));
            }
        }

        private static Collider SlotCollider(UIInventoryGridItem slot)
        {
            GameObject background = Compat.GameRef.Object(slot, "Background");
            Collider c = background != null ? background.GetComponent<Collider>() : null;
            return c != null && c.enabled ? c : slot.GetComponentInChildren<Collider>();
        }
    }
}
