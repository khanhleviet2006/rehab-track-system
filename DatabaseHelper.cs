using System;
using System.Data;
using System.Data.SqlClient;
using System.Windows;
using System.Threading.Tasks;

namespace AngleMonitorWPF
{
    // =====================================
    // LƯU TRỮ TRẠNG THÁI NGƯỜI DÙNG
    // =====================================
    public static class GlobalData
    {
        public static int CurrentUserId { get; set; } = -1; // -1: Chưa đăng nhập
    }

    // =====================================
    // DATABASE HELPER (Xử lý kết nối SQL)
    // =====================================
    public static class DatabaseHelper
    {
        // Connection string giữ nguyên theo máy của bạn
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
        // =====================================
        // THÊM MỚI: HÀM ĐỌC DỮ LIỆU BẤT ĐỒNG BỘ CHO ANALYSIS TAB
        // =====================================
        public static async Task<SqlDataReader> ExecuteReaderAsync(SqlCommand cmd)
        {
            try
            {
                SqlConnection conn = GetConnection();
                cmd.Connection = conn;

                // Mở kết nối không khóa luồng giao diện
                await conn.OpenAsync();

                // Trả về luồng đọc bất đồng bộ
                return await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đọc dữ liệu Async: " + ex.Message, "Lỗi DB", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        // THÊM MỚI: Hàm ExecuteScalar dùng để thực thi lệnh INSERT và lấy ID tự tăng
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

        // =====================================
        // CÁC HÀM XỬ LÝ THEO BUỔI TẬP (SESSION)
        // =====================================

        /// <summary>
        /// Tạo buổi tập mới khi ấn Bắt đầu. Trả về SessionId vừa được tạo.
        /// </summary>
        public static int CreateNewSession()
        {
            if (GlobalData.CurrentUserId == -1)
            {
                return -1;
            }

            // Lệnh OUTPUT INSERTED.SessionId sẽ trả về ID tự động tăng vừa được tạo
            string query = @"INSERT INTO dbo.TrainingSessions (UserId, StartTime) 
                             OUTPUT INSERTED.SessionId 
                             VALUES (@UserId, GETDATE())";

            using (SqlCommand cmd = new SqlCommand(query))
            {
                cmd.Parameters.AddWithValue("@UserId", GlobalData.CurrentUserId);

                return ExecuteScalar(cmd); // Gọi hàm mới thêm ở trên
            }
        }

        /// <summary>
        /// Gọi 1 lần duy nhất khi kết thúc bài tập để lưu tổng hợp và chuỗi JSON tọa độ.
        /// </summary>
        public static void SaveSessionData(int sessionId, int reps, double peakRom, double avgRom, string chartDataJson)
        {
            // Tránh lỗi nếu sessionId không hợp lệ
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

        /* ĐÃ VÔ HIỆU HÓA: Hàm InsertSensorData cũ
        Không dùng nữa để tránh lỗi tràn bộ nhớ / giật lag khi ghi dữ liệu thô liên tục
        
        public static void InsertSensorData(double angle, double forceWeight)
        {
            ...
        }
        */
    }
}