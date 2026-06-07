using System;
using System.Data.SqlClient;
using System.Threading.Tasks; // Cần thêm thư viện này cho async/await
using System.Windows;
using System.Windows.Controls;

namespace RehabTrack
{
    public partial class AddPatientWindow : Window
    {
        private const string ConnectionString =
            @"Server=DESKTOP-GUAOG8U;Database=RehabDB;Integrated Security=True;";

        // Tối ưu 1: Dùng chung một đối tượng Random để tiết kiệm RAM và tránh trùng MSBN
        private static readonly Random _random = new Random();

        public AddPatientWindow()
        {
            InitializeComponent();
            GeneratePatientID();
        }

        private void GeneratePatientID()
        {
            string year = DateTime.Now.Year.ToString();
            string rand = _random.Next(100, 999).ToString();
            txtPatientID.Text = $"BN{year}{rand}";
        }

        // Tối ưu 2: Thêm từ khóa 'async' vào sự kiện Click
        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate()) return;

            // Tối ưu 3: Vô hiệu hóa nút để tránh người dùng click 2 lần liên tục
            var button = sender as Button;
            if (button != null) button.IsEnabled = false;

            try
            {
                string patientCode = txtPatientID.Text.Trim();
                string fullName = txtFullName.Text.Trim();
                int age = int.Parse(txtAge.Text.Trim());
                string gender = ((ComboBoxItem)cboGender.SelectedItem).Content.ToString();
                string injuryMain = ((ComboBoxItem)cboInjury.SelectedItem)?.Content.ToString() ?? "";
                string injuryNote = txtInjuryDetail.Text.Trim();
                string injury = string.IsNullOrEmpty(injuryNote)
                                    ? injuryMain
                                    : $"{injuryMain} – {injuryNote}";

                double? height = string.IsNullOrEmpty(txtHeight.Text) ? (double?)null
                                 : double.Parse(txtHeight.Text.Trim());
                double? weight = string.IsNullOrEmpty(txtWeight.Text) ? (double?)null
                                 : double.Parse(txtWeight.Text.Trim());

                using (var conn = new SqlConnection(ConnectionString))
                {
                    // Chạy bất đồng bộ, giao diện vẫn có thể kéo/thả mượt mà trong lúc chờ
                    await conn.OpenAsync();

                    string sql = @"
                        INSERT INTO dbo.Users
                            (PatientCode, FullName, Age, Gender, Height, Weight, Injury, CreatedAt)
                        VALUES
                            (@PatientCode, @FullName, @Age, @Gender, @Height, @Weight, @Injury, @CreatedAt)";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@PatientCode", patientCode);
                        cmd.Parameters.AddWithValue("@FullName", fullName);
                        cmd.Parameters.AddWithValue("@Age", age);
                        cmd.Parameters.AddWithValue("@Gender", gender);
                        cmd.Parameters.AddWithValue("@Height", (object)height ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Weight", (object)weight ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Injury", injury);
                        cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                        // Thực thi truy vấn bất đồng bộ
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                MessageBox.Show(
                    $"✔ Đã lưu hồ sơ bệnh nhân {fullName} ({patientCode}) thành công!",
                    "RehabTrack",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (FormatException)
            {
                ShowError("Tuổi, chiều cao hoặc cân nặng nhập không đúng định dạng số.");
            }
            catch (SqlException ex)
            {
                ShowError($"Lỗi cơ sở dữ liệu: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowError($"Lỗi không xác định: {ex.Message}");
            }
            finally
            {
                // Bật lại nút nếu có lỗi xảy ra (để người dùng sửa thông tin và bấm lưu lại)
                if (button != null) button.IsEnabled = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Các hàm Validate, ShowError, HideError giữ nguyên như cũ
        private bool Validate()
        {
            HideError();

            if (string.IsNullOrWhiteSpace(txtFullName.Text))
            { ShowError("Vui lòng nhập họ và tên bệnh nhân."); return false; }

            if (string.IsNullOrWhiteSpace(txtAge.Text))
            { ShowError("Vui lòng nhập tuổi bệnh nhân."); return false; }

            if (!int.TryParse(txtAge.Text, out int age) || age <= 0 || age > 120)
            { ShowError("Tuổi không hợp lệ (1 – 120)."); return false; }

            if (cboInjury.SelectedItem == null)
            { ShowError("Vui lòng chọn loại chấn thương."); return false; }

            if (!string.IsNullOrWhiteSpace(txtHeight.Text) &&
                !double.TryParse(txtHeight.Text, out _))
            { ShowError("Chiều cao không đúng định dạng (ví dụ: 170.5)."); return false; }

            if (!string.IsNullOrWhiteSpace(txtWeight.Text) &&
                !double.TryParse(txtWeight.Text, out _))
            { ShowError("Cân nặng không đúng định dạng (ví dụ: 65.0)."); return false; }

            return true;
        }

        private void ShowError(string msg)
        {
            txtError.Text = "⚠ " + msg;
            txtError.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            txtError.Visibility = Visibility.Collapsed;
        }
    }
}