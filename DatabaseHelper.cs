using System;
using System.Data;
using System.Data.SqlClient;
using System.Windows;
using System.Threading.Tasks;

namespace AngleMonitorWPF
{
    // LƯU TRỮ TRẠNG THÁI NGƯỜI DÙNG
    public static class GlobalData
    {
        public static int CurrentUserId { get; set; } = -1; 
    }
    // DATABASE HELPER (Xử lý kết nối SQL)
    public static class DatabaseHelper
    {
        private static readonly string connectionString = @"Server=DESKTOP-GUAOG8U;Database=RehabDB;Integrated Security=True;";

        public static SqlConnection GetConnection()
        {
            return new SqlConnection(connectionString);
        }

        public static int ExecuteNonQuery(SqlCommand cmd)
        {
            try
            {
                using (SqlConnection conn = GetConnection())
                {
                    cmd.Connection = conn;
                    conn.Open();
                    return cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi thực thi SQL: " + ex.Message, "Lỗi DB", MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }

        public static SqlDataReader ExecuteReader(SqlCommand cmd)
        {
            try
            {
                SqlConnection conn = GetConnection();
                cmd.Connection = conn;
                conn.Open();
                return cmd.ExecuteReader(CommandBehavior.CloseConnection);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đọc dữ liệu: " + ex.Message, "Lỗi DB", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }
        // THÊM MỚI: HÀM ĐỌC DỮ LIỆU BẤT ĐỒNG BỘ CHO ANALYSIS TAB
        public static async Task<SqlDataReader> ExecuteReaderAsync(SqlCommand cmd)
        {
            try
            {
                SqlConnection conn = GetConnection();
                cmd.Connection = conn;
                await conn.OpenAsync();
                return await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đọc dữ liệu Async: " + ex.Message, "Lỗi DB", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }
        public static int ExecuteScalar(SqlCommand cmd)
        {
            try
            {
                using (SqlConnection conn = GetConnection())
                {
                    cmd.Connection = conn;
                    conn.Open();
                    object result = cmd.ExecuteScalar();
                    if (result != null && int.TryParse(result.ToString(), out int id))
                    {
                        return id;
                    }
                    return -1;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi lấy ID từ DB: " + ex.Message, "Lỗi DB", MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }
        public static int CreateNewSession()
        {
            if (GlobalData.CurrentUserId == -1)
            {
                return -1;
            }
            string query = @"INSERT INTO dbo.TrainingSessions (UserId, StartTime) 
                             OUTPUT INSERTED.SessionId 
                             VALUES (@UserId, GETDATE())";

            using (SqlCommand cmd = new SqlCommand(query))
            {
                cmd.Parameters.AddWithValue("@UserId", GlobalData.CurrentUserId);

                return ExecuteScalar(cmd);
            }
        }
        public static void SaveSessionData(int sessionId, int reps, double peakRom, double avgRom, string chartDataJson)
        {
            if (sessionId == -1) return;

            string query = @"UPDATE dbo.TrainingSessions 
                             SET EndTime = GETDATE(), 
                                 TotalReps = @Reps, 
                                 PeakROM = @PeakRom, 
                                 AvgROM = @AvgRom,
                                 ChartDataJson = @JsonData 
                             WHERE SessionId = @SessionId";

            using (SqlCommand cmd = new SqlCommand(query))
            {
                cmd.Parameters.AddWithValue("@SessionId", sessionId);
                cmd.Parameters.AddWithValue("@Reps", reps);
                cmd.Parameters.AddWithValue("@PeakRom", peakRom);
                cmd.Parameters.AddWithValue("@AvgRom", avgRom);
                cmd.Parameters.AddWithValue("@JsonData", chartDataJson);

                ExecuteNonQuery(cmd);
            }
        }
    }
}