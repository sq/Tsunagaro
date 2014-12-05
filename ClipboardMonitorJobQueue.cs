using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Squared.Task;

namespace Tsunagaro {
    public class ClipboardMonitorJobQueue : WindowsMessageJobQueue {
        [DllImport("user32.dll", SetLastError=true, CallingConvention=CallingConvention.Winapi)]
        private static extern int AddClipboardFormatListener (IntPtr hWnd);

        public event EventHandler ClipboardChanged;

        const int WM_CLIPBOARDUPDATE = 0x031D;

        public override void CreateHandle (System.Windows.Forms.CreateParams cp) {
            base.CreateHandle(cp);

            AddClipboardFormatListener(Handle);
        }

        protected override void WndProc (ref System.Windows.Forms.Message m) {
            if (m.Msg == WM_CLIPBOARDUPDATE) {
                if (ClipboardChanged != null)
                    ClipboardChanged(this, EventArgs.Empty);
            } else {
                base.WndProc(ref m);
            }
        }
    }
}
