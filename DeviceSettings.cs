namespace AngleMonitorWPF
{
    public static class DeviceSettings
    {
        public static double MinAngleLimit { get; set; } = 10.0;
        public static double MaxAngleLimit { get; set; } = 130.0;
        public static double TargetThreshold { get; set; } = 60.0;
        public static double MaxAngularVelocity { get; set; } = 150.0;
        public static double DumbbellWeight { get; set; } = 5.0;
        public static double MaxAngularAcceleration { get; set; } = 300.0;
        // THÊM MỚI: Ngưỡng góc tối thiểu để được tính là 1 Rep (Mặc định 40)
        public static double MinDeltaForRep { get; set; } = 40.0;

        // THÊM MỚI (Tùy chọn): Ngưỡng nhiễu để xác định có thực sự đổi chiều gập/duỗi chưa
        public static double NoiseMargin { get; set; } = 5.0;
    }
}