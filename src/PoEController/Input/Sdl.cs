using System;
using System.Runtime.InteropServices;

namespace PoEController.Input
{
    /// <summary>Minimal SDL2 GameController bindings (SDL 2.30).</summary>
    public static class Sdl
    {
        private const string Lib = "SDL2";

        public const uint INIT_JOYSTICK = 0x00000200;
        public const uint INIT_HAPTIC = 0x00001000;
        public const uint INIT_GAMECONTROLLER = 0x00002000;
        public const uint INIT_EVENTS = 0x00004000;

        public enum ControllerType
        {
            Unknown = 0,
            Xbox360 = 1,
            XboxOne = 2,
            PS3 = 3,
            PS4 = 4,
            SwitchPro = 5,
            Virtual = 6,
            PS5 = 7,
            AmazonLuna = 8,
            GoogleStadia = 9,
            NvidiaShield = 10,
            JoyConLeft = 11,
            JoyConRight = 12,
            JoyConPair = 13,
        }

        public enum Axis
        {
            LeftX = 0, LeftY = 1, RightX = 2, RightY = 3, TriggerLeft = 4, TriggerRight = 5,
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadLibrary(string path);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_Init(uint flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_Quit();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GetError")]
        private static extern IntPtr SDL_GetErrorNative();
        public static string SDL_GetError() => PtrToUtf8(SDL_GetErrorNative());

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPStr)] string value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_PumpEvents();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_FlushEvents(uint minType, uint maxType);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_NumJoysticks();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_IsGameController(int joystickIndex);

        /// <summary>Instance id of the device at a joystick index (stable for as long as the device stays attached).</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_JoystickGetDeviceInstanceID(int joystickIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr SDL_GameControllerGetJoystick(IntPtr controller);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_JoystickInstanceID(IntPtr joystick);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr SDL_GameControllerOpen(int joystickIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_GameControllerClose(IntPtr controller);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_GameControllerGetAttached(IntPtr controller);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_GameControllerName")]
        private static extern IntPtr SDL_GameControllerNameNative(IntPtr controller);
        public static string SDL_GameControllerName(IntPtr controller) => PtrToUtf8(SDL_GameControllerNameNative(controller));

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern ControllerType SDL_GameControllerGetType(IntPtr controller);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_GameControllerUpdate();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte SDL_GameControllerGetButton(IntPtr controller, int button);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern short SDL_GameControllerGetAxis(IntPtr controller, Axis axis);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_GameControllerRumble(IntPtr controller, ushort lowFrequency, ushort highFrequency, uint durationMs);

        private static string PtrToUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero)
            {
                return string.Empty;
            }
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0)
            {
                len++;
            }
            byte[] buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return System.Text.Encoding.UTF8.GetString(buf);
        }
    }
}
