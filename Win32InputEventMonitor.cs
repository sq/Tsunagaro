using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Threading;
using Squared.Task;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Tsunagaro.Win32 {
    enum LowLevelHookType : int {
        Keyboard = 13,
        Mouse = 14
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT {
        public int x, y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
    }

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
        public KeyboardEvent  Keyboard;
        [FieldOffset(8)]
        public MouseEvent     Mouse;
    }

    public class InputEventMonitor : IDisposable {
        delegate uint LowLevelHookProc (int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx (
            LowLevelHookType idHook,
            LowLevelHookProc lpFn,
            IntPtr hMod,
            uint dwThreadId
        );

        [DllImport("user32.dll")]
        private static extern uint CallNextHookEx (
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam
        );

        [DllImport("user32.dll")]
        private static extern uint UnhookWindowsHookEx (
            IntPtr hhk
        );

        public bool IsDisposed { get; private set; }

        public  readonly Signal DataReady = new Signal();
        private readonly ConcurrentQueue<InputEvent> Buffer = new ConcurrentQueue<InputEvent>();
        private readonly Thread Thread;

        private IntPtr HookKeyboard, HookMouse;

        public InputEventMonitor () {
            Thread = new Thread(ThreadProc) {
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };
        }

        public void Start () {
            Thread.Start();
        }

        private void ThreadProc () {
            HookKeyboard = SetWindowsHookEx(LowLevelHookType.Keyboard, KeyboardProc, IntPtr.Zero, 0);
            HookMouse = SetWindowsHookEx(LowLevelHookType.Mouse, MouseProc, IntPtr.Zero, 0);

            using (var context = new System.Windows.Forms.ApplicationContext())
                System.Windows.Forms.Application.Run(context);
        }

        unsafe uint KeyboardProc (int nCode, IntPtr wParam, IntPtr lParam) {
            var pData = (KBDLLHOOKSTRUCT *)lParam;

            var evt = new InputEvent {
                Type = InputEventType.Keyboard,
                Message = wParam.ToInt32(),
                Keyboard = new KeyboardEvent {
                    Scan = pData->scanCode,
                    Virtual = pData->vkCode,
                    Flags = pData->flags
                }
            };

            Buffer.Enqueue(evt);
            DataReady.Set();

            return CallNextHookEx(HookKeyboard, nCode, wParam, lParam);
        }

        unsafe uint MouseProc (int nCode, IntPtr wParam, IntPtr lParam) {
            var pData = (MSLLHOOKSTRUCT*)lParam;

            var evt = new InputEvent {
                Type = InputEventType.Mouse,
                Message = wParam.ToInt32(),
                Mouse = new MouseEvent {
                    X = pData->pt.x,
                    Y = pData->pt.y,
                    Data = pData->mouseData,
                    Flags = pData->flags
                }
            };

            Buffer.Enqueue(evt);
            DataReady.Set();

            return CallNextHookEx(HookMouse, nCode, wParam, lParam);
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            UnhookWindowsHookEx(HookKeyboard);
            UnhookWindowsHookEx(HookMouse);

            Thread.Priority = ThreadPriority.Lowest;

            DataReady.Dispose();
        }

        public bool TryRead (out InputEvent evt) {
            return Buffer.TryDequeue(out evt);
        }

        ~InputEventMonitor () {
            Dispose();
        }
    }
}
