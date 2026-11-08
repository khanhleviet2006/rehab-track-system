using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AngleMonitorWPF.Rom
{
    public enum MovementType { ElbowFlexion, ElbowExtension }
    public enum MeasurementConvention { Unknown, FlexionDegrees, ExtensionDeficit, IncludedJointAngle }
    public enum AssessmentStatus { NotAchieved, MeasuredAchieved, InsufficientEvidence }
    public enum MilestoneStatus { NotAchieved, MeasuredAchieved, DoctorConfirmed }

    public sealed class DoctorSession
    {
        internal DoctorSession(int id, string name, string role)
        { DoctorId = id; FullName = name; Role = role; ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30); }
        public int DoctorId { get; private set; }
        public string FullName { get; private set; }
        public string Role { get; private set; }
        public DateTime ExpiresAtUtc { get; private set; }
    }

    public sealed class RomMilestone : INotifyPropertyChanged
    {
        public int MilestoneId { get; set; }
        public int PatientId { get; set; }
        public int Revision { get; set; }
        public MovementType MovementType { get; set; }
        public MeasurementConvention MeasurementConvention { get; set; }
        public double TargetAngle { get; set; }
        public double Tolerance { get; set; }
        public int RequiredRepetitions { get; set; }
        public double RequiredHoldSeconds { get; set; }
        public double ResetDelta { get; set; }
        public double MaxSampleGapSeconds { get; set; }
        public MilestoneStatus Status { get; set; }
        public int? AssessmentId { get; set; }
        public int? SessionId { get; set; }
        public double? MeasuredAngle { get; set; }
        public DateTime? MeasurementTime { get; set; }
        public string MeasurementSource { get; set; }
        public string AssessmentState { get; set; }
        public string DoctorName { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public int? ConfirmationId { get; set; }
        public string Notes { get; set; }
        public bool OriginalConfirmed { get; set; }
        private bool confirmed;
        public bool IsConfirmed
        {
            get { return confirmed; }
            set { confirmed = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("IsConfirmed")); }
        }
        public string MovementLabel { get { return MovementType == MovementType.ElbowFlexion ? "Gập khuỷu tay" : "Duỗi khuỷu tay"; } }
        public string ConventionLabel { get { return MeasurementConvention == MeasurementConvention.FlexionDegrees ? "Góc gập (0° duỗi thẳng)" : MeasurementConvention == MeasurementConvention.ExtensionDeficit ? "Mức thiếu duỗi (0° đầy đủ)" : MeasurementConvention == MeasurementConvention.IncludedJointAngle ? "Góc trong (180° duỗi thẳng)" : "Chưa xác định"; } }
        public string StatusLabel { get { return Status == MilestoneStatus.DoctorConfirmed ? "Bác sĩ đã xác nhận" : Status == MilestoneStatus.MeasuredAchieved ? "Đo đạt, chờ xác nhận" : "Chưa có kết quả đo đạt"; } }
        public string EvidenceLabel { get { return AssessmentState == "MeasuredAchieved" ? "Đo đạt" : AssessmentState == "NotAchieved" ? "Chưa đạt" : "Chưa đủ bằng chứng"; } }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed class RomSample
    {
        public double Time { get; set; }
        public double Angle { get; set; }
    }

    public sealed class RomEvidence
    {
        public MovementType MovementType { get; set; }
        public MeasurementConvention Convention { get; set; }
        public bool ConventionVerified { get; set; }
        public bool QualityValid { get; set; }
        public bool SupportsHold { get; set; }
        public string Source { get; set; }
        public string Notes { get; set; }
        public List<RomSample> Samples { get; set; } = new List<RomSample>();
    }

    public sealed class RomEvaluation
    {
        public AssessmentStatus Status { get; set; }
        public double? MeasuredAngle { get; set; }
        public int AchievedRepetitions { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ClinicalMeasurement
    {
        public double Angle { get; set; }
        public int Repetitions { get; set; }
        public double HoldSeconds { get; set; }
        public DateTime MeasuredAt { get; set; }
        public string Source { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ConfirmationChange
    {
        public int MilestoneId { get; set; }
        public int Revision { get; set; }
        public int? AssessmentId { get; set; }
        public int? ExpectedConfirmationId { get; set; }
        public bool Confirm { get; set; }
        public string Reason { get; set; }
    }
}
