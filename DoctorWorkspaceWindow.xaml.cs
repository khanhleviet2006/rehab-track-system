using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AngleMonitorWPF.Rom;
using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;

namespace AngleMonitorWPF
{
    public partial class DoctorWorkspaceWindow : Window
    {
        private readonly DoctorWorkspaceRepository repository = new DoctorWorkspaceRepository(DatabaseHelper.GetConnection);
        private DoctorSession doctor;
        private int patientId;
        private bool loading, busy;
        private List<RomMilestone> milestones = new List<RomMilestone>();
        private RomMilestone editing;
        private DataTable sessions, assessments;
        private static bool DemoEnabled { get { return string.Equals(ConfigurationManager.AppSettings["DoctorWorkspaceDemoMode"], "true", StringComparison.OrdinalIgnoreCase); } }

        private static async Task<DoctorSession> AcquireSessionAsync(Window owner)
        {
            if (DemoEnabled)
                return await Task.Run(() => new DoctorRepository(DatabaseHelper.GetConnection).OpenDemoSession(true));
            var login = new DoctorLoginWindow { Owner = owner };
            return login.ShowDialog() == true ? login.Session : null;
        }

        public static async Task OpenAsync(Window owner, int selectedPatientId)
        {
            try
            {
                await Task.Run(() => new DoctorWorkspaceRepository(DatabaseHelper.GetConnection).CheckSchema());
                var session = await AcquireSessionAsync(owner);
                if (session != null)
                    new DoctorWorkspaceWindow(session, selectedPatientId) { Owner = owner }.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Không mở được không gian bác sĩ. Kiểm tra kết nối và chạy migrations/001_doctor_workspace.sql.\n" + ex.Message,
                    "RehabTrack", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        public DoctorWorkspaceWindow(DoctorSession session, int selectedPatientId)
        {
            doctor = session; patientId = selectedPatientId;
            InitializeComponent(); SetDoctorLabel();
            DemoBanner.Visibility = doctor.IsDemo ? Visibility.Visible : Visibility.Collapsed;
            AccessPolicyText.Text = doctor.IsDemo
                ? "Chế độ demo không xác minh danh tính bác sĩ. Mọi xác nhận được ghi Bác sĩ demo và [DEMO]. Tắt DoctorWorkspaceDemoMode để dùng tài khoản thật."
                : "Phiên đăng nhập có hiệu lực 30 phút. Mỗi lần lưu hoặc hủy xác nhận đều yêu cầu xác thực bác sĩ và ghi nhận xét/lý do.";
            ReloginButton.Content = doctor.IsDemo ? "Tải lại phiên demo" : "Đăng nhập lại / đổi bác sĩ";
            ConfirmationReason.MaxLength = doctor.IsDemo ? 1993 : 2000;
            ClinicalTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Loaded += async (s, e) => await RunAsync(async () => { await LoadPatientsAsync(); await RefreshPatientAsync(); });
        }
        private void SetDoctorLabel()
        {
            DoctorLabel.Text = doctor.FullName;
            DoctorLabel.ToolTip = doctor.IsDemo ? "Chế độ demo — danh tính chưa được xác minh" :
                "Phiên bác sĩ hết hạn (UTC): " + doctor.ExpiresAtUtc.ToString("HH:mm");
        }
        private async Task RunAsync(Func<Task> work)
        {
            if (busy) return;
            busy = true; WorkspaceRoot.IsEnabled = false; StatusText.Text = "Đang xử lý…";
            try { await work(); StatusText.Text = "Đã tải/lưu dữ liệu SQL Server. Xác nhận và lịch sử hiển thị theo UTC."; }
            catch (Exception ex) { StatusText.Text = ex.Message; MessageBox.Show(this, ex.Message, "RehabTrack", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { busy = false; WorkspaceRoot.IsEnabled = true; }
        }
        private bool HasChanges() { return milestones.Any(m => m.IsConfirmed != m.OriginalConfirmed); }
        private void RequireClean()
        { if (HasChanges()) throw new InvalidOperationException("Có checkbox chưa lưu. Hãy lưu xác nhận hoặc dùng Tải lại hồ sơ để bỏ thay đổi trước."); }
        private int RequirePatient()
        { if (patientId <= 0) throw new InvalidOperationException("Vui lòng chọn bệnh nhân."); return patientId; }
        private RomMilestone RequireMilestone()
        {
            RequirePatient();
            var m = ConfirmationGrid.SelectedItem as RomMilestone;
            if (m == null || m.PatientId != patientId) throw new InvalidOperationException("Chọn một mốc trong bảng xác nhận.");
            return m;
        }
        private async Task LoadPatientsAsync()
        {
            var table = await Task.Run(() => repository.Patients(doctor));
            loading = true;
            try
            {
                PatientPicker.ItemsSource = table.DefaultView; PatientGrid.ItemsSource = table.DefaultView;
                PatientPicker.SelectedValue = patientId;
                if (PatientPicker.SelectedItem == null && table.Rows.Count > 0) PatientPicker.SelectedIndex = 0;
                var selected = PatientPicker.SelectedItem as DataRowView;
                patientId = selected == null ? -1 : (int)selected["Id"];
            }
            finally { loading = false; }
        }
        private async Task RefreshPatientAsync()
        {
            if (patientId <= 0) { ClearPatientView(); PatientInfo.Text = "Chưa có bệnh nhân. Thêm hồ sơ để bắt đầu."; return; }
            int selectedMilestone = (ConfirmationGrid.SelectedItem as RomMilestone)?.MilestoneId ?? 0;
            var data = await Task.Run(() => new
            {
                Milestones = repository.Milestones(patientId, doctor), Sessions = repository.Sessions(patientId, doctor),
                Assessments = repository.Assessments(patientId, doctor), Audit = repository.Audit(patientId, doctor)
            });
            milestones = data.Milestones; sessions = data.Sessions; assessments = data.Assessments;
            GoalGrid.ItemsSource = milestones; ConfirmationGrid.ItemsSource = milestones; MilestoneSummaryGrid.ItemsSource = milestones; ChartMilestone.ItemsSource = milestones;
            SessionGrid.ItemsSource = sessions.DefaultView; SessionPicker.ItemsSource = sessions.DefaultView;
            AuditGrid.ItemsSource = data.Audit.DefaultView; AssessmentGrid.ItemsSource = assessments.DefaultView;
            var selected = milestones.FirstOrDefault(m => m.MilestoneId == selectedMilestone) ?? milestones.FirstOrDefault(m => !m.OriginalConfirmed) ?? milestones.FirstOrDefault();
            MilestoneSummaryGrid.SelectedItem = selected;
            ConfirmationGrid.SelectedItem = selected; ChartMilestone.SelectedItem = selected;
            if (sessions.Rows.Count > 0) SessionPicker.SelectedIndex = sessions.Rows.Count - 1;
            var p = PatientPicker.SelectedItem as DataRowView;
            PatientInfo.Text = p == null ? "Chưa chọn bệnh nhân" : p["PatientCode"] + " – " + p["FullName"];
            PatientAgeText.Text = p == null || p["Age"] == DBNull.Value ? "Tuổi: chưa ghi nhận" : "Tuổi: " + p["Age"];
            PatientInjuryText.Text = p == null || string.IsNullOrWhiteSpace(p["Injury"].ToString()) ? "Chấn thương: chưa ghi nhận" : "Chấn thương: " + p["Injury"];
            PatientInjuryText.ToolTip = PatientInjuryText.Text;
            var words = p == null ? new string[0] : p["FullName"].ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            PatientInitials.Text = words.Length == 0 ? "—" : (words[0].Substring(0, 1) + (words.Length > 1 ? words[words.Length - 1].Substring(0, 1) : "")).ToUpperInvariant();
            var lastSession = sessions.AsEnumerable().Where(r => r["StartTime"] != DBNull.Value && r["EndTime"] != DBNull.Value)
                .OrderByDescending(r => (DateTime)r["StartTime"]).FirstOrDefault();
            LatestSessionText.Text = lastSession == null ? "Chưa có buổi tập" : ((DateTime)lastSession["StartTime"]).ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("vi-VN"));
            int confirmed = milestones.Count(m => m.OriginalConfirmed);
            ConfirmedSummary.Text = confirmed + " / " + milestones.Count;
            TreatmentProgress.Value = milestones.Count == 0 ? 0 : 100.0 * confirmed / milestones.Count;
            ConventionVerified.IsChecked = QualityVerified.IsChecked = HoldSupported.IsChecked = false;
            SensorReviewNotes.Clear(); ConfirmationReason.Clear(); ClinicalAngle.Clear(); ClinicalReason.Clear(); ClinicalSource.Clear();
            ClinicalTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            NewGoal(); DrawSessions(); DrawProgress(); DrawDashboard();
        }
        private async void PatientChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading || busy) return;
            if (HasChanges())
            {
                loading = true; PatientPicker.SelectedValue = patientId; loading = false;
                MessageBox.Show(this, "Hãy lưu checkbox hoặc tải lại hồ sơ trước khi đổi bệnh nhân."); return;
            }
            var row = PatientPicker.SelectedItem as DataRowView;
            patientId = row == null ? -1 : (int)row["Id"];
            ClearPatientView();
            await RunAsync(RefreshPatientAsync);
        }
        private void ClearPatientView()
        {
            milestones = new List<RomMilestone>(); sessions = null; assessments = null;
            ConfirmationGrid.ItemsSource = GoalGrid.ItemsSource = ChartMilestone.ItemsSource = null;
            SessionGrid.ItemsSource = SessionPicker.ItemsSource = AssessmentGrid.ItemsSource = AuditGrid.ItemsSource = null;
            PatientInfo.Text = "Đang tải bệnh nhân đã chọn…"; CurrentRom.Text = "—";
            PatientInitials.Text = "—"; PatientAgeText.Text = "Tuổi: —"; PatientInjuryText.Text = "Chấn thương: —";
            LatestSessionText.Text = "—"; CurrentRomContext.Text = GoalContext.Text = ""; MilestoneSummaryGrid.ItemsSource = null; DashboardChart.Series = new SeriesCollection();
            GoalSummary.Text = ConfirmedSummary.Text = "—"; TreatmentProgress.Value = 0;
            SessionChart.Series = new SeriesCollection(); ProgressChart.Series = new SeriesCollection(); NewGoal();
        }
        private void PatientGridChanged(object sender, SelectionChangedEventArgs e)
        { if (!loading && !busy && PatientGrid.SelectedItem is DataRowView row) PatientPicker.SelectedValue = row["Id"]; }
        private void Navigate_Click(object sender, RoutedEventArgs e) { ShowModule(int.Parse(((FrameworkElement)sender).Tag.ToString())); }
        private void ShowModule(int index)
        {
            Modules.SelectedIndex = index;
            foreach (var nav in SidebarNavigation.Children.OfType<RadioButton>())
                nav.IsChecked = int.Parse(nav.Tag.ToString()) == (index == 3 ? 4 : index);
        }
        private void OpenConfirmation_Click(object sender, RoutedEventArgs e) { ShowModule(4); }
        private void OpenGoals_Click(object sender, RoutedEventArgs e) { ShowModule(3); }
        private void DashboardMilestoneSelected(object sender, SelectionChangedEventArgs e)
        {
            var m = MilestoneSummaryGrid.SelectedItem as RomMilestone;
            if (m == null) return;
            ConfirmationGrid.SelectedItem = m;
            ChartMilestone.SelectedItem = m;
            DrawDashboard();
        }
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (HasChanges() && MessageBox.Show(this, "Bỏ các checkbox chưa lưu và tải lại từ database?", "Thay đổi chưa lưu", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await RunAsync(RefreshPatientAsync);
        }
        private async void Relogin_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(async () =>
            {
                var session = await AcquireSessionAsync(this);
                if (session == null) return;
                doctor = session; SetDoctorLabel();
                if (!HasChanges()) await RefreshPatientAsync();
            });
        }
        private async void AddPatient_Click(object sender, RoutedEventArgs e)
        {
            if (HasChanges()) { MessageBox.Show(this, "Lưu checkbox trước khi thêm bệnh nhân."); return; }
            if (new RehabTrack.AddPatientWindow { Owner = this }.ShowDialog() == true)
                await RunAsync(async () => { await LoadPatientsAsync(); await RefreshPatientAsync(); });
        }
        private void NewGoal()
        {
            editing = null; GoalGrid.SelectedItem = null; EditingLabel.Text = "Mục tiêu mới (không có danh sách góc mặc định)";
            Target.Clear(); GoalReason.Clear(); Movement.SelectedIndex = 0; Convention.SelectedIndex = 0;
            Tolerance.Text = "0"; Repetitions.Text = "1"; Hold.Text = "0"; ResetDelta.Text = "10"; MaxGap.Text = "0.5";
        }
        private void NewGoal_Click(object sender, RoutedEventArgs e) { NewGoal(); }
        private void GoalSelected(object sender, SelectionChangedEventArgs e)
        {
            editing = GoalGrid.SelectedItem as RomMilestone; if (editing == null) return;
            EditingLabel.Text = "Sửa mốc #" + editing.MilestoneId + " • phiên bản " + editing.Revision;
            SelectTag(Movement, editing.MovementType.ToString()); SelectTag(Convention, editing.MeasurementConvention.ToString());
            Target.Text = Format(editing.TargetAngle); Tolerance.Text = Format(editing.Tolerance); Repetitions.Text = editing.RequiredRepetitions.ToString();
            Hold.Text = Format(editing.RequiredHoldSeconds); ResetDelta.Text = Format(editing.ResetDelta); MaxGap.Text = Format(editing.MaxSampleGapSeconds); GoalReason.Clear();
        }
        private static string Format(double value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static void SelectTag(ComboBox box, string value)
        { foreach (ComboBoxItem item in box.Items) if (item.Tag.ToString() == value) { box.SelectedItem = item; break; } }
        private static T SelectedEnum<T>(ComboBox box) { return (T)Enum.Parse(typeof(T), ((ComboBoxItem)box.SelectedItem).Tag.ToString()); }
        private static double Number(TextBox box)
        {
            double number;
            if (!double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) || !RomMilestoneService.Finite(number))
                throw new ArgumentException("Giá trị số không hợp lệ: " + box.Text);
            return number;
        }
        private static int Integer(TextBox box)
        { int n; if (!int.TryParse(box.Text, out n)) throw new ArgumentException("Số lần lặp phải là số nguyên."); return n; }
        private async void SaveGoal_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(async () =>
            {
                RequireClean();
                var m = new RomMilestone { MilestoneId = editing?.MilestoneId ?? 0, Revision = editing?.Revision ?? 0,
                    PatientId = RequirePatient(), MovementType = SelectedEnum<MovementType>(Movement), MeasurementConvention = SelectedEnum<MeasurementConvention>(Convention),
                    TargetAngle = Number(Target), Tolerance = Number(Tolerance), RequiredRepetitions = Integer(Repetitions),
                    RequiredHoldSeconds = Number(Hold), ResetDelta = Number(ResetDelta), MaxSampleGapSeconds = Number(MaxGap) };
                string reason = GoalReason.Text;
                await Task.Run(() => repository.SaveMilestone(m, doctor, reason)); await RefreshPatientAsync();
            });
        }
        private void ConfirmationSelected(object sender, SelectionChangedEventArgs e)
        {
            var m = ConfirmationGrid.SelectedItem as RomMilestone;
            if (m != null && ChartMilestone.SelectedItem != m) ChartMilestone.SelectedItem = m;
            AssessmentTarget.Text = m == null ? "Chọn mốc ở bảng phía trên" : "Mốc #" + m.MilestoneId + ": " + m.MovementLabel + " " + m.TargetAngle + "° • " + m.ConventionLabel;
            ConventionVerified.IsChecked = QualityVerified.IsChecked = HoldSupported.IsChecked = false;
            ClinicalAngle.Clear(); ClinicalSource.Clear(); ClinicalReason.Clear(); SensorReviewNotes.Clear();
        }
        private async void SaveClinical_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(async () =>
            {
                RequireClean(); var m = RequireMilestone(); DateTime time;
                if (!DateTime.TryParseExact(ClinicalTime.Text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                    throw new ArgumentException("Ngày đo cần có dạng yyyy-MM-dd HH:mm:ss.");
                var clinical = new ClinicalMeasurement { Angle = Number(ClinicalAngle), Repetitions = Integer(ClinicalReps), HoldSeconds = Number(ClinicalHold),
                    MeasuredAt = time, Source = ClinicalSource.Text, Reason = ClinicalReason.Text };
                var result = await Task.Run(() => repository.AssessClinical(m, clinical, doctor));
                await RefreshPatientAsync(); MessageBox.Show(this, "Đã lưu đánh giá: " + EvidenceName(result.Status) + ".\n" + result.Reason);
            });
        }
        private async void EvaluateSession_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(async () =>
            {
                RequireClean(); var m = RequireMilestone();
                if (SessionPicker.SelectedValue == null) throw new InvalidOperationException("Chọn một buổi tập.");
                int sid = (int)SessionPicker.SelectedValue; bool convention = ConventionVerified.IsChecked == true, quality = QualityVerified.IsChecked == true, hold = HoldSupported.IsChecked == true;
                string notes = SensorReviewNotes.Text;
                var result = await Task.Run(() => repository.AssessSession(m, sid, convention, quality, hold, notes, doctor));
                await RefreshPatientAsync(); MessageBox.Show(this, "Đã lưu đánh giá: " + EvidenceName(result.Status) + ".\n" + result.Reason);
            });
        }
        private static string EvidenceName(AssessmentStatus status)
        { return status == AssessmentStatus.MeasuredAchieved ? "Đo đạt, chờ bác sĩ xác nhận" : status == AssessmentStatus.NotAchieved ? "Chưa đạt" : "Chưa đủ bằng chứng"; }
        private async void SaveConfirmations_Click(object sender, RoutedEventArgs e)
        {
            var changes = milestones.Where(m => m.IsConfirmed != m.OriginalConfirmed).Select(m => new ConfirmationChange
            {
                MilestoneId = m.MilestoneId, Revision = m.Revision, AssessmentId = m.AssessmentId,
                ExpectedConfirmationId = m.ConfirmationId, Confirm = m.IsConfirmed, Reason = ConfirmationReason.Text
            }).ToList();
            if (changes.Count == 0) { MessageBox.Show(this, "Chưa có checkbox thay đổi."); return; }
            if (string.IsNullOrWhiteSpace(ConfirmationReason.Text)) { MessageBox.Show(this, "Nhập nhận xét xác nhận hoặc lý do hủy trước khi lưu."); return; }
            await RunAsync(async () =>
            {
                var session = await AcquireSessionAsync(this);
                if (session == null) return;
                doctor = session; SetDoctorLabel();
                foreach (var change in changes) RomMilestoneService.ValidateConfirmation(patientId, milestones.Single(m => m.MilestoneId == change.MilestoneId), change, doctor);
                await Task.Run(() => repository.SaveConfirmations(patientId, changes, doctor)); await RefreshPatientAsync();
            });
        }
        private void OpenAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (patientId <= 0) return;
            new AnalysisTab(patientId, doctor) { Owner = this }.ShowDialog();
        }
        private void ChartMilestoneChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = ChartMilestone.SelectedItem as RomMilestone;
            if (selected != null && MilestoneSummaryGrid.SelectedItem != selected)
                MilestoneSummaryGrid.SelectedItem = selected;
            DrawProgress(); DrawDashboard();
        }
        private static Axis DateAxis()
        { return new Axis { Title = "Ngày", LabelFormatter = x => x > 0 && x < 2958466 ? DateTime.FromOADate(x).ToString("dd/MM HH:mm") : "" }; }
        private void DrawSessions()
        {
            var peak = new ChartValues<ObservablePoint>(); var avg = new ChartValues<ObservablePoint>();
            foreach (DataRow r in sessions.Rows)
            {
                if (r["StartTime"] == DBNull.Value || r["EndTime"] == DBNull.Value) continue;
                double x = ((DateTime)r["StartTime"]).ToOADate();
                if (r["PeakROM"] != DBNull.Value) peak.Add(new ObservablePoint(x, Convert.ToDouble(r["PeakROM"])));
                if (r["AvgROM"] != DBNull.Value) avg.Add(new ObservablePoint(x, Convert.ToDouble(r["AvgROM"])));
            }
            SessionChart.Series = new SeriesCollection { new LineSeries { Title = "PeakROM ghi nhận", Values = peak, Fill = Brushes.Transparent }, new LineSeries { Title = "AvgROM ghi nhận", Values = avg, Fill = Brushes.Transparent } };
            SessionChart.AxisX.Clear(); SessionChart.AxisX.Add(DateAxis());
            SessionChart.AxisY.Clear(); SessionChart.AxisY.Add(new Axis { Title = "Góc ghi nhận (°)" });
        }
        private void DrawProgress()
        {
            if (ProgressChart == null) return;
            ProgressChart.Series = new SeriesCollection();
            var m = ChartMilestone.SelectedItem as RomMilestone;
            if (m == null || assessments == null) { ChartCriteria.Text = "Chưa có mốc được chọn"; return; }
            ChartCriteria.Text = m.MovementLabel + " • " + m.ConventionLabel + " • mục tiêu " + m.TargetAngle + "° (phiên bản " + m.Revision + ")";
            var points = new ChartValues<ObservablePoint>(); var achieved = new ChartValues<ObservablePoint>(); var confirmed = new ChartValues<ObservablePoint>();
            foreach (DataRow r in assessments.AsEnumerable().Where(r => (int)r["MilestoneId"] == m.MilestoneId && (int)r["MilestoneRevision"] == m.Revision).OrderBy(r => (DateTime)r["MeasurementTime"]))
            {
                if (r["MeasuredAngle"] == DBNull.Value || r["AssessmentStatus"].ToString() == "InsufficientEvidence") continue;
                double x = ((DateTime)r["MeasurementTime"]).ToOADate(), y = (double)r["MeasuredAngle"];
                points.Add(new ObservablePoint(x, y));
                if (r["AssessmentStatus"].ToString() == "MeasuredAchieved") achieved.Add(new ObservablePoint(x, y));
            }
            if (m.ConfirmedAt.HasValue && m.MeasuredAngle.HasValue) confirmed.Add(new ObservablePoint(DateTime.SpecifyKind(m.ConfirmedAt.Value, DateTimeKind.Utc).ToLocalTime().ToOADate(), m.MeasuredAngle.Value));
            double min = points.Count == 0 ? DateTime.Now.ToOADate() : points.Min(p => p.X);
            double max = Math.Max(min + 0.001, points.Count > 0 ? points.Max(p => p.X) : min + 1);
            if (confirmed.Count > 0) { min = Math.Min(min, confirmed[0].X); max = Math.Max(max, confirmed[0].X); }
            ProgressChart.Series = new SeriesCollection
            {
                new LineSeries { Title="Kết quả đo", Values=points, Fill=Brushes.Transparent, LineSmoothness=0 },
                new LineSeries { Title="Mục tiêu", Values=new ChartValues<ObservablePoint> { new ObservablePoint(min,m.TargetAngle), new ObservablePoint(max,m.TargetAngle) }, Fill=Brushes.Transparent, PointGeometry=null, Stroke=Brushes.Orange },
                new ScatterSeries { Title="Đo đạt", Values=achieved, Fill=Brushes.SteelBlue },
                new ScatterSeries { Title="Xác nhận hiện hành (ngày xác nhận)", Values=confirmed, Fill=Brushes.SeaGreen }
            };
            ProgressChart.AxisX.Clear(); ProgressChart.AxisX.Add(DateAxis());
            ProgressChart.AxisY.Clear(); ProgressChart.AxisY.Add(new Axis { Title = "Góc theo quy ước (°)" });
        }
        private void DrawDashboard()
        {
            if (DashboardChart == null) return;
            DashboardChart.Series = new SeriesCollection();
            DashboardChart.AxisX.Clear(); DashboardChart.AxisX.Add(DateAxis());
            DashboardChart.AxisY.Clear(); DashboardChart.AxisY.Add(new Axis { Title = "ROM (°)" });
            var m = ChartMilestone.SelectedItem as RomMilestone;
            CurrentRom.Text = "—"; GoalSummary.Text = m == null ? "—" : m.TargetAngle.ToString("0.0") + "°";
            CurrentRomContext.Text = m == null ? "Chưa có đánh giá" : m.MovementLabel;
            GoalContext.Text = m == null ? "Chưa thiết lập mục tiêu" : m.ConventionLabel;
            DashboardChartCaption.Text = m == null ? "Chọn hoặc tạo một mốc để theo dõi tiến triển." :
                "Mốc #" + m.MilestoneId + " • " + m.MovementLabel + " • " + m.ConventionLabel;
            if (m == null || assessments == null) return;
            // Compare only valid assessments for this specific patient, goal revision and
            // angle convention. Legacy unclassified PeakROM is kept in the ROM detail view.
            var rows = assessments.AsEnumerable().Where(r => (int)r["MilestoneId"] == m.MilestoneId &&
                (int)r["MilestoneRevision"] == m.Revision && r["MeasuredAngle"] != DBNull.Value &&
                r["MeasurementTime"] != DBNull.Value && r["AssessmentStatus"].ToString() != "InsufficientEvidence")
                .OrderBy(r => (DateTime)r["MeasurementTime"]).ThenBy(r => (int)r["AssessmentId"]).ToList();
            if (rows.Count == 0) { DashboardChartCaption.Text += " • Chưa có đánh giá hợp lệ"; return; }
            CurrentRom.Text = ((double)rows.Last()["MeasuredAngle"]).ToString("0.0") + "°";
            var values = new ChartValues<ObservablePoint>(rows.Select(r => new ObservablePoint(((DateTime)r["MeasurementTime"]).ToOADate(), (double)r["MeasuredAngle"])));
            var teal = (Color)ColorConverter.ConvertFromString("#0BAB9D");
            DashboardChart.Series.Add(new LineSeries { Title = "Kết quả đánh giá ROM", Values = values,
                Stroke = new SolidColorBrush(teal), StrokeThickness = 3, PointGeometrySize = 8,
                PointForeground = Brushes.White, LineSmoothness = 0,
                Fill = new LinearGradientBrush(Color.FromArgb(65, teal.R, teal.G, teal.B), Colors.Transparent, 90) });
            DashboardChart.AxisY[0].Sections = new SectionsCollection { new AxisSection { Value = m.TargetAngle, Stroke = Brushes.Silver, StrokeThickness = 1 } };
            DashboardChart.AxisY[0].MinValue = Math.Max(0, Math.Floor(Math.Min(values.Min(v => v.Y), m.TargetAngle) / 10) * 10 - 10);
            DashboardChart.AxisY[0].MaxValue = Math.Min(180, Math.Ceiling(Math.Max(values.Max(v => v.Y), m.TargetAngle) / 10) * 10 + 10);
        }
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }
            if (HasChanges() && MessageBox.Show(this, "Đóng và bỏ checkbox chưa lưu?", "Thay đổi chưa lưu", MessageBoxButton.YesNo) != MessageBoxResult.Yes) e.Cancel = true;
        }
    }
}
