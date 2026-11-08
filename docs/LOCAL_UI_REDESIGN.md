# Giao diện theo ảnh tham chiếu — bản sửa cục bộ

Giao diện được tích hợp vào nhánh phát triển rồi đưa vào `master` theo yêu cầu người dùng. Kết quả kiểm thử của bản SQL Server mới nhất nằm trong `DOCTOR_WORKSPACE_SQL_SERVER_UPDATE.md`.

- `DoctorWorkspaceWindow.xaml`: header trắng, logo xanh ngọc, tài khoản bác sĩ, sidebar có trạng thái chọn; thẻ bệnh nhân; bốn ô ROM/mục tiêu/buổi gần nhất/số mốc xác nhận; biểu đồ và bảng checkbox song song; vùng nhận xét và hai nút lưu/xuất báo cáo phía dưới. Ẩn dải tab cũ và giữ nội dung các module chi tiết qua sidebar/nút điều hướng.
- `DoctorWorkspaceWindow.xaml.cs`: kết nối dashboard với dữ liệu SQL đang có. Bảng tổng quan và bảng chi tiết dùng chung các đối tượng mốc; luồng lưu, xác thực lại, kiểm tra bằng chứng và audit không đổi. Avatar lấy chữ đầu tên; tên bác sĩ lấy từ phiên đăng nhập. Không cố định bệnh nhân, tên bác sĩ, khớp gối, tình trạng điều trị hoặc chỉ số của ảnh mẫu vào hồ sơ.
- Chọn một dòng mốc cập nhật ROM, mục tiêu và biểu đồ của đúng mốc/phiên bản/quy ước. Không trộn góc gập với thiếu duỗi hoặc suy ra mốc đạt từ PeakROM cũ. Chưa có đánh giá thì hiển thị trống; không tạo dữ liệu giả trong ứng dụng.
- `tools/Preview-Workspace.ps1`: cập nhật helper xem trước với các control mới; chỉ dùng dữ liệu minh họa khi chạy helper riêng trên Windows. Chưa chạy helper mới trong lượt này.

Các chức năng thêm/sửa mục tiêu, nhập đo lâm sàng, đánh giá chuỗi mẫu và xem lịch sử vẫn ở các màn hình chi tiết. Nút Xuất báo cáo mở AnalysisTab với đúng bệnh nhân như trước. Đổi mục tiêu không làm mất lý do/checkbox đang sửa. Màu badge phản ánh kết quả đã lưu; checkbox chưa lưu không tự đổi thành trạng thái bác sĩ đã công nhận.

## Kiểm tra thực hiện

- Parse XML/XAML, kiểm tra đường dẫn project, event handler, static resource: thành công.
- Biên dịch tham chiếu toàn bộ C# với DLL thực tế, net48/C# 7.3 và field XAML sinh tạm: thành công. Đây không phải build BAML.
- Bản SQL Server cập nhật sau đó đã chạy 34 kiểm thử nghiệp vụ: 34 pass, 0 fail. SQL integration SKIP do container hết dung lượng; xem `DOCTOR_WORKSPACE_SQL_SERVER_UPDATE.md`.
- `git diff --check`: thành công.
- Chưa chạy giao diện mới, helper ảnh hoặc build BAML trên Windows. Kết quả Windows ở các commit trước chỉ áp dụng giao diện cũ.

## Kiểm tra tiếp trên Windows

Mở project/solution với .NET Framework 4.8, restore package rồi build. Kiểm tra dashboard ở các độ phân giải, điều hướng sidebar, chọn mốc đổi đồ thị, ghi chú, lưu/hủy checkbox và mở PDF. Các góc trong ảnh tham chiếu chỉ minh họa; mục tiêu thực tế phải đọc từ hồ sơ được bác sĩ cấu hình.
