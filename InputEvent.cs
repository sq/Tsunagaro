using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Tsunagaro.Win32 {
    public enum InputEventType : int {
        Keyboard,
        Mouse
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardEvent {
        public uint Virtual;
        public uint Scan;
        public uint Flags;

        public override string ToString () {
            return String.Format("VK={0:X2} Scan={1:X2} Flags={2:X4}", Virtual, Scan, Flags);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseEvent {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;

        public override string ToString () {
            return String.Format("{0:00000},{1:00000} Data={2:X4} Flags={3:X4}", X, Y, Data, Flags);
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputEvent {
        [FieldOffset(0)]
        public InputEventType Type;
        [FieldOffset(4)]
        public int Message;
        [FieldOffset(8)]
        public KeyboardEvent Keyboard;
        [FieldOffset(8)]
        public MouseEvent Mouse;
    }
}
