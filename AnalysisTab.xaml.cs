using LiveCharts;
using LiveCharts.Defaults;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System.Linq;
using System.Windows.Media;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace AngleMonitorWPF
{
    public class SessionItem
    {
        public int SessionId { get; set; }
        public string Date { get; set; } = "";
        public string Label { get; set; } = "";
        public double PeakRom { get; set; }
        public double AvgRom { get; set; }
        public int Reps { get; set; }
        public int Duration { get; set; }
        public string PeakChange { get; set; } = "";

        public List<ObservablePoint> ChartData { get; set; }
    }

    public partial class AnalysisTab : Window
    {
        // Data binding cho Biểu đồ đường (Line Chart)
        public ChartValues<ObservablePoint> AngleValues { get; set; }
        public Func<double, string> TimeFormatter { get; set; }

        // MỚI: Data binding cho Biểu đồ cột (Bar Chart)
        public ChartValues<int> RepDistributionValues { get; set; }
        public string[] HistogramLabels { get; set; }

        private List<SessionItem> _sessions;
        private string _currentSessionDate = "";

        public AnalysisTab()
        {
            InitializeComponent();

            // Khởi tạo data biểu đồ đường
            AngleValues = new ChartValues<ObservablePoint>();
            TimeFormatter = value => TimeSpan.FromSeconds(value).ToString(@"mm\:ss");

            // MỚI: Khởi tạo data biểu đồ cột với 5 mốc mặc định bằng 0
            RepDistributionValues = new ChartValues<int> { 0, 0, 0, 0, 0 };
            HistogramLabels = new[] { "0-30°", "30-60°", "60-90°", "90-120°", ">120°" };

            DataContext = this;
            _sessions = new List<SessionItem>();

            this.Loaded += AnalysisTab_Loaded;
        }

        private async void AnalysisTab_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadSessionsFromDatabaseAsync();
        }

        // =======================================================================
        // 1. TẢI DANH SÁCH BUỔI TẬP (ASYNC)
        // =======================================================================
        private async Task LoadSessionsFromDatabaseAsync()
        {
            try
            {
                string query = @"
                    SELECT SessionId, StartTime, EndTime, TotalReps, PeakROM, AvgROM
                    FROM dbo.TrainingSessions
                    WHERE UserId = @pID
                    ORDER BY StartTime DESC";

                using (SqlCommand cmd = new SqlCommand(query))
                {
                    // LƯU Ý: Đảm bảo biến GlobalData.CurrentUserId hợp lệ trong project của bạn
                    cmd.Parameters.AddWithValue("@pID", GlobalData.CurrentUserId);

                    using (SqlDataReader reader = await DatabaseHelper.ExecuteReaderAsync(cmd))
                    {
                        int sessionNumber = 1;

                        while (reader != null && await reader.ReadAsync())
                        {
                            DateTime startTime = Convert.ToDateTime(reader["StartTime"]);
                            DateTime? endTime = reader["EndTime"] != DBNull.Value ? Convert.ToDateTime(reader["EndTime"]) : (DateTime?)null;

                            int durationMinutes = 0;
                            if (endTime.HasValue)
                            {
                                durationMinutes = (int)(endTime.Value - startTime).TotalMinutes;
                            }

                            _sessions.Add(new SessionItem
                            {
                                SessionId = Convert.ToInt32(reader["SessionId"]),
                                Date = startTime.ToString("yyyy-MM-dd HH:mm"),
                                Label = "Session #" + sessionNumber++,
                                PeakRom = Convert.ToDouble(reader["PeakROM"]),
                                AvgRom = Convert.ToDouble(reader["AvgROM"]),
                                Reps = Convert.ToInt32(reader["TotalReps"]),
                                Duration = durationMinutes,
                                PeakChange = "Hoàn thành",
                                ChartData = null
                            });
                        }
                    }
                }

                SessionList.ItemsSource = _sessions;

                if (_sessions.Count > 0)
                {
                    UpdateSessionUI(_sessions[0]);
                    await LoadChartDataForSessionAsync(_sessions[0]);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi tải lịch sử tập: " + ex.Message, "Lỗi Database", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =======================================================================
        // 2. TẢI ĐƯỜNG CONG BIỂU ĐỒ & TÍNH TOÁN HISTOGRAM (ASYNC)
        // =======================================================================
        private async Task LoadChartDataForSessionAsync(SessionItem session)
        {
            try
            {
                AngleValues.Clear();

                if (session.ChartData != null && session.ChartData.Count > 0)
                {
                    AngleValues.AddRange(session.ChartData);
                    // Nếu đã có dữ liệu, vẫn tính Histogram nhưng chạy ngầm
                    var bins = await Task.Run(() => CalculateHistogram(session.ChartData));
                    for (int i = 0; i < 5; i++) RepDistributionValues[i] = bins[i];
                    return;
                }

                string query = "SELECT ChartDataJson FROM dbo.TrainingSessions WHERE SessionId = @sid";

                using (SqlCommand cmd = new SqlCommand(query))
                {
                    cmd.Parameters.AddWithValue("@sid", session.SessionId);

                    using (SqlDataReader reader = await DatabaseHelper.ExecuteReaderAsync(cmd))
                    {
                        if (reader != null && await reader.ReadAsync())
                        {
                            string jsonString = reader["ChartDataJson"].ToString();

                            if (!string.IsNullOrEmpty(jsonString))
                            {
                                // ĐẨY XUỐNG LUỒNG NỀN XỬ LÝ (TỐI ƯU 1)
                                var processedResult = await Task.Run(() =>
                                {
                                    var historyData = JsonSerializer.Deserialize<List<SessionDataPoint>>(jsonString);
                                    var chartPoints = new List<ObservablePoint>();
                                    int[] bins = new int[5];

                                    if (historyData != null && historyData.Count > 0)
                                    {
                                        // Giảm mẫu (Downsampling) xuống tối đa ~500 điểm
                                        int step = Math.Max(1, historyData.Count / 500);
                                        for (int i = 0; i < historyData.Count; i += step)
                                        {
                                            chartPoints.Add(new ObservablePoint(historyData[i].Time, historyData[i].Angle));
                                        }

                                        // Tính Histogram trực tiếp trên data gốc
                                        for (int i = 1; i < historyData.Count - 1; i++)
                                        {
                                            double prevY = historyData[i - 1].Angle;
                                            double currY = historyData[i].Angle;
                                            double nextY = historyData[i + 1].Angle;

                                            if (currY > prevY && currY >= nextY && currY > 15)
                                            {
                                                if (currY <= 30) bins[0]++;
                                                else if (currY <= 60) bins[1]++;
                                                else if (currY <= 90) bins[2]++;
                                                else if (currY <= 120) bins[3]++;
                                                else bins[4]++;
                                                i += 5; // Bỏ qua đỉnh nhiễu
                                            }
                                        }
                                    }
                                    return new { Points = chartPoints, Bins = bins };
                                });

                                // TRỞ LẠI UI THREAD: Cập nhật giao diện mượt mà
                                session.ChartData = processedResult.Points;
                                AngleValues.AddRange(processedResult.Points);

                                for (int i = 0; i < 5; i++)
                                {
                                    RepDistributionValues[i] = processedResult.Bins[i];
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi tải dữ liệu biểu đồ: " + ex.Message, "Lỗi Database", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Thêm hàm hỗ trợ nhỏ này ngay dưới hàm LoadChartDataForSessionAsync:
        private int[] CalculateHistogram(List<ObservablePoint> points)
        {
            int[] bins = new int[5];
            for (int i = 1; i < points.Count - 1; i++)
            {
                double prevY = points[i - 1].Y;
                double currY = points[i].Y;
                double nextY = points[i + 1].Y;

                if (currY > prevY && currY >= nextY && currY > 15)
                {
                    if (currY <= 30) bins[0]++;
                    else if (currY <= 60) bins[1]++;
                    else if (currY <= 90) bins[2]++;
                    else if (currY <= 120) bins[3]++;
                    else bins[4]++;
                    i += 5;
                }
            }
            return bins;
        }
        private void ResetRepDistribution()
        {
            // Gán trực tiếp giá trị 0 thay vì gọi lệnh Clear()
            for (int i = 0; i < 5; i++)
            {
                RepDistributionValues[i] = 0;
            }
        }

        // =======================================================================
        // SỰ KIỆN TƯƠNG TÁC GIAO DIỆN
        // =======================================================================
        private void UpdateSessionUI(SessionItem s)
        {
            PeakRomValue.Text = s.PeakRom.ToString("F1");
            AvgRomValue.Text = s.AvgRom.ToString("F1");
            RepsValue.Text = s.Reps.ToString();
            DurationValue.Text = s.Duration.ToString();

            // Cập nhật biến ngày tháng thay vì đẩy ra UI
            if (DateTime.TryParseExact(s.Date, "yyyy-MM-dd HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            {
                _currentSessionDate = dt.ToString("MMM dd, yyyy");
            }
            else
            {
                _currentSessionDate = s.Date;
            }
        }

        private async void SessionItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SessionItem s)
            {
                UpdateSessionUI(s);
                await LoadChartDataForSessionAsync(s);
            }
        }

        // =======================================================================
        // 3. XỬ LÝ XUẤT PDF QUA WEBVIEW2 VÀ MÃ HTML MẪU
        // =======================================================================

        private string GetChartBase64(UIElement chartControl)
        {
            chartControl.UpdateLayout();
            RenderTargetBitmap rtb = new RenderTargetBitmap(
                (int)chartControl.RenderSize.Width,
                (int)chartControl.RenderSize.Height,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);

            rtb.Render(chartControl);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                byte[] imageBytes = ms.ToArray();
                return "data:image/png;base64," + Convert.ToBase64String(imageBytes);
            }
        }

        // MỚI: HÀM TẠO CỬA SỔ POPUP NHẬP GHI CHÚ BẰNG CODE (Không cần file XAML mới)
        private string PromptForDoctorNotes()
        {
            Window prompt = new Window()
            {
                Width = 450,
                Height = 320,
                Title = "Thêm ghi chú bác sĩ",
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Colors.White)
            };

            StackPanel stack = new StackPanel() { Margin = new Thickness(20) };

            TextBlock label = new TextBlock()
            {
                Text = "Nhập ghi chú hoặc đánh giá cho phiên tập này (Tùy chọn):",
                Margin = new Thickness(0, 0, 0, 10),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2c3e50"))
            };

            TextBox textBox = new TextBox()
            {
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Height = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e0e0e0"))
            };

            Button btnConfirm = new Button()
            {
                Content = "Xác nhận & Xuất PDF",
                Width = 140,
                Height = 35,
                Margin = new Thickness(0, 15, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#20b2aa")),
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            string result = "";
            btnConfirm.Click += (s, e) => { result = textBox.Text; prompt.DialogResult = true; };

            stack.Children.Add(label);
            stack.Children.Add(textBox);
            stack.Children.Add(btnConfirm);
            prompt.Content = stack;

            prompt.ShowDialog();
            return result;
        }

        private async void ExportPDF_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. LÝ GHI CHÚ CỦA BÁC SĨ VIA POPUP
                string doctorNotes = PromptForDoctorNotes();

                // 2. MỞ HỘP THOẠI CHỌN NƠI LƯU FILE
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "PDF Document|*.pdf";
                saveFileDialog.Title = "Chọn file để lưu mới hoặc gộp (Append) vào file cũ";
                saveFileDialog.FileName = $"RehabTrack_Report_{DateTime.Now:yyyyMM}.pdf";

                if (saveFileDialog.ShowDialog() == true)
                {
                    string finalFilePath = saveFileDialog.FileName;

                    // Tạo file tạm thời
                    string tempFilePath = Path.Combine(Path.GetTempPath(), $"TempReport_{Guid.NewGuid()}.pdf");

                    await PdfWebView.EnsureCoreWebView2Async();

                    // MỚI: Chụp cả 2 biểu đồ (Biểu đồ đường và Biểu đồ cột Histogram)
                    string chartBase64 = GetChartBase64(MyChart);
                    string barChartBase64 = GetChartBase64(MyBarChart);

                    // Xử lý chuỗi HTML ghi chú
                    string noteHtml = string.IsNullOrWhiteSpace(doctorNotes)
                        ? ""
                        : $"<div class='doctor-notes'><strong>Nhận xét:</strong> {doctorNotes.Replace("\r\n", "<br/>").Replace("\n", "<br/>")}</div>";

                    string htmlContent = @"
            <!DOCTYPE html>
            <html lang='vi'>
            <head>
                <meta charset='UTF-8'>
                <style>
                    @page { size: A4 portrait; margin: 0; }
                    body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; color: #334155; margin: 0; padding: 0; background: white; -webkit-print-color-adjust: exact; width: 210mm; height: 297mm; overflow: hidden; }
                    .container { width: 100%; height: 100%; box-sizing: border-box; padding: 12mm 15mm; }
                    h1.report-title { color: #1e3a8a; font-size: 20px; font-weight: 700; text-transform: uppercase; margin-bottom: 15px; margin-top: 0; }
                    .patient-card { display: flex; align-items: center; background: #f8fafc; border-radius: 12px; padding: 15px; margin-bottom: 20px; border: 1px solid #e2e8f0; }
                    .patient-avatar { width: 50px; height: 50px; background: #cbd5e1; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 24px; margin-right: 20px; }
                    .info-group { flex: 1; border-right: 1px solid #e2e8f0; padding-right: 10px; margin-right: 10px; }
                    .info-group:last-child { border-right: none; margin-right: 0; padding-right: 0; }
                    .info-label { font-size: 10px; color: #64748b; text-transform: uppercase; font-weight: 600; margin-bottom: 4px; }
                    .info-value { font-size: 14px; font-weight: 700; color: #0f172a; }
                    .info-sub { font-size: 11px; color: #64748b; margin-top: 2px; }
                    .metrics-section { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-bottom: 20px; }
                    .metric-box { background: #ffffff; border: 1px solid #e2e8f0; border-radius: 10px; padding: 12px; box-shadow: 0 1px 2px rgba(0,0,0,0.05); }
                    .metric-box h3 { margin: 0 0 8px 0; font-size: 10px; color: #64748b; text-transform: uppercase; font-weight: 600; }
                    .metric-val { font-size: 26px; font-weight: 700; color: #0f172a; display: flex; align-items: baseline; gap: 4px; }
                    .metric-unit { font-size: 14px; color: #64748b; font-weight: 500; }
                    .metric-status { margin-top: 8px; font-size: 10px; font-weight: 700; display: flex; align-items: center; gap: 4px; text-transform: uppercase; }
                    .status-good { color: #16a34a; }
                    .status-warn { color: #dc2626; }
                    .charts-wrapper { display: flex; gap: 15px; margin-bottom: 20px; }
                    .chart-card { background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 15px; display: flex; flex-direction: column; }
                    .chart-main { flex: 1.8; }
                    .chart-side { flex: 1; }
                    .chart-header { font-size: 12px; font-weight: 700; color: #334155; text-transform: uppercase; margin-bottom: 10px; display: flex; justify-content: space-between; align-items: center; }
                    .chart-badge { font-size: 9px; background: #1e3a8a; color: white; padding: 3px 8px; border-radius: 12px; }
                    .chart-img-container { flex: 1; position: relative; overflow: hidden; display: flex; align-items: center; justify-content: center; }
                    .chart-img-container img { max-width: 100%; height: 180px; object-fit: contain; }
                    .signature-section { padding-top: 10px; }
                    .doctor-notes { background: #f0fdf4; border-left: 4px solid #16a34a; padding: 10px 15px; border-radius: 0 8px 8px 0; font-size: 13px; line-height: 1.5; color: #166534; margin-bottom: 20px; }
                    .sig-grid { display: flex; justify-content: space-around; margin-top: 10px; }
                    .sig-box { text-align: center; width: 40%; }
                    .sig-title { font-size: 11px; color: #64748b; text-transform: uppercase; font-weight: 600; margin-bottom: 50px; }
                    .sig-line { border-top: 1px dashed #cbd5e1; padding-top: 8px; font-weight: 700; font-size: 14px; color: #0f172a; }
                    .sig-date { font-size: 11px; color: #64748b; font-weight: 400; margin-top: 4px; }
                    .footer { margin-top: auto; border-top: 1px solid #e2e8f0; padding-top: 15px; display: flex; justify-content: space-between; font-size: 9px; color: #94a3b8; font-weight: 500; text-transform: uppercase; position: absolute; bottom: 12mm; left: 15mm; right: 15mm; }
                </style>
            </head>
            <body>
                <div class='container'>
                    <h1 class='report-title'>Báo cáo kết quả phục hồi chức năng khớp khuỷu</h1>

                    <div class='patient-card'>
                        <div class='patient-avatar'>👨‍⚕️</div>
                        <div class='info-group'>
                            <div class='info-label'>Thông tin bệnh nhân</div>
                            <div class='info-value'>Bệnh nhân hiện tại</div>
                            <div class='info-sub'>ID: RT-2026-{{SessionDateShort}}</div>
                        </div>
                        <div class='info-group'>
                            <div class='info-label'>Phiên tập</div>
                            <div class='info-value'>Buổi tập hiện tại</div>
                            <div class='info-sub'>Giờ thực hiện: {{SessionTime}}</div>
                        </div>
                        <div class='info-group'>
                            <div class='info-label'>Ngày thực hiện</div>
                            <div class='info-value'>{{SessionDate}}</div>
                            <div class='info-sub'>Loại điều trị: Hậu phẫu khớp khuỷu</div>
                        </div>
                    </div>

                    <div class='metrics-section'>
                        <div class='metric-box'>
                            <h3>Biên độ đỉnh (Peak ROM)</h3>
                            <div class='metric-val'>{{PeakRom}}<span class='metric-unit'>°</span></div>
                            <div class='metric-status status-good'>✓ Đã hoàn thành | Target: 115°</div>
                        </div>
                        <div class='metric-box'>
                            <h3>Biên độ trung bình (Avg ROM)</h3>
                            <div class='metric-val'>{{AvgRom}}<span class='metric-unit'>°</span></div>
                            <div class='metric-status status-warn'>↘ Dưới mục tiêu</div>
                        </div>
                        <div class='metric-box'>
                            <h3>Số lần lặp (Reps)</h3>
                            <div class='metric-val'>{{Reps}}</div>
                            <div class='metric-status status-good'>✓ Full Extension</div>
                        </div>
                        <div class='metric-box'>
                            <h3>Thời lượng (Duration)</h3>
                            <div class='metric-val'>{{Duration}}<span class='metric-unit'>min</span></div>
                            <div class='metric-status' style='color: #64748b;'>⏱ Phiên hoạt động</div>
                        </div>
                    </div>

                    <div class='charts-wrapper'>
                        <div class='chart-card chart-main'>
                            <div class='chart-header'>
                                <span>Biểu đồ diễn biến góc khớp (Angle Trace)</span>
                                <span class='chart-badge'>Current Session</span>
                            </div>
                            <div class='chart-img-container'>
                                <img src='{{ChartImage}}' alt='Angle Trace' />
                            </div>
                        </div>

                        <div class='chart-card chart-side'>
                            <div class='chart-header'>
                                <span>Phân phối tần suất (ROM)</span>
                            </div>
                            <div class='chart-img-container'>
                                <img src='{{BarChartImage}}' alt='ROM Distribution' />
                            </div>
                        </div>
                    </div>
                    <div class='footer'>
                        <div>© 2026 REHABTRACK CLINICAL SYSTEMS. HUST BME. COMPLIANT.</div>
                        <div>Privacy Policy • Terms of Service • Clinical Support</div>
                    </div>
                </div>
            </body>
            </html>";
                    // ===== THAY THẾ BIẾN (DÙNG STRINGBUILDER ĐỂ TỐI ƯU RAM) =====
                    System.Text.StringBuilder htmlBuilder = new System.Text.StringBuilder(htmlContent);

                    htmlBuilder.Replace("{{DateToday}}", DateTime.Now.ToString("dd/MM/yyyy"));
                    htmlBuilder.Replace("{{SessionDateShort}}", DateTime.Now.ToString("MMdd"));

                    string displayDate = _currentSessionDate;
                    string displayTime = "00:00 AM";
                    if (DateTime.TryParse(_currentSessionDate, out DateTime parsedDate))
                    {
                        displayDate = parsedDate.ToString("dd/MM/yyyy");
                        displayTime = parsedDate.ToString("hh:mm tt");
                    }

                    htmlBuilder.Replace("{{SessionDate}}", displayDate);
                    htmlBuilder.Replace("{{SessionTime}}", displayTime);
                    htmlBuilder.Replace("{{PeakRom}}", PeakRomValue.Text);
                    htmlBuilder.Replace("{{AvgRom}}", AvgRomValue.Text);
                    htmlBuilder.Replace("{{Reps}}", RepsValue.Text);
                    htmlBuilder.Replace("{{Duration}}", DurationValue.Text);

                    // Nhúng dữ liệu hình ảnh 2 biểu đồ vào HTML
                    htmlBuilder.Replace("{{ChartImage}}", chartBase64);
                    htmlBuilder.Replace("{{BarChartImage}}", barChartBase64);
                    htmlBuilder.Replace("{{DoctorNotes}}", noteHtml);

                    // Xuất ra chuỗi cuối cùng duy nhất
                    string finalHtmlToRender = htmlBuilder.ToString();

                    // Chờ WebView2 Load nội dung HTML hoàn tất
                    bool isLoaded = false;
                    EventHandler<CoreWebView2NavigationCompletedEventArgs> loadHandler = null;
                    loadHandler = (s, args) =>
                    {
                        isLoaded = true;
                        PdfWebView.CoreWebView2.NavigationCompleted -= loadHandler;
                    };
                    PdfWebView.CoreWebView2.NavigationCompleted += loadHandler;

                    // Đưa chuỗi đã tối ưu vào WebView2
                    PdfWebView.NavigateToString(finalHtmlToRender);

                    while (!isLoaded) await Task.Delay(100);
                    await Task.Delay(500);

                    var printSettings = PdfWebView.CoreWebView2.Environment.CreatePrintSettings();
                    printSettings.Orientation = CoreWebView2PrintOrientation.Portrait;
                    printSettings.ShouldPrintBackgrounds = true;
                    printSettings.MarginBottom = 0; printSettings.MarginTop = 0;
                    printSettings.MarginLeft = 0; printSettings.MarginRight = 0;

                    // In ra file tạm trước
                    bool isSuccessful = await PdfWebView.CoreWebView2.PrintToPdfAsync(tempFilePath, printSettings);

                    if (isSuccessful)
                    {
                        // XỬ LÝ GỘP FILE VỚI PDFSHARP
                        if (File.Exists(finalFilePath))
                        {
                            using (PdfSharp.Pdf.PdfDocument targetDoc = PdfSharp.Pdf.IO.PdfReader.Open(finalFilePath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify))
                            using (PdfSharp.Pdf.PdfDocument tempDoc = PdfSharp.Pdf.IO.PdfReader.Open(tempFilePath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                            {
                                for (int i = 0; i < tempDoc.PageCount; i++)
                                {
                                    targetDoc.AddPage(tempDoc.Pages[i]);
                                }
                                targetDoc.Save(finalFilePath);
                            }
                        }
                        else
                        {
                            File.Copy(tempFilePath, finalFilePath);
                        }

                        if (File.Exists(tempFilePath)) File.Delete(tempFilePath);

                        MessageBox.Show("✅ Đã xử lý và lưu báo cáo thành công!", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);

                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                        {
                            FileName = finalFilePath,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        MessageBox.Show("Có lỗi xảy ra trong quá trình render PDF.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (System.IO.IOException)
            {
                MessageBox.Show("Không thể xuất hoặc gộp báo cáo.\nNguyên nhân: File PDF này đang được mở ở một chương trình khác.\n\nVui lòng đóng cửa sổ xem PDF đó lại và thử xuất lại!",
                                "File đang bị khóa", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi hệ thống: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}