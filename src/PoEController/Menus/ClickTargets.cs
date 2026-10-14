using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// What a UI collider is, decided the way a mouse click would find out: by what is registered to react to it. A
    /// collider alone means nothing in NGUI (frames and window backgrounds carry one to stop clicks falling through to
    /// the world, scroll areas to drag, tooltips to hover), and the game's button class (UIImageButtonRevised) is only
    /// the look. An element is a <see cref="Kind.Target"/> when, at runtime, something acts on its click:
    /// <list type="bullet">
    /// <item>a component on it with its own OnClick / OnDoubleClick / OnPress (not one of the look, sound, drag or
    /// click-blocker components);</item>
    /// <item>click handlers registered on its UIEventListener (onClick, onDoubleClick, onSubmit, onRightClick), unless
    /// they all come from a scroll bar / drag panel (then it scrolls);</item>
    /// <item>a control: text field, checkbox, slider, drop-down.</item>
    /// </list>
    /// Pieces of a UIMultiSpriteImageButton (the game's buttons are a left, middle and right piece, each with its own
    /// collider) are one target: <see cref="Kind.Piece"/> with the button as the owner. Scroll bars and drag areas are
    /// <see cref="Kind.Scroll"/>; something with only a tooltip is <see cref="Kind.Tooltip"/>; anything else (images,
    /// frames, UINoClick blockers) is <see cref="Kind.Blocker"/>. Per type answers are cached.
    /// </summary>
    internal static class ClickTargets
    {
        public enum Kind
        {
            Target,
            Piece,
            Tooltip,
            Scroll,
            /// <summary>A text whose only click opens its highlighted words' entries: the words are stops, the text isn't.</summary>
            Text,
            Blocker,
        }

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Components that react to a press or click only to look / sound / drag / block, never to act.</summary>
        private static readonly HashSet<Type> s_Passive = new HashSet<Type>
        {
            typeof(UINoClick), typeof(UIImageButtonRevised), typeof(UIButtonColor), typeof(UIButtonScale),
            typeof(UIButtonOffset), typeof(UIButtonRotation), typeof(UIButtonTween), typeof(UIButtonSound),
            typeof(UIButtonPlayAnimation), typeof(UIAudioTrigger), typeof(UIDragPanelContents), typeof(UIDragCamera),
            typeof(UIDragObject), typeof(UIEventListener), typeof(UIForwardEvents), typeof(UIMultiSpriteImageButton),
        };

        private static readonly HashSet<Type> s_Scroll = new HashSet<Type>
        {
            typeof(UIDragPanelContents), typeof(UIDraggablePanel), typeof(UIDragCamera), typeof(UIScrollBar),
            typeof(UIPuckScrollbar),
        };

        /// <summary>A scroll bar or drag area: the NGUI ones, the game's own (UIPuckScrollbar), any class named so.</summary>
        private static bool IsScroll(Type t)
        {
            if (t == null)
            {
                return false;
            }
            foreach (Type s in s_Scroll)
            {
                if (s.IsAssignableFrom(t))
                {
                    return true;
                }
            }
            return t.Name.IndexOf("scrollbar", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static readonly Dictionary<Type, bool> s_Acts = new Dictionary<Type, bool>();
        private static readonly Dictionary<Type, bool> s_Tooltips = new Dictionary<Type, bool>();
        private static readonly List<MonoBehaviour> s_Components = new List<MonoBehaviour>(16);

        /// <summary>Why the last <see cref="Classify"/> said Target or Piece (for the log): the component or click handler.</summary>
        public static string LastReason { get; private set; } = "";

        /// <summary>The kind of <paramref name="c"/>; <paramref name="owner"/> = the button a piece belongs to (else the collider).</summary>
        public static Kind Classify(Collider c, out Component owner)
        {
            owner = c;
            LastReason = "";
            GameObject go = c.gameObject;
            UIMultiSpriteImageButton button = NGUITools.FindInParents<UIMultiSpriteImageButton>(go);
            if (button != null && button.enabled && button.gameObject != go)
            {
                owner = button;
                LastReason = "piece";
                return Kind.Piece;
            }
            bool scroll = false;
            bool tooltip = false;
            bool text = false;
            go.GetComponents(s_Components);
            for (int i = 0; i < s_Components.Count; i++)
            {
                MonoBehaviour mb = s_Components[i];
                if (mb == null || !mb.enabled)
                {
                    continue;
                }
                switch (mb)
                {
                    case UIInput _:
                    case UICheckbox _:
                    case UISlider _:
                    case UIPopupList _:
                    case UIMultiSpriteImageButton _:
                        LastReason = mb.GetType().Name;
                        return Kind.Target;
                    case UIEventListener listener:
                        switch (ListenerKind(listener, out Component buttonOwner))
                        {
                            case Kind.Target: return Kind.Target;
                            case Kind.Piece:
                                owner = buttonOwner;
                                LastReason = "piece";
                                return Kind.Piece;
                            case Kind.Scroll: scroll = true; break;
                            case Kind.Text: text = true; break;
                            case Kind.Tooltip: tooltip = true; break;
                        }
                        continue;
                    case UIForwardEvents forward:
                        if (forward.target != null && forward.target != go)
                        {
                            owner = forward.target.transform;
                            LastReason = "forwarded";
                            return Kind.Piece;
                        }
                        continue;
                }
                Type t = mb.GetType();
                if (IsScroll(t))
                {
                    scroll = true;
                    continue;
                }
                if (Acts(t))
                {
                    LastReason = t.Name;
                    return Kind.Target;
                }
                tooltip |= HasTooltip(t);
            }
            return text ? Kind.Text : scroll ? Kind.Scroll : tooltip ? Kind.Tooltip : Kind.Blocker;
        }

        private static readonly List<Collider> s_Group = new List<Collider>(32);

        /// <summary>
        /// A tooltip-only element that belongs to one button: within its parent (or grandparent) there is exactly one
        /// <see cref="Kind.Target"/> and nothing else that acts, as with an option's checkbox and the label next to
        /// it whose hover fills the description. Returns that target's collider, or null (no such button, or several:
        /// a row with -/+ is not merged).
        /// </summary>
        public static Collider TooltipOwner(Collider tooltip)
        {
            Transform t = tooltip.transform.parent;
            for (int up = 0; up < 2 && t != null; up++, t = t.parent)
            {
                t.GetComponentsInChildren(false, s_Group);
                Collider only = null;
                int targets = 0;
                for (int i = 0; i < s_Group.Count && targets < 2; i++)
                {
                    Collider g = s_Group[i];
                    if (g == null || g == tooltip || !g.enabled)
                    {
                        continue;
                    }
                    Kind k = Classify(g, out _);
                    if (k == Kind.Target || k == Kind.Piece)
                    {
                        targets++;
                        only = g;
                    }
                }
                if (targets == 1)
                {
                    return only;
                }
                if (targets > 1)
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>The tooltip-only elements that belong to <paramref name="target"/> (see <see cref="TooltipOwner"/>).</summary>
        public static void TooltipsOf(Collider target, List<Collider> into)
        {
            into.Clear();
            Transform t = target.transform.parent;
            for (int up = 0; up < 2 && t != null; up++, t = t.parent)
            {
                t.GetComponentsInChildren(false, s_Group);
                var found = new List<Collider>();
                bool other = false;
                for (int i = 0; i < s_Group.Count; i++)
                {
                    Collider g = s_Group[i];
                    if (g == null || g == target || !g.enabled)
                    {
                        continue;
                    }
                    Kind k = Classify(g, out _);
                    if (k == Kind.Target || k == Kind.Piece)
                    {
                        other = true;
                        break;
                    }
                    if (k == Kind.Tooltip)
                    {
                        found.Add(g);
                    }
                }
                if (other)
                {
                    return;
                }
                if (found.Count > 0)
                {
                    into.AddRange(found);
                    return;
                }
            }
        }

        private static readonly FieldInfo s_OverridePressed = AccessTools.Field(typeof(UIImageButtonRevised), "overridePressed");
        private static readonly FieldInfo s_ToggledOn = AccessTools.Field(typeof(UIImageButtonRevised), "m_toggledOn");

        /// <summary>
        /// The game shows this element as the current choice: its button's "override pressed" state (the game sets
        /// it on the option matching the character's gender, race, class, culture ..., UICharacterCreationEnumSetter
        /// .UpdateButtonHighlight), a toggled-on button (tabs), or the checked one of a radio group. Not the plain
        /// pressed / forced-down look: spell and talent lists force every button down and mark the choice otherwise.
        /// </summary>
        public static bool IsSelected(Component c)
        {
            if (c == null)
            {
                return false;
            }
            UIImageButtonRevised look = c.GetComponent<UIImageButtonRevised>() ?? c.GetComponentInChildren<UIImageButtonRevised>();
            if (look != null && look.enabled && (Flag(s_OverridePressed, look) || Flag(s_ToggledOn, look)))
            {
                return true;
            }
            UICheckbox box = c.GetComponent<UICheckbox>();
            return box != null && box.radioButtonRoot != null && box.isChecked;
        }

        private static bool Flag(FieldInfo f, object o) => f != null && f.GetValue(o) is bool b && b;

        /// <summary>Short reason for the log ("UIInput", "listener", "piece of 1.Replay" ...).</summary>
        public static string Describe(Collider c, Kind kind, Component owner) =>
            kind == Kind.Piece ? $"piece of {owner.name}" : kind.ToString().ToLowerInvariant();

        private static Kind ListenerKind(UIEventListener l, out Component button)
        {
            button = null;
            bool any = false;
            bool allScroll = true;
            bool allText = true;
            foreach (Delegate d in new Delegate[] { l.onClick, l.onDoubleClick, l.onSubmit, l.onRightClick })
            {
                if (d == null)
                {
                    continue;
                }
                foreach (Delegate h in d.GetInvocationList())
                {
                    any = true;
                    Type owner = h.Method.DeclaringType;
                    LastReason = "click: " + (owner != null ? owner.Name : "?") + "." + h.Method.Name;
                    // A piece of a multi-piece button registers the button's own handlers (it can sit outside the
                    // button's hierarchy): it belongs to that button.
                    if (owner != null && typeof(UIMultiSpriteImageButton).IsAssignableFrom(owner) && h.Target is Component piecesOf)
                    {
                        button = piecesOf;
                        return Kind.Piece;
                    }
                    if (!IsScroll(owner))
                    {
                        allScroll = false;
                    }
                    if (owner == null || !typeof(UIGlossaryEnabledLabel).IsAssignableFrom(owner))
                    {
                        allText = false;
                    }
                }
            }
            if (any)
            {
                return allScroll ? Kind.Scroll : allText ? Kind.Text : Kind.Target;
            }
            if (l.onTooltip != null || l.onHover != null)
            {
                return Kind.Tooltip;
            }
            return Kind.Blocker;
        }

        /// <summary>The type (or a base below MonoBehaviour) declares OnClick / OnDoubleClick / OnPress and isn't passive.</summary>
        private static bool Acts(Type t)
        {
            if (s_Acts.TryGetValue(t, out bool acts))
            {
                return acts;
            }
            acts = false;
            if (!s_Passive.Contains(t))
            {
                for (Type b = t; b != null && b != typeof(MonoBehaviour); b = b.BaseType)
                {
                    if (s_Passive.Contains(b))
                    {
                        break;
                    }
                    if (Declares(b, "OnClick") || Declares(b, "OnDoubleClick") || Declares(b, "OnPress"))
                    {
                        acts = true;
                        break;
                    }
                }
            }
            s_Acts[t] = acts;
            return acts;
        }

        private static bool HasTooltip(Type t)
        {
            if (!s_Tooltips.TryGetValue(t, out bool has))
            {
                has = false;
                if (s_Passive.Contains(t))
                {
                    s_Tooltips[t] = false;
                    return false;
                }
                for (Type b = t; b != null && b != typeof(MonoBehaviour); b = b.BaseType)
                {
                    if (Declares(b, "OnTooltip") || Declares(b, "OnHover"))
                    {
                        has = true;
                        break;
                    }
                }
                s_Tooltips[t] = has;
            }
            return has;
        }

        private static bool Declares(Type t, string method)
        {
            foreach (MethodInfo m in t.GetMethods(Any | BindingFlags.DeclaredOnly))
            {
                if (m.Name == method)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
