using System;
using System.Collections.Generic;
using PoEController.Input;
using PoEController.Schemes;
using UnityEngine;

namespace PoEController.TextEntry
{
    internal enum KeyKind
    {
        Char,
        Shift,
        Layout,
        Space,
        NewLine,
        Delete,
        Done,
    }

    internal enum ShiftState
    {
        Off,
        /// <summary>Next letter only.</summary>
        Once,
        Locked,
    }

    internal readonly struct Key
    {
        public readonly KeyKind Kind;
        public readonly char Char;
        /// <summary>In key units; every row adds up to <see cref="OnScreenKeyboard.RowUnits"/>.</summary>
        public readonly float Width;

        public Key(KeyKind kind, char c, float width)
        {
            Kind = kind;
            Char = c;
            Width = width;
        }
    }

    /// <summary>
    /// Our controller keyboard. It opens wherever the game itself asks for an on-screen keyboard (it calls Steam's
    /// in those places — see <c>TextEntryPatches</c>), so it works the same on Steam, GOG and any other copy. While
    /// it is open, <see cref="KeyboardScheme"/> owns the controller. Layout: number row, three letter rows, then
    /// Shift / Symbols / Space / (New line) / Delete / Done.
    /// </summary>
    internal static class OnScreenKeyboard
    {
        public const float RowUnits = 10f;
        private const float StickThreshold = 0.5f;
        private const float StickRepeatDelay = 0.35f;
        private const float StickRepeatInterval = 0.09f;

        private static readonly Key[][] s_Letters = { Chars("1234567890"), Chars("qwertyuiop"), Chars("asdfghjkl'"), Chars("zxcvbnm,.-") };
        private static readonly Key[][] s_Symbols = { Chars("!?@#&*()+="), Chars(":;\"/\\_%<>~"), Chars("éèêàâçñöüä"), Chars("áíóúôîïëßø") };
        private static readonly Key[] s_Bottom =
        {
            new Key(KeyKind.Shift, '\0', 1.5f), new Key(KeyKind.Layout, '\0', 1.5f), new Key(KeyKind.Space, ' ', 3.5f),
            new Key(KeyKind.Delete, '\0', 1.5f), new Key(KeyKind.Done, '\0', 2f),
        };
        private static readonly Key[] s_BottomMultiline =
        {
            new Key(KeyKind.Shift, '\0', 1.5f), new Key(KeyKind.Layout, '\0', 1.5f), new Key(KeyKind.Space, ' ', 2.5f),
            new Key(KeyKind.NewLine, '\n', 1f), new Key(KeyKind.Delete, '\0', 1.5f), new Key(KeyKind.Done, '\0', 2f),
        };

        private static readonly Key[][] s_Rows = new Key[5][];
        private static float s_StickyX;
        private static Vector2Int s_StickDir;
        private static float s_StickTimer;

        public static TextTarget Target { get; private set; }
        public static bool IsOpen => Target != null;
        public static bool SymbolsShown { get; private set; }
        public static ShiftState Shift { get; private set; }
        public static int Row { get; private set; }
        public static int Col { get; private set; }

        /// <summary>The rows currently shown (letters or symbols, plus the bottom row).</summary>
        public static Key[][] Rows
        {
            get
            {
                Key[][] top = SymbolsShown ? s_Symbols : s_Letters;
                for (int i = 0; i < top.Length; i++)
                {
                    s_Rows[i] = top[i];
                }
                s_Rows[4] = Target != null && Target.Multiline ? s_BottomMultiline : s_Bottom;
                return s_Rows;
            }
        }

        /// <summary>The character a key types right now (shift applied).</summary>
        public static char Shown(Key k) => Shift != ShiftState.Off ? char.ToUpperInvariant(k.Char) : k.Char;

        public static bool IsEditing(UIInput input) => Target is InputTarget t && t.Input == input;

        // ---- Opening ----------------------------------------------------------------------------------------------

        public static void OpenFor(UIInput input)
        {
            if (input == null || IsEditing(input))
            {
                return;
            }
            Open(new InputTarget(input));
        }

        public static void OpenBuffer(string text, uint maxChars, bool multiline, string title, Action<string> callback)
        {
            Open(new BufferTarget(text, maxChars, multiline, title, callback));
        }

        private static void Open(TextTarget target)
        {
            bool wasOpen = IsOpen;
            Target = target;
            if (!wasOpen)
            {
                SymbolsShown = false;
                Row = 1;
                Col = 0;
                s_StickyX = 0.5f;
                s_StickDir = Vector2Int.zero;
            }
            // Names start with a capital.
            Shift = string.IsNullOrEmpty(target.Text) && !target.Password ? ShiftState.Once : ShiftState.Off;
            Plugin.Log.LogInfo($"Keyboard {(wasOpen ? "switched to" : "open for")} {target.Describe()}");
        }

        /// <summary>Called every frame (before scheme selection): closes the keyboard when it no longer applies.</summary>
        public static void Validate()
        {
            if (!IsOpen)
            {
                return;
            }
            if (Plugin.Instance.Mode.Active != InputDevice.Gamepad)
            {
                // Leave the box focused so the physical keyboard can carry on.
                Close("mouse/keyboard in use", dismiss: false);
            }
            else if (GameState.IsLoading || !Target.IsAlive)
            {
                Close("text box gone", dismiss: false);
            }
        }

        public static void Close(string reason, bool dismiss)
        {
            TextTarget t = Target;
            Target = null;
            if (t == null)
            {
                return;
            }
            Plugin.Log.LogInfo($"Keyboard closed ({reason})");
            if (dismiss)
            {
                Safe(t.Dismiss, "close");
            }
        }

        // ---- Input -----------------------------------------------------------------------------------------------

        public static void HandleInput(Gamepad pad, float dt)
        {
            if (!IsOpen)
            {
                return;
            }
            Navigate(pad, dt);

            if (pad.GetButtonDown(PadButton.East))
            {
                Close("closed", dismiss: true);
                return;
            }
            if (pad.GetButtonDown(PadButton.Start))
            {
                Done();
                return;
            }
            if (pad.GetButtonDown(PadButton.South))
            {
                Press(Rows[Row][Col]);
            }
            else if (pad.GetButtonRepeat(PadButton.West))
            {
                Type("\b");
            }
            else if (pad.GetButtonDown(PadButton.North))
            {
                Type(" ");
            }
            if (pad.GetButtonDown(PadButton.LeftTrigger))
            {
                CycleShift();
            }
            if (pad.GetButtonDown(PadButton.RightTrigger))
            {
                ToggleSymbols();
            }
        }

        public static void GetPrompts(List<Prompt> into)
        {
            into.Add(new Prompt(new[] { "dpad", "lstick" }, "Move"));
            into.Add(new Prompt("south", "Type"));
            into.Add(new Prompt("west", "Delete"));
            into.Add(new Prompt("north", "Space"));
            into.Add(new Prompt("lt", "Shift"));
            into.Add(new Prompt("rt", SymbolsShown ? "Letters" : "Symbols"));
            into.Add(new Prompt("start", "Done"));
            into.Add(new Prompt("east", "Close"));
        }

        private static void Press(Key k)
        {
            switch (k.Kind)
            {
                case KeyKind.Char:
                    Type(Shown(k).ToString());
                    if (Shift == ShiftState.Once)
                    {
                        Shift = ShiftState.Off;
                    }
                    break;
                case KeyKind.Space:
                case KeyKind.NewLine:
                    Type(k.Char.ToString());
                    break;
                case KeyKind.Delete:
                    Type("\b");
                    break;
                case KeyKind.Shift:
                    CycleShift();
                    break;
                case KeyKind.Layout:
                    ToggleSymbols();
                    break;
                case KeyKind.Done:
                    Done();
                    break;
            }
        }

        private static void CycleShift()
        {
            Shift = Shift == ShiftState.Off ? ShiftState.Once : (Shift == ShiftState.Once ? ShiftState.Locked : ShiftState.Off);
        }

        private static void ToggleSymbols()
        {
            SymbolsShown = !SymbolsShown;
        }

        private static void Type(string s)
        {
            TextTarget t = Target;
            if (!Safe(() => t.Type(s), "type"))
            {
                Close("error", dismiss: false);
            }
        }

        private static void Done()
        {
            TextTarget t = Target;
            // Close first: submitting can close the window or open another text box, which reopens us.
            Target = null;
            Plugin.Log.LogInfo("Keyboard closed (done)");
            Safe(t.Submit, "submit");
        }

        /// <summary>Runs a call into the game; a failure is logged and must not take the controller down.</summary>
        private static bool Safe(Action a, string what)
        {
            try
            {
                a();
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Keyboard {what} failed: {e}");
                return false;
            }
        }

        // ---- Navigation ------------------------------------------------------------------------------------------

        private static void Navigate(Gamepad pad, float dt)
        {
            if (pad.GetButtonRepeat(PadButton.DpadLeft))
            {
                Move(-1, 0);
            }
            else if (pad.GetButtonRepeat(PadButton.DpadRight))
            {
                Move(1, 0);
            }
            else if (pad.GetButtonRepeat(PadButton.DpadUp))
            {
                Move(0, -1);
            }
            else if (pad.GetButtonRepeat(PadButton.DpadDown))
            {
                Move(0, 1);
            }

            // Left stick: one step per push, repeating while held.
            Vector2 st = pad.LeftStick;
            Vector2Int dir = Vector2Int.zero;
            if (st.magnitude >= StickThreshold)
            {
                dir = Mathf.Abs(st.x) > Mathf.Abs(st.y) ? new Vector2Int(st.x > 0f ? 1 : -1, 0) : new Vector2Int(0, st.y > 0f ? -1 : 1);
            }
            if (dir == Vector2Int.zero)
            {
                s_StickDir = Vector2Int.zero;
                return;
            }
            if (dir != s_StickDir)
            {
                s_StickDir = dir;
                s_StickTimer = StickRepeatDelay;
                Move(dir.x, dir.y);
                return;
            }
            s_StickTimer -= dt;
            if (s_StickTimer <= 0f)
            {
                s_StickTimer = StickRepeatInterval;
                Move(dir.x, dir.y);
            }
        }

        /// <summary>Left/right wraps within the row; up/down picks the key nearest the remembered column position.</summary>
        private static void Move(int dx, int dy)
        {
            Key[][] rows = Rows;
            if (dx != 0)
            {
                int n = rows[Row].Length;
                Col = (Col + dx + n) % n;
                s_StickyX = KeyCentre(rows[Row], Col);
                return;
            }
            Row = (Row + dy + rows.Length) % rows.Length;
            Key[] row = rows[Row];
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < row.Length; i++)
            {
                float d = Mathf.Abs(KeyCentre(row, i) - s_StickyX);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            Col = best;
        }

        private static float KeyCentre(Key[] row, int index)
        {
            float x = 0f;
            for (int i = 0; i < index; i++)
            {
                x += row[i].Width;
            }
            return x + row[index].Width * 0.5f;
        }

        private static Key[] Chars(string s)
        {
            var keys = new Key[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                keys[i] = new Key(KeyKind.Char, s[i], 1f);
            }
            return keys;
        }
    }
}
