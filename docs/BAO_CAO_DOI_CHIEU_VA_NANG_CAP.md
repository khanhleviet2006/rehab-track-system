# Báo cáo đối chiếu ZIP và nâng cấp RehabTrack

Ngày cập nhật: 08/11/2026 (Asia/Bangkok).

## Đầu vào và kết quả đối chiếu

- File người dùng gửi: `rehab-track-system-master (2).zip` (92.040 byte).
- SHA-256: `fef84d220b22a098c45d5e33fef58fcd56a611127fe400a385177ebd1b971bf0`.
- ZIP có 39 file, nội dung trùng hoàn toàn với `master` tại commit `ddaa49f8a47f915bcecc1ae6575bac6ea27e6b16`. Không có thay đổi nguồn riêng trong ZIP cần ghi đè lên nhánh tính năng. ZIP chưa chứa Doctor Workspace.
- Yêu cầu triển khai lấy từ các tin nhắn của người dùng trong hội thoại. README trong ZIP được dùng làm tài liệu dự án để đối chiếu, không coi là yêu cầu thay thế chỉ dẫn của người dùng.
- Mã nâng cấp được giữ trên `feature/doctor-workspace-rom`, PR #1 vào `master`; không push hoặc merge trực tiếp vào master.

## Đối chiếu yêu cầu

| Yêu cầu | Phần triển khai | Bằng chứng / giới hạn |
|---|---|---|
| Tích hợp WPF hiện có, tiếng Việt | DoctorWorkspaceWindow, DoctorLoginWindow, các nút điều hướng bệnh nhân/MainWindow | Build WPF/BAML Windows đã thành công; thao tác giao diện vẫn cần nghiệm thu |
| Quản lý bệnh nhân | Chọn/tải bệnh nhân, tái sử dụng form thêm, giữ tìm kiếm/tập luyện | ID bác sĩ riêng với ID bệnh nhân; tải SQL mới chạy ngoài UI thread |
| Thiết lập mục tiêu theo bệnh nhân | PatientRomMilestones, thêm/sửa tiêu chí, revision và snapshot | Kiểm thử tạo/sửa/stale trên SQL đã chạy |
| Phân biệt gập, duỗi và quy ước góc | FlexionDegrees, ExtensionDeficit, IncludedJointAngle; Unknown không tự đạt | Unit tests đã chạy; firmware/hiệu chuẩn chưa được xác minh |
| Đánh giá có bằng chứng | Chu kỳ, reps, giữ, mất mẫu, chất lượng; JSON hoặc SensorData không chồng lấn | Kiểm thử trạng thái đo đạt/chưa đạt/thiếu bằng chứng đã chạy |
| Nhập khám lâm sàng | Góc, ngày đo, số lần, giữ, nguồn đo và lý do | SQL đã kiểm tra không cần session cảm biến |
| Checkbox bác sĩ | Xác thực lại khi lưu, transaction, một xác nhận hiện hành mỗi mốc | SQL đã kiểm tra lưu/tải qua kết nối mới, đồng thời, sai bệnh nhân, quyền tài khoản |
| Hủy và lịch sử | IsCurrent=0, lý do bắt buộc, audit không xóa bản ghi trước | Integration tests đã chạy |
| Migration an toàn | Migration có version, transaction, khóa, FK/index; sửa CCCD nullable | Chạy lặp trên schema gốc và giữ dữ liệu cũ đã kiểm tra |
| Biểu đồ và PDF | LiveCharts, lịch sử đánh giá/xác nhận, bảng ROM trong PDF | C# đã kiểm tra; PDF thực tế chưa render bằng WebView2 trên Windows |
| Giữ chức năng thiết bị | Không đổi giao thức serial, timer, queue và thuật toán reps cũ | Chưa có thiết bị thật để kiểm tra hồi quy Bluetooth/game |

## Sửa bổ sung sau khi nhận ZIP

- Chặn đổi phiên tập trong khi xuất PDF; chặn xuất khi chuỗi biểu đồ đang tải và chặn xuất lặp song song. Mục đích là giữ biểu đồ và thông tin phiên trong cùng một báo cáo.
- Thay vòng chờ tải HTML vô hạn bằng giới hạn 30 giây, kiểm tra WebView2 báo thành công rồi mới in PDF và luôn gỡ event handler. Không coi trang tải lỗi là báo cáo hợp lệ.
- Thêm `.github/workflows/windows-validation.yml`: runner Windows 2022 restore package gốc, build WPF/BAML Release, build và chạy kiểm thử net48, lưu artifact build. Workflow không triển khai phần mềm hoặc truy cập database bệnh nhân.

## Kiểm thử và kết quả

- Bản tính năng trước đó đã thực thi **31 kiểm thử nghiệp vụ + 30 kiểm thử SQL Server 2022**, 61 pass / 0 fail; log nằm tại `DOCTOR_WORKSPACE_TEST_RESULTS.txt`.
- Sau sửa báo cáo: chạy lại 31 kiểm thử nghiệp vụ, 31 pass / 0 fail. Lượt này SQL integration báo SKIP, không gộp thành kết quả SQL mới.
- Kiểm tra XML/XAML, đường dẫn project, event handler, static resource và biên dịch tham chiếu C# với .NET Framework 4.8 thành công. Đây không phải build BAML.
- Build test harness net48 thành công. Không chạy file exe net48 trên Linux.
- **Đã thực thi build WPF/BAML Release trên Windows 2022 thành công** tại commit `031c3104fd124d8928c849d32479c2e12fd629a9`. Build test harness net48 thành công (0 warning, 0 error); chạy .NET Framework thực tế: **31 pass, 0 fail**, SQL integration SKIP. [Workflow thành công](https://github.com/khanhleviet2006/rehab-track-system/actions/runs/42459637990).
- Artifact: [RehabTrack-Windows-net48](https://github.com/khanhleviet2006/rehab-track-system/actions/runs/42459637990/artifacts/13609102869), khoảng 32 MB, lưu 7 ngày. Build app có cảnh báo từ các biến chưa dùng và using lặp trong MainWindow gốc; không có lỗi build. Artifact chưa chứng minh phần mềm chạy đúng với thiết bị thật.
- Chưa nghiệm thu UI WPF, tắt/mở tiến trình ứng dụng thật, render/gộp PDF, PowerShell cấp tài khoản và Bluetooth trên máy Windows/thiết bị thật.

## Mở và chạy trên Windows

1. Checkout nhánh `feature/doctor-workspace-rom` từ PR #1.
2. Visual Studio 2022 17.13+ có workload .NET desktop development, Developer Pack .NET Framework 4.8, WebView2 Runtime. Mở `AngleMonitorWPF.slnx` hoặc trực tiếp `.csproj`.
3. Backup SQL Server. Database mới chạy `create_tables.sql` một lần; database có sẵn giữ các bảng cũ. Chạy `migrations/001_doctor_workspace.sql` rồi cấu hình connection string trong `App.config`.
4. Restore `nuget restore packages.config -PackagesDirectory packages`, build ứng dụng.
5. Quản trị SQL cấp tài khoản bằng `tools/Provision-Doctor.ps1`; không có mật khẩu bác sĩ mặc định và không dùng mã bệnh nhân để xác nhận.
6. Mở Không gian bác sĩ, tạo mục tiêu, nhập/đánh giá đo, tích checkbox và lưu bằng tài khoản bác sĩ. Kiểm tra mở lại và hủy có lý do trước khi nghiệm thu.

Chi tiết kiến trúc, luồng dữ liệu, danh sách file và các giới hạn: [DOCTOR_WORKSPACE_IMPLEMENTATION.md](DOCTOR_WORKSPACE_IMPLEMENTATION.md).
