using System;
using System.Data;
using System.Data.SqlClient;

namespace AngleMonitorWPF.Rom
{
    public sealed class DoctorRepository
    {
        private readonly Func<SqlConnection> connectionFactory;
        public DoctorRepository(Func<SqlConnection> connectionFactory) { this.connectionFactory = connectionFactory; }

        // Serialized per account; PBKDF2 and DB work are called from a worker by the WPF dialog.
        public DoctorSession Authenticate(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Trim().Length > 100 || string.IsNullOrEmpty(password) || password.Length > 256)
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
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.DoctorAccounts WITH(HOLDLOCK) WHERE DoctorId=@d AND IsActive=1 AND Role IN ('Doctor','Admin')", c, tx))
            {
                cmd.Parameters.AddWithValue("@d", doctor.DoctorId);
                if ((int)cmd.ExecuteScalar() != 1) throw new UnauthorizedAccessException("Tài khoản bác sĩ không còn quyền truy cập.");
            }
        }
    }
}
