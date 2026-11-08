using System;
using System.Linq;

namespace AngleMonitorWPF.Rom
{
    // No WPF/SQL dependencies. Sensor evidence never changes a clinical confirmation.
    public static class RomMilestoneService
    {
        public static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        public static void Validate(RomMilestone m)
        {
            if (m == null || m.PatientId <= 0 || !Enum.IsDefined(typeof(MovementType), m.MovementType) ||
                !Enum.IsDefined(typeof(MeasurementConvention), m.MeasurementConvention) ||
                !Finite(m.TargetAngle) || m.TargetAngle < 0 || m.TargetAngle > 180 ||
                !Finite(m.Tolerance) || m.Tolerance < 0 || m.Tolerance > 20 ||
                m.RequiredRepetitions < 1 || m.RequiredRepetitions > 1000 ||
                !Finite(m.RequiredHoldSeconds) || m.RequiredHoldSeconds < 0 || m.RequiredHoldSeconds > 600 ||
                !Finite(m.ResetDelta) || m.ResetDelta <= 0 || m.ResetDelta > 180 ||
                !Finite(m.MaxSampleGapSeconds) || m.MaxSampleGapSeconds <= 0 || m.MaxSampleGapSeconds > 10)
                throw new ArgumentException("Mục tiêu, sai số, số lần lặp hoặc thời gian không hợp lệ.");
            if (!ConventionMatches(m.MovementType, m.MeasurementConvention) && m.MeasurementConvention != MeasurementConvention.Unknown)
                throw new ArgumentException("Quy ước góc không phù hợp với loại vận động.");
        }

        public static bool ConventionMatches(MovementType movement, MeasurementConvention convention)
        {
            return movement == MovementType.ElbowFlexion ? convention == MeasurementConvention.FlexionDegrees :
                movement == MovementType.ElbowExtension && (convention == MeasurementConvention.ExtensionDeficit || convention == MeasurementConvention.IncludedJointAngle);
        }

        private static bool Increasing(RomMilestone m)
        { return m.MeasurementConvention != MeasurementConvention.ExtensionDeficit; }

        public static bool AngleMeets(RomMilestone m, double angle)
        {
            return Finite(angle) && angle >= 0 && angle <= 180 && ConventionMatches(m.MovementType, m.MeasurementConvention) &&
                (Increasing(m) ? angle >= m.TargetAngle - m.Tolerance : angle <= m.TargetAngle + m.Tolerance);
        }

        public static RomEvaluation Evaluate(RomMilestone m, RomEvidence e)
        {
            Validate(m);
            if (e == null || !e.ConventionVerified || !e.QualityValid || string.IsNullOrWhiteSpace(e.Source) ||
                string.IsNullOrWhiteSpace(e.Notes) || e.MovementType != m.MovementType || e.Convention != m.MeasurementConvention ||
                !ConventionMatches(e.MovementType, e.Convention)) return Insufficient("Chưa xác minh loại vận động, quy ước góc, nguồn hoặc chất lượng dữ liệu.");
            if (m.RequiredHoldSeconds > 0 && !e.SupportsHold) return Insufficient("Không đủ bằng chứng thời gian giữ.");
            if (e.Samples == null || e.Samples.Count < 3) return Insufficient("Không đủ chuỗi mẫu để xác minh một chu kỳ vận động.");
            for (int i = 0; i < e.Samples.Count; i++)
            {
                var s = e.Samples[i];
                if (s == null || !Finite(s.Angle) || s.Angle < 0 || s.Angle > 180 || !Finite(s.Time) || s.Time < 0 ||
                    (i > 0 && (s.Time <= e.Samples[i - 1].Time || s.Time - e.Samples[i - 1].Time > m.MaxSampleGapSeconds)))
                    return Insufficient("Mẫu không hợp lệ, thời gian không tăng hoặc có khoảng mất mẫu vượt giới hạn.");
            }
            // A repetition requires baseline -> sustained target -> baseline. Two or more
            // target samples are required even when the configured hold time is zero.
            bool armed = false, reached = false;
            double? runStart = null;
            int runSamples = 0, reps = 0;
            double threshold = Increasing(m) ? m.TargetAngle - m.Tolerance : m.TargetAngle + m.Tolerance;
            foreach (var s in e.Samples)
            {
                bool baseline = Increasing(m) ? s.Angle <= threshold - m.ResetDelta : s.Angle >= threshold + m.ResetDelta;
                if (baseline)
                {
                    if (armed && reached) reps++;
                    armed = true; reached = false; runStart = null; runSamples = 0;
                }
                else if (armed && AngleMeets(m, s.Angle))
                {
                    if (!runStart.HasValue) runStart = s.Time;
                    runSamples++;
                    if (runSamples >= 2 && s.Time - runStart.Value >= m.RequiredHoldSeconds) reached = true;
                }
                else { runStart = null; runSamples = 0; }
            }
            return new RomEvaluation
            {
                Status = reps >= m.RequiredRepetitions ? AssessmentStatus.MeasuredAchieved : AssessmentStatus.NotAchieved,
                MeasuredAngle = Increasing(m) ? e.Samples.Max(s => s.Angle) : e.Samples.Min(s => s.Angle),
                AchievedRepetitions = reps,
                Reason = string.Format("{0}/{1} chu kỳ đạt, giữ ≥ {2:0.##} giây; kết quả cần bác sĩ xét duyệt.", reps, m.RequiredRepetitions, m.RequiredHoldSeconds)
            };
        }

        public static RomEvaluation EvaluateClinical(RomMilestone m, ClinicalMeasurement c)
        {
            Validate(m);
            if (c == null || !Finite(c.Angle) || c.Angle < 0 || c.Angle > 180 || c.Repetitions < 1 || c.Repetitions > 1000 ||
                !Finite(c.HoldSeconds) || c.HoldSeconds < 0 || c.HoldSeconds > 600 ||
                c.MeasuredAt == default(DateTime) || c.MeasuredAt > DateTime.Now.AddMinutes(5) ||
                string.IsNullOrWhiteSpace(c.Source) || string.IsNullOrWhiteSpace(c.Reason))
                throw new ArgumentException("Nhập góc, số lần lặp, thời gian giữ, ngày đo, nguồn đo và lý do khám lâm sàng hợp lệ.");
            if (!ConventionMatches(m.MovementType, m.MeasurementConvention)) return Insufficient("Cần xác định quy ước góc của mốc trước khi đánh giá lâm sàng.");
            bool achieved = AngleMeets(m, c.Angle) && c.Repetitions >= m.RequiredRepetitions && c.HoldSeconds >= m.RequiredHoldSeconds;
            return new RomEvaluation { Status = achieved ? AssessmentStatus.MeasuredAchieved : AssessmentStatus.NotAchieved,
                MeasuredAngle = c.Angle, AchievedRepetitions = achieved ? c.Repetitions : 0, Reason = c.Reason };
        }

        public static void Authorize(DoctorSession doctor)
        {
            if (doctor == null || doctor.DoctorId <= 0 || doctor.ExpiresAtUtc <= DateTime.UtcNow ||
                (doctor.Role != "Doctor" && doctor.Role != "Admin"))
                throw new UnauthorizedAccessException("Vui lòng đăng nhập bằng tài khoản bác sĩ còn hiệu lực.");
        }

        public static void ValidateConfirmation(int patientId, RomMilestone m, ConfirmationChange change, DoctorSession doctor)
        {
            Authorize(doctor);
            if (m.PatientId != patientId || m.MilestoneId != change.MilestoneId) throw new InvalidOperationException("Mốc không thuộc bệnh nhân đang chọn.");
            if (m.Revision != change.Revision || m.ConfirmationId != change.ExpectedConfirmationId || m.AssessmentId != change.AssessmentId)
                throw new InvalidOperationException("Dữ liệu đã thay đổi. Hãy tải lại hồ sơ trước khi lưu.");
            if (string.IsNullOrWhiteSpace(change.Reason) || change.Reason.Length > 2000)
                throw new ArgumentException("Cần ghi chú xác nhận hoặc lý do hủy (tối đa 2000 ký tự).");
            if (change.Confirm && (m.ConfirmationId.HasValue || !m.AssessmentId.HasValue || m.AssessmentState != "MeasuredAchieved"))
                throw new InvalidOperationException("Cần kết quả đo đạt hợp lệ và chưa có xác nhận hiện hành.");
            if (!change.Confirm && !m.ConfirmationId.HasValue) throw new InvalidOperationException("Mốc chưa có xác nhận để hủy.");
        }

        private static RomEvaluation Insufficient(string reason)
        { return new RomEvaluation { Status = AssessmentStatus.InsufficientEvidence, Reason = reason }; }
    }
}
