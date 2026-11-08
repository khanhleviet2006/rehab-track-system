-- Run against the existing RehabDB (SQL Server 2016+). No legacy rows are deleted.
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock = sys.sp_getapplock @Resource=N'RehabTrack.DoctorWorkspace.Migration', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
 IF @lock < 0 THROW 51000, 'Cannot acquire migration lock.', 1;
 IF OBJECT_ID(N'dbo.Users',N'U') IS NULL OR OBJECT_ID(N'dbo.TrainingSessions',N'U') IS NULL OR OBJECT_ID(N'dbo.SensorData',N'U') IS NULL
   THROW 51001, 'Create the original RehabTrack tables first.', 1;
 IF OBJECT_ID(N'dbo.RehabSchemaMigrations',N'U') IS NULL
   CREATE TABLE dbo.RehabSchemaMigrations(Version int NOT NULL PRIMARY KEY, AppliedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
 IF EXISTS(SELECT 1 FROM dbo.RehabSchemaMigrations WHERE Version=1)
 BEGIN
   COMMIT;
   RETURN;
 END;
 IF OBJECT_ID(N'dbo.DoctorAccounts',N'U') IS NOT NULL
   THROW 51002, 'Partial or conflicting Doctor Workspace schema; inspect before migrating.', 1;
 -- Compatibility repair: original patient forms already read/write CCCD.
 IF COL_LENGTH(N'dbo.Users',N'CCCD') IS NULL ALTER TABLE dbo.Users ADD CCCD nvarchar(50) NULL;

 CREATE TABLE dbo.DoctorAccounts(
   DoctorId int IDENTITY PRIMARY KEY,
   FullName nvarchar(100) NOT NULL CHECK(LEN(LTRIM(RTRIM(FullName)))>0),
   Username nvarchar(100) NOT NULL UNIQUE,
   PasswordHash varchar(256) NOT NULL CHECK(PasswordHash LIKE 'PBKDF2-SHA256$%'),
   Role varchar(20) NOT NULL DEFAULT 'Doctor' CHECK(Role IN ('Doctor','Admin')),
   IsActive bit NOT NULL DEFAULT 1,
   FailedLoginAttempts int NOT NULL DEFAULT 0,
   LockedUntil datetime2 NULL,
   CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
 );
 CREATE TABLE dbo.PatientRomMilestones(
   MilestoneId int IDENTITY PRIMARY KEY,
   PatientId int NOT NULL REFERENCES dbo.Users(Id),
   MovementType varchar(30) NOT NULL CHECK(MovementType IN ('ElbowFlexion','ElbowExtension')),
   TargetAngle float NOT NULL CHECK(TargetAngle BETWEEN 0 AND 180),
   MeasurementConvention varchar(30) NOT NULL CHECK(MeasurementConvention IN ('Unknown','FlexionDegrees','ExtensionDeficit','IncludedJointAngle')),
   Tolerance float NOT NULL CHECK(Tolerance BETWEEN 0 AND 20),
   RequiredRepetitions int NOT NULL CHECK(RequiredRepetitions BETWEEN 1 AND 1000),
   RequiredHoldSeconds float NOT NULL CHECK(RequiredHoldSeconds BETWEEN 0 AND 600),
   ResetDelta float NOT NULL CHECK(ResetDelta>0 AND ResetDelta<=180),
   MaxSampleGapSeconds float NOT NULL CHECK(MaxSampleGapSeconds>0 AND MaxSampleGapSeconds<=10),
   Status varchar(30) NOT NULL DEFAULT 'NotAchieved' CHECK(Status IN ('NotAchieved','MeasuredAchieved','DoctorConfirmed')),
   Revision int NOT NULL DEFAULT 1 CHECK(Revision>0),
   CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
   UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
   UNIQUE(MilestoneId,PatientId),
   CHECK(MeasurementConvention='Unknown' OR (MovementType='ElbowFlexion' AND MeasurementConvention='FlexionDegrees') OR
         (MovementType='ElbowExtension' AND MeasurementConvention IN ('ExtensionDeficit','IncludedJointAngle')))
 );
 CREATE INDEX IX_Milestones_Patient ON dbo.PatientRomMilestones(PatientId,MovementType,TargetAngle);
 -- Supports a patient-scoped FK without changing any existing session row.
 CREATE UNIQUE INDEX UX_TrainingSessions_SessionPatient ON dbo.TrainingSessions(SessionId,UserId);
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TrainingSessions') AND name=N'IX_TrainingSessions_PatientTime')
   CREATE INDEX IX_TrainingSessions_PatientTime ON dbo.TrainingSessions(UserId,StartTime) INCLUDE(EndTime,TotalReps,PeakROM,AvgROM);
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SensorData') AND name=N'IX_SensorData_PatientTime')
   CREATE INDEX IX_SensorData_PatientTime ON dbo.SensorData(UserId,RecordTime) INCLUDE(Angle);
 CREATE TABLE dbo.RomAssessments(
   AssessmentId int IDENTITY PRIMARY KEY,
   PatientId int NOT NULL,
   MilestoneId int NOT NULL,
   MilestoneRevision int NOT NULL,
   SessionId int NULL,
   TargetAngle float NOT NULL,
   MovementType varchar(30) NOT NULL,
   MeasurementConvention varchar(30) NOT NULL,
   MeasuredAngle float NULL CHECK(MeasuredAngle BETWEEN 0 AND 180),
   MeasurementSource nvarchar(255) NOT NULL CHECK(LEN(LTRIM(RTRIM(MeasurementSource)))>0),
   MeasurementTime datetime2 NULL,
   AssessmentStatus varchar(30) NOT NULL CHECK(AssessmentStatus IN ('NotAchieved','MeasuredAchieved','InsufficientEvidence')),
   AchievedRepetitions int NOT NULL CHECK(AchievedRepetitions>=0),
   CriteriaSnapshot nvarchar(1000) NOT NULL,
   Notes nvarchar(2000) NOT NULL,
   CreatedByDoctorId int NOT NULL REFERENCES dbo.DoctorAccounts(DoctorId),
   CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
   FOREIGN KEY(MilestoneId,PatientId) REFERENCES dbo.PatientRomMilestones(MilestoneId,PatientId),
   FOREIGN KEY(SessionId,PatientId) REFERENCES dbo.TrainingSessions(SessionId,UserId),
   UNIQUE(AssessmentId,PatientId,MilestoneId)
 );
 CREATE INDEX IX_Assessments_Milestone ON dbo.RomAssessments(MilestoneId,MilestoneRevision,AssessmentId DESC);
 CREATE TABLE dbo.RomConfirmations(
   ConfirmationId int IDENTITY PRIMARY KEY,
   AssessmentId int NOT NULL,
   PatientId int NOT NULL,
   MilestoneId int NOT NULL,
   DoctorId int NOT NULL REFERENCES dbo.DoctorAccounts(DoctorId),
   ConfirmedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
   Notes nvarchar(2000) NOT NULL CHECK(LEN(LTRIM(RTRIM(Notes)))>0),
   IsCurrent bit NOT NULL DEFAULT 1,
   FOREIGN KEY(AssessmentId,PatientId,MilestoneId) REFERENCES dbo.RomAssessments(AssessmentId,PatientId,MilestoneId)
 );
 CREATE UNIQUE INDEX UX_Confirmations_CurrentMilestone ON dbo.RomConfirmations(MilestoneId) WHERE IsCurrent=1;
 CREATE INDEX IX_Confirmations_Patient ON dbo.RomConfirmations(PatientId,ConfirmedAt);
 CREATE TABLE dbo.RomConfirmationAudit(
   AuditId bigint IDENTITY PRIMARY KEY,
   PatientId int NOT NULL,
   MilestoneId int NOT NULL,
   ConfirmationId int NULL REFERENCES dbo.RomConfirmations(ConfirmationId),
   DoctorId int NOT NULL REFERENCES dbo.DoctorAccounts(DoctorId),
   Action varchar(30) NOT NULL CHECK(Action IN ('Confirm','Revoke','GoalCreated','GoalUpdated')),
   Reason nvarchar(2000) NOT NULL CHECK(LEN(LTRIM(RTRIM(Reason)))>0),
   ChangedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
   FOREIGN KEY(MilestoneId,PatientId) REFERENCES dbo.PatientRomMilestones(MilestoneId,PatientId)
 );
 CREATE INDEX IX_ConfirmationAudit_Patient ON dbo.RomConfirmationAudit(PatientId,ChangedAt DESC);
 INSERT dbo.RehabSchemaMigrations(Version) VALUES(1);
 COMMIT;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
