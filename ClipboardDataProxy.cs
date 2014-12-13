using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using Squared.Task;

namespace Tsunagaro {
    public class ClipboardDataProxy : IDataObject {
        public static readonly HashSet<string> TextFormats = new HashSet<string> {
            "Text", "UnicodeText", "System.String"
        };

        public static readonly string SentinelFormat = "Tsunagaro.ClipboardDataProxy";

        public static readonly int TimeoutSeconds = 45;
        public const int LargeDataThreshold = 8 * 1024;

        static ClipboardDataProxy () {
            // Force our format to be registered
            DataFormats.GetFormat(SentinelFormat);
        }

        public readonly PeerService.Connection Owner;
        public readonly string[] Formats;

        public ClipboardDataProxy (PeerService.Connection owner, string[] formats) {
            if (owner == null)
                throw new ArgumentNullException("owner");
            if (formats == null)
                throw new ArgumentNullException("formats");

            Owner = owner;
            Formats = formats;
        }

        private WebClient MakeClient () {
            var result = new LongTimeoutWebClient();
            result.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

            var lastPercentage = new int[] { -999 };

            result.DownloadProgressChanged += (s, e) => {
                if (e.TotalBytesToReceive >= LargeDataThreshold) {
                    if (Math.Abs(lastPercentage[0] - e.ProgressPercentage) < 5)
                        return;

                    lastPercentage[0] = e.ProgressPercentage;
                    Program.Feedback(String.Format("Transferring clipboard: {0}%", e.ProgressPercentage), true);
                }
            };

            result.Encoding = new UTF8Encoding(false);

            return result;
        }

        private Uri MakeUri (string format) {
            return new Uri(
                "http://" + Owner.RemoteEndPoint + "/clipboard/data?format=" + HttpUtility.UrlEncode(format), true
            );
        }

        private string FetchText (string format) {
            using (var wc = MakeClient()) {
                var result = wc.DownloadString(MakeUri(format));
                return result;
            }
        }

        private object FetchData (string format) {
            using (var wc = MakeClient()) {
                var result = wc.DownloadData(MakeUri(format));

                return new MemoryStream(
                    result,
                    false
                );
            }
        }

        public object GetData (string format, bool autoConvert) {
            if (format == SentinelFormat)
                return Owner.HostName;
            else if (TextFormats.Contains(format))
                return FetchText(format);
            else
                return FetchData(format);
        }

        public bool GetDataPresent (string format, bool autoConvert) {
            if (format == SentinelFormat)
                return true;
            else {
                var fGetDataPresent = Owner.SendMessage<bool>(
                    "ClipboardGetDataPresent", new Dictionary<string, object> {
                        {"Format", format}
                    }
                );

                fGetDataPresent.GetCompletionEvent().Wait(TimeoutSeconds * 1000);
                return fGetDataPresent.Result;
            }
        }

        public string[] GetFormats (bool autoConvert) {
            return Formats;
        }

        // The rest of the overloads are just forwarders.

        public object GetData (Type format) {
            if (format != null)
                return GetData(format.FullName, true);
            else
                throw new ArgumentNullException("format");
        }

        public object GetData (string format) {
            return GetData(format, true);
        }

        public bool GetDataPresent (Type format) {
            if (format != null)
                return this.GetDataPresent(format.FullName, true);
            else
                return false;
        }

        public bool GetDataPresent (string format) {
            return this.GetDataPresent(format, true);
        }

        public string[] GetFormats () {
            return GetFormats(true);
        }

        // WinForms does not actually implement forwarding for these, they always fail in the internals.
        // So we don't have any reason to implement them.

        public void SetData (object data) {
            throw new NotImplementedException();
        }

        public void SetData (Type format, object data) {
            throw new NotImplementedException();
        }

        public void SetData (string format, object data) {
            throw new NotImplementedException();
        }

        public void SetData (string format, bool autoConvert, object data) {
            throw new NotImplementedException();
        }
    }
    
    class LongTimeoutWebClient : WebClient {
        protected override WebRequest GetWebRequest (Uri address) {
            var result = base.GetWebRequest(address);
            result.Timeout = 1000 * ClipboardDataProxy.TimeoutSeconds;

            var hwr = result as HttpWebRequest;
            if (hwr != null) {
                hwr.AutomaticDecompression = DecompressionMethods.GZip;
            }

            return result;
        }
    }
}
