using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace PoEController.Input
{
    /// <summary>Positional buttons (SDL semantics): South is A on Xbox, Cross on PlayStation, B on Switch.</summary>
    public enum PadButton
    {
        South = 0,
        East = 1,
        West = 2,
        North = 3,
        Back = 4,
        Guide = 5,
        Start = 6,
        LeftStick = 7,
        RightStick = 8,
        LeftShoulder = 9,
        RightShoulder = 10,
        DpadUp = 11,
        DpadDown = 12,
        DpadLeft = 13,
        DpadRight = 14,
        // Triggers exposed as digital buttons; not SDL button ids.
        LeftTrigger = 15,
        RightTrigger = 16,
    }

    public enum GlyphFamily
    {
        Xbox,
        PlayStation,
        Switch,
        SteamDeck,
    }

    /// <summary>
    /// Keeps every attached SDL game controller open and reads the one used most recently, exposing button edges,
    /// deadzoned sticks and repeat-aware navigation queries. Controllers can come and go at any time (plugged in after
    /// launch, batteries dying, Bluetooth sleep): new ones are picked up within a second, and buttons held while a
    /// controller arrives or takes over are ignored until released so they can't fire half a press.
    /// </summary>
    public sealed class Gamepad : IDisposable
    {
        public const int ButtonCount = 17;
        private const int SdlButtonCount = 15;
        private const float TriggerPressThreshold = 0.35f;
        private const float RescanInterval = 1f;

        private readonly ModLog m_Log;
        private readonly bool[] m_Down = new bool[ButtonCount];
        private readonly bool[] m_PrevDown = new bool[ButtonCount];
        private readonly float[] m_HeldTime = new float[ButtonCount];
        private readonly float[] m_RepeatTimer = new float[ButtonCount];
        private readonly bool[] m_Repeat = new bool[ButtonCount];
        private readonly bool[] m_Suppressed = new bool[ButtonCount];
        private const float SwitchStickThreshold = 0.5f;

        private sealed class Device
        {
            public IntPtr Handle;
            public int InstanceId;
            public string Name;
            public Sdl.ControllerType Type;
        }

        private readonly List<Device> m_Devices = new List<Device>();
        private Device m_Current;
        private IntPtr m_Controller = IntPtr.Zero;
        private float m_RescanTimer;
        private bool m_SuppressAfterRead;

        public float StickDeadzone = 0.2f;
        public float RepeatDelay = 0.35f;
        public float RepeatInterval = 0.08f;

        public bool Connected => m_Controller != IntPtr.Zero;
        public string Name { get; private set; } = string.Empty;
        public Sdl.ControllerType Type { get; private set; }
        public GlyphFamily DetectedFamily { get; private set; } = GlyphFamily.Xbox;
        public Vector2 LeftStick { get; private set; }
        public Vector2 RightStick { get; private set; }
        public float LeftTrigger { get; private set; }
        public float RightTrigger { get; private set; }

        /// <summary>True on any frame where a button, stick or trigger was actively used.</summary>
        public bool AnyActivity { get; private set; }

        /// <summary>A controller became available this frame while none was.</summary>
        public bool ConnectedThisFrame { get; private set; }

        /// <summary>The last controller went away this frame.</summary>
        public bool DisconnectedThisFrame { get; private set; }

        public Gamepad(ModLog log)
        {
            m_Log = log;
        }

        public bool GetButton(PadButton b) => m_Down[(int)b] && !m_Suppressed[(int)b];
        public bool GetButtonDown(PadButton b) => m_Down[(int)b] && !m_PrevDown[(int)b] && !m_Suppressed[(int)b];
        public bool GetButtonUp(PadButton b) => !m_Down[(int)b] && m_PrevDown[(int)b] && !m_Suppressed[(int)b];
        public float GetHeldTime(PadButton b) => m_Suppressed[(int)b] ? 0f : m_HeldTime[(int)b];

        /// <summary>Pressed this frame, or auto-repeating while held (for menu navigation).</summary>
        public bool GetButtonRepeat(PadButton b) => GetButtonDown(b) || (m_Repeat[(int)b] && !m_Suppressed[(int)b]);

        /// <summary>
        /// Ignores every currently held button until it is released (its release is ignored too). Used when the
        /// control scheme changes, so the press that opened a screen can't also act inside it.
        /// </summary>
        public void SuppressHeldButtons()
        {
            for (int i = 0; i < ButtonCount; i++)
            {
                if (m_Down[i])
                {
                    m_Suppressed[i] = true;
                }
            }
        }

        public void Update(float unscaledDeltaTime)
        {
            Sdl.SDL_PumpEvents();
            Sdl.SDL_GameControllerUpdate();
            // Nothing consumes SDL's event queue; drain it so it never fills.
            Sdl.SDL_FlushEvents(0, 0xFFFF);

            bool hadController = m_Current != null;
            DropDetached();
            m_RescanTimer -= unscaledDeltaTime;
            if (m_RescanTimer <= 0f)
            {
                m_RescanTimer = RescanInterval;
                OpenNew();
            }
            FollowMostRecentlyUsed();
            if (m_Current == null && m_Devices.Count > 0)
            {
                MakeCurrent(m_Devices[0]);
            }
            ConnectedThisFrame = !hadController && m_Current != null;
            DisconnectedThisFrame = hadController && m_Current == null;

            // A suppressed button stays suppressed through its release frame, then clears.
            for (int i = 0; i < ButtonCount; i++)
            {
                if (m_Suppressed[i] && !m_Down[i])
                {
                    m_Suppressed[i] = false;
                }
            }
            Array.Copy(m_Down, m_PrevDown, ButtonCount);
            if (m_Controller == IntPtr.Zero)
            {
                Array.Clear(m_Down, 0, ButtonCount);
                LeftStick = RightStick = Vector2.zero;
                LeftTrigger = RightTrigger = 0f;
                AnyActivity = false;
                UpdateTimers(unscaledDeltaTime);
                return;
            }

            for (int i = 0; i < SdlButtonCount; i++)
            {
                m_Down[i] = Sdl.SDL_GameControllerGetButton(m_Controller, i) != 0;
            }
            LeftStick = ReadStick(Sdl.Axis.LeftX, Sdl.Axis.LeftY);
            RightStick = ReadStick(Sdl.Axis.RightX, Sdl.Axis.RightY);
            LeftTrigger = ReadTrigger(Sdl.Axis.TriggerLeft);
            RightTrigger = ReadTrigger(Sdl.Axis.TriggerRight);
            m_Down[(int)PadButton.LeftTrigger] = LeftTrigger > TriggerPressThreshold;
            m_Down[(int)PadButton.RightTrigger] = RightTrigger > TriggerPressThreshold;
            if (m_SuppressAfterRead)
            {
                m_SuppressAfterRead = false;
                // Whatever is held on a controller that just arrived or took over is not a fresh press.
                Array.Copy(m_Down, m_PrevDown, ButtonCount);
                SuppressHeldButtons();
            }

            bool activity = LeftStick != Vector2.zero || RightStick != Vector2.zero;
            for (int i = 0; i < ButtonCount && !activity; i++)
            {
                activity = m_Down[i];
            }
            AnyActivity = activity;
            UpdateTimers(unscaledDeltaTime);
        }

        public void Rumble(float low, float high, float seconds)
        {
            if (m_Controller != IntPtr.Zero)
            {
                Sdl.SDL_GameControllerRumble(m_Controller, (ushort)(Mathf.Clamp01(low) * 65535f), (ushort)(Mathf.Clamp01(high) * 65535f), (uint)(seconds * 1000f));
            }
        }

        public void Dispose()
        {
            foreach (Device d in m_Devices)
            {
                Sdl.SDL_GameControllerClose(d.Handle);
            }
            m_Devices.Clear();
            m_Current = null;
            m_Controller = IntPtr.Zero;
        }

        private void UpdateTimers(float dt)
        {
            for (int i = 0; i < ButtonCount; i++)
            {
                m_Repeat[i] = false;
                if (!m_Down[i])
                {
                    m_HeldTime[i] = 0f;
                    m_RepeatTimer[i] = 0f;
                    continue;
                }
                m_HeldTime[i] += dt;
                if (m_HeldTime[i] >= RepeatDelay)
                {
                    m_RepeatTimer[i] -= dt;
                    if (m_RepeatTimer[i] <= 0f)
                    {
                        m_Repeat[i] = true;
                        m_RepeatTimer[i] = RepeatInterval;
                    }
                }
            }
        }

        private void DropDetached()
        {
            for (int i = m_Devices.Count - 1; i >= 0; i--)
            {
                Device d = m_Devices[i];
                if (Sdl.SDL_GameControllerGetAttached(d.Handle) != 0)
                {
                    continue;
                }
                m_Log.LogInfo($"Controller disconnected: '{d.Name}'");
                Sdl.SDL_GameControllerClose(d.Handle);
                m_Devices.RemoveAt(i);
                if (d == m_Current)
                {
                    SetCurrent(null);
                }
            }
        }

        /// <summary>Opens controllers that are attached but not open yet (plugged in or woken up since the last scan).</summary>
        private void OpenNew()
        {
            int count = Sdl.SDL_NumJoysticks();
            for (int i = 0; i < count; i++)
            {
                if (Sdl.SDL_IsGameController(i) == 0 || IsOpen(Sdl.SDL_JoystickGetDeviceInstanceID(i)))
                {
                    continue;
                }
                IntPtr c = Sdl.SDL_GameControllerOpen(i);
                if (c == IntPtr.Zero)
                {
                    m_Log.LogWarning($"Failed to open controller {i}: {Sdl.SDL_GetError()}");
                    continue;
                }
                Device d = new Device
                {
                    Handle = c,
                    InstanceId = Sdl.SDL_JoystickInstanceID(Sdl.SDL_GameControllerGetJoystick(c)),
                    Name = Sdl.SDL_GameControllerName(c) ?? string.Empty,
                    Type = Sdl.SDL_GameControllerGetType(c),
                };
                m_Devices.Add(d);
                m_Log.LogInfo($"Controller connected: '{d.Name}' type={d.Type} glyphs={FamilyFor(d.Type, d.Name)}");
            }
        }

        private bool IsOpen(int instanceId)
        {
            foreach (Device d in m_Devices)
            {
                if (d.InstanceId == instanceId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// With several controllers attached, the one being used wins (e.g. picking up a PlayStation pad while an
        /// Xbox pad sits on the desk). Small stick drift on an idle pad doesn't count.
        /// </summary>
        private void FollowMostRecentlyUsed()
        {
            if (m_Devices.Count < 2 || (m_Current != null && IsDeliberatelyUsed(m_Current.Handle)))
            {
                return;
            }
            foreach (Device d in m_Devices)
            {
                if (d != m_Current && IsDeliberatelyUsed(d.Handle))
                {
                    m_Log.LogInfo($"Switched to controller '{d.Name}'");
                    MakeCurrent(d);
                    return;
                }
            }
        }

        private static bool IsDeliberatelyUsed(IntPtr c)
        {
            for (int i = 0; i < SdlButtonCount; i++)
            {
                if (Sdl.SDL_GameControllerGetButton(c, i) != 0)
                {
                    return true;
                }
            }
            for (Sdl.Axis a = Sdl.Axis.LeftX; a <= Sdl.Axis.TriggerRight; a++)
            {
                if (Mathf.Abs(Sdl.SDL_GameControllerGetAxis(c, a) / 32767f) > SwitchStickThreshold)
                {
                    return true;
                }
            }
            return false;
        }

        private void MakeCurrent(Device d)
        {
            SetCurrent(d);
            m_SuppressAfterRead = true;
        }

        private void SetCurrent(Device d)
        {
            m_Current = d;
            m_Controller = d != null ? d.Handle : IntPtr.Zero;
            Name = d != null ? d.Name : string.Empty;
            if (d != null)
            {
                Type = d.Type;
                DetectedFamily = FamilyFor(d.Type, d.Name);
            }
        }

        private static GlyphFamily FamilyFor(Sdl.ControllerType type, string name)
        {
            // The Deck's built-in controller (through Steam Input) has no SDL type of its own: "Steam Deck Controller", Unknown.
            if (!string.IsNullOrEmpty(name) && name.IndexOf("Steam Deck", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GlyphFamily.SteamDeck;
            }
            switch (type)
            {
                case Sdl.ControllerType.PS3:
                case Sdl.ControllerType.PS4:
                case Sdl.ControllerType.PS5:
                    return GlyphFamily.PlayStation;
                case Sdl.ControllerType.SwitchPro:
                case Sdl.ControllerType.JoyConLeft:
                case Sdl.ControllerType.JoyConRight:
                case Sdl.ControllerType.JoyConPair:
                    return GlyphFamily.Switch;
                default:
                    return GlyphFamily.Xbox;
            }
        }

        private Vector2 ReadStick(Sdl.Axis xAxis, Sdl.Axis yAxis)
        {
            // SDL Y is down-positive; flip so up is +Y like Unity.
            Vector2 v = new Vector2(Normalize(Sdl.SDL_GameControllerGetAxis(m_Controller, xAxis)), -Normalize(Sdl.SDL_GameControllerGetAxis(m_Controller, yAxis)));
            float mag = v.magnitude;
            if (mag < StickDeadzone)
            {
                return Vector2.zero;
            }
            // Radial deadzone, rescaled so output starts at 0 just outside the deadzone.
            float scaled = Mathf.Clamp01((mag - StickDeadzone) / (1f - StickDeadzone));
            return v / mag * scaled;
        }

        private float ReadTrigger(Sdl.Axis axis)
        {
            return Mathf.Clamp01(Sdl.SDL_GameControllerGetAxis(m_Controller, axis) / 32767f);
        }

        private static float Normalize(short raw)
        {
            return Mathf.Clamp(raw / 32767f, -1f, 1f);
        }
    }
}
