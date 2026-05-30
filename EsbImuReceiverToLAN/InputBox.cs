using System.Drawing;
using System.Windows.Forms;

namespace EspImuReceiverToLAN {
    /// <summary>Minimal modal text-prompt dialog (WinForms has no built-in InputBox).</summary>
    internal static class InputBox {
        /// <summary>Returns the entered text, or null if cancelled.</summary>
        public static string? Show(string prompt, string title, string defaultValue = "") {
            using var form = new Form {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(360, 150),
                ShowInTaskbar = false
            };

            var label = new Label {
                Text = prompt,
                AutoSize = false,
                Location = new Point(12, 12),
                Size = new Size(336, 50)
            };

            var textBox = new TextBox {
                Text = defaultValue,
                Location = new Point(12, 70),
                Size = new Size(336, 23)
            };

            var ok = new Button {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(192, 110),
                Size = new Size(75, 28)
            };

            var cancel = new Button {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(273, 110),
                Size = new Size(75, 28)
            };

            form.Controls.Add(label);
            form.Controls.Add(textBox);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            return form.ShowDialog() == DialogResult.OK ? textBox.Text : null;
        }
    }
}
