using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace AngleMonitorWPF.Rom
{
    // Synchronous ADO.NET boundary: all UI callers execute these operations on a worker.
    public sealed class DoctorWorkspaceRepository
    {
        private readonly Func<SqlConnection> connectionFactory;
        public DoctorWorkspaceRepository(Func<SqlConnection> connectionFactory) { this.connectionFactory = connectionFactory; }
        private SqlConnection Open(DoctorSession doctor)
        {
            var c = connectionFactory();
            try { c.Open(); DoctorRepository.RequireActive(c, null, doctor); return c; }
            catch { c.Dispose(); throw; }
        }
        public void CheckSchema()
        {
            using (var c = connectionFactory())
            {
                c.Open();
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.RehabSchemaMigrations WHERE Version=1", c))
                    if ((int)cmd.ExecuteScalar() != 1) throw new InvalidOperationException("Chạy migrations/001_doctor_workspace.sql trước khi sử dụng.");
            }
        }
        public DataTable Patients(DoctorSession doctor)
        {
            return Table(doctor, @"SELECT Id,PatientCode,FullName,Age,Injury,PatientCode+N' — '+FullName AS DisplayName
                FROM dbo.Users ORDER BY FullName", null);
        }
        public DataTable Sessions(int patientId, DoctorSession doctor)
        {
            return Table(doctor, @"SELECT SessionId,StartTime,EndTime,TotalReps,PeakROM,AvgROM,
                N'#'+CONVERT(nvarchar(20),SessionId)+N' — '+COALESCE(CONVERT(nvarchar(19),StartTime,120),N'Không rõ ngày') AS DisplayName,
                (SELECT COUNT_BIG(*) FROM dbo.SensorData d WHERE d.UserId=s.UserId AND d.RecordTime BETWEEN s.StartTime AND s.EndTime) AS SensorSamples
                FROM dbo.TrainingSessions s WHERE UserId=@p ORDER BY StartTime", patientId);
        }
        public DataTable Assessments(int patientId, DoctorSession doctor)
        {
            return Table(doctor, @"SELECT a.AssessmentId,a.MilestoneId,a.MilestoneRevision,a.MovementType,a.MeasurementConvention,
                a.TargetAngle,a.MeasuredAngle,a.MeasurementSource,a.MeasurementTime,a.SessionId,a.AssessmentStatus,
                a.AchievedRepetitions,a.CriteriaSnapshot,a.Notes,d.FullName AS ReviewedBy,
                CASE a.AssessmentStatus WHEN 'MeasuredAchieved' THEN N'Đo đạt' WHEN 'NotAchieved' THEN N'Chưa đạt' ELSE N'Chưa đủ bằng chứng' END AS AssessmentLabel
                FROM dbo.RomAssessments a JOIN dbo.DoctorAccounts d ON d.DoctorId=a.CreatedByDoctorId
                WHERE a.PatientId=@p ORDER BY a.AssessmentId DESC", patientId);
        }
        public DataTable Audit(int patientId, DoctorSession doctor)
        {
            return Table(doctor, @"SELECT a.AuditId,a.MilestoneId,a.ConfirmationId,
                CASE a.Action WHEN 'Confirm' THEN N'Xác nhận' WHEN 'Revoke' THEN N'Hủy xác nhận'
                WHEN 'GoalCreated' THEN N'Thêm mục tiêu' ELSE N'Sửa mục tiêu' END AS Action,
                d.FullName AS DoctorName,a.ChangedAt,a.Reason FROM dbo.RomConfirmationAudit a
                JOIN dbo.DoctorAccounts d ON a.DoctorId=d.DoctorId WHERE a.PatientId=@p ORDER BY a.AuditId DESC", patientId);
        }
        private DataTable Table(DoctorSession doctor, string sql, int? patientId)
        {
            using (var c = Open(doctor))
            using (var cmd = new SqlCommand(sql, c))
            {
                if (patientId.HasValue) cmd.Parameters.AddWithValue("@p", patientId.Value);
                using (var r = cmd.ExecuteReader()) { var t = new DataTable(); t.Load(r); return t; }
            }
        }
        public List<RomMilestone> Milestones(int patientId, DoctorSession doctor)
        { using (var c = Open(doctor)) return ReadMilestones(c, null, patientId); }

        private static List<RomMilestone> ReadMilestones(SqlConnection c, SqlTransaction tx, int patientId)
        {
            string sql = @"SELECT m.*,a.AssessmentId,a.SessionId,a.MeasuredAngle,a.MeasurementTime,a.MeasurementSource,
                a.AssessmentStatus,a.Notes AS AssessmentNotes,rc.ConfirmationId,rc.ConfirmedAt,rc.Notes AS ConfirmationNotes,d.FullName AS DoctorName,d.Username AS DoctorUsername
                FROM dbo.PatientRomMilestones m " + (tx == null ? "" : "WITH(UPDLOCK,HOLDLOCK) ") + @"
                OUTER APPLY(SELECT TOP(1) * FROM dbo.RomConfirmations r WHERE r.MilestoneId=m.MilestoneId AND r.IsCurrent=1) rc
                OUTER APPLY(SELECT TOP(1) * FROM dbo.RomAssessments x WHERE x.MilestoneId=m.MilestoneId AND x.MilestoneRevision=m.Revision
                  AND (rc.AssessmentId IS NULL OR x.AssessmentId=rc.AssessmentId) ORDER BY x.AssessmentId DESC) a
                LEFT JOIN dbo.DoctorAccounts d ON rc.DoctorId=d.DoctorId
                WHERE m.PatientId=@p ORDER BY m.MovementType,m.TargetAngle,m.MilestoneId";
            var list = new List<RomMilestone>();
            using (var cmd = new SqlCommand(sql, c, tx))
            {
                cmd.Parameters.AddWithValue("@p", patientId);
                using (var r = cmd.ExecuteReader()) while (r.Read())
                {
                    var m = new RomMilestone
                    {
                        MilestoneId = (int)r["MilestoneId"], PatientId = (int)r["PatientId"], Revision = (int)r["Revision"],
                        MovementType = (MovementType)Enum.Parse(typeof(MovementType), (string)r["MovementType"]),
                        MeasurementConvention = (MeasurementConvention)Enum.Parse(typeof(MeasurementConvention), (string)r["MeasurementConvention"]),
                        TargetAngle = (double)r["TargetAngle"], Tolerance = (double)r["Tolerance"], RequiredRepetitions = (int)r["RequiredRepetitions"],
                        RequiredHoldSeconds = (double)r["RequiredHoldSeconds"], ResetDelta = (double)r["ResetDelta"], MaxSampleGapSeconds = (double)r["MaxSampleGapSeconds"],
                        AssessmentId = NullableValue<int>(r, "AssessmentId"), SessionId = NullableValue<int>(r, "SessionId"),
                        MeasuredAngle = NullableValue<double>(r, "MeasuredAngle"), MeasurementTime = NullableValue<DateTime>(r, "MeasurementTime"),
                        MeasurementSource = r["MeasurementSource"].ToString(), AssessmentState = r["AssessmentStatus"].ToString(),
                        ConfirmationId = NullableValue<int>(r, "ConfirmationId"), ConfirmedAt = NullableValue<DateTime>(r, "ConfirmedAt"),
                        DoctorName = r["DoctorName"].ToString(), DoctorUsername = r["DoctorUsername"].ToString(),
                        Notes = r["AssessmentNotes"].ToString() + " " + r["ConfirmationNotes"].ToString()
                    };
                    m.IsConfirmed = m.OriginalConfirmed = m.ConfirmationId.HasValue;
                    m.Status = m.IsConfirmed ? MilestoneStatus.DoctorConfirmed : m.AssessmentState == "MeasuredAchieved" ? MilestoneStatus.MeasuredAchieved : MilestoneStatus.NotAchieved;
                    list.Add(m);
                }
            }
            return list;
        }
        private static T? NullableValue<T>(SqlDataReader r, string name) where T : struct
        { return r[name] == DBNull.Value ? (T?)null : (T)r[name]; }

        public int SaveMilestone(RomMilestone m, DoctorSession doctor, string reason)
        {
            RomMilestoneService.Validate(m); RequireText(reason, 2000, "Lý do sửa/thêm mục tiêu");
            using (var c = Open(doctor))
            using (var tx = c.BeginTransaction(IsolationLevel.Serializable))
            {
                DoctorRepository.RequireActive(c, tx, doctor);
                string before = "";
                if (m.MilestoneId != 0)
                {
                    var old = ReadMilestones(c, tx, m.PatientId).SingleOrDefault(x => x.MilestoneId == m.MilestoneId);
                    if (old == null || old.Revision != m.Revision) throw new InvalidOperationException("Mục tiêu đã thay đổi hoặc không thuộc bệnh nhân.");
                    if (old.IsConfirmed) throw new InvalidOperationException("Hủy xác nhận có lý do trước khi sửa mục tiêu đã xác nhận.");
                    before = "Trước: " + Snapshot(old) + "; ";
                }
                string sql = m.MilestoneId == 0 ? @"INSERT dbo.PatientRomMilestones(PatientId,MovementType,TargetAngle,MeasurementConvention,Tolerance,
                     RequiredRepetitions,RequiredHoldSeconds,ResetDelta,MaxSampleGapSeconds)
                     OUTPUT INSERTED.MilestoneId VALUES(@p,@move,@target,@convention,@tol,@reps,@hold,@reset,@gap)" :
                    @"UPDATE dbo.PatientRomMilestones SET MovementType=@move,TargetAngle=@target,MeasurementConvention=@convention,Tolerance=@tol,
                     RequiredRepetitions=@reps,RequiredHoldSeconds=@hold,ResetDelta=@reset,MaxSampleGapSeconds=@gap,Revision=Revision+1,
                     Status='NotAchieved',UpdatedAt=SYSUTCDATETIME() OUTPUT INSERTED.MilestoneId WHERE MilestoneId=@id AND PatientId=@p AND Revision=@rev";
                int id;
                using (var cmd = new SqlCommand(sql, c, tx))
                {
                    cmd.Parameters.AddWithValue("@p", m.PatientId); cmd.Parameters.AddWithValue("@id", m.MilestoneId); cmd.Parameters.AddWithValue("@rev", m.Revision);
                    cmd.Parameters.AddWithValue("@move", m.MovementType.ToString()); cmd.Parameters.AddWithValue("@target", m.TargetAngle);
                    cmd.Parameters.AddWithValue("@convention", m.MeasurementConvention.ToString()); cmd.Parameters.AddWithValue("@tol", m.Tolerance);
                    cmd.Parameters.AddWithValue("@reps", m.RequiredRepetitions); cmd.Parameters.AddWithValue("@hold", m.RequiredHoldSeconds);
                    cmd.Parameters.AddWithValue("@reset", m.ResetDelta); cmd.Parameters.AddWithValue("@gap", m.MaxSampleGapSeconds);
                    id = Convert.ToInt32(cmd.ExecuteScalar());
                }
                AuditWrite(c, tx, m.PatientId, id, null, doctor, m.MilestoneId == 0 ? "GoalCreated" : "GoalUpdated",
                    before + "Sau: " + Snapshot(m) + "; " + reason);
                tx.Commit(); return id;
            }
        }
        private static string Snapshot(RomMilestone m)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}/{1}; target={2}; tol={3}; reps={4}; hold={5}s; reset={6}; gap={7}s",
                m.MovementType, m.MeasurementConvention, m.TargetAngle, m.Tolerance, m.RequiredRepetitions, m.RequiredHoldSeconds, m.ResetDelta, m.MaxSampleGapSeconds);
        }

        public RomEvaluation AssessClinical(RomMilestone m, ClinicalMeasurement measurement, DoctorSession doctor)
        {
            var result = RomMilestoneService.EvaluateClinical(m, measurement);
            SaveAssessment(m, null, result, "Lâm sàng: " + measurement.Source, measurement.MeasuredAt,
                measurement.Reason + string.Format(CultureInfo.InvariantCulture, "; khai báo {0} lần, giữ {1}s", measurement.Repetitions, measurement.HoldSeconds), doctor);
            return result;
        }

        public RomEvaluation AssessSession(RomMilestone m, int sessionId, bool conventionVerified, bool qualityValid,
            bool supportsHold, string reviewNotes, DoctorSession doctor)
        {
            RequireText(reviewNotes, 1200, "Ghi nhận quy ước góc, hiệu chuẩn và chất lượng đo");
            var evidence = new RomEvidence { MovementType = m.MovementType, Convention = m.MeasurementConvention,
                ConventionVerified = conventionVerified, QualityValid = qualityValid, SupportsHold = supportsHold, Notes = reviewNotes };
            DateTime? time;
            using (var c = Open(doctor))
            {
                DateTime? start = null, end = null; string json = null;
                using (var cmd = new SqlCommand("SELECT StartTime,EndTime,ChartDataJson FROM dbo.TrainingSessions WHERE SessionId=@s AND UserId=@p", c))
                {
                    cmd.Parameters.AddWithValue("@s", sessionId); cmd.Parameters.AddWithValue("@p", m.PatientId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) throw new InvalidOperationException("Buổi tập không thuộc bệnh nhân.");
                        start = NullableValue<DateTime>(r, "StartTime"); end = NullableValue<DateTime>(r, "EndTime"); json = r["ChartDataJson"].ToString();
                    }
                }
                time = end ?? start;
                evidence.Source = "TrainingSessions.ChartDataJson #" + sessionId;
                if (start.HasValue && end.HasValue && end > start && !string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        evidence.Samples = JsonSerializer.Deserialize<List<RomSample>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (evidence.Samples != null && evidence.Samples.Any(s => s != null && s.Time > (end.Value - start.Value).TotalSeconds + 1))
                            evidence.QualityValid = false;
                    }
                    catch (JsonException) { evidence.QualityValid = false; }
                }
                else if (start.HasValue && end.HasValue && end > start)
                {
                    // SensorData has no SessionId. Use only an unambiguous, closed time window;
                    // operator still must explicitly verify its movement/convention and quality.
                    evidence.Source = "SensorData: cửa sổ buổi #" + sessionId;
                    using (var cmd = new SqlCommand(@"SELECT COUNT(*) FROM dbo.TrainingSessions WHERE UserId=@p AND SessionId<>@s
                         AND StartTime<=@end AND (EndTime IS NULL OR EndTime>=@start)", c))
                    {
                        cmd.Parameters.AddWithValue("@p", m.PatientId); cmd.Parameters.AddWithValue("@s", sessionId);
                        cmd.Parameters.AddWithValue("@start", start); cmd.Parameters.AddWithValue("@end", end);
                        if ((int)cmd.ExecuteScalar() != 0) evidence.QualityValid = false;
                    }
                    using (var cmd = new SqlCommand(@"SELECT Angle,RecordTime FROM dbo.SensorData WHERE UserId=@p AND RecordTime BETWEEN @start AND @end ORDER BY RecordTime,Id", c))
                    {
                        cmd.Parameters.AddWithValue("@p", m.PatientId); cmd.Parameters.AddWithValue("@start", start); cmd.Parameters.AddWithValue("@end", end);
                        using (var r = cmd.ExecuteReader()) while (r.Read()) evidence.Samples.Add(new RomSample { Angle = (double)r["Angle"], Time = ((DateTime)r["RecordTime"] - start.Value).TotalSeconds });
                    }
                }
                else evidence.QualityValid = false;
            }
            var result = RomMilestoneService.Evaluate(m, evidence);
            SaveAssessment(m, sessionId, result, evidence.Source, time,
                reviewNotes + "; quy ước xác minh=" + conventionVerified + "; chất lượng hợp lệ=" + qualityValid + "; hỗ trợ giữ=" + supportsHold + "; " + result.Reason, doctor);
            return result;
        }

        private void SaveAssessment(RomMilestone m, int? sessionId, RomEvaluation result, string source, DateTime? time, string notes, DoctorSession doctor)
        {
            RequireText(source, 255, "Nguồn đo"); RequireText(DoctorRepository.MarkDemo(doctor, notes), 2000, "Nhận xét đánh giá");
            using (var c = Open(doctor))
            using (var tx = c.BeginTransaction(IsolationLevel.Serializable))
            {
                DoctorRepository.RequireActive(c, tx, doctor);
                var current = ReadMilestones(c, tx, m.PatientId).SingleOrDefault(x => x.MilestoneId == m.MilestoneId);
                if (current == null || current.Revision != m.Revision || Snapshot(current) != Snapshot(m)) throw new InvalidOperationException("Mục tiêu đã thay đổi. Hãy tải lại.");
                if (current.IsConfirmed) throw new InvalidOperationException("Hủy xác nhận trước khi thay kết quả đánh giá đang được công nhận.");
                using (var cmd = new SqlCommand(@"INSERT dbo.RomAssessments(PatientId,MilestoneId,MilestoneRevision,SessionId,TargetAngle,MovementType,
                     MeasurementConvention,MeasuredAngle,MeasurementSource,MeasurementTime,AssessmentStatus,AchievedRepetitions,CriteriaSnapshot,Notes,CreatedByDoctorId)
                     VALUES(@p,@m,@rev,@s,@target,@move,@convention,@angle,@source,@time,@status,@reps,@criteria,@notes,@d);
                     UPDATE dbo.PatientRomMilestones SET Status=@ms,UpdatedAt=SYSUTCDATETIME() WHERE MilestoneId=@m AND PatientId=@p;", c, tx))
                {
                    cmd.Parameters.AddWithValue("@p", m.PatientId); cmd.Parameters.AddWithValue("@m", m.MilestoneId); cmd.Parameters.AddWithValue("@rev", m.Revision);
                    cmd.Parameters.AddWithValue("@s", (object)sessionId ?? DBNull.Value); cmd.Parameters.AddWithValue("@target", m.TargetAngle);
                    cmd.Parameters.AddWithValue("@move", m.MovementType.ToString()); cmd.Parameters.AddWithValue("@convention", m.MeasurementConvention.ToString());
                    cmd.Parameters.AddWithValue("@angle", (object)result.MeasuredAngle ?? DBNull.Value); cmd.Parameters.AddWithValue("@source", source);
                    cmd.Parameters.AddWithValue("@time", (object)time ?? DBNull.Value); cmd.Parameters.AddWithValue("@status", result.Status.ToString());
                    cmd.Parameters.AddWithValue("@reps", result.AchievedRepetitions); cmd.Parameters.AddWithValue("@criteria", Snapshot(m));
                    cmd.Parameters.AddWithValue("@notes", DoctorRepository.MarkDemo(doctor, notes)); cmd.Parameters.AddWithValue("@d", doctor.DoctorId);
                    cmd.Parameters.AddWithValue("@ms", result.Status == AssessmentStatus.MeasuredAchieved ? "MeasuredAchieved" : "NotAchieved"); cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        public void SaveConfirmations(int patientId, IList<ConfirmationChange> changes, DoctorSession doctor)
        {
            RomMilestoneService.Authorize(doctor);
            if (changes == null || changes.Count == 0) return;
            if (changes.GroupBy(x => x.MilestoneId).Any(g => g.Count() > 1)) throw new ArgumentException("Mỗi mốc chỉ được thay đổi một lần trong giao dịch.");
            using (var c = Open(doctor))
            using (var tx = c.BeginTransaction(IsolationLevel.Serializable))
            {
                DoctorRepository.RequireActive(c, tx, doctor);
                var milestones = ReadMilestones(c, tx, patientId);
                foreach (var change in changes)
                {
                    var m = milestones.SingleOrDefault(x => x.MilestoneId == change.MilestoneId);
                    if (m == null) throw new InvalidOperationException("Mốc không thuộc bệnh nhân đang chọn.");
                    RomMilestoneService.ValidateConfirmation(patientId, m, change, doctor);
                    int confirmationId;
                    if (change.Confirm)
                    {
                        using (var cmd = new SqlCommand(@"INSERT dbo.RomConfirmations(AssessmentId,PatientId,MilestoneId,DoctorId,Notes)
                            OUTPUT INSERTED.ConfirmationId VALUES(@a,@p,@m,@d,@n)", c, tx))
                        {
                            cmd.Parameters.AddWithValue("@a", m.AssessmentId); cmd.Parameters.AddWithValue("@p", patientId);
                            cmd.Parameters.AddWithValue("@m", m.MilestoneId); cmd.Parameters.AddWithValue("@d", doctor.DoctorId);
                            string confirmationNote = DoctorRepository.MarkDemo(doctor, change.Reason.Trim());
                            RequireText(confirmationNote, 2000, "Ghi chú xác nhận");
                            cmd.Parameters.AddWithValue("@n", confirmationNote); confirmationId = (int)cmd.ExecuteScalar();
                        }
                    }
                    else
                    {
                        confirmationId = m.ConfirmationId.Value;
                        using (var cmd = new SqlCommand("UPDATE dbo.RomConfirmations SET IsCurrent=0 WHERE ConfirmationId=@id AND IsCurrent=1", c, tx))
                        { cmd.Parameters.AddWithValue("@id", confirmationId); if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Xác nhận đã thay đổi."); }
                    }
                    AuditWrite(c, tx, patientId, m.MilestoneId, confirmationId, doctor, change.Confirm ? "Confirm" : "Revoke", change.Reason.Trim());
                    using (var cmd = new SqlCommand("UPDATE dbo.PatientRomMilestones SET Status=@status,UpdatedAt=SYSUTCDATETIME() WHERE MilestoneId=@id", c, tx))
                    {
                        cmd.Parameters.AddWithValue("@id", m.MilestoneId); cmd.Parameters.AddWithValue("@status", change.Confirm ? "DoctorConfirmed" : m.AssessmentState == "MeasuredAchieved" ? "MeasuredAchieved" : "NotAchieved"); cmd.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
        }
        private static void AuditWrite(SqlConnection c, SqlTransaction tx, int patientId, int milestoneId, int? confirmationId, DoctorSession doctor, string action, string reason)
        {
            reason = DoctorRepository.MarkDemo(doctor, reason);
            RequireText(reason, 2000, "Nội dung lịch sử");
            using (var cmd = new SqlCommand(@"INSERT dbo.RomConfirmationAudit(PatientId,MilestoneId,ConfirmationId,DoctorId,Action,Reason)
                VALUES(@p,@m,@c,@d,@a,@r)", c, tx))
            {
                cmd.Parameters.AddWithValue("@p", patientId); cmd.Parameters.AddWithValue("@m", milestoneId);
                cmd.Parameters.AddWithValue("@c", (object)confirmationId ?? DBNull.Value); cmd.Parameters.AddWithValue("@d", doctor.DoctorId);
                cmd.Parameters.AddWithValue("@a", action); cmd.Parameters.AddWithValue("@r", reason); cmd.ExecuteNonQuery();
            }
        }
        private static void RequireText(string value, int max, string label)
        { if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new ArgumentException(label + " bắt buộc, tối đa " + max + " ký tự."); }
    }
}
