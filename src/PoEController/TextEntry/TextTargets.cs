using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using PoEController.Context;
using UnityEngine;

namespace PoEController.TextEntry
{
    /// <summary>What the on-screen keyboard is typing into.</summary>
    internal abstract class TextTarget
    {
        /// <summary>Heading shown above the text (what the box is for), or null.</summary>
        public abstract string Title { get; }

        public abstract string Text { get; }

        /// <summary>Offers a new-line key.</summary>
        public virtual bool Multiline => false;

        public virtual bool Password => false;

        /// <summary>False once the box is gone or no longer focused; the keyboard then closes.</summary>
        public abstract bool IsAlive { get; }

        public abstract string Describe();

        /// <summary>Sends characters; '\b' deletes the one before the caret.</summary>
        public abstract void Type(string s);

        /// <summary>Accept the text, exactly like pressing Enter.</summary>
        public abstract void Submit();

        /// <summary>Leave the box (like clicking elsewhere).</summary>
        public abstract void Dismiss();
    }

    /// <summary>
    /// One of the game's NGUI text boxes (<see cref="UIInput"/>). Characters go in through the box's own
    /// <c>OnInput</c> message — the same path a physical keyboard's text takes via NGUI — so the box's validator,
    /// length limit, change events and caret all behave as with typing. Text appears in the box live.
    /// </summary>
    internal sealed class InputTarget : TextTarget
    {
        // The box takes a moment to become NGUI's selected object after asking for a keyboard.
        private const float SelectGrace = 0.3f;

        private static readonly MethodInfo s_NotifySubmit = AccessTools.Method(typeof(UIInput), "NotifySubmit");
        private static readonly FieldInfo s_Text = AccessTools.Field(typeof(UIInput), "mText");
        private static readonly FieldInfo s_Carat = AccessTools.Field(typeof(UIInput), "m_CaratCharacterIndex");

        private readonly float m_RequestedAt;

        public InputTarget(UIInput input)
        {
            Input = input;
            m_RequestedAt = Time.realtimeSinceStartup;
        }

        public UIInput Input { get; }

        public override string Title
        {
            get
            {
                DatabaseString id = Input != null ? Input.IdentifierString : null;
                return id != null && id.IsValidString ? id.GetText() : null;
            }
        }

        public override string Text => Input != null ? Input.text : string.Empty;

        public override bool Password => Input != null && Input.isPassword;

        public override bool IsAlive => Input != null && Input.isActiveAndEnabled
            && (Input.selected || Time.realtimeSinceStartup - m_RequestedAt < SelectGrace);

        public override string Describe() => $"text box '{(Input != null ? Input.name : "?")}'";

        public override void Type(string s)
        {
            if (Input == null || !Input.selected)
            {
                return;
            }
            // Our keyboard always edits at the end. The box keeps its caret clamped to the *displayed* text, which is
            // shorter than the real text once it overflows, so put the caret at the real end before every key.
            if (s_Text != null && s_Carat != null && s_Text.GetValue(Input) is string current)
            {
                s_Carat.SetValue(Input, current.Length);
            }
            Input.gameObject.SendMessage("OnInput", s, SendMessageOptions.DontRequireReceiver);
        }

        public override void Submit()
        {
            // What Enter does in UIInput: notify the submit handlers, then leave the box.
            if (Input != null && Input.selected && s_NotifySubmit != null)
            {
                s_NotifySubmit.Invoke(Input, null);
            }
            Dismiss();
        }

        public override void Dismiss()
        {
            if (Input != null && Input.selected)
            {
                Input.selected = false;
            }
        }
    }

    /// <summary>
    /// Text the game edits as a whole and wants back through a callback — the journal's note text, which isn't a
    /// <see cref="UIInput"/> and reads raw keys itself. This is the same hand-over the game uses for Steam's keyboard:
    /// starting text in, finished text to the callback. Nothing changes until Done.
    /// </summary>
    internal sealed class BufferTarget : TextTarget
    {
        private readonly StringBuilder m_Text;
        private readonly int m_MaxChars;
        private readonly Action<string> m_Callback;
        private readonly Behaviour m_Owner;
        private readonly bool m_HasOwner;
        private readonly UIHudWindow m_Window;
        private readonly string m_Title;
        private readonly bool m_Multiline;

        public BufferTarget(string text, uint maxChars, bool multiline, string title, Action<string> callback)
        {
            // The journal builds its text with AppendLine (\r\n) and splits the result on \n.
            m_Text = new StringBuilder((text ?? string.Empty).Replace("\r", string.Empty).TrimEnd('\n'));
            m_MaxChars = maxChars > 0 ? (int)maxChars : 1024;
            m_Multiline = multiline;
            m_Title = title;
            m_Callback = callback;
            m_Owner = callback?.Target as Behaviour;
            m_HasOwner = callback?.Target is Behaviour;
            m_Window = ScreenTracker.Top;
        }

        public override string Title => m_Title;

        public override string Text => m_Text.ToString();

        public override bool Multiline => m_Multiline;

        public override bool IsAlive => (!m_HasOwner || (m_Owner != null && m_Owner.isActiveAndEnabled)) && ScreenTracker.Top == m_Window;

        public override string Describe() => $"text of {(m_HasOwner && m_Owner != null ? m_Owner.GetType().Name : "?")}";

        public override void Type(string s)
        {
            foreach (char c in s)
            {
                if (c == '\b')
                {
                    if (m_Text.Length > 0)
                    {
                        m_Text.Length--;
                    }
                }
                else if (m_Text.Length < m_MaxChars && (c != '\n' || m_Multiline))
                {
                    m_Text.Append(c);
                }
            }
        }

        public override void Submit()
        {
            m_Callback?.Invoke(m_Text.ToString());
        }

        public override void Dismiss()
        {
        }
    }
}
