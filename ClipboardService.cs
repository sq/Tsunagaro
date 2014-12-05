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

            Program.JobQueue.ClipboardChanged += (s, e) =>
                ClipboardChangedSignal.Set();
        }

        public IEnumerator<object> Initialize () {
            Scheduler.Start(ListenTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Program.Control.Handlers.Add("/clipboard", ServeClipboard);

            yield break;
        }

        public IEnumerator<object> ListenTask () {
            while (true) {
                yield return ClipboardChangedSignal.Wait();

                Console.WriteLine("Clipboard changed");
            }
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
        <h2>Clipboard Formats</h2>
        <pre>
{0}
        </pre>
    </body>
</html>",
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
