# Doctor Workspace cho repository SQL Server

Ngày cập nhật cục bộ: 08/11/2026. Repository đích: `khanhleviet2006/rehab-track-system`, nhánh làm việc `feature/doctor-workspace-rom`. Bản này chưa commit hoặc push lên GitHub.

## Kiến trúc và tương thích

Ứng dụng gốc là WPF .NET Framework 4.8, code-behind, `System.Data.SqlClient` và database SQL Server `RehabDB`. `App.xaml` mở `PatientSelectionWindow`; `MainWindow` giữ Bluetooth, timer, game, đếm reps và lưu `TrainingSessions`; `AnalysisTab` giữ lịch sử và xuất PDF. `Users` là bệnh nhân và `GlobalData.CurrentUserId` là ID bệnh nhân. `DoctorWorkspaceWindow` dùng `patientId` và `DoctorSession.DoctorId` riêng. Không chuyển sang PostgreSQL, không đổi định dạng cảm biến hay framework.

Nền tảng Doctor Workspace đã có trong nhánh làm việc: cấu hình mốc theo bệnh nhân, hai loại gập/duỗi với quy ước góc riêng, đánh giá chuỗi mẫu hoặc khám lâm sàng, checkbox lưu vào `RomConfirmations`, lịch sử trong `RomConfirmationAudit`. Bộ quy tắc không suy kết quả lâm sàng từ một `PeakROM` hay mẫu góc tức thời. Mốc chỉ thành `MeasuredAchieved` khi có đủ bằng chứng; bác sĩ còn phải xác nhận riêng. Cơ chế SQL parameterized, transaction và FK được giữ lại. Migration `migrations/001_doctor_workspace.sql` bổ sung bảng và cột `Users.CCCD`, không xóa dữ liệu cũ.

## Giao diện và điều hướng

`DoctorWorkspaceWindow.xaml` dùng `Segoe UI`, thẻ trắng, nền xanh nhạt, nút xanh `#1D9E75`, đường viền xanh `#3B82F6`, cùng bảng màu với `PatientSelectionWindow` và `MainWindow`. Dashboard giữ mô hình theo ảnh tham chiếu nhưng tên bệnh nhân, ROM, mốc và lịch sử đọc từ SQL Server. Sidebar mở các phần Tổng quan, Bệnh nhân, Theo dõi ROM, Xác nhận mốc, Báo cáo, Cài đặt. Nút thêm bệnh nhân mở `AddPatientWindow`; xuất báo cáo mở `AnalysisTab` với đúng patientId và phiên bác sĩ.

`AuthWindow` được thay XAML thiếu resource/binding bằng form Segoe UI và nút dùng màu gốc: nhập MSBN để vào buổi tập, mở danh sách bệnh nhân hoặc thêm hồ sơ. Luồng chính vẫn bắt đầu ở `PatientSelectionWindow`. Nút “Không gian bác sĩ” ở danh sách, nút “Bác sĩ / ROM” trên dòng bệnh nhân và nút ở `MainWindow` đều mở cùng Doctor Workspace. Nút hồ sơ trên dòng truyền ID bệnh nhân, còn nút chung cho bác sĩ chọn trong workspace.

## Chế độ không mật khẩu đã yêu cầu

`App.config` mặc định `DoctorWorkspaceDemoMode=true`, theo lựa chọn “1” của người dùng. Mở workspace sẽ tạo hoặc dùng một tài khoản SQL Server dành riêng `__rehabtrack_demo__` với tên **Bác sĩ demo**, mật khẩu ngẫu nhiên chỉ dùng để tạo hash rồi bỏ. Tài khoản này không đăng nhập được bằng form mật khẩu. Chế độ demo cần migration và kết nối SQL Server hoạt động; tài khoản SQL kết nối cần quyền đọc/ghi các bảng Doctor Workspace, gồm quyền tạo tài khoản demo lần đầu. Tài khoản demo bị vô hiệu hóa hoặc bị sửa khác dự kiến sẽ bị từ chối, không tự kích hoạt lại.

Màn hình hiện banner DEMO, tên “Bác sĩ demo”; ghi chú đánh giá, xác nhận và audit có tiền tố `[DEMO]`. Checkbox đã xác nhận demo hiện “Xác nhận demo”; báo cáo PDF có cảnh báo demo nếu có mốc xác nhận demo. Tài khoản bệnh nhân không được biến thành ID bác sĩ. Tuy nhiên, khi demo bật, người có thể mở ứng dụng có thể thao tác bằng phiên demo; dữ liệu demo chỉ dùng thử nghiệm. Để dùng bác sĩ thật, đổi `DoctorWorkspaceDemoMode` sang `false`, khởi động lại ứng dụng và cấp tài khoản thật bằng `tools/Provision-Doctor.ps1`. Dữ liệu demo cũ vẫn có nhãn demo; không tự chuyển thành xác nhận của bác sĩ thật.

## Chạy trên Windows và SQL Server

1. Mở `AngleMonitorWPF.csproj` bằng Visual Studio 2022 với workload .NET desktop development và Developer Pack .NET Framework 4.8. Visual Studio 17.13+ có thể mở `AngleMonitorWPF.slnx`.
2. Restore `packages.config` bằng NuGet/Visual Studio. Không cần thêm thư viện UI hoặc chuyển project SDK-style.
3. Cấu hình `connectionStrings/RehabDB` trong `App.config`. Database mới: tạo `RehabDB`, chạy `create_tables.sql`, rồi `migrations/001_doctor_workspace.sql` trong SSMS. Database cũ: sao lưu, chỉ chạy migration. Migration bản 1 có transaction và có thể chạy lại.
4. Build Debug, chạy app. Mở workspace từ danh sách bệnh nhân hoặc màn hình tập. Chọn bệnh nhân, tạo mốc, lưu đánh giá hợp lệ hoặc nhập kết quả khám, tích checkbox, ghi nhận xét và nhấn Lưu. Đóng/mở lại để kiểm tra trạng thái; bỏ tích phải ghi lý do. Vào phần Báo cáo để xem lịch sử và PDF.
5. Nếu vận hành với bác sĩ thật, đặt demo `false` trước khi triển khai và tạo tài khoản theo tài liệu chính. Kiểm tra quyền SQL cho môi trường của cơ sở.

## Kiểm thử bản cập nhật này

- Đã chạy 34 kiểm thử nghiệp vụ net8 trên chính `Rom/*.cs`: 34 pass, 0 fail. Các ca mới bao gồm cổng bật/tắt demo, nhãn và báo cáo demo. SQL integration được **SKIP**, không tính vào 34 ca.
- Đã parse XAML, kiểm tra 8 Page trong `.csproj`, event handler/resource, và biên dịch tham chiếu toàn bộ C# theo net48/C# 7.3 với các DLL thực tế: đạt. Công cụ tạo field XAML tạm cho phép kiểm tra C#, **không sinh BAML hoặc chạy WPF thật**.
- Đã thử tải SQL Server 2022 container để chạy integration; Docker hết dung lượng khi giải nén image (`no space left on device`). Các ca SQL demo mới đã được viết nhưng **chưa thực thi**. Kết quả 30 ca SQL ở tài liệu chính thuộc mã trước đợt sửa này.
- Chưa build BAML/giao diện trên Windows, chưa nhấn nút thực tế, chưa render PDF/WebView2, chưa kết nối thiết bị. CI Windows của các commit cũ không kiểm chứng giao diện và demo mới.

Trên máy có SQL Server kiểm thử riêng, đặt `REHABTRACK_TEST_ADMIN_CONNECTION_STRING` trỏ database **master** với quyền tạo/xóa database rồi chạy `dotnet run --project tests/RehabTrack.Rom.Tests.csproj -- --integration`. Runner tạo database tên ngẫu nhiên, chạy migration hai lần, kiểm tra dữ liệu cũ, lưu/tải/hủy checkbox và các ca demo, sau đó xóa database thử nghiệm. Không dùng database bệnh nhân thật.

File sửa chính: `App.config`, `AuthWindow.xaml/.cs`, `DoctorWorkspaceWindow.xaml/.cs`, `PatientSelectionWindow.xaml`, `MainWindow.xaml`, `Rom/DoctorRepository.cs`, `Rom/DoctorWorkspaceRepository.cs`, `Rom/RomModels.cs`, `Rom/RomReportBuilder.cs`, `tests/Program.cs`, `README.md`, tài liệu này và tài liệu triển khai chính. `tools/Preview-Workspace.ps1` cùng hai tài liệu giao diện đã có thay đổi cục bộ từ lượt trước và được giữ lại.
