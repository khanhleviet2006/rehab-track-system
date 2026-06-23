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
    // Model bệnh nhân 
    public class Patient
    {
        public int Id { get; set; }
        public string MSBN { get; set; }
        public string CCCD { get; set; }
        public string HoVaTen { get; set; }
        public int Tuoi { get; set; }
        public string LoaiChanThuong { get; set; } 
        public string KhopDieuTri { get; set; }
        public string TrangThai { get; set; }
        public int SoBuoiTap { get; set; }
        public string Initials => HoVaTen?.Length >= 2
            ? $"{HoVaTen[0]}{HoVaTen.Split(' ').LastOrDefault()?.FirstOrDefault()}"
            : HoVaTen?.Substring(0, 1) ?? "?";
    }

    public partial class PatientSelectionWindow : Window
    {
        // KHAI BÁO BIẾN DỮ LIỆU
        private List<Patient> _allPatients = new List<Patient>();
        private List<Patient> _filtered;
        private readonly string[] _avatarColors = { "#4C51BF", "#DD6B20", "#6B46C1", "#2B6CB0", "#276749", "#C53030" };

        public PatientSelectionWindow()
        {
            InitializeComponent();
            tbDate.Text = DateTime.Now.ToString("dd MMM yyyy");
            LoadPatientsFromDatabase();
        }

        // TẢI DỮ LIỆU TỪ SQL SERVER
        private void LoadPatientsFromDatabase()
        {
            _allPatients.Clear();

            try
            {
                using (SqlConnection conn = new SqlConnection(@"Server=.;Database=RehabDB;Integrated Security=True;"))
                {
                    conn.Open();
                    string sql = @"
                        SELECT 
                        u.Id, 
                        u.PatientCode, 
                        u.CCCD, -- THÊM CỘT NÀY VÀO SELECT
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
                            string khopDieuTri = "Khác";
                            if (injury.Contains("Knee")) khopDieuTri = "Knee";
                            else if (injury.Contains("Shoulder")) khopDieuTri = "Shoulder";
                            else if (injury.Contains("Hip")) khopDieuTri = "Hip";

                            _allPatients.Add(new Patient
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                MSBN = reader["PatientCode"].ToString(),
                                CCCD = reader["CCCD"] != DBNull.Value ? reader["CCCD"].ToString() : "", // GÁN GIÁ TRỊ CCCD
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
            _filtered = new List<Patient>(_allPatients);
            if (txtSearch != null && !string.IsNullOrEmpty(txtSearch.Text))
            {
                txtSearch_TextChanged(null, null);
            }
            else
            {
                RenderCards(_filtered);
                UpdateCount(_filtered.Count);
            }
        }
        // TÌM KIẾM
        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            tbPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text)
                ? Visibility.Visible : Visibility.Collapsed;

            string q = txtSearch.Text.Trim().ToLower();
            _filtered = string.IsNullOrEmpty(q)
                ? new List<Patient>(_allPatients)
                : _allPatients.Where(p =>
                    p.MSBN.ToLower().Contains(q) ||
                    (p.CCCD != null && p.CCCD.Contains(q)) ||
                    p.HoVaTen.ToLower().Contains(q) ||
                    p.LoaiChanThuong.ToLower().Contains(q) ||
                    p.KhopDieuTri.ToLower().Contains(q)).ToList();

            RenderCards(_filtered);
            UpdateCount(_filtered.Count);
        }
        // RENDER CARDS
        private void RenderCards(List<Patient> patients)
        {
            panelEmpty.Visibility = patients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var displayList = patients.Select((p, index) => new
            {
                Id = p.Id,
                STT = index + 1,
                Initials = p.Initials,
                FullName = p.HoVaTen,
                Age = p.Tuoi,
                PatientCode = p.MSBN,
                CCCD = !string.IsNullOrEmpty(p.CCCD) ? p.CCCD : "Chưa có CCCD", 
                InjuryType = p.LoaiChanThuong,
                BodyPart = p.KhopDieuTri,
                Status = string.IsNullOrWhiteSpace(p.TrangThai) ? "Active" : p.TrangThai,
                SessionCount = p.SoBuoiTap
            }).ToList();
            dgPatients.ItemsSource = displayList;
        }
        private void BtnViewPatient_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            dynamic patientContext = button?.DataContext;

            if (patientContext != null)
            {
                GlobalData.CurrentUserId = patientContext.Id;
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
            }
        }
        // XÂY DỰNG MỘT CARD
        private Border BuildCard(Patient p, string avatarColor)
        {
            string badgeBg = p.KhopDieuTri == "Shoulder" ? "#EBF8FF" : "#E6FFFA";
            string badgeFg = p.KhopDieuTri == "Shoulder" ? "#2B6CB0" : "#276749";
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
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            headerRow.Children.Add(avatar);
            headerRow.Children.Add(nameStack);
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
            var injuryBlock = new TextBlock
            {
                Text = p.LoaiChanThuong,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            };
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
            var divider = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var content = new StackPanel();
            content.Children.Add(headerRow);
            content.Children.Add(badgeRow);
            content.Children.Add(injuryBlock);
            content.Children.Add(divider);
            content.Children.Add(footerRow);
            var card = new Border
            {
                Style = (Style)Resources["PatientCard"],
                Child = content,
                Tag = p
            };
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
            card.MouseLeftButtonUp += Card_Click;

            return card;
        }
        // XỬ LÝ SỰ KIỆN CLICKS VÀ CẬP NHẬT GIAO DIỆN
        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is Patient p)
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
            }
        }

        private void BtnNewPatient_Click(object sender, RoutedEventArgs e)
        {
            AddPatientWindow addPatientWin = new AddPatientWindow();
            bool? result = addPatientWin.ShowDialog();
            if (result == true)
            {
                LoadPatientsFromDatabase();
            }
        }

        private void UpdateCount(int count)
        {
            if (tbPatientCount != null)
            {
                tbPatientCount.Text = $"{count} bệnh nhân";
            }
        }
    }
}