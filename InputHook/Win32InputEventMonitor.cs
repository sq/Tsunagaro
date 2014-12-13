using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Threading;
using Squared.Task;
using Squared.Util;

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

        private readonly AutoResetEvent            DataReady = new AutoResetEvent(false);
        private readonly UnorderedList<InputEvent> Buffer = new UnorderedList<InputEvent>();
        private readonly Thread                    Thread;

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

            lock (Buffer)
                Buffer.Add(evt);

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

            lock (Buffer)
                Buffer.Add(evt);

            DataReady.Set();

            return CallNextHookEx(HookMouse, nCode, wParam, lParam);
        }

        public Future<int> ReadEvents (Win32.InputEvent[] buffer, int offset, int maxCount) {
            return Future.RunInThread(() => {
                DataReady.WaitOne();

                int count;

                lock (Buffer) {
                    count = Math.Min(Buffer.Count, maxCount);
                    Array.Copy(Buffer.GetBuffer(), 0, buffer, offset, count);
                    // FIXME: Does this reshuffle stuff?
                    Buffer.RemoveRange(0, count);
                }

                return count;
            });
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            UnhookWindowsHookEx(HookKeyboard);
            UnhookWindowsHookEx(HookMouse);
        }

        ~InputEventMonitor () {
            Dispose();
        }
    }
}
