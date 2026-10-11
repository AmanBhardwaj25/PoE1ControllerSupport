using System;
using System.Collections.Generic;
using System.Text;
using PoEController.Input;
using PoEController.Menus;
using PoEController.UI;
using UnityEngine;

namespace PoEController.Glyphs
{
    /// <summary>
    /// Controller glyphs inside the game's own text. Text sources (<see cref="ControllerText"/>) hand out strings with
    /// glyph tokens; when such a string is put into an NGUI label (<c>UILabel.text</c> prefix), each token becomes a
    /// gap of non-breaking spaces on the controller (or its keyboard/mouse fallback text otherwise), and every frame
    /// the glyph is drawn into that gap, found with the label's own text layout (<see cref="LabelLayout"/>).
    /// Labels remember their unresolved text, so switching device re-resolves them in place.
    /// </summary>
    internal static class TextGlyphs
    {
        private const char TokenStart = '\u0002';
        private const char TokenSplit = '\u001F';
        private const char TokenEnd = '\u0003';
        private const char Gap = ' ';
        /// <summary>Non-breaking spaces per square glyph (a space is roughly a quarter of the font size wide).</summary>
        private const int GapPerGlyph = 5;
        private const int GapPerWideGlyph = 8;

        private sealed class Entry
        {
            /// <summary>The text as the game set it (tokens unresolved).</summary>
            public string Raw;
            /// <summary>Glyph names per gap, in text order (empty when resolved for mouse/keyboard).</summary>
            public readonly List<string[]> Groups = new List<string[]>();
            public string LaidOutText;
            public Vector2 LaidOutSize;
            /// <summary>Gaps found in the layout (layout space, min/max), in text order.</summary>
            public readonly List<Rect> Runs = new List<Rect>();
            /// <summary>What the last diagnostic log line said for this label (logged on change only).</summary>
            public string Logged;
        }

        private static readonly Dictionary<UILabel, Entry> s_Labels = new Dictionary<UILabel, Entry>();
        /// <summary>Whole-text controller rewrites (tutorials) → the game's original text, for mouse/keyboard.</summary>
        private static readonly Dictionary<string, string> s_Originals = new Dictionary<string, string>();
        private static readonly List<UILabel> s_Scratch = new List<UILabel>();
        private static readonly BetterList<Vector3> s_Verts = new BetterList<Vector3>();
        private static readonly BetterList<int> s_Indices = new BetterList<int>();
        private static readonly StringBuilder s_Builder = new StringBuilder(256);

        public static bool Broken => !Compat.Features.On(Compat.Features.TextGlyphs);

        public static bool OnGamepad => Plugin.Instance != null && Plugin.Instance.Mode != null && Plugin.Instance.Mode.Active == InputDevice.Gamepad;

        /// <summary>A glyph token: <paramref name="glyphs"/> = glyph names joined by '+', drawn side by side.</summary>
        public static string Token(string glyphs, string fallback) => TokenStart + glyphs + TokenSplit + fallback + TokenEnd;

        /// <summary>Remembers the game's own text behind a whole-text controller rewrite.</summary>
        public static string Rewrite(string controllerText, string original)
        {
            s_Originals[controllerText] = original;
            return controllerText;
        }

        /// <summary>Cheap test for the label setter: does this call concern us at all?</summary>
        public static bool Concerns(UILabel label, string value)
        {
            return !Broken && ((value != null && value.IndexOf(TokenStart) >= 0) || (s_Labels.Count > 0 && s_Labels.ContainsKey(label)));
        }

        /// <summary>From the <c>UILabel.text</c> setter prefix.</summary>
        public static void OnSetText(UILabel label, ref string value)
        {
            if (value != null && value.IndexOf(TokenStart) >= 0)
            {
                if (!s_Labels.TryGetValue(label, out Entry e))
                {
                    if (s_Labels.Count > 256)
                    {
                        Sweep();
                    }
                    e = new Entry();
                    s_Labels[label] = e;
                }
                e.Raw = value;
                e.LaidOutText = null;
                value = Resolve(value, OnGamepad, e.Groups);
                if (e.Logged == null)
                {
                    e.Logged = "set";
                    Plugin.Log.LogInfo($"Text glyphs in '{label.name}': text set with {e.Groups.Count} glyph gap(s)");
                }
            }
            else if (value == null || value.IndexOf(Gap) < 0)
            {
                // Replaced by plain text. (Text that still has our gaps is the label rewriting itself, e.g. the
                // glossary colouring links — keep it.)
                s_Labels.Remove(label);
            }
        }

        /// <summary>Device changed: put every tracked label's text through the resolver again.</summary>
        public static void Refresh()
        {
            if (Broken)
            {
                return;
            }
            Sweep();
            s_Scratch.Clear();
            s_Scratch.AddRange(s_Labels.Keys);
            foreach (UILabel label in s_Scratch)
            {
                if (s_Labels.TryGetValue(label, out Entry e) && e.Raw != null)
                {
                    label.text = e.Raw;
                }
            }
        }

        /// <summary>Something went wrong: give every label its plain keyboard/mouse text back and stop.</summary>
        public static void Fail(Exception ex)
        {
            if (Broken)
            {
                return;
            }
            Plugin.Log.LogError($"Controller glyphs in text failed, showing the game's normal text: {ex}");
            try
            {
                Sweep();
                foreach (KeyValuePair<UILabel, Entry> kv in s_Labels)
                {
                    if (kv.Value.Raw != null)
                    {
                        kv.Key.text = Resolve(kv.Value.Raw, false, kv.Value.Groups);
                    }
                }
            }
            catch (Exception)
            {
                // Best effort only.
            }
            Compat.Features.Off(Compat.Features.TextGlyphs, "turned off after an error (logged above)");
            s_Labels.Clear();
        }

        private static string Resolve(string raw, bool glyphs, List<string[]> groups)
        {
            groups.Clear();
            if (!glyphs && s_Originals.TryGetValue(raw, out string original))
            {
                return original;
            }
            StringBuilder sb = s_Builder;
            sb.Length = 0;
            int i = 0;
            while (i < raw.Length)
            {
                int start = raw.IndexOf(TokenStart, i);
                if (start < 0)
                {
                    sb.Append(raw, i, raw.Length - i);
                    break;
                }
                int split = raw.IndexOf(TokenSplit, start);
                int end = raw.IndexOf(TokenEnd, start);
                if (split < 0 || end < 0 || split > end)
                {
                    // Malformed: drop the marker character and carry on.
                    sb.Append(raw, i, start - i);
                    i = start + 1;
                    continue;
                }
                string names = raw.Substring(start + 1, split - start - 1);
                string fallback = raw.Substring(split + 1, end - split - 1);
                // "[I]"-style hotkeys: the brackets belong to the key name, not to a glyph.
                bool bracketed = start > i && raw[start - 1] == '[' && end + 1 < raw.Length && raw[end + 1] == ']';
                if (glyphs)
                {
                    sb.Append(raw, i, start - i - (bracketed ? 1 : 0));
                    string[] parts = names.Split('+');
                    groups.Add(parts);
                    sb.Append(Gap, GapWidth(parts));
                    i = end + 1 + (bracketed ? 1 : 0);
                }
                else
                {
                    sb.Append(raw, i, start - i);
                    sb.Append(fallback);
                    i = end + 1;
                }
            }
            return sb.ToString();
        }

        private static int GapWidth(string[] glyphs)
        {
            int n = glyphs.Length - 1;
            foreach (string g in glyphs)
            {
                Texture2D tex = Plugin.Instance.Glyphs.Get(Plugin.Instance.ActiveGlyphFamily, g);
                n += tex != null && tex.width > tex.height * 1.4f ? GapPerWideGlyph : GapPerGlyph;
            }
            return n;
        }

        private static void Sweep()
        {
            s_Scratch.Clear();
            foreach (UILabel label in s_Labels.Keys)
            {
                if (label == null)
                {
                    s_Scratch.Add(label);
                }
            }
            foreach (UILabel dead in s_Scratch)
            {
                s_Labels.Remove(dead);
            }
        }

        /// <summary>Draws the glyphs into their gaps (IMGUI, from <c>Plugin.OnGUI</c>, controller mode only).</summary>
        public static void Draw(GlyphLibrary library, GlyphFamily family)
        {
            if (Broken || s_Labels.Count == 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }
            try
            {
                foreach (KeyValuePair<UILabel, Entry> kv in s_Labels)
                {
                    UILabel label = kv.Key;
                    Entry e = kv.Value;
                    if (label == null || e.Groups.Count == 0)
                    {
                        continue;
                    }
                    if (!label.enabled || !label.gameObject.activeInHierarchy)
                    {
                        Diagnose(label, e, "label hidden");
                        continue;
                    }
                    float alpha = LabelLayout.Alpha(label);
                    if (alpha < 0.05f)
                    {
                        Diagnose(label, e, $"label transparent at {alpha:0.00}");
                        continue;
                    }
                    // The UI camera that draws this label (NGUITools' version returns the first camera of any kind
                    // that sees the layer — possibly the 3D game camera).
                    UICamera uiCam = UICamera.FindCameraForLayer(label.gameObject.layer);
                    Camera cam = uiCam != null ? uiCam.cachedCamera : null;
                    if (cam == null)
                    {
                        Diagnose(label, e, "no UI camera");
                        continue;
                    }
                    if (!FindRuns(label, e))
                    {
                        Diagnose(label, e, "no gaps found in the layout");
                        continue;
                    }
                    Rect visible = LabelLayout.VisibleArea(label, cam);
                    Color old = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    int count = Mathf.Min(e.Runs.Count, e.Groups.Count);
                    for (int i = 0; i < count; i++)
                    {
                        string result = DrawGroup(library, family, label, cam, visible, e.Runs[i], e.Groups[i]);
                        if (i == 0)
                        {
                            Diagnose(label, e, result);
                        }
                    }
                    GUI.color = old;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        /// <summary>One log line per label and outcome, so a glyph that doesn't show can be traced from the log.</summary>
        private static void Diagnose(UILabel label, Entry e, string what)
        {
            // Compare only the kind of outcome ("drawn", "gap", "no …"), not the coordinates, to avoid repeats.
            int cut = what.IndexOf(" at ", StringComparison.Ordinal);
            string kind = cut > 0 ? what.Substring(0, cut) : what;
            if (kind != e.Logged)
            {
                e.Logged = kind;
                Plugin.Log.LogInfo($"Text glyphs in '{label.name}': {what}");
            }
        }

        /// <summary>Draws one gap's glyphs; returns what happened (for <see cref="Diagnose"/>).</summary>
        private static string DrawGroup(GlyphLibrary library, GlyphFamily family, UILabel label, Camera cam, Rect visible, Rect run, string[] glyphs)
        {
            Vector3 a = cam.WorldToScreenPoint(LabelLayout.ToWorld(label, run.min));
            Vector3 b = cam.WorldToScreenPoint(LabelLayout.ToWorld(label, run.max));
            Rect screen = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            if (!visible.Contains(screen.center))
            {
                return $"gap at {screen} is outside its panel's visible area {visible}"; // scrolled away
            }
            // GUI space (y down).
            Rect r = new Rect(screen.x, Screen.height - screen.yMax, screen.width, screen.height);
            float totalAspect = 0f;
            foreach (string g in glyphs)
            {
                Texture2D t = library.Get(family, g);
                totalAspect += t != null && t.height > 0 ? (float)t.width / t.height : 1f;
            }
            float spacing = r.height * 0.1f;
            float h = Mathf.Min(r.height * 1.15f, (r.width - spacing * (glyphs.Length - 1)) / Mathf.Max(0.1f, totalAspect));
            float x = r.center.x - (h * totalAspect + spacing * (glyphs.Length - 1)) * 0.5f;
            float y = r.center.y - h * 0.5f;
            foreach (string g in glyphs)
            {
                Texture2D t = library.Get(family, g);
                float w = t != null && t.height > 0 ? h * t.width / t.height : h;
                if (t != null)
                {
                    GUI.DrawTexture(new Rect(x, y, w, h), t, ScaleMode.ScaleToFit);
                }
                x += w + spacing;
            }
            return $"drawn at {r} ({string.Join("+", glyphs)})";
        }

        private static readonly List<int> s_RealIndex = new List<int>(512);

        /// <summary>
        /// Maps NGUI's printed-character numbering back to positions in <paramref name="processed"/>: NGUI skips each
        /// '\r' (Windows line endings from StringBuilder.AppendLine) and numbers later characters that many lower.
        /// </summary>
        private static void BuildRealIndex(string processed)
        {
            s_RealIndex.Clear();
            for (int i = 0; i < processed.Length; i++)
            {
                if (processed[i] != '\r')
                {
                    s_RealIndex.Add(i); // entry n = real position of the n-th non-'\r' character
                }
            }
        }

        /// <summary>Finds the runs of gap characters in the label's layout; cached until its text or size changes.</summary>
        private static bool FindRuns(UILabel label, Entry e)
        {
            string text = label.text;
            Vector2 size = label.relativeSize;
            if (ReferenceEquals(text, e.LaidOutText) && size == e.LaidOutSize)
            {
                return e.Runs.Count > 0;
            }
            e.LaidOutText = text;
            e.LaidOutSize = size;
            e.Runs.Clear();
            if (!LabelLayout.Print(label, s_Verts, s_Indices))
            {
                return false;
            }
            string processed = label.processedText;
            BuildRealIndex(processed);
            int lastIndex = -2;
            for (int i = 0; i < s_Indices.size && i * 2 + 1 < s_Verts.size; i++)
            {
                // NGUI numbers printed characters without the '\r's before them (UIFont: indices.Add(i - num8)).
                int printed = s_Indices[i];
                int idx = printed >= 0 && printed < s_RealIndex.Count ? s_RealIndex[printed] : -1;
                if (idx < 0 || idx >= processed.Length || processed[idx] != Gap)
                {
                    continue;
                }
                Vector3 lo = s_Verts[i * 2];
                Vector3 hi = s_Verts[i * 2 + 1];
                int last = e.Runs.Count - 1;
                if (last >= 0 && idx == lastIndex + 1 && Mathf.Approximately(e.Runs[last].yMin, lo.y))
                {
                    Rect r = e.Runs[last];
                    e.Runs[last] = Rect.MinMaxRect(r.xMin, r.yMin, Mathf.Max(r.xMax, hi.x), Mathf.Max(r.yMax, hi.y));
                }
                else
                {
                    e.Runs.Add(Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y));
                }
                lastIndex = idx;
            }
            s_Verts.Clear();
            s_Indices.Clear();
            if (e.Runs.Count != e.Groups.Count)
            {
                Plugin.Log.LogWarning($"Text glyphs in '{label.name}': found {e.Runs.Count} gaps, expected {e.Groups.Count}");
            }
            return e.Runs.Count > 0;
        }
    }
}
