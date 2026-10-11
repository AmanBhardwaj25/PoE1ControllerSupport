using System.Collections.Generic;
using PoEController.Compare;
using PoEController.Input;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// The item inspect window (<see cref="UIItemInspectManager"/>, opened by X / right-click from inventory, stash,
    /// shops, loot, grimoire, character sheet…). Cursor navigation as in any menu, plus the generic rule for a
    /// window's action buttons: in a fixed priority order (Compare, Enchant, Learn Spell, Examine, Soulbind), the
    /// visible ones get X then Y; with three or more, X keeps the first and Y opens a list of all of them. Buttons are
    /// pressed through their own <c>onClick</c> (exactly what a mouse click runs); Compare starts our own comparison
    /// (<see cref="CompareSession"/>) instead of the game's.
    /// </summary>
    public sealed class InspectScheme : MenuScheme
    {
        private readonly List<PopupMenu.Entry> m_Actions = new List<PopupMenu.Entry>();

        public InspectScheme(params Binding[] bindings)
            : base("Inspect", bindings)
        {
        }

        // If the card's buttons can't be handled (game changed), X is the plain right click again.
        protected override bool RightClickOnX => !Compat.Features.On(Compat.Features.InspectActions);

        public override void Activate()
        {
            base.Activate();
            PopupMenu.Close();
        }

        public override void Deactivate()
        {
            PopupMenu.Close();
            SubmitGuard.Restore(force: true);
            base.Deactivate();
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            if (PopupMenu.HandleInput(pad))
            {
                return;
            }
            SubmitGuard.Restore(force: false);
            base.HandleInput(ctx, pad, dt);
            UIItemInspectManager window = ctx.TopWindow as UIItemInspectManager;
            if (window == null)
            {
                return;
            }
            CollectSafe(window, m_Actions);
            if (m_Actions.Count == 0)
            {
                return;
            }
            if (pad.GetButtonDown(PadButton.West))
            {
                Run(m_Actions[0]);
            }
            else if (pad.GetButtonDown(PadButton.North) && m_Actions.Count >= 2)
            {
                if (m_Actions.Count == 2)
                {
                    Run(m_Actions[1]);
                }
                else
                {
                    PopupMenu.Open("Actions", CompareMenus.WindowRect(window), new List<PopupMenu.Entry>(m_Actions), null, Compat.GameRef.Get<UIWidget>(window, "TitleLabel"));
                }
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            if (PopupMenu.IsOpen)
            {
                AddPopupPrompts(into);
                return;
            }
            base.GetPrompts(ctx, into);
            if (ctx.TopWindow is UIItemInspectManager window)
            {
                CollectSafe(window, m_Actions);
                if (m_Actions.Count >= 1)
                {
                    into.Add(new Prompt("west", m_Actions[0].Label));
                    UI.ButtonHints.Add(m_Actions[0].Target, "west");
                }
                if (m_Actions.Count == 2)
                {
                    into.Add(new Prompt("north", m_Actions[1].Label));
                    UI.ButtonHints.Add(m_Actions[1].Target, "north");
                }
                else if (m_Actions.Count >= 3)
                {
                    into.Add(new Prompt("north", "More…"));
                }
            }
        }

        internal static void AddPopupPrompts(List<Prompt> into)
        {
            into.Add(new Prompt("dpad_ud", "Choose"));
            into.Add(new Prompt("south", "Select"));
            into.Add(new Prompt("east", "Back"));
        }

        private static void Run(PopupMenu.Entry e)
        {
            Plugin.Log.LogInfo($"Inspect action: {e.Label}");
            CompareSession.Safe(e.Run, "Inspect action '" + e.Label + "'", Compat.Features.InspectActions);
        }

        /// <summary>The window's usable actions, highest priority first (none if this feature is off).</summary>
        private static void CollectSafe(UIItemInspectManager w, List<PopupMenu.Entry> into)
        {
            into.Clear();
            Compat.Features.Run(Compat.Features.InspectActions, () => Collect(w, into));
        }

        /// <summary>The card's fields are read by name (GameRef): a renamed button just isn't offered.</summary>
        private static void Collect(UIItemInspectManager w, List<PopupMenu.Entry> into)
        {
            Equippable item = Compat.GameRef.Get<MonoBehaviour>(w, "InspectionObject") as Equippable;
            Compat.GameRef.TryGet(w, "NoCompare", out bool noCompare);
            if (item != null && !noCompare && Compat.Features.On(Compat.Features.Compare) && CompareSession.Comparable(item))
            {
                // The game shows its Compare button only when the selected character has something to compare;
                // ours works for any companion, so the button (if shown) just carries the glyph.
                GameObject button = Compat.GameRef.Object(w, "CompareButton");
                into.Add(new PopupMenu.Entry
                {
                    Label = "Compare",
                    Run = () => CompareMenus.OpenChoice(w, item),
                    Target = button != null && button.activeInHierarchy ? button : null,
                });
            }
            AddButton(Compat.GameRef.Get<UIMultiSpriteImageButton>(w, "EnchantButton"), "Enchant", into);
            AddButton(Compat.GameRef.Get<UIMultiSpriteImageButton>(w, "LearnSpellButton"), "Learn spell", into);
            AddButton(Compat.GameRef.Get<UIMultiSpriteImageButton>(w, "ExamineButton"), "Examine", into);
            AddButton(Compat.GameRef.Get<UIMultiSpriteImageButton>(w, "SoulbindButton"), "Soulbind", into);
        }

        private static void AddButton(UIMultiSpriteImageButton button, string fallback, List<PopupMenu.Entry> into)
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.enabled)
            {
                return;
            }
            UIEventListener.VoidDelegate onClick = Compat.GameRef.Get<UIEventListener.VoidDelegate>(button, "onClick");
            if (onClick == null)
            {
                return;
            }
            UILabel label = button.GetComponentInChildren<UILabel>();
            string text = label != null && !string.IsNullOrEmpty(label.text) ? NGUITools.StripSymbols(label.text).Trim() : fallback;
            into.Add(new PopupMenu.Entry
            {
                Target = button.gameObject,
                Label = string.IsNullOrEmpty(text) ? fallback : text,
                Run = () =>
                {
                    GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
                    // Exactly what a mouse click on the button runs.
                    onClick(button.gameObject);
                },
            });
        }
    }
}
