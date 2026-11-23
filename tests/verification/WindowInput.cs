using System;
using System.Runtime.InteropServices;

namespace Porting.Verification
{
    /// <summary>Sends keyboard and mouse input to a Windows fixture window.</summary>
    public static class WindowInput
    {
        [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
        private static extern uint MapVirtualKey(uint key, uint mode);

        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr key, IntPtr flags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out Rectangle bounds);

        /// <summary>Posts one key press and release to the specified test window.</summary>
        public static bool Press(IntPtr window, int key)
        {
            uint down = 1 | (MapVirtualKey((uint)key, 0) << 16);
            if (key is >= 0x25 and <= 0x28)
            {
                down |= 1u << 24;
            }

            return PostMessage(window, 0x0100, new IntPtr(key), new IntPtr(down))
                && PostMessage(window, 0x0101, new IntPtr(key), new IntPtr(down | (3u << 30)));
        }

        /// <summary>Posts one left mouse click in the selected half of the fixture window.</summary>
        public static bool ClickHalf(IntPtr window, bool right)
        {
            if (!GetClientRect(window, out Rectangle bounds) || bounds.Right < 4 || bounds.Bottom < 2)
            {
                return false;
            }

            int x = right ? bounds.Right * 3 / 4 : bounds.Right / 4;
            int y = bounds.Bottom / 2;
            IntPtr point = new((y << 16) | x);
            return PostMessage(window, 0x0200, IntPtr.Zero, point)
                && PostMessage(window, 0x0201, new IntPtr(1), point)
                && PostMessage(window, 0x0202, IntPtr.Zero, point);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}