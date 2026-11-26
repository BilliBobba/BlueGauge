using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BlueGauge
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            if (args.Contains("--en")) L.Russian = false;
            if (args.Contains("--ru")) L.Russian = true;

            if (args.Length >= 2 && args[0] == "--snapshot")
            {
                RunSnapshot(args[1]);
                return;
            }

            if (args.Length >= 2 && args[0] == "--taskbar-diagnostic")
            {
                string result;
                try
                {
                    var slot = System.Threading.Tasks.Task.Run(TaskbarPlacement.Probe).GetAwaiter().GetResult();
                    result = slot.HasValue ? $"Free slot: {slot.Value.Free}; DPI: {slot.Value.Dpi}" : "No usable slot was found.";
                }
                catch (Exception ex) { result = ex.ToString(); Environment.ExitCode = 1; }
                File.WriteAllText(Path.GetFullPath(args[1]), result);
                return;
            }

            bool demo = args.Contains("--demo");
            string instance = (demo ? @"Local\BlueGauge.Demo." : @"Local\BlueGauge.Live.")
                + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            using (var single = new Mutex(true, instance, out bool owns))
            {
                if (!owns)
                {
                    if (!args.Contains("--background")) MessageBox.Show(L.T("BlueGauge is already running. Open it from the system tray.",
                        "BlueGauge уже запущен. Откройте его из системного трея."), "BlueGauge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (var app = new TrayApplication(demo, args.Contains("--background"))) Application.Run(app);
            }
        }

        private static void RunSnapshot(string file)
        {
            using (var window = new Dashboard(true))
            {
                window.UpdateDevices(new DemoBatterySource().ReadAsync(CancellationToken.None).GetAwaiter().GetResult(),
                    new DateTime(2026, 1, 1, 14, 5, 0), 20, null);
                window.Shown += (s, e) => window.BeginInvoke((Action)(() =>
                {
                    using (var image = new Bitmap(window.Width, window.Height))
                    {
                        window.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                        image.Save(Path.GetFullPath(file), ImageFormat.Png);
                    }
                    window.AllowClose = true;
                    window.Close();
                }));
                Application.Run(window);
            }
        }
    }
}
