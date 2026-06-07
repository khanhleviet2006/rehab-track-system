using RehabTrack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Data.SqlClient;

namespace AngleMonitorWPF
{
    // -------------------------------------------------------
    // Model bệnh nhân 
    // -------------------------------------------------------
    public class Patient
    {
        public int Id { get; set; }
        public string MSBN { get; set; }
        public string HoVaTen { get; set; }
        public int Tuoi { get; set; }
        public string LoaiChanThuong { get; set; }   // VD: "ACL Tear"
        public string KhopDieuTri { get; set; }      // VD: "Knee", "Shoulder"
        public string TrangThai { get; set; }        // VD: "Recovery", "Active"
        public int SoBuoiTap { get; set; }

        // Màu avatar tự động từ tên
        public string Initials => HoVaTen?.Length >= 2
            ? $"{HoVaTen[0]}{HoVaTen.Split(' ').LastOrDefault()?.FirstOrDefault()}"
            : HoVaTen?.Substring(0, 1) ?? "?";
    }

    public partial class PatientSelectionWindow : Window
    {
        // -------------------------------------------------------
        // KHAI BÁO BIẾN DỮ LIỆU
        // -------------------------------------------------------
        private List<Patient> _allPatients = new List<Patient>();
        private List<Patient> _filtered;
        private readonly string[] _avatarColors = { "#4C51BF", "#DD6B20", "#6B46C1", "#2B6CB0", "#276749", "#C53030" };

        public PatientSelectionWindow()
        {
            InitializeComponent();
            tbDate.Text = DateTime.Now.ToString("dd MMM yyyy");

            // Gọi hàm kéo dữ liệu thật từ DB
            LoadPatientsFromDatabase();
        }

        // -------------------------------------------------------
        // TẢI DỮ LIỆU TỪ SQL SERVER
        // -------------------------------------------------------
        private void LoadPatientsFromDatabase()
        {
            _allPatients.Clear(); // Xóa danh sách cũ

            try
            {
                // Dùng chuỗi kết nối trực tiếp
                using (SqlConnection conn = new SqlConnection(@"Server=DESKTOP-GUAOG8U;Database=RehabDB;Integrated Security=True;"))
                {
                    conn.Open();
                    // Lấy bệnh nhân mới nhất lên đầu (ORDER BY CreatedAt DESC)

                    string sql = @"
    SELECT 
        u.Id, 
        u.PatientCode, 
        u.FullName, 
        u.Age, 
        u.Injury,
        (SELECT COUNT(*) FROM dbo.TrainingSessions ts WHERE ts.UserId = u.Id) AS SessionCount
    FROM dbo.Users u 
    ORDER BY u.CreatedAt DESC";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string injury = reader["Injury"] != DBNull.Value ? reader["Injury"].ToString() : "";

                            // Tạm thời suy ra khớp điều trị từ tên chấn thương do DB chưa có cột KhopDieuTri
                            string khopDieuTri = "Khác";
                            if (injury.Contains("Knee")) khopDieuTri = "Knee";
                            else if (injury.Contains("Shoulder")) khopDieuTri = "Shoulder";
                            else if (injury.Contains("Hip")) khopDieuTri = "Hip";

                            _allPatients.Add(new Patient
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                MSBN = reader["PatientCode"].ToString(),
                                HoVaTen = reader["FullName"].ToString(),
                                Tuoi = reader["Age"] != DBNull.Value ? Convert.ToInt32(reader["Age"]) : 0,
                                LoaiChanThuong = injury,
                                KhopDieuTri = khopDieuTri,
                                SoBuoiTap = reader["SessionCount"] != DBNull.Value ? Convert.ToInt32(reader["SessionCount"]) : 0
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi tải dữ liệu: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Đồng bộ danh sách tìm kiếm và vẽ lại thẻ
            _filtered = new List<Patient>(_allPatients);

            // Xử lý trường hợp đang gõ tìm kiếm mà lại tải lại trang
            if (txtSearch != null && !string.IsNullOrEmpty(txtSearch.Text))
            {
                txtSearch_TextChanged(null, null); // Tự động lọc lại
            }
            else
            {
                RenderCards(_filtered);
                UpdateCount(_filtered.Count);
            }
        }

        // -------------------------------------------------------
        // TÌM KIẾM
        // -------------------------------------------------------
        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            tbPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text)
                ? Visibility.Visible : Visibility.Collapsed;

            string q = txtSearch.Text.Trim().ToLower();
            _filtered = string.IsNullOrEmpty(q)
                ? new List<Patient>(_allPatients)
                : _allPatients.Where(p =>
                    p.MSBN.ToLower().Contains(q) ||
                    p.HoVaTen.ToLower().Contains(q) ||
                    p.LoaiChanThuong.ToLower().Contains(q) ||
                    p.KhopDieuTri.ToLower().Contains(q)).ToList();

            RenderCards(_filtered);
            UpdateCount(_filtered.Count);
        }

        // -------------------------------------------------------
        // RENDER CARDS
        // -------------------------------------------------------
        private void RenderCards(List<Patient> patients)
        {
            // 1. Hiển thị giao diện "Không tìm thấy" nếu danh sách trống
            panelEmpty.Visibility = patients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 2. Map (Ánh xạ) dữ liệu từ class Patient sang các thuộc tính Binding của XAML
            var displayList = patients.Select((p, index) => new
            {
                Id = p.Id,
                STT = index + 1,
                Initials = p.Initials,              // Dùng luôn thuộc tính xịn bạn đã viết sẵn
                FullName = p.HoVaTen,               // Map sang Binding FullName
                Age = p.Tuoi,                       // Map sang Binding Age
                PatientCode = p.MSBN,                 // Map sang Binding PatientID
                InjuryType = p.LoaiChanThuong,      // Map sang Binding InjuryType
                BodyPart = p.KhopDieuTri,           // Map sang Binding BodyPart
                Status = string.IsNullOrWhiteSpace(p.TrangThai) ? "Active" : p.TrangThai,
                SessionCount = p.SoBuoiTap          // Map sang Binding SessionCount
            }).ToList();

            // 3. Đổ dữ liệu vào bảng
            dgPatients.ItemsSource = displayList;
        }
        private void BtnViewPatient_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;

            // Dùng dynamic để C# có thể tự do đọc thuộc tính Id từ biến vô danh (Anonymous Type)
            dynamic patientContext = button?.DataContext;

            if (patientContext != null)
            {
                // 1. Lưu ID bệnh nhân vào bộ nhớ chung
                GlobalData.CurrentUserId = patientContext.Id;

                // 2. Khởi tạo và mở cửa sổ MainWindow (Dashboard)
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();

                // 3. Đóng cửa sổ Chọn bệnh nhân hiện tại
                this.Close();
            }
        }

        // -------------------------------------------------------
        // XÂY DỰNG MỘT CARD
        // -------------------------------------------------------
        private Border BuildCard(Patient p, string avatarColor)
        {
            // Xác định màu badge khớp
            string badgeBg = p.KhopDieuTri == "Shoulder" ? "#EBF8FF" : "#E6FFFA";
            string badgeFg = p.KhopDieuTri == "Shoulder" ? "#2B6CB0" : "#276749";

            // --- Avatar ---
            var avatar = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(avatarColor)),
                Margin = new Thickness(0, 0, 12, 0)
            };
            avatar.Child = new TextBlock
            {
                Text = p.Initials.ToUpper(),
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // --- Tên + tuổi ---
            var nameBlock = new TextBlock
            {
                Text = p.HoVaTen,
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 32, 44))
            };
            var ageBlock = new TextBlock
            {
                Text = $"{p.Tuoi} tuổi · {p.MSBN}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 150))
            };
            var nameStack = new StackPanel();
            nameStack.Children.Add(nameBlock);
            nameStack.Children.Add(ageBlock);

            // --- Header row ---
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            headerRow.Children.Add(avatar);
            headerRow.Children.Add(nameStack);

            // --- Badge khớp ---
            var jointBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeBg)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 8, 0)
            };
            jointBadge.Child = new TextBlock
            {
                Text = p.KhopDieuTri,
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeFg))
            };

            // --- Badge trạng thái ---
            var statusBadge = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            statusBadge.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromRgb(113, 128, 150)), VerticalAlignment = VerticalAlignment.Center });
            statusBadge.Children.Add(new TextBlock
            {
                Text = p.TrangThai,
                FontSize = 12,
                Margin = new Thickness(5, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 150))
            });

            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            badgeRow.Children.Add(jointBadge);
            badgeRow.Children.Add(statusBadge);

            // --- Tên chấn thương ---
            var injuryBlock = new TextBlock
            {
                Text = p.LoaiChanThuong,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // --- Footer: số buổi + mũi tên ---
            var sessionText = new TextBlock
            {
                Text = $"{p.SoBuoiTap} buổi tập",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var arrow = new TextBlock
            {
                Text = "→",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var footerRow = new Grid();
            footerRow.Children.Add(sessionText);
            footerRow.Children.Add(arrow);
            arrow.HorizontalAlignment = HorizontalAlignment.Right;

            // --- Divider ---
            var divider = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                Margin = new Thickness(0, 0, 0, 12)
            };

            // --- Stack chính của card ---
            var content = new StackPanel();
            content.Children.Add(headerRow);
            content.Children.Add(badgeRow);
            content.Children.Add(injuryBlock);
            content.Children.Add(divider);
            content.Children.Add(footerRow);

            // --- Border ngoài (card) ---
            var card = new Border
            {
                Style = (Style)Resources["PatientCard"],
                Child = content,
                Tag = p
            };

            // Hover effect
            card.MouseEnter += (s, e) =>
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(29, 158, 117));
                card.BorderThickness = new Thickness(1.5);
            };
            card.MouseLeave += (s, e) =>
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                card.BorderThickness = new Thickness(1);
            };

            // Gắn sự kiện click vào card
            card.MouseLeftButtonUp += Card_Click;

            return card;
        }

        // -------------------------------------------------------
        // XỬ LÝ SỰ KIỆN CLICKS VÀ CẬP NHẬT GIAO DIỆN
        // -------------------------------------------------------

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is Patient p)
            {
                // Mở cửa sổ chính
                var mainWindow = new MainWindow();
                mainWindow.Show();

                // Đóng màn hình chọn bệnh nhân
                this.Close();
            }
        }

        private void BtnNewPatient_Click(object sender, RoutedEventArgs e)
        {
            AddPatientWindow addPatientWin = new AddPatientWindow();

            // Hiển thị form và chờ người dùng thao tác
            bool? result = addPatientWin.ShowDialog();

            // Nếu người dùng lưu thành công (DialogResult = true)
            if (result == true)
            {
                // Cập nhật lại giao diện ngay lập tức
                LoadPatientsFromDatabase();
            }
        }

        private void UpdateCount(int count)
        {
            // Kiểm tra null để tránh lỗi khi màn hình đang khởi tạo chưa xong
            if (tbPatientCount != null)
            {
                tbPatientCount.Text = $"{count} bệnh nhân";
            }
        }
    }
}