using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PoEController.Menus
{
    /// <summary>
    /// Drives the real OS mouse for menu navigation, so every mouse-driven UI behaviour (hover tooltips, NGUI
    /// clicks, inventory pick-up/drop, scroll lists) works without per-screen code. Positions are Unity screen
    /// coordinates (origin bottom-left). Remembers what it did so device detection can ignore it.
    /// </summary>
    internal static class MouseEmulator
    {
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const int WHEEL_DELTA = 120;
        private const float SyntheticGraceSeconds = 0.25f;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extraInfo);

        private static bool s_LeftDown;
        private static bool s_RightDown;

        /// <summary>Where we last put the cursor (Unity screen coords), for device detection.</summary>
        public static Vector2 LastSetPosition { get; private set; } = new Vector2(-1000f, -1000f);

        /// <summary>Until this time, mouse button/wheel activity is assumed to be ours.</summary>
        public static float SyntheticUntil { get; private set; }

        public static bool IsSyntheticActivity => Time.unscaledTime < SyntheticUntil || s_LeftDown || s_RightDown;

        public static Vector2 Position => UnityEngine.Input.mousePosition;

        /// <summary>Moves the cursor; only while the game window is active so we never move it over other apps.</summary>
        public static bool MoveTo(Vector2 unityScreenPos)
        {
            IntPtr hwnd = GetActiveWindow();
            if (hwnd == IntPtr.Zero || !Application.isFocused)
            {
                return false;
            }
            float x = Mathf.Clamp(unityScreenPos.x, 0f, Screen.width - 1);
            float y = Mathf.Clamp(unityScreenPos.y, 0f, Screen.height - 1);
            POINT p = new POINT { X = Mathf.RoundToInt(x), Y = Mathf.RoundToInt(Screen.height - 1 - y) };
            if (!ClientToScreen(hwnd, ref p))
            {
                return false;
            }
            LastSetPosition = new Vector2(x, y);
            MarkSynthetic();
            return SetCursorPos(p.X, p.Y);
        }

        // Presses need the window active and the cursor inside it; releases always go through so nothing sticks.
        public static void SetLeft(bool down)
        {
            if (down == s_LeftDown || (down && !CanClick()))
            {
                return;
            }
            s_LeftDown = down;
            MarkSynthetic();
            mouse_event(down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void SetRight(bool down)
        {
            if (down == s_RightDown || (down && !CanClick()))
            {
                return;
            }
            s_RightDown = down;
            MarkSynthetic();
            mouse_event(down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }

        /// <summary>Positive = scroll up (away from the user), in wheel notches.</summary>
        public static void Scroll(int notches)
        {
            if (notches == 0 || !CanClick())
            {
                return;
            }
            MarkSynthetic();
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, notches * WHEEL_DELTA, UIntPtr.Zero);
        }

        /// <summary>Releases any button we are holding. Safe to call any time.</summary>
        public static void ReleaseAll()
        {
            if (s_LeftDown)
            {
                s_LeftDown = false;
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
            if (s_RightDown)
            {
                s_RightDown = false;
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
            }
        }

        private static bool CanClick()
        {
            // Never inject clicks unless our window is active and the cursor is inside it.
            if (GetActiveWindow() == IntPtr.Zero || !Application.isFocused)
            {
                return false;
            }
            Vector2 m = UnityEngine.Input.mousePosition;
            return m.x >= 0f && m.y >= 0f && m.x < Screen.width && m.y < Screen.height;
        }

        private static void MarkSynthetic()
        {
            SyntheticUntil = Time.unscaledTime + SyntheticGraceSeconds;
        }
    }
}
