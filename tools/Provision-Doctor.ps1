param(
    [Parameter(Mandatory=$true)][string]$ConnectionString,
    [Parameter(Mandatory=$true)][string]$Username,
    [Parameter(Mandatory=$true)][string]$FullName
)
# Run by the SQL database owner on Windows PowerShell 5.1+ / .NET Framework 4.8.
# No self-registration in the patient UI. Never pass a password as a CLI argument.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Username) -or $Username.Length -gt 100 -or
    [string]::IsNullOrWhiteSpace($FullName) -or $FullName.Length -gt 100) { throw 'Invalid username or full name.' }
Add-Type -AssemblyName System.Data
$connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
try {
    $connection.Open()
    $check = $connection.CreateCommand()
    $check.CommandText = "SELECT CASE WHEN IS_ROLEMEMBER('db_owner')=1 OR IS_SRVROLEMEMBER('sysadmin')=1 THEN 1 ELSE 0 END"
    if ($check.ExecuteScalar() -ne 1) { throw 'Doctor provisioning requires the database owner.' }
    $securePassword = Read-Host 'Doctor password (12-256 characters)' -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
    if ($plain.Length -lt 12 -or $plain.Length -gt 256) { throw 'Password must contain 12-256 characters.' }
    $salt = New-Object byte[] 16
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($salt); $rng.Dispose()
    $kdf = [Security.Cryptography.Rfc2898DeriveBytes]::new($plain, $salt, 210000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $hash = 'PBKDF2-SHA256$210000$' + [Convert]::ToBase64String($salt) + '$' + [Convert]::ToBase64String($kdf.GetBytes(32)) }
    finally { $kdf.Dispose(); $plain = $null; $securePassword.Dispose() }
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = "INSERT dbo.DoctorAccounts(FullName,Username,PasswordHash,Role) VALUES(@name,@username,@hash,'Doctor')"
    $null = $cmd.Parameters.Add('@name',[System.Data.SqlDbType]::NVarChar,100); $cmd.Parameters['@name'].Value=$FullName.Trim()
    $null = $cmd.Parameters.Add('@username',[System.Data.SqlDbType]::NVarChar,100); $cmd.Parameters['@username'].Value=$Username.Trim()
    $null = $cmd.Parameters.Add('@hash',[System.Data.SqlDbType]::VarChar,256); $cmd.Parameters['@hash'].Value=$hash
    $null = $cmd.ExecuteNonQuery()
    Write-Host 'Doctor account created. No password was written to disk.'
} finally { $connection.Dispose() }
