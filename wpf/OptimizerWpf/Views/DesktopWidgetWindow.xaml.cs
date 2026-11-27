using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OptimizerWpf.Services;

namespace OptimizerWpf.Views
{
    public partial class DesktopWidgetWindow : Window
    {
        private IDisposable? _gpuSubscription;
        private bool _gpuReading;
        private long _lastOverlayAttempt;
        private readonly DispatcherTimer _timer;
        private int _currentGamePid = -1;
        private bool _voltageQueryInFlight;
        private bool _closed;
        private long _lastNetworkCheck;
        private long _lastVoltageCheck;

        public DesktopWidgetWindow()
        {
            InitializeComponent();
            ThemeManager.AttachWindow(this);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _timer.Tick += (_, _) => Refresh();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = AppSettingsService.Current;
            // ΝΕΟ - θέση null (πρώτη φορά) -> ίδια προεπιλογή κάτω-δεξιά με το tray popup (DPI-ασφαλής, βλ.
            // PopupPlacement). Αλλιώς επαναφέρει την τελευταία θέση όπου το άφησε ο χρήστης (Window_Closing).
            if (settings.DesktopWidgetX is { } wx && settings.DesktopWidgetY is { } wy) { Left = wx; Top = wy; }
            else PopupPlacement.PlaceBottomRightOnCursorMonitor(this);

            IsVisibleChanged += (_, _) => { if (!IsVisible) ReleaseOverlay(); };

            Refresh();
            _timer.Start();
        }

        private void ReleaseOverlay()
        {
            _gpuSubscription?.Dispose();
            _gpuSubscription = null;
            if (_currentGamePid != -1) GameOverlayService.Stop();
            _currentGamePid = -1;
        }

        private async System.Threading.Tasks.Task RefreshGpuAsync()
        {
            if (_gpuReading || _gpuSubscription == null) return;
            _gpuReading = true;
            try
            {
                var value = await SharedTelemetryService.ReadGpuAsync();
                if (!_closed && _gpuSubscription != null) TxtGpu.Text = value.HasValue ? $"{value}%" : "--";
            }
            catch { }
            finally { _gpuReading = false; }
        }

        private bool _networkCheckInFlight;

        private async void Refresh()
        {
            if (_closed || !IsVisible) return;
            var cpu = PerformanceProfileService.SampleCpu();
            TxtCpu.Text = cpu >= 0 ? $"{cpu:0}%" : "--";

            if (NativeMethods.GlobalMemoryStatusEx(out var mem) && mem.ullTotalPhys > 0)
                TxtRam.Text = $"{mem.dwMemoryLoad}%";

            // 6.9.1: ποτέ επικαλυπτόμενα pings (αργό δίκτυο) και καμία εξαίρεση από async void (θα έφτανε στον global handler κάθε tick)
            if (!_networkCheckInFlight && (_lastNetworkCheck == 0 || Environment.TickCount64 - _lastNetworkCheck >= 20000))
            {
                _lastNetworkCheck = Environment.TickCount64;
                _networkCheckInFlight = true;
                try
                {
                    var (connected, latency) = await NetworkService.CheckInternetAsync();
                    if (_closed) return;
                    TxtNetwork.Text = connected
                        ? $"{LanguageService.T("Tray_Connected")}{(latency.HasValue ? $" ({latency} ms)" : "")}"
                        : LanguageService.T("Tray_Disconnected");
                }
                catch (Exception ex) { CrashLogService.Write("Widget network check", ex); }
                finally { _networkCheckInFlight = false; }
            }

            if (!_closed && IsVisible) RefreshGameOverlay();
        }

        // ΝΕΟ - ρητό αίτημα χρήστη: "κάνε πάλι ως widget το FPS/in-game overlay που θα επεκτείνει το
        // Widget Επιφάνειας Εργασίας". Εντοπίζει fullscreen παιχνίδι (ίδιο heuristic με το
        // AutoGamingModeService, βλ. GameOverlayService.GetForegroundFullscreenProcess) - όταν
        // εντοπιστεί, εμφανίζει GPU%/FPS και ξεκινά/σταματά τη μέτρηση DXGI Present events για το
        // συγκεκριμένο PID (ξεκινά ξανά μόνο όταν αλλάξει το PID, όχι σε κάθε tick).
        private void RefreshGameOverlay()
        {
            var game = GameOverlayService.GetForegroundFullscreenProcess();
            if (game == null)
            {
                ReleaseOverlay();
                RowGpu.Visibility = Visibility.Collapsed;
                RowGpuVoltage.Visibility = Visibility.Collapsed;
                RowFps.Visibility = Visibility.Collapsed;
                TxtGameName.Visibility = Visibility.Collapsed;
                return;
            }

            RowGpu.Visibility = Visibility.Visible;
            RowGpuVoltage.Visibility = Visibility.Visible;
            RowFps.Visibility = Visibility.Visible;
            TxtGameName.Visibility = Visibility.Visible;
            TxtGameName.Text = $"{LanguageService.T("Widget_MonitoringPrefix")}{game.Value.Name}";

            _gpuSubscription ??= SharedTelemetryService.SubscribeGpu();
            _ = RefreshGpuAsync();
            RefreshGpuVoltageAsync();

            if (_currentGamePid != game.Value.Pid || (!GameOverlayService.IsRunning && Environment.TickCount64 - _lastOverlayAttempt >= 30000))
            {
                _currentGamePid = game.Value.Pid;
                _lastOverlayAttempt = Environment.TickCount64;
                GameOverlayService.Start(game.Value.Pid);
            }
            var fps = GameOverlayService.SampleFps();
            TxtFps.Text = fps?.ToString() ?? "--";
        }

        // ΝΕΟ - ρητό αίτημα χρήστη: "πρόσθεσε και το voltage της GPU" - SensorService.GetGpuVoltageVolts
        // κάνει Update() σε LibreHardwareMonitorLib hardware objects, ίδιο σκεπτικό "όχι στο UI thread"
        // με το HomeView's RefreshGpuTemperatureAsync (βλ. HomeView.xaml.cs) - Task.Run εδώ ομοίως.
        // _voltageQueryInFlight αποτρέπει επικαλυπτόμενα queries αν ο tick (1.5s) είναι πιο γρήγορος από
        // την επιστροφή του προηγούμενου query.
        private async void RefreshGpuVoltageAsync()
        {
            if (_closed || _voltageQueryInFlight || (_lastVoltageCheck != 0 && Environment.TickCount64 - _lastVoltageCheck < 3000)) return;
            _lastVoltageCheck = Environment.TickCount64;
            _voltageQueryInFlight = true;
            try
            {
                var volts = await System.Threading.Tasks.Task.Run(SensorService.GetGpuVoltageVolts);
                if (_closed) return;
                TxtGpuVoltage.Text = volts.HasValue ? $"{volts.Value:0.000} V" : "--";
            }
            finally
            {
                _voltageQueryInFlight = false;
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => DesktopWidgetService.Stop(persistDisabled: true);

        // ΝΕΟ - θυμάται τη θέση όπου το άφησε ο χρήστης, ώστε να ξανανοίξει ΕΚΕΙ (όχι πάντα στην ίδια
        // προεπιλεγμένη γωνία) στην επόμενη εκκίνηση της εφαρμογής.
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            AppSettingsService.Current.DesktopWidgetX = Left;
            AppSettingsService.Current.DesktopWidgetY = Top;
            AppSettingsService.Save();
            _closed = true;
            _timer.Stop();
            ReleaseOverlay();
        }
    }
}
