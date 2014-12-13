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

namespace Tsunagaro {
    public class InputMonitorService {
        public readonly TaskScheduler Scheduler;
        public readonly Win32.InputEventMonitor Monitor;

        public InputMonitorService (TaskScheduler scheduler) {
            Scheduler = scheduler;

            Monitor = new Win32.InputEventMonitor();
        }

        public IEnumerator<object> Initialize () {
            Scheduler.Start(MonitorTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            // Program.Control.Handlers.Add("/input", ServeInput);

            Monitor.Start();

            yield break;
        }

        public IEnumerator<object> MonitorTask () {
            while (true) {
                Win32.InputEvent evt;
                while (Monitor.TryRead(out evt)) {
                    if (evt.Type == Win32.InputEventType.Keyboard) {
                        Debug.WriteLine(String.Format("Keyboard {0} {1}", evt.Message, evt.Keyboard));
                    } else if (evt.Type == Win32.InputEventType.Mouse) {
                        Debug.WriteLine(String.Format("Mouse    {0} {1}", evt.Message, evt.Mouse));
                    }
                }

                yield return Monitor.DataReady.Wait();
            }
        }
    }
}
