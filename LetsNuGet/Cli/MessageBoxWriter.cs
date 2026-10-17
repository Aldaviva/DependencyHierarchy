using System.IO;
using System.Windows.Forms;

namespace LetsNuGet.Cli;

internal class MessageBoxWriter(string title, MessageBoxIcon icon = MessageBoxIcon.None): StringWriter {

    private volatile bool disposed;

    protected override void Dispose(bool disposing) {
        if (disposing && !disposed) {
            disposed = true;
            string message = ToString();
            if (message.HasText) {
                MessageBox.Show(message, title, MessageBoxButtons.OK, icon);
            }
        }
        base.Dispose(disposing);
    }

}