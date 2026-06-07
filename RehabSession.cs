using System.Collections.Generic; // Bắt buộc phải có để dùng List<>

namespace AngleMonitorWPF
{
    // 1. Thêm class này để đại diện cho 1 chấm tọa độ trên biểu đồ Chart.js
    public class SessionDataPoint
    {
        public double Time { get; set; }  // Trục X: Thời gian (giây)
        public double Angle { get; set; } // Trục Y: Góc gập (độ)
    }

    public class RehabSession
    {
        public int SessionId { get; set; }
        public string Date { get; set; }
        public string SessionName { get; set; }
        public double PeakRom { get; set; }
        public double AvgRom { get; set; }
        public int Reps { get; set; }
        public string Duration { get; set; }
        public int Samples { get; set; }

        // 2. BỔ SUNG QUAN TRỌNG NHẤT: Danh sách chứa toàn bộ tọa độ trong lúc tập
        // Khởi tạo sẵn bằng 'new List<SessionDataPoint>()' để tránh lỗi NullReferenceException
        public List<SessionDataPoint> ChartData { get; set; } = new List<SessionDataPoint>();
    }
}