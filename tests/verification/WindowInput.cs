using System;
using System.Runtime.InteropServices;

namespace Porting.Verification
{
    /// <summary>Sends keyboard input to a Windows fixture window.</summary>
    public static class WindowInput
    {
        [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
        private static extern uint MapVirtualKey(uint key, uint mode);

        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr key, IntPtr flags);

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
    }
}