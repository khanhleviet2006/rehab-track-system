using System;
using System.Data.SqlClient;
using System.Windows;
using System.Windows.Input;

namespace AngleMonitorWPF
{
    public partial class AuthWindow : Window
    {
        public AuthWindow()
        {
            InitializeComponent();
        }

        // Hàm xử lý khi bấm nút HỒ SƠ MỚI
        private void BtnHoSoMoi_Click(object sender, RoutedEventArgs e)
        {
            // Đã sửa lỗi thiếu dấu chấm phẩy ở đây
            MessageBox.Show("Chức năng tạo hồ sơ bệnh nhân mới đang được phát triển.", "Thông báo");
        }

        // Hàm xử lý khi bấm nút X (Đóng cửa sổ)
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Hàm xử lý việc nắm kéo thanh tiêu đề
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        // Hàm xử lý khi bấm nút TRUY CẬP (Đã thêm luồng xử lý Database)
        private void BtnTruyCap_Click(object sender, RoutedEventArgs e)
        {
            string msbnInput = TxtMsbn.Text.Trim();

            if (string.IsNullOrWhiteSpace(msbnInput))
            {
                MessageBox.Show("Vui lòng nhập Mã số bệnh nhân!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isLoginSuccess = false;

            // Câu lệnh SQL truy vấn vào bảng Users của RehabDB
            string query = "SELECT Id FROM Users WHERE PatientCode = @MSBN";

            using (SqlCommand cmd = new SqlCommand(query))
            {
                cmd.Parameters.AddWithValue("@MSBN", msbnInput);

                // Gọi lớp DatabaseHelper để chạy SQL
                using (SqlDataReader reader = DatabaseHelper.ExecuteReader(cmd))
                {
                    if (reader != null && reader.Read())
                    {
                        isLoginSuccess = true;

                        // Lưu lại ID người dùng vào GlobalData
                        GlobalData.CurrentUserId = Convert.ToInt32(reader["Id"]);
                    }
                }
            }

            if (isLoginSuccess)
            {
                // Mở giao diện theo dõi góc khớp (MainWindow)
                MainWindow main = new MainWindow();
                main.Show();

                // Đóng màn hình đăng nhập
                this.Close();
            }
            else
            {
                MessageBox.Show("Mã số bệnh nhân không tồn tại. Vui lòng kiểm tra lại!", "Thất bại", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}