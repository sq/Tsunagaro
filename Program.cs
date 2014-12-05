using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Squared.Task;

namespace Tsunagaro {
    static class Program {
        public static readonly TaskScheduler Scheduler = new TaskScheduler(JobQueue.WindowsMessageBased);

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
                Scheduler.Start(
                    StartupTask(
                        () => trayIcon.Visible = true
                    ), 
                    TaskExecutionPolicy.RunAsBackgroundTask
                );

                Application.Run();
            }
        }

        public static IEnumerator<object> StartupTask (Action ready) {
            ready();

            yield break;
        }
    }
}
