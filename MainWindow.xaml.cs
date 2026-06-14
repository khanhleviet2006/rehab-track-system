using LiveCharts;
using LiveCharts.Defaults;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Globalization;

namespace AngleMonitorWPF
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _serialBuffer = "";
        private double _lastSentAngleToGame = -999.0;

        private double _axisMax;
        private double _axisMin;

        public double AxisMax
        {
            get { return _axisMax; }
            set { _axisMax = value; OnPropertyChanged("AxisMax"); }
        }
        public double AxisMin
        {
            get { return _axisMin; }
            set { _axisMin = value; OnPropertyChanged("AxisMin"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // --- BIẾN TỐI ƯU LUỒNG DỮ LIỆU ---
        private bool isGamePaused = true;
        private ConcurrentQueue<(double Angle, double Time)> _dataQueue = new ConcurrentQueue<(double, double)>();
        private Queue<double> _smoothingBuffer = new Queue<double>();
        private const int SMOOTHING_WINDOW = 10;
        private int _sampleCounter = 0;
        private readonly int DOWN_SAMPLE_FACTOR = 3;
        private DispatcherTimer _uiUpdateTimer;
        private DispatcherTimer _stopwatchTimer;
        private TimeSpan _elapsedTime;
        private double _tempAngle = 0;
        private bool _hasAngle = false;
        private SerialPort _serialPort;
        private bool _isConnected = false;
        private RehabSession _currentSession;

        // --- THUẬT TOÁN PEAK-TO-VALLEY STATE ---
        private double _peak = double.MinValue;
        private double _valley = double.MaxValue;
        private bool _lookingForValley = false;

        // --- REPS & HISTOGRAM ---
        private int _repCount = 0;
        private List<double> _sessionMaxAngles = new List<double>();
        private double _sessionMaxSpeed = 0;

        // --- GIẢ LẬP & THỜI GIAN ---
        private DispatcherTimer _simTimer;
        private double _simTimeCounter = 0;
        private Random _rnd = new Random();
        private DateTime _startTime = DateTime.MinValue;

        // --- LIVECHARTS BINDING ---
        public ChartValues<ObservablePoint> AngleValues { get; set; }
        public ChartValues<ObservablePoint> UpperThresholdValues { get; set; }
        public ChartValues<ObservablePoint> LowerThresholdValues { get; set; }
        public ChartValues<int> HistogramValues { get; set; }
        public string[] HistogramLabels { get; set; }
        public Func<double, string> TimeFormatter { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            AngleValues = new ChartValues<ObservablePoint>();
            UpperThresholdValues = new ChartValues<ObservablePoint>();
            LowerThresholdValues = new ChartValues<ObservablePoint>();
            TimeFormatter = value => value.ToString("0") + "s";

            HistogramValues = new ChartValues<int> { 0, 0, 0, 0, 0, 0 };
            HistogramLabels = new[] { "0-30°", "30-60°", "60-90°", "90-120°", "120-150°", "150-180°" };

            AxisMax = 10;
            AxisMin = 0;

            DataContext = this;

            // KHỞI TẠO WEBVIEW2 (TRÒ CHƠI)
            InitializeWebViewAsync();

            _stopwatchTimer = new DispatcherTimer();
            _stopwatchTimer.Interval = TimeSpan.FromSeconds(1);
            _stopwatchTimer.Tick += (s, e) =>
            {
                _elapsedTime = _elapsedTime.Add(TimeSpan.FromSeconds(1));
                txtSessionTime.Text = _elapsedTime.ToString(@"hh\:mm\:ss");
            };

            _uiUpdateTimer = new DispatcherTimer();
            _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(50);
            _uiUpdateTimer.Tick += ProcessQueueToUI;
            _uiUpdateTimer.Start();
        }

        // --- KHỞI TẠO VÀ XỬ LÝ SỰ KIỆN WEBVIEW2 (GAME) ---
        private async void InitializeWebViewAsync()
        {
            try
            {
                await webViewGame.EnsureCoreWebView2Async(null);
                string defaultGamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Game", "game_dynamic.html");
                if (File.Exists(defaultGamePath))
                {
                    webViewGame.CoreWebView2.Navigate(defaultGamePath);
                }
                else
                {
                    webViewGame.CoreWebView2.NavigateToString("<html><body><h2>Không tìm thấy file Game!</h2><p>Vui lòng copy thư mục 'Game' vào thư mục Debug của phần mềm.</p></body></html>");
                }
                webViewGame.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khởi tạo WebView2: " + ex.Message);
            }
        }

        private void CalculateReps(double angle)
        {
            if (_peak == double.MinValue && _valley == double.MaxValue)
            {
                _peak = angle;
                _valley = angle;
            }
            double currentNoiseMargin = DeviceSettings.NoiseMargin;
            double currentMinDelta = DeviceSettings.MinDeltaForRep;

            if (!_lookingForValley)
            {
                if (angle > _peak)
                {
                    _peak = angle;
                }
                else if (_peak - angle >= currentNoiseMargin)
                {
                    _valley = angle;
                    _lookingForValley = true;
                }
            }
            else
            {
                if (angle < _valley)
                {
                    _valley = angle;
                }
                else if (angle - _valley >= currentNoiseMargin)
                {
                    double delta = _peak - _valley;
                    if (delta >= currentMinDelta)
                    {
                        _repCount++;
                        Dispatcher.Invoke(() =>
                        {
                            txtReps.Text = _repCount.ToString();
                        });

                        _sessionMaxAngles.Add(_peak);
                        int index = Math.Max(0, Math.Min(5, (int)(_peak / 30.0)));
                        HistogramValues[index]++;
                    }

                    _peak = angle;
                    _valley = double.MaxValue;
                    _lookingForValley = false;
                }
            }
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string jsonResult = e.TryGetWebMessageAsString();
            MessageBox.Show("Bài tập Game đã hoàn thành!\nDữ liệu nhận được: " + jsonResult, "Thông báo hệ thống", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (chartAngle == null || webViewGame == null) return;

            if (rbClinicalMode.IsChecked == true)
            {
                chartAngle.Visibility = Visibility.Visible;
                webViewGame.Visibility = Visibility.Collapsed;
                if (cboGameType != null) cboGameType.Visibility = Visibility.Collapsed;
            }
            else if (rbGameMode.IsChecked == true)
            {
                chartAngle.Visibility = Visibility.Collapsed;
                webViewGame.Visibility = Visibility.Visible;
                if (cboGameType != null) cboGameType.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<string> AutoFindBluetoothPortAsync()
        {
            return await Task.Run(() =>
            {
                string[] danhSachCong = SerialPort.GetPortNames();
                foreach (string port in danhSachCong)
                {
                    // Bỏ qua cổng COM6 hoặc COM3 nếu đang cắm cáp nạp code để ưu tiên cổng Bluetooth ảo
                    try
                    {
                        using (SerialPort testPort = new SerialPort(port, 115200))
                        {
                            testPort.ReadTimeout = 1000; // Giảm xuống 1s để quét nhanh hơn
                            testPort.Open();

                            for (int i = 0; i < 5; i++)
                            {
                                string data = testPort.ReadLine().Trim();
                                if (string.IsNullOrEmpty(data)) continue;

                                // KIỂM TRA MỚI: Nếu chuỗi đổ về parse được thành số thực, chính là mạch MPU6050!
                                if (double.TryParse(data, NumberStyles.Any, CultureInfo.InvariantCulture, out double testAngle))
                                {
                                    // Kiểm tra thêm điều kiện biên của góc khuỷu tay để chắc chắn
                                    if (testAngle >= -180 && testAngle <= 180)
                                    {
                                        return port;
                                    }
                                }
                            }
                        }
                    }
                    catch { continue; }
                }
                return null;
            });
        }

        private void btnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settings = new SettingsWindow();
            settings.ShowDialog();
        }

        private async void btnStartStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isConnected)
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    txtStatus.Text = "● Đang quét tìm thiết bị Bluetooth...";
                    btnStartStop.IsEnabled = false;
                    string congTuDong = await AutoFindBluetoothPortAsync();

                    Mouse.OverrideCursor = null;
                    btnStartStop.IsEnabled = true;

                    if (string.IsNullOrEmpty(congTuDong))
                    {
                        MessageBox.Show("Không tìm thấy mạch đo! Vui lòng kiểm tra lại:\n1. Mạch đã được bật nguồn chưa?\n2. Đã ghép đôi Bluetooth với máy tính chưa?", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Warning);
                        txtStatus.Text = "● Chưa kết nối";
                        return;
                    }
                    AngleValues.Clear();
                    UpperThresholdValues.Clear();
                    LowerThresholdValues.Clear();
                    AxisMax = 10;
                    AxisMin = 0;

                    _startTime = DateTime.Now;
                    _currentSession = new RehabSession();
                    _currentSession.SessionId = DatabaseHelper.CreateNewSession();

                    _serialPort = new SerialPort(congTuDong, 115200);
                    _serialPort.DataReceived += SerialPort_DataReceived;
                    _serialPort.Open();

                    _isConnected = true;
                    btnStartStop.Content = "⏹ Ngắt kết nối";
                    btnStartStop.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    txtStatus.Text = "● Đã kết nối tự động qua " + congTuDong;

                    _elapsedTime = TimeSpan.Zero;
                    txtSessionTime.Text = "00:00:00";
                    _stopwatchTimer.Start();

                    if (rbGameMode.IsChecked == true)
                    {
                        btnPauseResumeGame.Visibility = Visibility.Visible;
                        if (webViewGame != null && webViewGame.CoreWebView2 != null)
                        {
                            await webViewGame.CoreWebView2.ExecuteScriptAsync("window.startGame();");
                            isGamePaused = false;
                            btnPauseResumeGame.Content = "⏸ Tạm dừng Game";
                            btnPauseResumeGame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                        }
                    }
                }
                else
                {
                    // 1. NGẮT KẾT NỐI VÀ DỪNG THỜI GIAN
                    if (_serialPort != null && _serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                    if (webViewGame != null && webViewGame.CoreWebView2 != null)
                    {
                        await webViewGame.CoreWebView2.ExecuteScriptAsync("window.stopGame();");
                    }

                    _isConnected = false;
                    _stopwatchTimer.Stop();

                    // 2. TÍNH TOÁN VÀ LƯU DỮ LIỆU VÀO DATABASE (PHẦN BỊ THIẾU)
                    // 2. TÍNH TOÁN VÀ LƯU DỮ LIỆU VÀO DATABASE
                    try
                    {
                        if (_currentSession != null && _currentSession.ChartData != null && _currentSession.ChartData.Count > 0)
                        {
                            // Tính toán các chỉ số
                            double peakRom = _currentSession.ChartData.Max(d => d.Angle);
                            double avgRom = _currentSession.ChartData.Average(d => d.Angle);

                            // Đóng gói mảng thành JSON
                            string chartDataJson = System.Text.Json.JsonSerializer.Serialize(_currentSession.ChartData);

                            // Gọi luôn hàm thần thánh bạn đã viết sẵn!
                            DatabaseHelper.SaveSessionData(
                                _currentSession.SessionId,
                                _repCount,
                                Math.Round(peakRom, 1),
                                Math.Round(avgRom, 1),
                                chartDataJson
                            );
                        }
                    }
                    catch (Exception dbEx)
                    {
                        MessageBox.Show("Lỗi khi lưu phiên tập: " + dbEx.Message, "Lỗi DB", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    // 3. TRẢ LẠI TRẠNG THÁI GIAO DIỆN
                    btnStartStop.Content = "▶ Bắt đầu tập";
                    btnStartStop.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    txtStatus.Text = "● Đã ngắt kết nối";
                    btnPauseResumeGame.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                btnStartStop.IsEnabled = true;
                MessageBox.Show("Có lỗi xảy ra: " + ex.Message);
            }
        }
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                while (_serialPort.BytesToRead > 0)
                {
                    string line = _serialPort.ReadLine().Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    // Đọc trực tiếp con số, không cần Split hay Regex
                    if (double.TryParse(line, NumberStyles.Any, CultureInfo.InvariantCulture, out double rawAngle))
                    {
                        _smoothingBuffer.Enqueue(rawAngle);
                        if (_smoothingBuffer.Count > SMOOTHING_WINDOW)
                        {
                            _smoothingBuffer.Dequeue();
                        }
                        double smoothedAngle = _smoothingBuffer.Average();
                        double time = (DateTime.Now - _startTime).TotalSeconds;

                        // Chỉ lưu Góc và Thời gian
                        _dataQueue.Enqueue((smoothedAngle, time));

                        _tempAngle = smoothedAngle;
                        _hasAngle = true;
                    }
                }
            }
            catch { /* Bỏ qua nhiễu khung truyền */ }
        }

        private void ProcessQueueToUI(object sender, EventArgs e)
        {
            if (_dataQueue.IsEmpty) return;

            var newAngles = new List<ObservablePoint>();
            var newUppers = new List<ObservablePoint>();
            var newLowers = new List<ObservablePoint>();

            // XÓA: var newForces = new List<ObservablePoint>();

            double latestAngle = 0;
            bool hasData = false;
            int pointsProcessed = 0;

            while (pointsProcessed < 50 && _dataQueue.TryDequeue(out var dataPoint))
            {
                hasData = true;
                latestAngle = dataPoint.Angle;
                pointsProcessed++;

                CalculateReps(dataPoint.Angle);

                _sampleCounter++;
                if (_sampleCounter % DOWN_SAMPLE_FACTOR == 0)
                {
                    double targetThresh = DeviceSettings.TargetThreshold;
                    double startRepThresh = DeviceSettings.MinAngleLimit + 10;
                    newAngles.Add(new ObservablePoint(dataPoint.Time, dataPoint.Angle));
                    newUppers.Add(new ObservablePoint(dataPoint.Time, targetThresh));
                    newLowers.Add(new ObservablePoint(dataPoint.Time, startRepThresh));


                    if (_currentSession != null)
                    {
                        _currentSession.ChartData.Add(new SessionDataPoint { Time = dataPoint.Time, Angle = dataPoint.Angle });
                    }
                }
            }

            if (hasData)
            {
                txtAngle.Text = latestAngle.ToString("F1");

                if (newAngles.Count > 0)
                {
                    AngleValues.AddRange(newAngles);
                    UpperThresholdValues.AddRange(newUppers);
                    LowerThresholdValues.AddRange(newLowers);

                    if (AngleValues.Count > 500)
                    {
                        int overflowCount = AngleValues.Count - 500;
                        for (int i = 0; i < overflowCount; i++)
                        {
                            AngleValues.RemoveAt(0);
                            UpperThresholdValues.RemoveAt(0);
                            LowerThresholdValues.RemoveAt(0);
                        }
                    }
                }

                if (webViewGame != null && webViewGame.CoreWebView2 != null)
                {
                    if (Math.Abs(latestAngle - _lastSentAngleToGame) >= 0.5)
                    {
                        string angleString = latestAngle.ToString(CultureInfo.InvariantCulture);
                        webViewGame.CoreWebView2.PostWebMessageAsString(angleString);
                        _lastSentAngleToGame = latestAngle;
                    }
                }
            }

            if (_isConnected && _startTime != DateTime.MinValue)
            {
                double realTime = (DateTime.Now - _startTime).TotalSeconds;
                if (realTime > 10)
                {
                    AxisMax = realTime;
                    AxisMin = realTime - 10;
                }
            }
        }

        private async void btnPauseResumeGame_Click(object sender, RoutedEventArgs e)
        {
            if (webViewGame == null || webViewGame.CoreWebView2 == null) return;

            if (!isGamePaused)
            {
                await webViewGame.CoreWebView2.ExecuteScriptAsync("pauseGame();");
                btnPauseResumeGame.Content = "▶ Tiếp tục Game";
                btnPauseResumeGame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")); // Đổi màu xanh lá
                isGamePaused = true;
            }
            else
            {
                await webViewGame.CoreWebView2.ExecuteScriptAsync("startGame();");
                btnPauseResumeGame.Content = "⏸ Tạm dừng Game";
                btnPauseResumeGame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // Đổi màu cam
                isGamePaused = false;
            }
        }

        private void btnResetReps_Click(object sender, RoutedEventArgs e)
        {
            _repCount = 0;
            txtReps.Text = "0";
            for (int i = 0; i < HistogramValues.Count; i++) HistogramValues[i] = 0;
            if (_sessionMaxAngles != null) _sessionMaxAngles.Clear();

            _peak = double.MinValue;
            _valley = double.MaxValue;
            _lookingForValley = false;

            _sessionMaxSpeed = 0;
            _elapsedTime = TimeSpan.Zero;
            txtSessionTime.Text = "00:00:00";
        }

        private void btnOpenAnalysis_Click(object sender, RoutedEventArgs e)
        {
            AnalysisTab tab = new AnalysisTab();
            tab.ShowDialog();
        }

        private void LoadChartData(DateTime startDate, DateTime endDate)
        {
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                }
                _isConnected = false;
                if (_stopwatchTimer != null) _stopwatchTimer.Stop();
            }

            if (_simTimer != null && _simTimer.IsEnabled)
            {
                _simTimer.Stop();
            }

            bool isFound = false;
            foreach (Window window in Application.Current.Windows)
            {
                if (window is PatientSelectionWindow)
                {
                    window.Show();
                    isFound = true;
                    break;
                }
            }

            if (!isFound)
            {
                PatientSelectionWindow patientWindow = new PatientSelectionWindow();
                patientWindow.Show();
            }

            this.Close();
        }
    }
}