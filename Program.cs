using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Squared.Task;

namespace Tsunagaro {
    static class Program {
        public static readonly ClipboardMonitorJobQueue JobQueue = new ClipboardMonitorJobQueue();
        public static readonly TaskScheduler Scheduler = new TaskScheduler(() => JobQueue);
        public static readonly ControlService Control = new ControlService(Scheduler);
        public static readonly DiscoveryService Discovery = new DiscoveryService(Scheduler);

        public static readonly MemoryStream StdOut = new MemoryStream();

        [STAThread]
        static void Main () {
            var enc = new UTF8Encoding(false);
            var tw = new StreamWriter(StdOut, enc);
            tw.AutoFlush = true;

            Console.SetOut(tw);
            Console.SetError(tw);

            UIMain();
        }

        private static void UIMain () {
            using (var trayContextMenu = new ContextMenuStrip {
                Items = {
                    {"E&xit", null, (s, e) => Application.Exit()}
                }
            }) 
            using (var trayIcon = new NotifyIcon {
                ContextMenuStrip = trayContextMenu,
                Text = "Tsunagaro",
                Icon = Tsunagaro.Properties.Resources.TrayIcon
            }) {
                trayIcon.DoubleClick += OpenBrowser;

                Scheduler.Start(
                    StartupTask(
                        () => trayIcon.Visible = true
                    ), 
                    TaskExecutionPolicy.RunAsBackgroundTask
                );

                Application.Run();
            }
        }

        private static void OpenBrowser (object sender, EventArgs e) {
            Process.Start(Control.URL);
        }

        public static IEnumerator<object> StartupTask (Action ready) {
            var controlStartup = Scheduler.Start(Control.Initialize());
            var discoveryStartup = Scheduler.Start(Discovery.Initialize());

            yield return controlStartup;
            yield return discoveryStartup;

            ready();
        }
    }
}
