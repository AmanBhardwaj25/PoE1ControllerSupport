using System.Collections.Generic;
using PoEController.Glyphs;
using PoEController.Input;

namespace PoEController.Schemes
{
    /// <summary>One entry in the prompt bar: one or more glyphs followed by a label.</summary>
    public readonly struct Prompt
    {
        /// <summary>Positional glyph names (see GlyphLibrary), drawn side by side.</summary>
        public readonly string[] Glyphs;
        public readonly string Label;

        /// <summary>0 = plain prompt; above 0 = a hold in progress, drawn as a ring filling around the glyph (0..1).</summary>
        public readonly float Progress;

        public Prompt(string glyph, string label)
        {
            Glyphs = new[] { glyph };
            Label = label;
            Progress = 0f;
        }

        public Prompt(string[] glyphs, string label)
        {
            Glyphs = glyphs;
            Label = label;
            Progress = 0f;
        }

        public Prompt(string glyph, string label, float progress)
        {
            Glyphs = new[] { glyph };
            Label = label;
            Progress = progress;
        }
    }

    /// <summary>
    /// A controller button that presses one of the game's own actions while held. A null label hides it from the
    /// prompt bar (used when one button drives several actions). Consecutive bindings with the same label share
    /// one prompt (e.g. LB/RB "Party").
    /// </summary>
    public readonly struct Binding
    {
        public readonly PadButton Button;
        public readonly MappedControl Control;
        public readonly string Label;

        public Binding(PadButton button, MappedControl control, string label)
        {
            Button = button;
            Control = control;
            Label = label;
        }
    }

    public enum BarStyle
    {
        /// <summary>Soft shadow strip for in-world play.</summary>
        Hud,
        /// <summary>Framed bar for menus and dialogs.</summary>
        Menu,
    }

    /// <summary>Game state a scheme needs for its prompts and input handling.</summary>
    public struct SchemeContext
    {
        public UIHudWindow TopWindow;
        public bool Paused;
        public bool InCombat;
    }

    /// <summary>
    /// Controls for one situation (exploring, a specific screen…). Input handling and prompts live together so the
    /// bar can never advertise a binding that does something else.
    /// </summary>
    public abstract class ControlScheme
    {
        public abstract string Name { get; }

        public virtual BarStyle Style => BarStyle.Menu;

        /// <summary>Called once per frame while this scheme is active and a controller is connected.</summary>
        public abstract void HandleInput(in SchemeContext ctx, Gamepad pad, float dt);

        public abstract void GetPrompts(in SchemeContext ctx, List<Prompt> into);

        /// <summary>Called when this scheme becomes active.</summary>
        public virtual void Activate()
        {
        }

        /// <summary>Called when this scheme stops being active, to drop any in-progress action.</summary>
        public virtual void Deactivate()
        {
        }
    }

    /// <summary>A scheme made only of button → game action bindings.</summary>
    public class BindingScheme : ControlScheme
    {
        private readonly string m_Name;
        private readonly Binding[] m_Bindings;

        public BindingScheme(string name, params Binding[] bindings)
        {
            m_Name = name;
            m_Bindings = bindings;
        }

        public override string Name => m_Name;

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            for (int i = 0; i < m_Bindings.Length; i++)
            {
                if (pad.GetButton(m_Bindings[i].Button) && !TakesOver(m_Bindings[i].Button))
                {
                    VirtualControls.Hold(m_Bindings[i].Control);
                }
            }
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            AddBindingPrompts(m_Bindings, into);
            // Glyphs on the window's own buttons for these bindings (close, confirm, take all, continue…).
            UIHudWindow top = ctx.TopWindow;
            Compat.Features.Run(Compat.Features.ButtonGlyphs, () => UI.GameButtonHints.ForWindow(top, m_Bindings));
        }

        /// <summary>The scheme handles this button itself right now (its binding isn't passed to the game).</summary>
        protected virtual bool TakesOver(PadButton button) => false;

        protected static void AddBindingPrompts(Binding[] bindings, List<Prompt> into)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].Label == null)
                {
                    continue;
                }
                int j = i + 1;
                while (j < bindings.Length && bindings[j].Label == bindings[i].Label)
                {
                    j++;
                }
                if (j - i == 1)
                {
                    into.Add(new Prompt(GlyphLibrary.NameFor(bindings[i].Button), bindings[i].Label));
                }
                else
                {
                    string[] glyphs = new string[j - i];
                    for (int k = i; k < j; k++)
                    {
                        glyphs[k - i] = GlyphLibrary.NameFor(bindings[k].Button);
                    }
                    into.Add(new Prompt(glyphs, bindings[i].Label));
                }
                i = j - 1;
            }
        }
    }
}
