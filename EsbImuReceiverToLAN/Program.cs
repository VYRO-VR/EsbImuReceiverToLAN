using System.Windows.Forms;

namespace EspImuReceiverToLAN {
    internal static class Program {
        [STAThread]
        static void Main() {
            // Prevent multiple tray instances fighting over the same dongle.
            using var single = new Mutex(true, "EsbImuReceiverToLAN.SingleInstance", out bool createdNew);
            if (!createdNew) {
                MessageBox.Show("ESB IMU Receiver is already running (check the system tray).",
                    "ESB IMU Receiver", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext());
        }
    }
}
