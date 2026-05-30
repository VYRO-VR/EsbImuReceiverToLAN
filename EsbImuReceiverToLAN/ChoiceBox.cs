using System.Drawing;
using System.Windows.Forms;

namespace EspImuReceiverToLAN {
    /// <summary>Minimal modal single-choice dialog (a radio-button list with OK/Cancel).</summary>
    internal static class ChoiceBox {
        /// <summary>Returns the index of the chosen option, or -1 if cancelled.</summary>
        public static int Show(string prompt, string title, string[] options) {
            using var form = new Form {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = true,
                TopMost = true,
                ClientSize = new Size(380, 120 + options.Length * 28)
            };

            var label = new Label {
                Text = prompt,
                AutoSize = false,
                Location = new Point(12, 12),
                Size = new Size(356, 44)
            };
            form.Controls.Add(label);

            var radios = new List<RadioButton>();
            int y = 60;
            for (int i = 0; i < options.Length; i++) {
                var rb = new RadioButton {
                    Text = options[i],
                    Location = new Point(20, y),
                    Size = new Size(340, 24),
                    Checked = i == 0
                };
                form.Controls.Add(rb);
                radios.Add(rb);
                y += 28;
            }

            var ok = new Button {
                Text = "Connect",
                DialogResult = DialogResult.OK,
                Location = new Point(200, y + 8),
                Size = new Size(80, 28)
            };
            var cancel = new Button {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(286, y + 8),
                Size = new Size(80, 28)
            };
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            if (form.ShowDialog() != DialogResult.OK)
                return -1;
            for (int i = 0; i < radios.Count; i++)
                if (radios[i].Checked) return i;
            return -1;
        }
    }
}
