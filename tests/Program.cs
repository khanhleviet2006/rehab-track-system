using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using AngleMonitorWPF.Rom;

internal static class Program
{
    private static int passed, failed;
    private static void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message); }
    }
    private static void Equal<T>(T expected, T actual)
    { if (!Equals(expected, actual)) throw new Exception("Expected " + expected + ", got " + actual); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static RomMilestone Goal(int patientId = 1)
    { return new RomMilestone { PatientId=patientId, MovementType=MovementType.ElbowFlexion, MeasurementConvention=MeasurementConvention.FlexionDegrees, TargetAngle=100, Tolerance=2, RequiredRepetitions=2, RequiredHoldSeconds=0.2, ResetDelta=10, MaxSampleGapSeconds=0.5 }; }
    private static RomEvidence Evidence(params double[] angles)
    { return new RomEvidence { MovementType=MovementType.ElbowFlexion, Convention=MeasurementConvention.FlexionDegrees, ConventionVerified=true, QualityValid=true, SupportsHold=true, Source="Test trace", Notes="Verified calibration and movement", Samples=angles.Select((a,i)=>new RomSample { Angle=a, Time=i*0.25 }).ToList() }; }
    private static RomEvidence TwoCycles() { return Evidence(50,99,103,50,99,105,50); }
    private static DoctorSession Doctor() { return new DoctorSession(1,"Doctor","Doctor"); }
    private static void Units()
    {
        Test("create valid patient goal", () => RomMilestoneService.Validate(Goal()));
        Test("update goal rejects invalid target", () => { var g=Goal(); g.TargetAngle=181; Throws<ArgumentException>(()=>RomMilestoneService.Validate(g)); });
        Test("NaN and infinity rejected", () => { var g=Goal(); g.Tolerance=double.NaN; Throws<ArgumentException>(()=>RomMilestoneService.Validate(g)); g=Goal(); g.TargetAngle=double.PositiveInfinity; Throws<ArgumentException>(()=>RomMilestoneService.Validate(g)); });
        Test("repeated sustained flexion meets target", () => { var r=RomMilestoneService.Evaluate(Goal(),TwoCycles()); Equal(AssessmentStatus.MeasuredAchieved,r.Status); Equal(2,r.AchievedRepetitions); });
        Test("one peak cannot meet two repetitions", () => Equal(AssessmentStatus.NotAchieved,RomMilestoneService.Evaluate(Goal(),Evidence(50,105,50)).Status));
        Test("single instantaneous sample is insufficient", () => Equal(AssessmentStatus.InsufficientEvidence,RomMilestoneService.Evaluate(Goal(),Evidence(150)).Status));
        Test("incomplete final cycle is not a repetition", () => Equal(AssessmentStatus.NotAchieved,RomMilestoneService.Evaluate(Goal(),Evidence(50,105,106)).Status));
        Test("hold time enforced", () => { var g=Goal(); g.RequiredHoldSeconds=1; Equal(AssessmentStatus.NotAchieved,RomMilestoneService.Evaluate(g,TwoCycles()).Status); });
        Test("hold unsupported is insufficient", () => { var e=TwoCycles(); e.SupportsHold=false; Equal(AssessmentStatus.InsufficientEvidence,RomMilestoneService.Evaluate(Goal(),e).Status); });
        Test("unknown sensor convention is insufficient", () => { var e=TwoCycles(); e.ConventionVerified=false; Equal(AssessmentStatus.InsufficientEvidence,RomMilestoneService.Evaluate(Goal(),e).Status); });
        Test("invalid quality is insufficient", () => { var e=TwoCycles(); e.QualityValid=false; Equal(AssessmentStatus.InsufficientEvidence,RomMilestoneService.Evaluate(Goal(),e).Status); });
        Test("movement mismatch is insufficient", () => { var e=TwoCycles(); e.MovementType=MovementType.ElbowExtension; Equal(AssessmentStatus.InsufficientEvidence,RomMilestoneService.Evaluate(Goal(),e).Status); });
        Test("sample gaps cannot prove hold", () => { var e=TwoCycles(); e.Samples[2].Time+=10; Equal(AssessmentStatus.InsufficientEvidence,RomMilestoneService.Evaluate(Goal(),e).Status); });
        Test("duplicate timestamps rejected", () => { var e=TwoCycles(); e.Samples[2].Time=e.Samples[1].Time; Equal(AssessmentStatus.InsufficientEvidence,RomMilestoneService.Evaluate(Goal(),e).Status); });
        Test("negative / nonfinite angles insufficient", () => { var e=TwoCycles(); e.Samples[2].Angle=-1; Equal(AssessmentStatus.InsufficientEvidence,RomMilestoneService.Evaluate(Goal(),e).Status); e.Samples[2].Angle=double.NaN; Equal(AssessmentStatus.InsufficientEvidence,RomMilestoneService.Evaluate(Goal(),e).Status); });
        Test("extension deficit uses decreasing angles", () => { var g=Goal(); g.MovementType=MovementType.ElbowExtension; g.MeasurementConvention=MeasurementConvention.ExtensionDeficit; g.TargetAngle=5; var e=Evidence(30,5,4,30,5,3,30); e.MovementType=g.MovementType; e.Convention=g.MeasurementConvention; Equal(AssessmentStatus.MeasuredAchieved,RomMilestoneService.Evaluate(g,e).Status); Equal(false,RomMilestoneService.AngleMeets(g,100)); });
        Test("included joint angle extension increases toward 180", () => { var g=Goal(); g.MovementType=MovementType.ElbowExtension; g.MeasurementConvention=MeasurementConvention.IncludedJointAngle; g.TargetAngle=175; Equal(true,RomMilestoneService.AngleMeets(g,177)); Equal(false,RomMilestoneService.AngleMeets(g,5)); });
        Test("wrong flexion/extension convention rejected", () => { var g=Goal(); g.MeasurementConvention=MeasurementConvention.ExtensionDeficit; Throws<ArgumentException>(()=>RomMilestoneService.Validate(g)); });
        Test("clinical evidence needs source and reason", () => Throws<ArgumentException>(()=>RomMilestoneService.EvaluateClinical(Goal(),new ClinicalMeasurement { Angle=110, Repetitions=2, HoldSeconds=1, MeasuredAt=DateTime.Now })));
        Test("clinical result does not auto-confirm", () => { var g=Goal(); var c=new ClinicalMeasurement { Angle=110,Repetitions=2,HoldSeconds=1,MeasuredAt=DateTime.Now,Source="Goniometer",Reason="Clinical review" }; Equal(AssessmentStatus.MeasuredAchieved,RomMilestoneService.EvaluateClinical(g,c).Status); Equal(false,g.IsConfirmed); });
        Test("clinical results enforce repetition criteria", () => { var c=new ClinicalMeasurement { Angle=110,Repetitions=1,HoldSeconds=1,MeasuredAt=DateTime.Now,Source="Goniometer",Reason="Clinical review" }; Equal(AssessmentStatus.NotAchieved,RomMilestoneService.EvaluateClinical(Goal(),c).Status); });
        Test("password salted hashes verify and reject wrong password", () => { var a=PasswordHasher.Hash("A-strong-test-password1"); var b=PasswordHasher.Hash("A-strong-test-password1"); Equal(false,a==b); Equal(true,PasswordHasher.Verify("A-strong-test-password1",a)); Equal(false,PasswordHasher.Verify("wrong",a)); Equal(false,PasswordHasher.Verify("anything","bad")); });
        Test("demo cannot open when disabled before any SQL access", () => Throws<UnauthorizedAccessException>(()=>new DoctorRepository(()=>throw new Exception("SQL was accessed")).OpenDemoSession(false)));
        Test("demo marker and session are explicit", () => { var d=new DoctorSession(4,"Bác sĩ demo","Doctor",true); Equal(true,d.IsDemo); Equal(DateTime.MaxValue,d.ExpiresAtUtc); Equal("[DEMO] review",DoctorRepository.MarkDemo(d,"review")); Equal("review",DoctorRepository.MarkDemo(Doctor(),"review")); });
        Test("demo confirmation is visibly distinct in report", () => { var m=Confirmable(); m.Status=MilestoneStatus.DoctorConfirmed; m.OriginalConfirmed=true; m.ConfirmationId=7; m.DoctorName="Bác sĩ demo"; m.DoctorUsername=DoctorRepository.DemoUsername; Equal("Xác nhận demo",m.StatusLabel); Equal(true,RomReportBuilder.Build(1,new[]{m}).Contains("BÁO CÁO DEMO")); });
        Test("doctor can check eligible milestone", () => { var m=Confirmable(); RomMilestoneService.ValidateConfirmation(1,m,Change(m,true),Doctor()); });
        Test("cannot check without achieved evidence", () => { var m=Confirmable(); m.AssessmentState="InsufficientEvidence"; Throws<InvalidOperationException>(()=>RomMilestoneService.ValidateConfirmation(1,m,Change(m,true),Doctor())); });
        Test("revoke requires reason", () => { var m=Confirmable(); m.ConfirmationId=2; var change=Change(m,false); change.Reason=" "; Throws<ArgumentException>(()=>RomMilestoneService.ValidateConfirmation(1,m,change,Doctor())); });
        Test("doctor can revoke with reason", () => { var m=Confirmable(); m.ConfirmationId=2; RomMilestoneService.ValidateConfirmation(1,m,Change(m,false),Doctor()); });
        Test("duplicate current confirmation rejected", () => { var m=Confirmable(); m.ConfirmationId=2; Throws<InvalidOperationException>(()=>RomMilestoneService.ValidateConfirmation(1,m,Change(m,true),Doctor())); });
        Test("wrong patient cannot be confirmed", () => { var m=Confirmable(); Throws<InvalidOperationException>(()=>RomMilestoneService.ValidateConfirmation(2,m,Change(m,true),Doctor())); });
        Test("patient identity has no confirmation permission", () => { var m=Confirmable(); Throws<UnauthorizedAccessException>(()=>RomMilestoneService.ValidateConfirmation(1,m,Change(m,true),new DoctorSession(1,"Patient","Patient"))); Throws<UnauthorizedAccessException>(()=>RomMilestoneService.Authorize(null)); });
        Test("stale assessment cannot be confirmed", () => { var m=Confirmable(); var change=Change(m,true); change.AssessmentId=9; Throws<InvalidOperationException>(()=>RomMilestoneService.ValidateConfirmation(1,m,change,Doctor())); });
        Test("report escapes clinical notes", () => { var m=Confirmable(); m.Notes="<script>bad</script>"; var html=RomReportBuilder.Build(1,new[]{m}); Equal(false,html.Contains("<script>")); Equal(true,html.Contains("&lt;script&gt;")); });
    }
    private static RomMilestone Confirmable()
    { var m=Goal(); m.MilestoneId=1; m.Revision=1; m.AssessmentId=1; m.AssessmentState="MeasuredAchieved"; return m; }
    private static ConfirmationChange Change(RomMilestone m, bool confirm)
    { return new ConfirmationChange { MilestoneId=m.MilestoneId,Revision=m.Revision,AssessmentId=m.AssessmentId,ExpectedConfirmationId=m.ConfirmationId,Confirm=confirm,Reason=confirm ? "Clinically reviewed" : "Correction after examination" }; }

    private static string root;
    private static string FindRoot()
    {
        var p=new DirectoryInfo(Environment.CurrentDirectory);
        while (p!=null) { if (File.Exists(Path.Combine(p.FullName,"create_tables.sql"))) return p.FullName; p=p.Parent; }
        throw new Exception("Run tests from repository root.");
    }
    private static void Execute(string connection, string sql)
    { using (var c=new SqlConnection(connection)) using (var cmd=new SqlCommand(sql,c)) { c.Open(); cmd.CommandTimeout=90; cmd.ExecuteNonQuery(); } }
    private static int Scalar(string connection, string sql)
    { using (var c=new SqlConnection(connection)) using (var cmd=new SqlCommand(sql,c)) { c.Open(); return Convert.ToInt32(cmd.ExecuteScalar()); } }
    private static void Integration()
    {
        string admin=Environment.GetEnvironmentVariable("REHABTRACK_TEST_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrEmpty(admin)) throw new Exception("Integration requires REHABTRACK_TEST_ADMIN_CONNECTION_STRING; no application DB is used.");
        var builder=new SqlConnectionStringBuilder(admin);
        if (builder.InitialCatalog!="master") throw new Exception("Test admin connection must target master.");
        string database="RehabTrackTests_"+Guid.NewGuid().ToString("N");
        Execute(admin,"CREATE DATABASE ["+database+"]"); builder.InitialCatalog=database; string connection=builder.ConnectionString;
        try
        {
            string legacy=File.ReadAllText(Path.Combine(root,"create_tables.sql")).Replace("USE [RehabDB]", "USE ["+database+"]");
            foreach (string batch in Regex.Split(legacy,@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase)) if (!string.IsNullOrWhiteSpace(batch)) Execute(connection,batch);
            Execute(connection,@"INSERT dbo.Users(PatientCode,FullName) VALUES(N'LEGACY1',N'Legacy patient'),(N'LEGACY2',N'Other patient');
                INSERT dbo.TrainingSessions(UserId,StartTime,EndTime,TotalReps,PeakROM,AvgROM,ChartDataJson)
                VALUES(1,'2026-01-01T10:00:00','2026-01-01T10:00:10',2,105,80,N'[{""Time"":0,""Angle"":50},{""Time"":0.25,""Angle"":99},{""Time"":0.5,""Angle"":103},{""Time"":0.75,""Angle"":50},{""Time"":1,""Angle"":99},{""Time"":1.25,""Angle"":105},{""Time"":1.5,""Angle"":50}]');
                INSERT dbo.SensorData(UserId,Angle,ForceWeight,RecordTime) VALUES(1,50,0,'2026-01-01T10:00:00');");
            string migration=File.ReadAllText(Path.Combine(root,"migrations","001_doctor_workspace.sql"));
            Test("SQL migration executes twice without losing legacy rows", () => { Execute(connection,migration); Execute(connection,migration); Equal(2,Scalar(connection,"SELECT COUNT(*) FROM dbo.Users")); Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.TrainingSessions")); Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.SensorData")); Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.RehabSchemaMigrations")); });
            using (var c=new SqlConnection(connection)) using (var cmd=new SqlCommand("INSERT dbo.DoctorAccounts(FullName,Username,PasswordHash) VALUES(N'Test Doctor',N'doctor',@h)",c))
            { c.Open(); cmd.Parameters.AddWithValue("@h",PasswordHasher.Hash("Valid-test-password1")); cmd.ExecuteNonQuery(); }
            Func<SqlConnection> factory=()=>new SqlConnection(connection);
            var auth=new DoctorRepository(factory); DoctorSession doctor=null;
            Test("SQL doctor authentication", () => { doctor=auth.Authenticate("doctor","Valid-test-password1"); Equal(1,doctor.DoctorId); });
            if (doctor==null) throw new Exception("Cannot continue without authenticated doctor.");
            var repo=new DoctorWorkspaceRepository(factory);
            Test("SQL legacy sessions and patient-scoped load", () => { Equal(1,repo.Sessions(1,doctor).Rows.Count); Equal(0,repo.Sessions(2,doctor).Rows.Count); Equal(2,repo.Patients(doctor).Rows.Count); });
            int id=0;
            Test("SQL create ROM milestone", () => { id=repo.SaveMilestone(Goal(),doctor,"Initial clinical goal"); Equal(true,id>0); });
            Func<RomMilestone> load=()=>repo.Milestones(1,doctor).Single(m=>m.MilestoneId==id);
            Test("SQL update creates revision and invalidates old evaluation", () => { var m=load(); m.TargetAngle=101; repo.SaveMilestone(m,doctor,"Adjust therapy plan"); Equal(2,load().Revision); });
            Test("SQL old raw data insufficient until explicitly verified", () => { var r=repo.AssessSession(load(),1,false,false,false,"Sensor convention not verified",doctor); Equal(AssessmentStatus.InsufficientEvidence,r.Status); Equal(false,load().IsConfirmed); });
            Test("SQL verified repeated sensor evidence only measures achieved", () => { Equal(AssessmentStatus.MeasuredAchieved,repo.AssessSession(load(),1,true,true,true,"Verified flexion 0 degrees straight, calibration and timestamps",doctor).Status); Equal(false,load().IsConfirmed); });
            Test("SQL wrong-patient session cannot be assessed", () => { var g=Goal(2); g.MilestoneId=repo.SaveMilestone(g,doctor,"Other patient goal"); g=repo.Milestones(2,doctor).Single(); Throws<InvalidOperationException>(()=>repo.AssessSession(g,1,true,true,true,"Verified evidence",doctor)); });
            Test("SQL transaction rolls back entire invalid checkbox batch", () => { var m=load(); var other=repo.Milestones(2,doctor).Single(); Throws<InvalidOperationException>(()=>repo.SaveConfirmations(1,new[]{Change(m,true),Change(other,true)},doctor)); Equal(false,load().IsConfirmed); Equal(0,Scalar(connection,"SELECT COUNT(*) FROM dbo.RomConfirmations")); });
            var stale=Change(load(),true);
            Test("SQL save checkbox and audit", () => { repo.SaveConfirmations(1,new[]{Change(load(),true)},doctor); Equal(true,load().IsConfirmed); Equal(true,load().ConfirmedAt.HasValue); Equal("Test Doctor",load().DoctorName); Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.RomConfirmationAudit WHERE Action='Confirm'")); });
            Test("SQL reopen connection / repository preserves checkbox", () => { SqlConnection.ClearAllPools(); var another=new DoctorWorkspaceRepository(factory); var login=auth.Authenticate("doctor","Valid-test-password1"); Equal(true,another.Milestones(1,login).Single().IsConfirmed); });
            Test("SQL stale duplicate confirmation rejected", () => Throws<InvalidOperationException>(()=>repo.SaveConfirmations(1,new[]{stale},doctor)));
            Test("SQL unique index prevents second current confirmation", () => Throws<SqlException>(()=>Execute(connection,"INSERT dbo.RomConfirmations(AssessmentId,PatientId,MilestoneId,DoctorId,Notes) SELECT AssessmentId,PatientId,MilestoneId,DoctorId,N'duplicate' FROM dbo.RomConfirmations WHERE IsCurrent=1")));
            Test("SQL confirmed goal cannot be silently edited", () => Throws<InvalidOperationException>(()=>repo.SaveMilestone(load(),doctor,"Attempt silent edit")));
            Test("SQL revoke without reason rejected", () => { var ch=Change(load(),false); ch.Reason=" "; Throws<ArgumentException>(()=>repo.SaveConfirmations(1,new[]{ch},doctor)); Equal(true,load().IsConfirmed); });
            Test("SQL revoke keeps history and persists after reopen", () => { repo.SaveConfirmations(1,new[]{Change(load(),false)},doctor); Equal(false,new DoctorWorkspaceRepository(factory).Milestones(1,doctor).Single().IsConfirmed); Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.RomConfirmations WHERE IsCurrent=0")); Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.RomConfirmationAudit WHERE Action='Revoke' AND LEN(Reason)>0")); });
            Test("SQL clinical assessment without sensor session", () => { var r=repo.AssessClinical(load(),new ClinicalMeasurement{Angle=110,Repetitions=2,HoldSeconds=1,MeasuredAt=DateTime.Now,Source="Goniometer",Reason="Manual examination confirms repeated movement"},doctor); Equal(AssessmentStatus.MeasuredAchieved,r.Status); Equal(null,load().SessionId); repo.SaveConfirmations(1,new[]{Change(load(),true)},doctor); Equal(true,load().IsConfirmed); });
            Test("SQL wrong-patient FK rejects assessment association", () =>
            {
                try
                {
                    Execute(connection,"INSERT dbo.RomConfirmations(AssessmentId,PatientId,MilestoneId,DoctorId,Notes) SELECT TOP(1) AssessmentId,2,(SELECT TOP(1) MilestoneId FROM dbo.PatientRomMilestones WHERE PatientId=2),1,N'wrong patient' FROM dbo.RomAssessments WHERE PatientId=1");
                }
                catch (SqlException ex) { Equal(547,ex.Number); return; }
                throw new Exception("Expected composite foreign key violation.");
            });
            Test("SQL concurrent editors detect stale revision", () => { var m=repo.Milestones(2,doctor).Single(); var old=repo.Milestones(2,doctor).Single(); m.TargetAngle=105; repo.SaveMilestone(m,doctor,"New plan"); Throws<InvalidOperationException>(()=>repo.SaveMilestone(old,doctor,"Stale plan")); });
            Test("SQL audit and assessment history reload", () => { Equal(true,repo.Audit(1,doctor).Rows.Count>=5); Equal(true,repo.Assessments(1,doctor).Rows.Count>=3); Equal(1,repo.Sessions(1,doctor).Rows.Count); });
            Test("SQL two concurrent checkbox saves produce exactly one current confirmation", () =>
            {
                var other=repo.Milestones(2,doctor).Single();
                repo.AssessClinical(other,new ClinicalMeasurement { Angle=110, Repetitions=2, HoldSeconds=1, MeasuredAt=DateTime.Now, Source="Goniometer", Reason="Concurrent review fixture" },doctor);
                var change=Change(repo.Milestones(2,doctor).Single(),true);
                int success=0, rejected=0;
                Action save=()=>
                {
                    try { new DoctorWorkspaceRepository(factory).SaveConfirmations(2,new[]{change},doctor); Interlocked.Increment(ref success); }
                    catch(InvalidOperationException) { Interlocked.Increment(ref rejected); }
                    catch(SqlException ex) { if (ex.Number!=1205) throw; Interlocked.Increment(ref rejected); }
                };
                Parallel.Invoke(save,save); Equal(1,success); Equal(1,rejected);
                Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.RomConfirmations WHERE PatientId=2 AND IsCurrent=1"));
            });
            Execute(connection,"INSERT dbo.Users(PatientCode,FullName) VALUES(N'SENSOR3',N'Sensor fallback patient')");
            int thirdId=repo.SaveMilestone(Goal(3),doctor,"Sensor fallback evaluation");
            Func<RomMilestone> third=()=>repo.Milestones(3,doctor).Single(m=>m.MilestoneId==thirdId);
            int sensorSession=Scalar(connection,@"INSERT dbo.TrainingSessions(UserId,StartTime,EndTime,TotalReps) OUTPUT INSERTED.SessionId VALUES(3,'2026-02-01T10:00:00','2026-02-01T10:00:10',2)");
            Execute(connection,@"INSERT dbo.SensorData(UserId,Angle,ForceWeight,RecordTime) VALUES
                (3,50,0,'2026-02-01T10:00:00'),(3,99,0,'2026-02-01T10:00:00.250'),(3,103,0,'2026-02-01T10:00:00.500'),
                (3,50,0,'2026-02-01T10:00:00.750'),(3,99,0,'2026-02-01T10:00:01'),(3,105,0,'2026-02-01T10:00:01.250'),(3,50,0,'2026-02-01T10:00:01.500')");
            Test("SQL unambiguous SensorData window can be evaluated after explicit verification", () =>
            {
                Equal(AssessmentStatus.MeasuredAchieved,repo.AssessSession(third(),sensorSession,true,true,true,"Verified sensor window, flexion convention, calibration, quality",doctor).Status);
                Equal(true,third().MeasurementSource.StartsWith("SensorData")); Equal(false,third().IsConfirmed);
            });
            Test("SQL overlapping SensorData sessions are insufficient evidence", () =>
            {
                Execute(connection,"INSERT dbo.TrainingSessions(UserId,StartTime,EndTime) VALUES(3,'2026-02-01T10:00:00','2026-02-01T10:00:20')");
                Equal(AssessmentStatus.InsufficientEvidence,repo.AssessSession(third(),sensorSession,true,true,true,"Cannot distinguish overlapping session data",doctor).Status);
                Equal(false,third().IsConfirmed);
            });
            Test("SQL insufficient evidence cannot be clinically confirmed through checkbox", () => Throws<InvalidOperationException>(()=>repo.SaveConfirmations(3,new[]{Change(third(),true)},doctor)));
            Test("SQL editing goal invalidates prior assessments and rejects stale evidence", () =>
            {
                var old=third(); var newer=third(); newer.TargetAngle=110; repo.SaveMilestone(newer,doctor,"Adjust target after examination");
                Equal(null,third().AssessmentId);
                Throws<InvalidOperationException>(()=>repo.AssessClinical(old,new ClinicalMeasurement { Angle=120,Repetitions=2,HoldSeconds=1,MeasuredAt=DateTime.Now,Source="Goniometer",Reason="Stale evaluation must be rejected" },doctor));
            });
            Test("SQL missing measurement timestamps stay null and insufficient", () =>
            {
                int missing=Scalar(connection,"INSERT dbo.TrainingSessions(UserId) OUTPUT INSERTED.SessionId VALUES(3)");
                Equal(AssessmentStatus.InsufficientEvidence,repo.AssessSession(third(),missing,true,true,true,"Source session has no measurement timestamps",doctor).Status);
                Equal(null,third().MeasurementTime);
            });
            Test("SQL malformed chart JSON never produces measured achievement", () =>
            {
                int malformed=Scalar(connection,"INSERT dbo.TrainingSessions(UserId,StartTime,EndTime,ChartDataJson) OUTPUT INSERTED.SessionId VALUES(3,'2026-03-01T10:00:00','2026-03-01T10:00:10',N'{bad')");
                Equal(AssessmentStatus.InsufficientEvidence,repo.AssessSession(third(),malformed,true,true,true,"Malformed chart evidence must remain insufficient",doctor).Status);
            });
            Test("SQL patient session cannot confirm", () => Throws<UnauthorizedAccessException>(()=>repo.SaveConfirmations(1,new[]{Change(load(),false)},new DoctorSession(1,"Patient","Patient"))));
            Test("SQL deactivated doctor cannot change confirmations", () => { Execute(connection,"UPDATE dbo.DoctorAccounts SET IsActive=0 WHERE DoctorId=1"); Throws<UnauthorizedAccessException>(()=>repo.SaveConfirmations(1,new[]{Change(load(),false)},doctor)); Execute(connection,"UPDATE dbo.DoctorAccounts SET IsActive=1 WHERE DoctorId=1"); });
            Test("SQL login lockout after five wrong passwords", () => { for(int i=0;i<5;i++) Throws<UnauthorizedAccessException>(()=>auth.Authenticate("doctor","wrong")); Throws<UnauthorizedAccessException>(()=>auth.Authenticate("doctor","Valid-test-password1")); });
            Test("SQL demo account is gated by configuration", () => { Throws<UnauthorizedAccessException>(()=>auth.OpenDemoSession(false)); Equal(0,Scalar(connection,"SELECT COUNT(*) FROM dbo.DoctorAccounts WHERE Username=N'__rehabtrack_demo__'")); });
            DoctorSession demo=null;
            Test("SQL demo account is created once with an unusable password", () =>
            {
                demo=auth.OpenDemoSession(true); Equal(true,demo.IsDemo); Equal("Bác sĩ demo",demo.FullName);
                Equal(demo.DoctorId,auth.OpenDemoSession(true).DoctorId);
                Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.DoctorAccounts WHERE Username=N'__rehabtrack_demo__'"));
                Throws<UnauthorizedAccessException>(()=>auth.Authenticate(DoctorRepository.DemoUsername,"unknown"));
            });
            if (demo==null) throw new Exception("Cannot continue without demo account.");
            Execute(connection,"INSERT dbo.Users(PatientCode,FullName) VALUES(N'DEMO4',N'Demo patient')");
            int demoGoal=0;
            Test("SQL demo assessment, checkbox and audit are marked", () =>
            {
                demoGoal=repo.SaveMilestone(Goal(4),demo,"Demo treatment goal");
                var m=repo.Milestones(4,demo).Single(x=>x.MilestoneId==demoGoal);
                repo.AssessClinical(m,new ClinicalMeasurement { Angle=110,Repetitions=2,HoldSeconds=1,MeasuredAt=DateTime.Now,Source="Goniometer",Reason="Demo examination" },demo);
                m=repo.Milestones(4,demo).Single(x=>x.MilestoneId==demoGoal);
                repo.SaveConfirmations(4,new[]{Change(m,true)},demo);
                m=new DoctorWorkspaceRepository(factory).Milestones(4,auth.OpenDemoSession(true)).Single(x=>x.MilestoneId==demoGoal);
                Equal(true,m.IsConfirmed); Equal(true,m.IsDemoConfirmation); Equal("Xác nhận demo",m.StatusLabel);
                Equal(true,m.Notes.Contains("[DEMO]"));
                Equal(true,RomReportBuilder.Build(4,new[]{m}).Contains("BÁO CÁO DEMO"));
                Equal(2,Scalar(connection,"SELECT COUNT(*) FROM dbo.RomConfirmationAudit WHERE PatientId=4 AND Reason LIKE N'[[]DEMO]%'"));
            });
            Test("SQL demo revoke persists reason and history", () =>
            {
                var m=repo.Milestones(4,demo).Single(x=>x.MilestoneId==demoGoal);
                repo.SaveConfirmations(4,new[]{Change(m,false)},demo);
                Equal(false,new DoctorWorkspaceRepository(factory).Milestones(4,demo).Single(x=>x.MilestoneId==demoGoal).IsConfirmed);
                Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.RomConfirmationAudit WHERE PatientId=4 AND Action='Revoke' AND Reason LIKE N'[[]DEMO]%'"));
            });
            Test("SQL demo identity cannot impersonate real doctor", () =>
                Throws<UnauthorizedAccessException>(()=>repo.Patients(new DoctorSession(doctor.DoctorId,"Bác sĩ demo","Doctor",true))));
            Test("SQL disabled demo account is not reactivated", () =>
            {
                Execute(connection,"UPDATE dbo.DoctorAccounts SET IsActive=0 WHERE Username=N'__rehabtrack_demo__'");
                Throws<UnauthorizedAccessException>(()=>auth.OpenDemoSession(true));
                Equal(0,Scalar(connection,"SELECT COUNT(*) FROM dbo.DoctorAccounts WHERE Username=N'__rehabtrack_demo__' AND IsActive=1"));
                Equal(1,Scalar(connection,"SELECT COUNT(*) FROM dbo.DoctorAccounts WHERE Username=N'doctor' AND IsActive=1"));
            });
        }
        finally { SqlConnection.ClearAllPools(); Execute(admin,"ALTER DATABASE ["+database+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+database+"]"); }
    }
    private static int Main(string[] args)
    {
        root=FindRoot(); Units();
        if (args.Contains("--integration"))
        { try { Integration(); } catch (Exception ex) { failed++; Console.WriteLine("FAIL integration setup: "+ex.Message); } }
        else Console.WriteLine("SKIP SQL integration (use --integration with disposable SQL Server admin connection)");
        Console.WriteLine(passed+" passed, "+failed+" failed"); return failed==0 ? 0 : 1;
    }
}
