using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PoEController.Input;
using UnityEngine;

namespace PoEController.Schemes
{
    /// <summary>
    /// The game's message box: A confirm, B cancel, plus the parts the mouse reaches that a pad otherwise can't. The
    /// amount box (how many to buy / sell / split / take): d-pad or left stick left / right one less / more (held =
    /// repeats), LB / RB the least / the most; the tick box ("don't show again" and the like): Y, as on the console
    /// version. The amount steps through the box's own arrow handlers (their sound, their limits).
    /// </summary>
    internal sealed class MessageBoxScheme : BindingScheme
    {
        private const float StickThreshold = 0.5f;

        private static readonly MethodInfo s_Up = AccessTools.Method(typeof(UIMessageBox), "OnNumericUp");
        private static readonly MethodInfo s_Down = AccessTools.Method(typeof(UIMessageBox), "OnNumericDown");
        private static readonly FieldInfo s_Min = AccessTools.Field(typeof(UIMessageBox), "m_NumericMin");
        private static readonly FieldInfo s_Max = AccessTools.Field(typeof(UIMessageBox), "m_NumericMax");

        private int m_StickDir;
        private float m_StickTimer;

        public MessageBoxScheme(params Binding[] bindings) : base("Message box", bindings)
        {
        }

        private static bool On => Compat.Features.On(Compat.Features.MessageBoxInput);

        private static bool HasAmount(UIMessageBox box) => box != null && box.NumericParent != null && box.NumericParent.activeInHierarchy;

        private static bool HasCheckbox(UIMessageBox box) =>
            box != null && box.CheckboxParent != null && box.CheckboxParent.activeInHierarchy && box.CheckboxComponent != null;

        public override void Activate()
        {
            m_StickDir = 0;
            m_StickTimer = 0f;
        }

        public override void HandleInput(in SchemeContext ctx, Gamepad pad, float dt)
        {
            base.HandleInput(ctx, pad, dt);
            UIMessageBox box = ctx.TopWindow as UIMessageBox;
            if (!On || box == null)
            {
                return;
            }
            Compat.Features.Run(Compat.Features.MessageBoxInput, () => HandleBox(box, pad, dt));
        }

        private void HandleBox(UIMessageBox box, Gamepad pad, float dt)
        {
            if (HasAmount(box))
            {
                int step = StickStep(pad.LeftStick.x, pad, dt);
                if (pad.GetButtonRepeat(PadButton.DpadRight))
                {
                    step = 1;
                }
                else if (pad.GetButtonRepeat(PadButton.DpadLeft))
                {
                    step = -1;
                }
                if (step != 0)
                {
                    (step > 0 ? s_Up : s_Down).Invoke(box, new object[] { box.gameObject });
                }
                if (pad.GetButtonDown(PadButton.LeftShoulder) || pad.GetButtonDown(PadButton.RightShoulder))
                {
                    int min = (int)s_Min.GetValue(box);
                    int max = (int)s_Max.GetValue(box);
                    int to = pad.GetButtonDown(PadButton.RightShoulder) ? max : min;
                    if (to != box.NumericValue)
                    {
                        box.SetNumericValue(to, min, max);
                        GlobalAudioPlayer.SPlay(to > min ? UIAudioList.UIAudioType.Increment : UIAudioList.UIAudioType.Decrement);
                    }
                }
            }
            if (HasCheckbox(box) && pad.GetButtonDown(PadButton.North))
            {
                box.CheckboxComponent.isChecked = !box.CheckboxComponent.isChecked;
                GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            }
        }

        /// <summary>Left stick past the threshold: one step at once, then repeats at the d-pad's rate while held.</summary>
        private int StickStep(float x, Gamepad pad, float dt)
        {
            int dir = x > StickThreshold ? 1 : x < -StickThreshold ? -1 : 0;
            if (dir == 0)
            {
                m_StickDir = 0;
                return 0;
            }
            if (dir != m_StickDir)
            {
                m_StickDir = dir;
                m_StickTimer = pad.RepeatDelay;
                return dir;
            }
            m_StickTimer -= dt;
            if (m_StickTimer > 0f)
            {
                return 0;
            }
            m_StickTimer += pad.RepeatInterval;
            return dir;
        }

        public override void GetPrompts(in SchemeContext ctx, List<Prompt> into)
        {
            UIMessageBox box = ctx.TopWindow as UIMessageBox;
            if (On && HasAmount(box))
            {
                into.Add(new Prompt("dpad_lr", "Amount"));
                into.Add(new Prompt(new[] { "lb", "rb" }, "Least / most"));
            }
            if (On && HasCheckbox(box))
            {
                into.Add(new Prompt("north", box.CheckboxComponent.isChecked ? "Untick" : "Tick"));
            }
            base.GetPrompts(ctx, into);
        }
    }
}
