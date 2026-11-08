using System;
using System.Data;
using System.Data.SqlClient;

namespace AngleMonitorWPF.Rom
{
    public sealed class DoctorRepository
    {
        public const string DemoUsername = "__rehabtrack_demo__";
        private readonly Func<SqlConnection> connectionFactory;
        public DoctorRepository(Func<SqlConnection> connectionFactory) { this.connectionFactory = connectionFactory; }

        // This account is only available when the application explicitly enables demo mode.
        // Its random password is discarded, so it cannot be used as a normal doctor login.
        public DoctorSession OpenDemoSession(bool demoEnabled)
        {
            if (!demoEnabled) throw new UnauthorizedAccessException("Chế độ demo chưa được bật.");
            using (var c = connectionFactory())
            {
                c.Open();
                using (var tx = c.BeginTransaction(IsolationLevel.Serializable))
                {
                    int id;
                    using (var cmd = new SqlCommand(@"SELECT DoctorId,FullName,Username,Role,IsActive FROM dbo.DoctorAccounts WITH(UPDLOCK,HOLDLOCK) WHERE Username=@u", c, tx))
                    {
                        cmd.Parameters.Add("@u", SqlDbType.NVarChar, 100).Value = DemoUsername;
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                if (!string.Equals((string)r["Username"], DemoUsername, StringComparison.Ordinal) ||
                                    (string)r["FullName"] != "Bác sĩ demo" || (string)r["Role"] != "Doctor" || !(bool)r["IsActive"])
                                    throw new UnauthorizedAccessException("Tài khoản demo đã bị thay đổi hoặc vô hiệu hóa; cần quản trị viên kiểm tra.");
                                id = (int)r["DoctorId"];
                            }
                            else id = 0;
                        }
                    }
                    if (id == 0)
                    {
                        var random = new byte[32];
                        using (var generator = System.Security.Cryptography.RandomNumberGenerator.Create()) generator.GetBytes(random);
                        using (var cmd = new SqlCommand(@"INSERT dbo.DoctorAccounts(FullName,Username,PasswordHash,Role)
                            OUTPUT INSERTED.DoctorId VALUES(@name,@u,@hash,'Doctor')", c, tx))
                        {
                            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = "Bác sĩ demo";
                            cmd.Parameters.Add("@u", SqlDbType.NVarChar, 100).Value = DemoUsername;
                            cmd.Parameters.Add("@hash", SqlDbType.VarChar, 256).Value = PasswordHasher.Hash(Convert.ToBase64String(random));
                            id = (int)cmd.ExecuteScalar();
                        }
                    }
                    tx.Commit();
                    return new DoctorSession(id, "Bác sĩ demo", "Doctor", true);
                }
            }
        }

        // Serialized per account; PBKDF2 and DB work are called from a worker by the WPF dialog.
        public DoctorSession Authenticate(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Trim().Length > 100 || username.Trim() == DemoUsername || string.IsNullOrEmpty(password) || password.Length > 256)
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không hợp lệ.");
            using (var c = connectionFactory())
            {
                c.Open();
                using (var tx = c.BeginTransaction(IsolationLevel.Serializable))
                {
                    int id = 0, failures = 0; string name = null, role = null, hash = null; bool active = false, locked = false;
                    using (var cmd = new SqlCommand(@"SELECT DoctorId,FullName,Role,PasswordHash,IsActive,FailedLoginAttempts,
                         CASE WHEN LockedUntil>SYSUTCDATETIME() THEN 1 ELSE 0 END AS Locked
                         FROM dbo.DoctorAccounts WITH(UPDLOCK,HOLDLOCK) WHERE Username=@u", c, tx))
                    {
                        cmd.Parameters.Add("@u", SqlDbType.NVarChar, 100).Value = username.Trim();
                        using (var r = cmd.ExecuteReader()) if (r.Read())
                        {
                            id = (int)r["DoctorId"]; name = (string)r["FullName"]; role = (string)r["Role"];
                            hash = (string)r["PasswordHash"]; active = (bool)r["IsActive"];
                            failures = (int)r["FailedLoginAttempts"]; locked = (int)r["Locked"] == 1;
                        }
                    }
                    bool valid = PasswordHasher.Verify(password, hash ?? DummyHash) && active && !locked && (role == "Doctor" || role == "Admin");
                    if (id > 0 && !locked)
                    {
                        using (var cmd = new SqlCommand(@"UPDATE dbo.DoctorAccounts SET FailedLoginAttempts=@n,
                          LockedUntil=CASE WHEN @n>=5 THEN DATEADD(minute,15,SYSUTCDATETIME()) ELSE NULL END WHERE DoctorId=@id", c, tx))
                        {
                            cmd.Parameters.AddWithValue("@n", valid ? 0 : (failures >= 5 ? 1 : failures + 1));
                            cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                    if (!valid) throw new UnauthorizedAccessException("Sai thông tin, tài khoản bị vô hiệu hóa hoặc tạm khóa. Sau 5 lần sai, chờ 15 phút.");
                    return new DoctorSession(id, name, role);
                }
            }
        }
        private const string DummyHash = "PBKDF2-SHA256$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        internal static void RequireActive(SqlConnection c, SqlTransaction tx, DoctorSession doctor)
        {
            RomMilestoneService.Authorize(doctor);
            using (var cmd = new SqlCommand("SELECT Username FROM dbo.DoctorAccounts WITH(HOLDLOCK) WHERE DoctorId=@d AND IsActive=1 AND Role IN ('Doctor','Admin')", c, tx))
            {
                cmd.Parameters.AddWithValue("@d", doctor.DoctorId);
                var username = cmd.ExecuteScalar() as string;
                if (username == null || (doctor.IsDemo && !string.Equals(username, DemoUsername, StringComparison.Ordinal)))
                    throw new UnauthorizedAccessException("Tài khoản bác sĩ không còn quyền truy cập.");
            }
        }

        internal static string MarkDemo(DoctorSession doctor, string value)
        { return doctor.IsDemo ? "[DEMO] " + value : value; }
    }
}
