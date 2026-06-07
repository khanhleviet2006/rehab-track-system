using System;
using System.Windows;

namespace AngleMonitorWPF
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            txtMinDelta.Text = DeviceSettings.MinDeltaForRep.ToString();
            txtNoiseMargin.Text = DeviceSettings.NoiseMargin.ToString();
            txtWeight.Text = DeviceSettings.DumbbellWeight.ToString();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // 1. Lưu Hiệu đỉnh - đáy
            if (double.TryParse(txtMinDelta.Text.Trim(), out double newDelta) && newDelta > 0)
            {
                DeviceSettings.MinDeltaForRep = newDelta;
            }
            else
            {
                MessageBox.Show("Vui lòng nhập số hợp lệ lớn hơn 0 cho Hiệu đỉnh - đáy!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Lưu Ngưỡng nhiễu
            if (double.TryParse(txtNoiseMargin.Text.Trim(), out double newNoise) && newNoise >= 0)
            {
                DeviceSettings.NoiseMargin = newNoise;
            }
            else
            {
                MessageBox.Show("Vui lòng nhập số hợp lệ cho Ngưỡng nhiễu!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Lưu Mức tạ
            if (double.TryParse(txtWeight.Text.Trim(), out double newWeight) && newWeight >= 0)
            {
                DeviceSettings.DumbbellWeight = newWeight;
            }
            else
            {
                MessageBox.Show("Vui lòng nhập số hợp lệ cho Mức tạ!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show("Đã lưu cấu hình thiết bị thành công!", "RehabTrack", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true;
            this.Close();
        }
    }
}