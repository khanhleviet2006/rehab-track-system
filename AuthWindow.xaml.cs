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
        private void BtnHoSoMoi_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Chức năng tạo hồ sơ bệnh nhân mới đang được phát triển.", "Thông báo");
        }
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }
        private void BtnTruyCap_Click(object sender, RoutedEventArgs e)
        {
            string msbnInput = TxtMsbn.Text.Trim();

            if (string.IsNullOrWhiteSpace(msbnInput))
            {
                MessageBox.Show("Vui lòng nhập Mã số bệnh nhân!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isLoginSuccess = false;
            string query = "SELECT Id FROM Users WHERE PatientCode = @MSBN";

            using (SqlCommand cmd = new SqlCommand(query))
            {
                cmd.Parameters.AddWithValue("@MSBN", msbnInput);

                using (SqlDataReader reader = DatabaseHelper.ExecuteReader(cmd))
                {
                    if (reader != null && reader.Read())
                    {
                        isLoginSuccess = true;
                        GlobalData.CurrentUserId = Convert.ToInt32(reader["Id"]);
                    }
                }
            }

            if (isLoginSuccess)
            {
                MainWindow main = new MainWindow();
                main.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Mã số bệnh nhân không tồn tại. Vui lòng kiểm tra lại!", "Thất bại", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}