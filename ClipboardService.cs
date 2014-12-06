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
            Program.MainThreadJobQueue.ClipboardChanged += (s, e) =>
                ClipboardChangedSignal.Set();

            Scheduler.Start(ListenTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Program.Control.Handlers.Add("/clipboard", ServeClipboard);
            Program.Control.Handlers.Add("/clipboard/data", ServeClipboardData);

            Program.Peer.MessageHandlers.Add("ClipboardChanged", OnClipboardChanged);
            Program.Peer.MessageHandlers.Add("ClipboardGetDataPresent", OnClipboardGetDataPresent);

            yield break;
        }

        private Future<DataObject> GetClipboardData () {
            var result = new Future<DataObject>();

            Program.MainThreadJobQueue.QueueWorkItem(() => {
                result.Complete(Clipboard.GetDataObject());
            });

            return result;
        }

        private IFuture SetClipboardData (IDataObject obj) {
            var result = new SignalFuture();

            Program.MainThreadJobQueue.QueueWorkItem(() => {
                Clipboard.SetDataObject(obj);
                result.Complete();
            });

            return result;
        }

        private IEnumerator<object> OnClipboardChanged (PeerService.Connection sender, Dictionary<string, object> message) {
            var formatsRaw = (JArray)message["Formats"];
            var formats = (from v in formatsRaw.Children() select v.Value<string>())
                .Concat(new[] { ClipboardDataProxy.SentinelFormat })
                .ToArray();

            var proxy = PlaceProxyClipboard(sender, formats);

            Program.Feedback(sender.HostName + " owns the clipboard", false);

            yield break;
        }

        private IEnumerator<object> OnClipboardGetDataPresent (PeerService.Connection sender, Dictionary<string, object> message) {
            var fClipboardData = GetClipboardData();
            yield return fClipboardData;
            var clipboardData = fClipboardData.Result;

            var format = Convert.ToString(message["Format"]);

            yield return new Result(
                clipboardData.GetDataPresent(format, true)
            );
        }

        private IEnumerator<object> ProcessClipboardChange () {
            var fClipboardData = GetClipboardData();
            yield return fClipboardData;
            var clipboardData = fClipboardData.Result;

            if (clipboardData.GetDataPresent(ClipboardDataProxy.SentinelFormat)) {
            } else {
                var payload = new Dictionary<string, object> {
                    {"Owner", Program.Control.URL},
                    {"Formats", clipboardData.GetFormats()}
                };

                Program.Feedback("I own the clipboard", false);

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
            request.Response.ContentType = "text/html; charset=utf-8";

            var fClipboardData = GetClipboardData();
            yield return fClipboardData;
            var clipboardData = fClipboardData.Result;

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

        public IEnumerator<object> ServeClipboardData (HttpServer.Request request) {
            var fClipboardData = GetClipboardData();
            yield return fClipboardData;
            var clipboardData = fClipboardData.Result;

            var format = request.QueryString["format"];

            Stream resultStream = null;

            var data = clipboardData.GetData(format);
            if (data is string) {
                var s = (string)data;
                var bytes = Encoding.UTF8.GetBytes(s);
                request.Response.ContentType = "text/plain; charset=utf-8";

                resultStream = new MemoryStream(bytes);
            } else if (data is Stream) {
                request.Response.ContentType = "application/octet-stream";

                resultStream = (Stream)data;
            } else {
                yield return ControlService.ServeError(request, 501, "Could not retrieve data in the requested format");
                yield break;
            }

            // If supported, gzip-compress the data.
            Header header;
            if (request.Headers.TryGetValue("Accept-Encoding", out header)) {
                if (header.Value.ToLower().Contains("gzip")) {
                    request.Response.Headers.Add(new Header("Content-Encoding", "gzip"));

                    var uncompressedStream = resultStream;
                    resultStream = new MemoryStream();

                    using (uncompressedStream)
                    using (var gzipStream = new GZipOutputStream(resultStream) {
                        IsStreamOwner = false
                    }) {
                        uncompressedStream.CopyTo(gzipStream, 1024 * 16);
                    }

                    resultStream.Seek(0, SeekOrigin.Begin);
                }
            }

            request.Response.ContentLength = (int)resultStream.Length;
            using (resultStream)
                yield return ControlService.WriteResponseBody(request, resultStream);            
        }
    }
}
