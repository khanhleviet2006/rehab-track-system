# Doctor Workspace — RehabTrack

## Tình trạng bàn giao

Mã nguồn được sửa trực tiếp trong ứng dụng hiện có. Không tạo ứng dụng WPF thay thế, không đổi target framework và không thay giao thức cảm biến. Nhánh làm việc: `feature/doctor-workspace-rom`.

Đã triển khai luồng UI → đánh giá ROM → xác thực bác sĩ → transaction SQL → tải lại checkbox, hủy xác nhận và lịch sử. Phần nghiệp vụ và lưu trữ được thực thi bằng kiểm thử với SQL Server 2022 tạm. Chưa chạy ứng dụng WPF, BAML, Bluetooth hoặc render PDF bằng WebView2 trên Windows; vì vậy chưa xác nhận toàn bộ tiêu chí nghiệm thu đầu cuối.

## Phân tích trước khi thay đổi

- `AngleMonitorWPF.csproj` là project WPF cổ điển, khai báo từng `Compile`/`Page`, target **.NET Framework 4.8**. README ban đầu ghi .NET 10 nhưng không khớp project. Không chuyển sang SDK-style hay nâng framework ứng dụng.
- Ứng dụng sử dụng code-behind, ADO.NET `System.Data.SqlClient`, SQL Server `RehabDB`. `App.xaml` khởi động `PatientSelectionWindow`. `AuthWindow` chỉ tra mã bệnh nhân, không phải xác thực bác sĩ.
- `Users` là bệnh nhân. `GlobalData.CurrentUserId` là bệnh nhân đang tập. Workspace giữ `patientId` riêng; `DoctorSession.DoctorId` được cấp sau khi xác thực bảng `DoctorAccounts`. Workspace không ghi vào `GlobalData.CurrentUserId`. `AnalysisTab` nhận và giữ ID bệnh nhân riêng để tránh truy vấn theo biến toàn cục thay đổi sau đó.
- Luồng hiện có được tái sử dụng: tìm kiếm/thêm/chọn bệnh nhân, COM Bluetooth, hàng đợi mẫu, làm mượt hai mẫu, cập nhật UI mỗi 16 ms, đếm reps, lưu session và JSON, LiveCharts 0.9.7, biểu đồ lịch sử, WebView2/PDFsharp xuất PDF.
- `MainWindow` đọc dòng số thực từ serial; JSON gồm `Time`, `Angle` và được lấy mẫu thưa khi vẽ/lưu. Comment gọi góc gập nhưng repository không có firmware, hiệu chuẩn, mã vận động, nguồn gốc lâm sàng hay cờ chất lượng. Không đủ chứng cứ xác định giá trị là góc gập, góc trong khớp hoặc thiếu duỗi. Không dùng comment, `PeakROM`, histogram hoặc tổng reps để suy ra quy ước hay chất lượng.
- `SensorData` có `UserId`, `Angle`, `ForceWeight`, `RecordTime`; không có `SessionId`. Luồng đo hiện tại chủ yếu ghi `TrainingSessions.ChartDataJson`, không chèn các mẫu mới vào `SensorData`. Dữ liệu cũ có thể vẫn được đánh giá khi đáp ứng điều kiện được mô tả bên dưới.
- Script gốc UTF-16 không có `Users.CCCD` trong khi UI đọc/ghi cột này. Migration thêm cột nullable nếu thiếu. Không xóa hay dựng lại bảng cũ.
- Rủi ro tương thích: WPF/WebView2/COM cần Windows; project `.slnx` cần Visual Studio/MSBuild đủ mới; PDFsharp dùng netstandard2.0 (đã bổ sung reference `netstandard`); giữ nguyên các phiên bản NuGet sẵn có. SDK Linux không có pipeline Windows WPF/BAML.

## Cấu trúc và luồng dữ liệu

```text
PatientSelectionWindow / MainWindow / AnalysisTab
  → kiểm tra phiên bản schema → DoctorLoginWindow
  → DoctorWorkspaceWindow (patientId riêng, DoctorSession riêng)
    → RomMilestoneService (kiểm tra tiêu chí, phân quyền, tính trạng thái)
    → DoctorWorkspaceRepository (ADO.NET, tham số SQL, transaction)
      → Users / TrainingSessions / SensorData (giữ dữ liệu cũ)
      → PatientRomMilestones / RomAssessments
      → RomConfirmations / RomConfirmationAudit
```

`DoctorRepository` xác thực và kiểm tra tài khoản đang hoạt động. `PasswordHasher` dùng PBKDF2-HMAC-SHA256, salt ngẫu nhiên 16 byte, 210.000 vòng, kết quả 32 byte và so sánh toàn bộ byte. Không lưu plaintext. Năm lần sai khóa tài khoản 15 phút; phiên bác sĩ hết hạn sau 30 phút. Mọi thao tác ghi đọc lại quyền active/role ở SQL Server. UI yêu cầu xác thực lại trước khi lưu checkbox, kể cả khi đã đăng nhập workspace.

UI gọi các thao tác repository trong `Task.Run`; worker không truy cập control WPF. Trong lúc xử lý, workspace tạm khóa thao tác và không cho đóng giữa transaction. Timer/queue/serial của `MainWindow` không thêm truy vấn ROM mới. Khi chuyển bệnh nhân, dữ liệu hiển thị cũ được xóa trước khi tải bệnh nhân mới. Checkbox chưa lưu không được tự mang sang bệnh nhân khác.

## Quy tắc ROM

Mục tiêu được cấu hình từng bệnh nhân trong database, không seed danh sách góc điều trị. Có góc mục tiêu, dung sai, số lần đạt, thời gian giữ mỗi lần, biên độ trở về và khoảng mất mẫu tối đa. Các giá trị mẫu trong form là giá trị nhập ban đầu của tiêu chí kỹ thuật, không phải khuyến cáo điều trị.

| Vận động | Quy ước | Điều kiện góc |
|---|---|---|
| Gập khuỷu tay | `FlexionDegrees`: 0° khi duỗi thẳng | góc ≥ mục tiêu − dung sai |
| Duỗi khuỷu tay | `ExtensionDeficit`: số độ thiếu duỗi, 0° khi đủ duỗi | góc ≤ mục tiêu + dung sai |
| Duỗi khuỷu tay | `IncludedJointAngle`: góc trong, 180° khi duỗi thẳng | góc ≥ mục tiêu − dung sai |
| Chưa rõ quy ước | `Unknown` | `InsufficientEvidence`; không tự đánh giá đạt |

Không chuyển đổi tự động giữa các quy ước. Cặp vận động/quy ước không tương thích bị từ chối bởi nghiệp vụ và CHECK constraint.

Đánh giá chuỗi cảm biến yêu cầu bác sĩ khai báo rõ đã xác minh vận động/quy ước, hiệu chuẩn và chất lượng của buổi đang chọn, kèm ghi chú có nguồn gốc. Chưa xác minh hoặc dữ liệu không đủ: lưu `InsufficientEvidence`, không tạo xác nhận. Cờ này là ghi nhận của người đánh giá, không phải chứng nhận tự động của thiết bị.

Thuật toán đếm chu kỳ **về nền → đạt mục tiêu đủ lâu → về nền**. Nền cách ngưỡng mục tiêu ít nhất `ResetDelta`. Cần ít nhất hai mẫu liên tiếp tại vùng mục tiêu ngay cả khi yêu cầu giữ bằng 0. Lần cuối chưa trở về nền không được tính. Mỗi khoảng thời gian giữ không bị nội suy qua mất mẫu; NaN/infinity, góc ngoài [0,180], timestamp trùng/giảm hoặc khoảng mất mẫu vượt giới hạn làm kết quả `InsufficientEvidence`. Chuỗi hợp lệ nhưng thiếu số chu kỳ hoặc thời gian giữ có trạng thái `NotAchieved`. Không dùng PeakROM riêng lẻ hay số reps tổng trong session để suy ra từng lần đạt.

Nguồn ưu tiên là JSON của session đã có thời điểm kết thúc hợp lệ. Nếu không có JSON, chỉ dùng `SensorData` trong cửa sổ thời gian của session đã đóng, cùng bệnh nhân và **không có session chồng lấn**. Nguồn này được ghi rõ là cửa sổ thời gian, không giả định có khóa SessionId. JSON lỗi hoặc thiếu chứng cứ không được tự thay bằng một peak.

Đo lâm sàng không cần dữ liệu cảm biến hoặc session, nhưng bắt buộc nhập quy ước ở mốc, góc, số lần đạt, thời gian giữ, ngày đo, nguồn/dụng cụ đo và nhận xét/lý do. Vẫn phải đạt tiêu chí đã cấu hình. Đánh giá lâm sàng chỉ lưu kết quả đo; bác sĩ tiếp tục chọn checkbox và lưu xác nhận.

Ba trạng thái mốc: `NotAchieved`, `MeasuredAchieved`, `DoctorConfirmed`. `InsufficientEvidence` thuộc **đánh giá**, hiển thị riêng là “Chưa đủ bằng chứng”, không làm phát sinh trạng thái xác nhận thứ tư. Đo đạt không bao giờ tự tạo `RomConfirmations`.

## Checkbox, sửa mục tiêu và tính toàn vẹn

1. Chọn bệnh nhân, tạo mục tiêu hoặc chọn mốc hiện có.
2. Trong “Xác nhận ROM”, chọn dòng; lưu đo lâm sàng hoặc đánh giá một buổi tập.
3. Chỉ kết quả `MeasuredAchieved` của phiên bản mục tiêu hiện hành được xác nhận.
4. Tích checkbox; nhập nhận xét; nhấn Lưu; đăng nhập bác sĩ bằng tài khoản được quản trị viên cấp.
5. Transaction `Serializable` kiểm tra bệnh nhân, phiên bản mốc, AssessmentId và ConfirmationId dự kiến, quyền bác sĩ; ghi xác nhận + audit + trạng thái mốc cùng lúc.
6. Tải lại hoặc mở kết nối/ứng dụng mới: checkbox dựa trên xác nhận `IsCurrent=1` trong SQL, không dựa vào trạng thái UI.
7. Bỏ tích: lý do bắt buộc, xác thực lại bác sĩ; đặt `IsCurrent=0`, thêm audit `Revoke`; không xóa xác nhận trước.

Mỗi mốc chỉ có một xác nhận hiện hành nhờ filtered unique index. Composite FK bảo đảm assessment–mốc–bệnh nhân và session–bệnh nhân cùng hồ sơ. Hai cửa sổ thao tác cùng dữ liệu: một giao dịch thành công; giao dịch còn lại bị từ chối do dữ liệu thay đổi hoặc SQL deadlock và phải tải lại. Không tự retry một quyết định lâm sàng lên dữ liệu mới.

Không sửa mục tiêu hoặc thay đánh giá đang được công nhận khi còn xác nhận hiện hành; hủy có lý do trước. Khi sửa mục tiêu, tăng `Revision`, không tái sử dụng assessment cũ cho checkbox mới. Các assessment lưu snapshot tiêu chí, góc mục tiêu, vận động/quy ước và người đánh giá. Audit ghi giá trị mục tiêu trước/sau cùng lý do sửa. Các xác nhận và lịch sử cũ vẫn truy xuất được.

## Biểu đồ và báo cáo

- Workspace có biểu đồ PeakROM/AvgROM theo buổi với nhãn rõ dữ liệu chưa phân loại quy ước.
- Biểu đồ tiến triển từng mốc chỉ dùng assessment của phiên bản hiện hành, gồm đường mục tiêu, điểm đo đạt, điểm xác nhận hiện hành theo ngày xác nhận. Bảng xác nhận có bác sĩ, thời gian UTC, nguồn và ghi chú; bảng audit giữ cả những xác nhận đã hủy.
- `AnalysisTab(patientId, doctor)` giữ nguyên lịch sử, histogram và xuất PDF. Chỉ thêm đường mục tiêu lên trace khi có xác nhận dựa trên chính session đó và các mốc so sánh cùng vận động/quy ước; không đặt ngưỡng lâm sàng lên trace chưa phân loại.
- PDF mở từ workspace tải lại mốc từ SQL và thêm bảng mục tiêu, đo được, nguồn, ngày đo, session, trạng thái, bác sĩ/thời gian và ghi chú. Nội dung nhập được HTML-encode. PDF từ luồng bệnh nhân cũ ghi rõ chưa tải xác nhận bác sĩ; muốn có bảng xác nhận thì mở qua workspace. Ghi chú tự nhập khi xuất PDF được ghi rõ không phải xác nhận mốc.
- Loại bỏ các tuyên bố cố định “Target 115°”, “Full Extension” trong kết quả xuất. Không kết luận hồi phục hoàn toàn từ ROM.

## Cài đặt Visual Studio và SQL Server

1. Windows 10/11, Visual Studio 2022 có workload **.NET desktop development**, Developer Pack .NET Framework **4.8**, SQL Server 2016+ và WebView2 Evergreen Runtime. Để mở `.slnx`, dùng Visual Studio 2022 17.13+; cũng có thể mở trực tiếp `AngleMonitorWPF.csproj` trên bản cũ. Không cần đổi target ứng dụng.
2. Backup database hiện có. Nếu database mới: tạo `RehabDB` rồi chạy `create_tables.sql` một lần. Không chạy script tạo bảng gốc trên database đang có dữ liệu.
3. Trong SSMS chọn đúng database, chạy **`migrations/001_doctor_workspace.sql`**. Migration có `XACT_ABORT`, transaction, application lock và bảng `RehabSchemaMigrations`; chạy lại bản 1 là no-op. Schema xung đột/thiếu bảng gốc sẽ báo lỗi thay vì xóa dữ liệu. Phiên bản tối thiểu SQL 2016 áp dụng migration; script tạo bảng gốc có tùy chọn `OPTIMIZE_FOR_SEQUENTIAL_KEY` cần SQL Server 2019+ hoặc bỏ tùy chọn đó trên bản cũ.
4. Sửa `connectionStrings/RehabDB` trong `App.config`. Mặc định giữ `Server=.;Database=RehabDB;Integrated Security=True;`. Danh sách bệnh nhân, thêm bệnh nhân, phiên tập và workspace dùng chung cấu hình này. Ưu tiên Windows authentication; không commit mật khẩu SQL.
5. Cấp tài khoản bằng Windows PowerShell 5.1+ trên máy đã cài .NET Framework 4.8, với kết nối có quyền database owner:

   ```powershell
   .\tools\Provision-Doctor.ps1 `
       -ConnectionString 'Server=.;Database=RehabDB;Integrated Security=True;' `
       -Username 'bacsi.an' -FullName 'Nguyễn Văn An'
   ```

   Script kiểm tra `db_owner`/`sysadmin`, hỏi mật khẩu bằng `Read-Host -AsSecureString`, hash trong bộ nhớ và INSERT có tham số. Không có đăng ký bác sĩ từ màn hình bệnh nhân hay mật khẩu mặc định. Để ngừng quyền bác sĩ: quản trị viên đặt `DoctorAccounts.IsActive=0`. Không dùng tài khoản SQL owner cho triển khai tới máy bệnh nhân; giới hạn quyền truy cập DB theo quản trị của cơ sở.

6. Restore/build từ Developer PowerShell của Visual Studio:

   ```powershell
   nuget restore packages.config -PackagesDirectory packages
   msbuild AngleMonitorWPF.slnx /restore /p:Configuration=Debug
   ```

   Có thể build riêng ứng dụng bằng `msbuild AngleMonitorWPF.csproj /p:Configuration=Debug` sau restore. Project tests SDK-style dùng net48 mặc định trên Windows, cùng các file nghiệp vụ. Không thêm thư viện UI/chart mới.

7. Chạy ứng dụng. Trong danh sách bệnh nhân chọn “Bác sĩ / ROM” hoặc nút “Không gian bác sĩ”; trong màn hình tập có nút “KHÔNG GIAN BÁC SĨ / ROM”. Đăng nhập, chọn bệnh nhân và thao tác theo luồng checkbox ở trên.

## Kiểm thử và kết quả thực tế

Ngày thực hiện: 08/11/2026 UTC. Môi trường Debian 13, SDK 8.0.427 cho test harness Linux; SQL Server 2022 Developer trong Docker chỉ mở loopback. **Ứng dụng chính vẫn target net48.** Tests net8 chạy cùng các file `Rom/*.cs` được link trực tiếp, không dùng bản mô phỏng repository/SQL. Không có dữ liệu thật trong database kiểm thử. Database test được tạo tên ngẫu nhiên `RehabTrackTests_<guid>` và xóa trong `finally`.

Chạy kiểm thử nghiệp vụ trên Linux:

```sh
dotnet run --project tests/RehabTrack.Rom.Tests.csproj
```

Chạy trên Windows net48:

```powershell
dotnet build tests/RehabTrack.Rom.Tests.csproj -p:FrameworkTests=true
.\tests\bin\Debug\net48\RehabTrack.Rom.Tests.exe
```

Integration yêu cầu biến `REHABTRACK_TEST_ADMIN_CONNECTION_STRING` trỏ **master trên SQL Server kiểm thử**, có quyền CREATE/DROP database. Nhập giá trị bí mật qua cấu hình môi trường, không đưa vào mã nguồn hay log. Runner từ chối connection không trỏ master, không dùng connection string của ứng dụng. Chạy:

```sh
dotnet run --project tests/RehabTrack.Rom.Tests.csproj -- --integration
```

Hoặc Windows: thêm `--integration` sau đường dẫn `.exe`. Khi thiếu tham số integration, báo `SKIP SQL integration`; không coi đó là integration pass. Runner trả exit code 1 khi có lỗi.

Kết quả đã chạy: **31 kiểm thử nghiệp vụ + 30 kiểm thử SQL = 61 pass, 0 fail**. Kết quả từng ca nằm trong `docs/DOCTOR_WORKSPACE_TEST_RESULTS.txt`.

Phạm vi nghiệp vụ: tạo/cập nhật/giá trị không hợp lệ; chu kỳ/hold/reps/tolerance; gập vs hai quy ước duỗi; mẫu tức thời/thiếu dữ liệu/mất mẫu; đo lâm sàng; hash mật khẩu; check/uncheck; lý do hủy; trùng/sai bệnh nhân/quyền bệnh nhân; dữ liệu stale; HTML encoding báo cáo.

Phạm vi SQL: kết nối; chạy migration hai lần trên schema gốc; dữ liệu cũ giữ nguyên; xác thực; tạo/sửa mục tiêu; trạng thái thiếu bằng chứng vs đo đạt; session sai bệnh nhân; rollback cả batch; lưu checkbox/audit; tạo repository, đăng nhập và mở kết nối mới vẫn có checkbox; unique index; không sửa âm thầm mốc đã xác nhận; hủy có lý do; đo lâm sàng không session; composite FK; phiên bản stale; đọc lịch sử; vô hiệu hóa quyền; khóa đăng nhập. Bổ sung kiểm tra hai giao dịch xác nhận đồng thời, SensorData hợp lệ/chồng lấn và vô hiệu hóa assessment sau chỉnh mục tiêu.

Các kiểm tra bổ sung đã thực thi:

- Restore **toàn bộ 33 packages** theo `packages.config`, không đổi phiên bản.
- Build `tests/RehabTrack.Rom.Tests.csproj -p:FrameworkTests=true --no-restore`: thành công, **0 warning / 0 error**, chỉ là build test harness net48, không phải app WPF chạy được.
- `python tools/validate_wpf_sources.py`: parse XML, kiểm tra 8 WPF Page + App, inclusion project, StaticResource và event handler.
- Cùng script với `--sdk ... --framework ...`: biên dịch toàn bộ C# ứng dụng theo C# 7.3 bằng reference assemblies net48 và các DLL thực tế, dùng khai báo field sinh tạm từ XAML. Thành công. **Không thay thế build BAML, không chứng minh binding/render/event hoạt động khi chạy.** Không đưa các partial tạm này vào repository.
- Đã thử `dotnet build AngleMonitorWPF.slnx --no-restore`: thất bại `MSB4068` vì MSBuild 17.11/SDK8 không hỗ trợ `.slnx`.
- Đã thử `dotnet build AngleMonitorWPF.csproj --no-restore`: thất bại `MSB3644` do Linux không có Developer Pack hệ thống. Khi cung cấp reference assemblies, build đi tới C# nhưng không sinh `InitializeComponent`, entry point và các field XAML vì không có pipeline Windows WPF/BAML. Đây **không phải Build PASS**.

## Việc cần nghiệm thu trên Windows

- Build BAML/full solution bằng Visual Studio và chạy app thật; mở từ các nút điều hướng, kiểm tra binding, kích thước/scroll, chọn dòng/tích checkbox bằng chuột và bàn phím.
- Dùng SQL Server triển khai: nhập lâm sàng → tích → lưu → tắt tiến trình WPF → mở lại → kiểm tra đúng bệnh nhân, bác sĩ, thời gian/ghi chú → hủy có lý do → mở lịch sử. Integration đã kiểm tra kết nối/repository mới; chưa thao tác tắt/mở tiến trình WPF thực tế.
- Xuất/gộp PDF qua WebView2, kiểm tra phân trang bảng dài và dấu tiếng Việt; kiểm tra đường mục tiêu chỉ trên session có bằng chứng phù hợp. Bộ test mới kiểm tra HTML encoding, chưa render file PDF.
- Kết nối thiết bị thật, xác minh quy ước góc, hiệu chuẩn, tần số mẫu và khả năng giữ. Không có firmware/thiết bị trong môi trường này. Không coi dữ liệu cũ mặc định là đã xác minh. Kiểm tra hồi quy Bluetooth, game, timer, reps và lưu session thực tế.
- `Provision-Doctor.ps1` chưa được chạy bằng Windows PowerShell trong môi trường Linux. Thuật toán hash tương ứng đã được kiểm thử bằng C#.

Đây là cơ chế phân quyền tối thiểu ở ứng dụng desktop với SQL tin cậy. Chưa triển khai SSO/MFA, dịch vụ backend hay sổ audit chống sửa bởi quản trị SQL. Tài khoản có quyền sửa trực tiếp database vẫn cần được quản trị theo chính sách triển khai của cơ sở.

## Danh sách file

Mới:

- `DoctorWorkspaceWindow.xaml`, `DoctorWorkspaceWindow.xaml.cs`: dashboard sáu mục điều hướng, bệnh nhân, mục tiêu, đo, checkbox, biểu đồ và lịch sử.
- `DoctorLoginWindow.xaml`, `DoctorLoginWindow.xaml.cs`: xác thực bác sĩ.
- `Rom/RomModels.cs`, `Rom/RomMilestoneService.cs`: model và quy tắc nghiệp vụ.
- `Rom/DoctorRepository.cs`, `Rom/DoctorWorkspaceRepository.cs`: xác thực, SQL và transaction.
- `Rom/PasswordHasher.cs`, `Rom/RomReportBuilder.cs`: hash và HTML bảng ROM.
- `migrations/001_doctor_workspace.sql`: schema bổ sung và sửa tương thích CCCD.
- `tools/Provision-Doctor.ps1`, `tools/validate_wpf_sources.py`: cấp tài khoản và kiểm tra nguồn.
- `tests/RehabTrack.Rom.Tests.csproj`, `tests/Program.cs`: test nghiệp vụ và SQL.
- `docs/DOCTOR_WORKSPACE_IMPLEMENTATION.md`, `docs/DOCTOR_WORKSPACE_TEST_RESULTS.txt`: tài liệu và kết quả chạy.

Sửa:

- `AngleMonitorWPF.csproj`, `AngleMonitorWPF.slnx`: include XAML/C#, reference và project test.
- `App.config`, `DatabaseHelper.cs`: connection string cấu hình dùng chung.
- `PatientSelectionWindow.xaml`, `.xaml.cs`: điều hướng workspace; sửa nhánh card chọn bệnh nhân chưa đặt ID; giữ tìm kiếm/thêm/xem/tập.
- `AddPatientWindow.xaml.cs`: dùng kết nối chung.
- `MainWindow.xaml`, `.xaml.cs`: chỉ thêm nút/handler mở workspace, giữ xử lý cảm biến.
- `AnalysisTab.xaml`, `.xaml.cs`: nhận ID bệnh nhân, đọc session đúng hồ sơ, so sánh mốc, PDF, xử lý dữ liệu null và HTML encoding.
- `README.md`: sửa framework/DB và nối hướng dẫn chạy.

Giữ nguyên `App.xaml`, `App.xaml.cs`, `AuthWindow`, `RehabSession`, `DataOptimizer`, `DeviceSettings`, `create_tables.sql`, `packages.config` và mã game/giao thức cảm biến.
