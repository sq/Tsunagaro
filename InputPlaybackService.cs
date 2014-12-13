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
        private static extern bool keybd_event (byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

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

        private enum WindowsMessages : int {
            WM_KEYDOWN = 0x0100,
            WM_KEYUP = 0x0101
        }

        const int KEYEVENTF_KEYUP = 0x0002;

        private static unsafe void PlaybackEventBytes (byte[] eventBytes) {
            int MessageSize = Marshal.SizeOf(typeof(Win32.InputEvent));

            int numEvents = eventBytes.Length / MessageSize;
            fixed (byte* pEventBytes = eventBytes) {
                var pEvents = (Win32.InputEvent *)pEventBytes;

                for (int i = 0; i < numEvents; i++) {
                    var evt = pEvents[i];

                    if (evt.Type == Win32.InputEventType.Keyboard) {
                        var msg = (WindowsMessages)evt.Message;

                        switch (msg) {
                            case WindowsMessages.WM_KEYDOWN:
                                keybd_event(
                                    (byte)evt.Keyboard.Virtual,
                                    (byte)evt.Keyboard.Scan,
                                    0,
                                    UIntPtr.Zero
                                );
                                break;
                            case WindowsMessages.WM_KEYUP:
                                keybd_event(
                                    (byte)evt.Keyboard.Virtual,
                                    (byte)evt.Keyboard.Scan,
                                    KEYEVENTF_KEYUP,
                                    UIntPtr.Zero
                                );
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
        }
    }
}
