using System.Collections.Generic;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// Console style's trimmed HUD (like the Switch: portraits only, the radials replace the buttons). Hides the
    /// action bar's button groups, its trim, turn-based toggle and minimise arrow, and the ability bar above the
    /// portraits, while Console style is played on the pad (config <c>Controls.ConsoleHideActionBar</c>). Kept:
    /// portraits, the clock / pause dial, the turn-based widget, the combat log, the chanter phrase bar.
    /// <para>
    /// Hidden by making every widget transparent and switching off its colliders — never by deactivating (the radials
    /// press the HUD buttons' own hotkeys, which only listen while active) and never by resizing: other HUD parts are
    /// anchored to these (0.31.1 fix: shrinking the trim moved the "PAUSED / Slow mode / Fast mode" banner, the game
    /// speed text and the minimise arrow onto the clock; the chanter bar is anchored inside the ability bar). Widgets
    /// are re-faded every frame (the game re-colours buttons) and re-scanned every 2 s (the ability bar builds its
    /// buttons at runtime). Original alpha / collider state comes back on mouse / keyboard, PC layout, setting off,
    /// plugin failure or unload. Objects are found by their scene path (checked against the PC scene, level13); a
    /// missing one is logged once and left alone.
    /// </para>
    /// </summary>
    internal static class ConsoleHud
    {
        private const string Bottom = "UI Root (2D)/UICamera/HUD/Bottom/";
        private const string ActionBar = Bottom + "ActionBarWindow/ActionBar/";

        private static readonly string[] s_Paths =
        {
            ActionBar + "ButtonsLeft",
            ActionBar + "ButtonsRight",
            ActionBar + "trimB",
            ActionBar + "BUT_TBM_Toggle",
            Bottom + "ActionBarMinimize",
        };

        private static readonly Dictionary<UIWidget, float> s_Alphas = new Dictionary<UIWidget, float>();
        private static readonly Dictionary<Collider, bool> s_Colliders = new Dictionary<Collider, bool>();
        private static readonly List<UIWidget> s_Widgets = new List<UIWidget>();
        private static readonly List<Collider> s_ColliderList = new List<Collider>();
        /// <summary>Helpers that animate these widgets' alpha (fade tweens, UIHideWhenMinimized): paused while hidden.</summary>
        private static readonly Dictionary<Behaviour, bool> s_Animators = new Dictionary<Behaviour, bool>();
        private static readonly List<Behaviour> s_AnimatorList = new List<Behaviour>();
        /// <summary>
        /// Panels in the hidden parts (0.34.4 fix for flickering ability-bar icons: the bar builds buttons at runtime,
        /// which showed until the next 2 s re-scan). A panel that copies the HUD's alpha (UICopyPanelAlpha) gets its
        /// own Multiplier set to 0 — the game's knob, which keeps it invisible whatever the HUD's alpha does; other
        /// panels get alpha 0. Everything a panel draws is hidden, including widgets created later.
        /// </summary>
        private static readonly Dictionary<UIPanel, float> s_PanelAlphas = new Dictionary<UIPanel, float>();
        private static readonly Dictionary<UICopyPanelAlpha, float> s_Copies = new Dictionary<UICopyPanelAlpha, float>();
        private static readonly List<UIPanel> s_PanelList = new List<UIPanel>();
        private static readonly HashSet<string> s_Missing = new HashSet<string>();
        private static float s_NextScan;

        /// <summary>The action bar's buttons are hidden right now (their glyph badges would float in empty space).</summary>
        public static bool Hidden => s_Alphas.Count > 0;

        /// <summary>Per frame, after the router. <paramref name="hide"/> = Console style + pad + setting on, in game.</summary>
        public static void Update(bool hide)
        {
            if (!hide)
            {
                Restore();
                return;
            }
            if (Time.unscaledTime >= s_NextScan)
            {
                Scan();
            }
            foreach (UIWidget w in s_Widgets)
            {
                if (w == null)
                {
                    continue;
                }
                if (!s_Alphas.ContainsKey(w))
                {
                    s_Alphas[w] = w.alpha;
                }
                if (w.alpha != 0f)
                {
                    w.alpha = 0f;
                }
            }
            foreach (Collider c in s_ColliderList)
            {
                if (c == null)
                {
                    continue;
                }
                if (!s_Colliders.ContainsKey(c))
                {
                    s_Colliders[c] = c.enabled;
                }
                if (c.enabled)
                {
                    c.enabled = false;
                }
            }
            foreach (Behaviour b in s_AnimatorList)
            {
                if (b == null)
                {
                    continue;
                }
                if (!s_Animators.ContainsKey(b))
                {
                    s_Animators[b] = b.enabled;
                }
                if (b.enabled)
                {
                    b.enabled = false;
                }
            }
            foreach (UIPanel p in s_PanelList)
            {
                if (p == null || !p.gameObject.activeInHierarchy)
                {
                    continue;
                }
                UICopyPanelAlpha copy = p.GetComponent<UICopyPanelAlpha>();
                if (copy != null && copy.From != null)
                {
                    if (!s_Copies.ContainsKey(copy))
                    {
                        s_Copies[copy] = copy.Multiplier;
                    }
                    if (copy.Multiplier != 0f)
                    {
                        copy.Multiplier = 0f;
                    }
                    continue;
                }
                if (!s_PanelAlphas.ContainsKey(p))
                {
                    s_PanelAlphas[p] = p.alpha;
                }
                if (p.alpha != 0f)
                {
                    p.alpha = 0f;
                }
            }
        }

        /// <summary>Gives every hidden part its original look and clickability back (also on plugin failure / unload).</summary>
        public static void Restore()
        {
            if (s_Alphas.Count == 0 && s_Colliders.Count == 0 && s_PanelAlphas.Count == 0 && s_Copies.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<UICopyPanelAlpha, float> kv in s_Copies)
            {
                if (kv.Key != null)
                {
                    kv.Key.Multiplier = kv.Value;
                }
            }
            foreach (KeyValuePair<UIPanel, float> kv in s_PanelAlphas)
            {
                if (kv.Key != null)
                {
                    kv.Key.alpha = kv.Value;
                }
            }
            s_Copies.Clear();
            s_PanelAlphas.Clear();
            s_PanelList.Clear();
            foreach (KeyValuePair<UIWidget, float> kv in s_Alphas)
            {
                if (kv.Key != null)
                {
                    kv.Key.alpha = kv.Value;
                }
            }
            foreach (KeyValuePair<Collider, bool> kv in s_Colliders)
            {
                if (kv.Key != null)
                {
                    kv.Key.enabled = kv.Value;
                }
            }
            foreach (KeyValuePair<Behaviour, bool> kv in s_Animators)
            {
                if (kv.Key != null)
                {
                    kv.Key.enabled = kv.Value;
                }
            }
            s_Alphas.Clear();
            s_Colliders.Clear();
            s_Animators.Clear();
            s_Widgets.Clear();
            s_ColliderList.Clear();
            s_AnimatorList.Clear();
            s_NextScan = 0f;
        }

        private static void Scan()
        {
            s_NextScan = Time.unscaledTime + 2f;
            s_Widgets.Clear();
            s_ColliderList.Clear();
            s_AnimatorList.Clear();
            s_PanelList.Clear();
            foreach (string path in s_Paths)
            {
                Collect(GameObject.Find(path), path);
            }
            Collect(UIAbilityBar.Instance != null ? UIAbilityBar.Instance.gameObject : null, "UIAbilityBar");
            // Forget destroyed objects (scene reload).
            foreach (UIWidget w in new List<UIWidget>(s_Alphas.Keys))
            {
                if (w == null)
                {
                    s_Alphas.Remove(w);
                }
            }
            foreach (Collider c in new List<Collider>(s_Colliders.Keys))
            {
                if (c == null)
                {
                    s_Colliders.Remove(c);
                }
            }
        }

        private static void Collect(GameObject go, string what)
        {
            if (go == null)
            {
                if (s_Missing.Add(what))
                {
                    Plugin.Log.LogInfo($"Console HUD: '{what}' not found (left as it is)");
                }
                return;
            }
            s_Widgets.AddRange(go.GetComponentsInChildren<UIWidget>(true));
            s_ColliderList.AddRange(go.GetComponentsInChildren<Collider>(true));
            s_AnimatorList.AddRange(go.GetComponentsInChildren<UIHideWhenMinimized>(true));
            s_PanelList.AddRange(go.GetComponentsInChildren<UIPanel>(true));
            foreach (UITweener t in go.GetComponentsInChildren<UITweener>(true))
            {
                if (t is TweenAlpha || t is TweenColor)
                {
                    s_AnimatorList.Add(t);
                }
            }
        }
    }
}
