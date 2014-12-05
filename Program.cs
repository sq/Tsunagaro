using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Squared.Task;

namespace Tsunagaro {
    static class Program {
        public static readonly TaskScheduler Scheduler = new TaskScheduler(JobQueue.WindowsMessageBased);
        public static readonly ControlService Control = new ControlService(Scheduler);
        public static readonly DiscoveryService Discovery = new DiscoveryService(Scheduler);

        [MTAThread]
        static void Main () {
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
