using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tsunagaro {
    public class ClipboardDataProxy : IDataObject {
        public static readonly string SentinelFormat = "Tsunagaro.ClipboardDataProxy";

        public static readonly double? TimeoutSeconds = 1;

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

        public object GetData (string format, bool autoConvert) {
            Console.WriteLine("GetData('{0}', autoConvert={1})", format, autoConvert);

            if (format == SentinelFormat)
                return Owner.HostName;
            else if (Formats.Contains(format))
                return "Synthesized Text";
            else
                return null;
        }

        public bool GetDataPresent (string format, bool autoConvert) {
            if (format == SentinelFormat)
                return true;
            else {
                Console.WriteLine("GetDataPresent('{0}', autoConvert={1})", format, autoConvert);

                var fGetDataPresent = Owner.SendMessage<bool>(
                    "ClipboardGetDataPresent", new Dictionary<string, object> {
                        {"Format", format}
                    }
                );

                var result = Program.Scheduler.WaitFor(fGetDataPresent, TimeoutSeconds);

                return result;
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
}
