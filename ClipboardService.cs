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

namespace Tsunagaro {
    public class ClipboardService {
        public readonly TaskScheduler Scheduler;
        private readonly Signal ClipboardChangedSignal = new Signal();

        private static readonly HashSet<string> BlacklistedFormats = new HashSet<string> {
            "FileContents",
            "FileDrop",
            "FileName",
            "FileNameW",
            "DeviceIndependentBitmap",
            "PaintDotNet.Rendering.MaskedSurface",
            "Bitmap",
            "System.Drawing.Bitmap",
            "PNG",
            "Format17" // CF_DIBV5. WTF?
        };

        public ClipboardService (TaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public IEnumerator<object> Initialize () {
            Program.JobQueue.ClipboardChanged += (s, e) =>
                ClipboardChangedSignal.Set();

            Scheduler.Start(ListenTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Program.Control.Handlers.Add("/clipboard", ServeClipboard);

            Program.Peer.MessageHandlers.Add("ClipboardChanged", OnClipboardChanged);
            Program.Peer.MessageHandlers.Add("ClipboardGetDataPresent", OnClipboardGetDataPresent);

            yield break;
        }

        private IEnumerator<object> OnClipboardChanged (PeerService.Connection sender, Dictionary<string, object> message) {
            var formatsRaw = (JArray)message["Formats"];
            var formats = (from v in formatsRaw.Children() select v.Value<string>())
                .Concat(new[] { ClipboardDataProxy.SentinelFormat })
                .ToArray();

            var proxy = PlaceProxyClipboard(sender, formats);

            Program.Feedback(sender.HostName + " owns clipboard");

            yield break;
        }

        private IEnumerator<object> OnClipboardGetDataPresent (PeerService.Connection sender, Dictionary<string, object> message) {
            var clipboardData = Clipboard.GetDataObject();

            var format = Convert.ToString(message["Format"]);

            yield return new Result(
                clipboardData.GetDataPresent(format, true)
            );
        }

        private IEnumerator<object> ProcessClipboardChange () {
            var clipboardData = Clipboard.GetDataObject();
            if (clipboardData.GetDataPresent(ClipboardDataProxy.SentinelFormat)) {
            } else {
                Console.WriteLine("Broadcasting new clipboard contents");

                var payload = new Dictionary<string, object> {
                    {"Owner", Program.Control.URL},
                    {"Formats", clipboardData.GetFormats()}
                };

                yield return Program.Peer.Broadcast("ClipboardChanged", payload);
            }
        }

        public IEnumerator<object> ListenTask () {
            while (true) {
                yield return ClipboardChangedSignal.Wait();

                yield return Scheduler.Start(ProcessClipboardChange(), TaskExecutionPolicy.RunAsBackgroundTask);
            }
        }

        public ClipboardDataProxy PlaceProxyClipboard (PeerService.Connection owner, string[] formats) {
            var result = new ClipboardDataProxy(owner, formats);

            Clipboard.SetDataObject(result, false, 5, 15);

            return result;
        }

        private static string GenerateClipboardDataInfo (IDataObject data, string format) {
            if (BlacklistedFormats.Contains(format))
                return format;

            try {
                var dataObject = data.GetData(format);

                var text = dataObject as string;
                if (text != null)
                    return String.Format("{0} ({1} character(s))", format, text.Length);

                var stream = dataObject as Stream;
                if (stream != null) {
                    using (stream)
                        return String.Format("{0} ({1} byte(s))", format, stream.Length);
                }

                return string.Format("{0} (unknown size)", format);                
            } catch {
                return String.Format("{0} (unsupported format)", format);
            }
        }

        public IEnumerator<object> ServeClipboard (HttpServer.Request request) {
            request.Response.ContentType = "text/html";

            var clipboardData = Clipboard.GetDataObject();

            var html = String.Format(
                @"<html>
    <head>
        <title>Clipboard</title>
        <meta charset=""UTF-8"">
        <meta http-equiv=""refresh"" content=""10"">
    </head>
    <body>
        <h2>Status</h2>
        {0}
        <h2>Formats</h2>
        <pre>
{1}
        </pre>
    </body>
</html>",
                clipboardData.GetDataPresent(ClipboardDataProxy.SentinelFormat)
                    ? "Remote data from " + (string)clipboardData.GetData(ClipboardDataProxy.SentinelFormat)
                    : "Local data",
                String.Join(
                    "<br>",
                    (
                        from cf in clipboardData.GetFormats() 
                        select HttpUtility.HtmlEncode(GenerateClipboardDataInfo(clipboardData, cf))
                    )
                )
            );
            
            yield return ControlService.WriteResponseBody(request, html);
        }
    }
}
