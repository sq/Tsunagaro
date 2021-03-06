﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Linq;
using Squared.Task;

namespace Tsunagaro {
    static class Program {
        public static readonly ClipboardMonitorJobQueue MainThreadJobQueue  = new ClipboardMonitorJobQueue();
        public static readonly TaskScheduler            MainThreadScheduler = new TaskScheduler(() => MainThreadJobQueue);

        public static readonly TaskScheduler            Scheduler    = new TaskScheduler(JobQueue.ThreadSafe);

        public static readonly ControlService           Control       = new ControlService(Scheduler);
        public static readonly DiscoveryService         Discovery     = new DiscoveryService(Scheduler);
        public static readonly ClipboardService         Clipboard     = new ClipboardService(Scheduler);
        public static readonly PeerService              Peer          = new PeerService(Scheduler);
        public static readonly InputMonitorService      InputMonitor  = new InputMonitorService(Scheduler);
        public static readonly InputPlaybackService     InputPlayback = new InputPlaybackService(Scheduler);

        public static readonly MemoryStream StdOut          = new MemoryStream();
        public static readonly Thread       SchedulerThread = new Thread(SchedulerThreadMain) {
            Priority = ThreadPriority.AboveNormal,
            IsBackground = true
        };

        private static Action<string, bool> DoShowFeedback;

        [STAThread]
        static void Main () {
            var enc = new UTF8Encoding(false);
            var tw = new StreamWriter(StdOut, enc);
            tw.AutoFlush = true;

            Console.SetOut(tw);
            Console.SetError(tw);

            Scheduler.ErrorHandler = MainThreadScheduler.ErrorHandler = OnTaskError;

            SchedulerThread.Start();
            UIMain();
        }

        [STAThread]
        private static void SchedulerThreadMain () {
            while (true) {
                Scheduler.WaitForWorkItems(1);
                Scheduler.Step();
            }
        }

        private static bool OnTaskError (Exception exc) {
            Console.WriteLine("Unhandled error in background task: {0}", exc);

            // FIXME: More serious handling? We want to be resilient so that we don't crash constantly
            return true;
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

                DoShowFeedback = (text, balloon) => {
                    if (!String.IsNullOrWhiteSpace(text)) {
                        trayIcon.Text = "Tsunagaro - " + text;
                    } else {
                        trayIcon.Text = "Tsunagaro";
                    }

                    if (balloon) {
                        trayIcon.BalloonTipText = text;
                        trayIcon.ShowBalloonTip(1000 + (text.Length * 50));
                    } else if (trayIcon.BalloonTipText != "") {
                        trayIcon.BalloonTipText = "";
                        trayIcon.Visible = false;
                        trayIcon.Visible = true;
                    }
                };

                MainThreadScheduler.Start(
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
            bool BroadcastingInput = System.Net.Dns.GetHostName().ToLowerInvariant().Trim() != "eir";
            bool SyncingInput = false;

            var tasks = new[] {
                Control.Initialize(),
                Discovery.Initialize(),
                Clipboard.Initialize(),
                Peer.Initialize(),
                SyncingInput
                    ? (
                        BroadcastingInput 
                            ? InputMonitor.Initialize()
                            : InputPlayback.Initialize()
                    )
                    : null
            };

            yield return Future.WaitForAll(
                from t in tasks where t != null select Scheduler.Start(t)
            );

            ready();
        }

        public static void Feedback (string text, bool balloon) {
            if (!balloon)
                Console.WriteLine(text);

            MainThreadScheduler.QueueWorkItem(() => DoShowFeedback(text, balloon));
        }
    }
}
