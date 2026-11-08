# RehabTrack - Angle Monitor Dashboard 🦾

Ứng dụng Desktop giám sát và trực quan hóa dữ liệu phục hồi chức năng khớp khuỷu tay theo thời gian thực. 

## 🚀 Công nghệ sử dụng
* **Nền tảng:** C# WPF (.NET Framework 4.8, theo `AngleMonitorWPF.csproj`)
* **Trực quan hóa:** Tích hợp WebView2 xử lý biểu đồ thời gian thực.
* **Cơ sở dữ liệu:** SQL Server quản lý thông tin bệnh nhân và lịch sử tập luyện.

Doctor Workspace trên SQL Server: xem [hướng dẫn triển khai, migration, chế độ demo và kiểm thử](docs/DOCTOR_WORKSPACE_IMPLEMENTATION.md).

## ⚙️ Hướng dẫn cài đặt
1. Clone repository này về máy tính.
2. Mở project bằng Visual Studio.
3. Visual Studio sẽ tự động **Restore NuGet Packages** để tải các thư viện cần thiết (WebView2, SQL client,...).
4. Nhấn **Start** để chạy ứng dụng.

# 🦾 Hệ Thống Giám Sát Tập Phục Hồi Chức Năng (AngleMonitorWPF)

Ứng dụng desktop viết bằng C# WPF dùng để theo dõi, ghi nhận dữ liệu góc độ và lực kéo trong quá trình tập luyện phục hồi chức năng khớp khuỷu của bệnh nhân. Hệ thống kết nối cơ sở dữ liệu **SQL Server** để quản lý thông tin bệnh nhân, phiên tập luyện và dữ liệu cảm biến thời gian thực.

---

## 🛠️ Hướng Dẫn Cấu Hình Cơ Sở Dữ Liệu

Để chạy được dự án trên máy tính của bạn, vui lòng thực hiện thiết lập Database theo 2 bước đơn giản dưới đây:

### Bước 1: Khởi tạo cấu trúc các bảng (Database Setup)
Hệ thống sử dụng cơ sở dữ liệu tên là **`RehabDB`** gồm 3 bảng chính: `Users` (Thông tin bệnh nhân), `TrainingSessions` (Phiên tập luyện), và `SensorData` (Dữ liệu cảm biến góc/lực).

1. Mở **SQL Server Management Studio (SSMS)** trên máy của bạn.
2. Tạo một Database trống mới tên là **`RehabDB`** bằng câu lệnh:
   ```sql
   CREATE DATABASE RehabDB;
   ```
3. Chọn database `RehabDB`, chạy `create_tables.sql` **chỉ với database mới**. Với database đang có dữ liệu, giữ nguyên các bảng hiện có.
4. Chạy `migrations/001_doctor_workspace.sql` trên database đó; migration kiểm tra phiên bản và có thể chạy lại.
5. Sửa connection string `RehabDB` trong `App.config`. Mặc định `DoctorWorkspaceDemoMode=true` mở chế độ demo không mật khẩu, có nhãn DEMO trên xác nhận. Khi dùng bác sĩ thật, đặt `false`, cấp tài khoản bằng `tools/Provision-Doctor.ps1` rồi mở ứng dụng.
