using System;
using System.Threading.Tasks;
using System.Windows;
using AngleMonitorWPF.Rom;

namespace AngleMonitorWPF
{
    public partial class DoctorLoginWindow : Window
    {
        public DoctorSession Session { get; private set; }
        public DoctorLoginWindow() { InitializeComponent(); Loaded += (s, e) => Username.Focus(); }
        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            string username = Username.Text, password = Password.Password;
            LoginButton.IsEnabled = false; Error.Text = "Đang xác thực…";
            try
            {
                Session = await Task.Run(() => new DoctorRepository(DatabaseHelper.GetConnection).Authenticate(username, password));
                Password.Clear(); DialogResult = true;
            }
            catch (Exception ex) { Error.Text = ex.Message; Password.Clear(); }
            finally { password = null; LoginButton.IsEnabled = true; }
        }
    }
}
