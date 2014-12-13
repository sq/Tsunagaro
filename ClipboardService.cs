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

        private static SignalFuture RunOnMainThread (Action fn) {
            var result = new SignalFuture();

            Program.MainThreadJobQueue.QueueWorkItem(() => {
                try {
                    fn();
                    result.Complete();
                } catch (Exception exc) {
                    result.Fail(exc);
                }
            });

            return result;
        }

        private static Future<T> RunOnMainThread<T> (Func<T> fn) {
            var result = new Future<T>();

            Program.MainThreadJobQueue.QueueWorkItem(() => {
                try {
                    var value = fn();
                    result.Complete(value);
                } catch (Exception exc) {
                    result.Fail(exc);
                }
            });

            return result;
        }

        private static Future<string[]> GetFormats () {
            return RunOnMainThread(() => Clipboard.GetDataObject().GetFormats());
        }

        private static Future<bool> GetDataPresent (string format, bool autoConvert) {
            return RunOnMainThread(() => Clipboard.GetDataObject().GetDataPresent(format, autoConvert));
        }

        private static Future<object> GetData (string format, bool autoConvert) {
            return RunOnMainThread(() => Clipboard.GetDataObject().GetData(format, autoConvert));
        }

        private static IFuture SetClipboardData (IDataObject obj) {
            return RunOnMainThread(
                () => Clipboard.SetDataObject(obj, false, 5, 15)
            );
        }

        private IEnumerator<object> OnClipboardChanged (PeerService.Connection sender, Dictionary<string, object> message) {
            var formatsRaw = (JArray)message["Formats"];
            var formats = (from v in formatsRaw.Children() select v.Value<string>())
                .Concat(new[] { ClipboardDataProxy.SentinelFormat })
                .ToArray();

            yield return PlaceProxyClipboard(sender, formats);

            Program.Feedback(sender.HostName + " owns the clipboard", false);
        }

        private IEnumerator<object> OnClipboardGetDataPresent (PeerService.Connection sender, Dictionary<string, object> message) {
            var format = Convert.ToString(message["Format"]);
            var fResult = GetDataPresent(format, true);
            yield return fResult;

            yield return new Result(fResult.Result);
        }

        private IEnumerator<object> ProcessClipboardChange () {
            var fSentinelPresent = GetDataPresent(ClipboardDataProxy.SentinelFormat, true);
            yield return fSentinelPresent;

            if (fSentinelPresent.Result) {
            } else {
                var fFormats = GetFormats();
                yield return fFormats;

                var payload = new Dictionary<string, object> {
                    {"Owner", Program.Control.URL},
                    {"Formats", fFormats.Result}
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

        public IFuture PlaceProxyClipboard (PeerService.Connection owner, string[] formats) {
            var proxy = new ClipboardDataProxy(owner, formats);

            return SetClipboardData(proxy);
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

            var fFormats = GetFormats();
            var fSentinelData = GetData(ClipboardDataProxy.SentinelFormat, true);

            yield return fFormats;
            yield return fSentinelData;

            var html = String.Format(
                @"<html>
    <head>
        <title>Clipboard on {0}</title>
        <meta charset=""UTF-8"">
        <meta http-equiv=""refresh"" content=""10"">
    </head>
    <body>
        <h2>Status</h2>
        {1}
        <h2>Formats</h2>
        <pre>
{2}
        </pre>
    </body>
</html>",
                System.Net.Dns.GetHostName(),
                (fSentinelData.Result != null)
                    ? "Remote data from " + (string)fSentinelData.Result
                    : "Local data",
                String.Join(
                    "<br>",
                    (
                        from fmt in fFormats.Result 
                        select String.Format(
                            "{0} <a href=\"/clipboard/data?format={1}\">View</a>",
                            fmt, HttpUtility.UrlEncode(fmt)
                        )
                    )
                )
            );
            
            yield return ControlService.WriteResponseBody(request, html);
        }

        public IEnumerator<object> ServeClipboardData (HttpServer.Request request) {
            var format = request.QueryString["format"];

            Stream resultStream = null;

            var fData = GetData(format, true);
            yield return fData;

            var data = fData.Result;
            if (data is string) {
                var s = (string)data;
                var bytes = (new UTF8Encoding(false)).GetBytes(s);
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
