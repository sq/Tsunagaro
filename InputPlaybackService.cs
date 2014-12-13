using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Squared.Task;
using Squared.Task.IO;
using Squared.Task.Http;
using System.Windows.Forms;
using System.Web;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Linq;
using ICSharpCode.SharpZipLib.GZip;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;

namespace Tsunagaro {
    public class InputPlaybackService {
        [DllImport("user32.dll")]
        private static extern void keybd_event (byte bVk, byte bScan, KeyEventF dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void mouse_event (MouseEventF dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [Flags]
        enum MouseEventF : uint {
            ABSOLUTE = 0x8000,
            LEFTDOWN = 0x0002,
            LEFTUP = 0x0004,
            MIDDLEDOWN = 0x0020,
            MIDDLEUP = 0x0040,
            MOVE = 0x0001,
            RIGHTDOWN = 0x0008,
            RIGHTUP = 0x0010,
            XDOWN = 0x0080,
            XUP = 0x0100,
            WHEEL = 0x0800,
            HWHEEL = 0x01000,
        }

        enum WindowsMessage : int {
            KEYDOWN = 0x0100,
            KEYUP = 0x0101,
            SYSKEYDOWN = 0x0104,
            SYSKEYUP = 0x0105,

            LBUTTONDOWN = 0x0201,
            LBUTTONUP = 0x0202,
            MOUSEMOVE = 0x0200, 
            MOUSEWHEEL = 0x020A, 
            MOUSEHWHEEL = 0x020E, 
            RBUTTONDOWN = 0x0204, 
            RBUTTONUP = 0x0205
        }

        [Flags]
        enum KeyEventF : uint {
            KEYUP = 0x0002
        }

        public readonly TaskScheduler Scheduler;

        public InputPlaybackService (TaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public IEnumerator<object> Initialize () {
            // Scheduler.Start(MainTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Program.Peer.MessageHandlers.Add("RemoteInput", OnRemoteInput);

            // Program.Control.Handlers.Add("/input", ServeInput);

            yield break;
        }

        public IEnumerator<object> OnRemoteInput (PeerService.Connection sender, Dictionary<string, object> message) {
            var eventsBase64 = Convert.ToString(message["Events"]);
            var eventBytes = Convert.FromBase64String(eventsBase64);

            yield return Future.RunInThread(() => PlaybackEventBytes(eventBytes));
        }

        private static unsafe void PlaybackEventBytes (byte[] eventBytes) {
            int MessageSize = Marshal.SizeOf(typeof(Win32.InputEvent));

            int numEvents = eventBytes.Length / MessageSize;
            fixed (byte* pEventBytes = eventBytes) {
                var pEvents = (Win32.InputEvent *)pEventBytes;

                for (int i = 0; i < numEvents; i++) {
                    var evt = pEvents[i];

                    var msg = (WindowsMessage)evt.Message;

                    switch (msg) {
                        case WindowsMessage.KEYDOWN:
                        case WindowsMessage.KEYUP:
                            keybd_event(
                                (byte)evt.Keyboard.Virtual,
                                (byte)evt.Keyboard.Scan,
                                (msg == WindowsMessage.KEYUP)
                                    ? KeyEventF.KEYUP
                                    : default(KeyEventF),
                                UIntPtr.Zero
                            );
                            break;
                        default:
                            Debug.WriteLine(msg.ToString());
                            break;
                    }
                }
            }
        }
    }
}
