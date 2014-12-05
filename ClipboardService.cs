using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using Squared.Task;
using Squared.Task.IO;
using System.Diagnostics;

namespace Tsunagaro {
    public class ClipboardService {
        public readonly TaskScheduler Scheduler;
        private readonly Signal ClipboardChangedSignal = new Signal();

        public ClipboardService (TaskScheduler scheduler) {
            Scheduler = scheduler;

            Program.JobQueue.ClipboardChanged += (s, e) =>
                ClipboardChangedSignal.Set();
        }

        public IEnumerator<object> Initialize () {
            Scheduler.Start(ListenTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            yield break;
        }

        public IEnumerator<object> ListenTask () {
            while (true) {
                yield return ClipboardChangedSignal.Wait();

                Console.WriteLine("Clipboard changed");
            }
        }
    }
}
