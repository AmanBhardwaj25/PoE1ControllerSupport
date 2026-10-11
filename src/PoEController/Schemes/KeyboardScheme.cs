using System.Collections.Generic;
using PoEController.Input;
using PoEController.TextEntry;

namespace PoEController.Schemes
{
    /// <summary>Active whenever the on-screen keyboard is open, over any screen.</summary>
    public sealed class KeyboardScheme : ControlScheme
    {
        public override string Name => "Keyboard";

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            OnScreenKeyboard.HandleInput(pad, dt);
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            OnScreenKeyboard.GetPrompts(into);
        }
    }
}
