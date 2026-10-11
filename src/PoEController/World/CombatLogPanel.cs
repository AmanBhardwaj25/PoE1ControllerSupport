using System.Collections.Generic;
using PoEController.Compat;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// Console style's readable combat log (General Radial → Info → Combat log). The PC log window (UIConsole) keeps
    /// its last <c>MaxEntries</c> messages per tab as UIConsoleEntry objects in a ring buffer; the panel reads the
    /// combat tab's entries by name (private <c>m_Modes</c>, <c>m_Entries</c>, <c>m_EndPointer</c>, each entry's
    /// <c>m_Message</c>), oldest to newest, with each message's batched children indented, and shows them large and
    /// scrollable. X switches to the detailed (verbose: rolls) text where the game has one. Pauses in real time.
    /// Read-only: the log window itself is untouched. Feature <see cref="Features.CombatLog"/>.
    /// </summary>
    internal sealed class CombatLogPanel
    {
        public sealed class Line
        {
            public string Text;
            public string Verbose;
            public Color Color;
            public bool Child;
        }

        private readonly List<Line> m_Lines = new List<Line>();
        private bool m_PausedByUs;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<Line> Lines => m_Lines;

        /// <summary>The line shown at the bottom of the panel (newest = Lines.Count - 1).</summary>
        public int Bottom { get; private set; }

        /// <summary>X: detailed text (the rolls) instead of the short line, where the game wrote one.</summary>
        public bool Details { get; private set; }

        public bool Open()
        {
            if (!Features.On(Features.CombatLog))
            {
                return false;
            }
            m_Lines.Clear();
            List<Line> lines = Features.Get(Features.CombatLog, Read, null);
            if (lines != null)
            {
                m_Lines.AddRange(lines);
            }
            Bottom = m_Lines.Count - 1;
            Details = false;
            IsOpen = true;
            m_PausedByUs = false;
            TimeController tc = TimeController.Instance;
            if (tc != null && !tc.Paused && !TacticalModeManager.IsInTacticalCombat())
            {
                tc.SafePaused = true;
                m_PausedByUs = tc.Paused;
            }
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
            Plugin.Log.LogInfo($"Combat log opened ({m_Lines.Count} lines)");
            return true;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            m_Lines.Clear();
            if (m_PausedByUs && TimeController.Instance != null && TimeController.Instance.Paused)
            {
                TimeController.Instance.Paused = false;
            }
            m_PausedByUs = false;
        }

        public void ScrollBy(int delta)
        {
            if (m_Lines.Count > 0)
            {
                Bottom = Mathf.Clamp(Bottom + delta, 0, m_Lines.Count - 1);
            }
        }

        public void ToggleDetails()
        {
            Details = !Details;
            GlobalAudioPlayer.SPlay(UIAudioList.UIAudioType.ButtonUp);
        }

        /// <summary>The combat tab's messages, oldest first.</summary>
        private static List<Line> Read()
        {
            var lines = new List<Line>();
            UIConsole console = UIConsole.Instance;
            UIConsoleMode[] modes = console != null ? GameRef.Get<UIConsoleMode[]>(console, "m_Modes") : null;
            if (modes == null)
            {
                return lines;
            }
            UIConsoleMode combat = null;
            foreach (UIConsoleMode m in modes)
            {
                if (m != null && m.Mode == Console.ConsoleState.Combat)
                {
                    combat = m;
                    break;
                }
            }
            List<UIConsoleEntry> entries = combat != null ? GameRef.Get<List<UIConsoleEntry>>(combat, "m_Entries") : null;
            if (entries == null || entries.Count == 0)
            {
                return lines;
            }
            // Ring buffer: once full, the oldest entry is the one the end pointer will overwrite next.
            int start = 0;
            if (entries.Count >= console.MaxEntries && GameRef.TryGet(combat, "m_EndPointer", out int end))
            {
                start = Mathf.Clamp(end, 0, entries.Count - 1);
            }
            for (int k = 0; k < entries.Count; k++)
            {
                UIConsoleEntry entry = entries[(start + k) % entries.Count];
                Console.ConsoleMessage msg = entry != null ? GameRef.Get<Console.ConsoleMessage>(entry, "m_Message") : null;
                if (msg == null || msg.IsEmpty)
                {
                    continue;
                }
                Add(lines, msg, child: false);
                if (msg.Children != null)
                {
                    foreach (Console.ConsoleMessage c in msg.Children)
                    {
                        if (c != null && !c.IsEmpty)
                        {
                            Add(lines, c, child: true);
                        }
                    }
                }
            }
            return lines;
        }

        private static void Add(List<Line> lines, Console.ConsoleMessage msg, bool child)
        {
            lines.Add(new Line
            {
                Text = NGUITools.StripSymbols(msg.m_message ?? ""),
                Verbose = msg.IsVerboseEmpty ? null : NGUITools.StripSymbols(msg.m_verbosemessage),
                Color = msg.m_color.a > 0.05f ? msg.m_color : Color.white,
                Child = child,
            });
        }
    }
}
