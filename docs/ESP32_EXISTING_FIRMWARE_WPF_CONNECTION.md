# Kết nối WPF với firmware ESP32_MPU6500 hiện có

Không sửa, không thay thế và không cần nạp lại mã ESP32 hiện tại của người dùng.

## Giao thức đã có

Firmware dùng `BluetoothSerial.h` (Bluetooth Classic SPP), quảng bá tên `ESP32_MPU6500`, đọc MPU qua SDA 21 / SCL 22, gửi giá trị góc qua `SerialBT.println(angle, 2)` (mỗi dòng một số, dấu thập phân `.`), tốc độ USB debug 115200. Nó không phải BLE/GATT. Trong firmware hiện có, `SerialBT` chỉ gửi góc nếu `hasClient()` là true.

## Sửa WPF

- `MainWindow.xaml.cs`: dò các COM, ưu tiên cổng được Windows nhận dạng Bluetooth và chờ tối đa 3,5 giây/cổng; mỗi lần `ReadLine` timeout chỉ chờ tiếp, không bỏ ngay cổng; xác nhận 2 dòng số hợp lệ trong khoảng -180..180. Sau dò, chờ 350ms trước khi mở cổng dùng thực tế.
- `AngleMonitorWPF.csproj`: tham chiếu `System.Management` (có sẵn trong .NET Framework 4.8) để đọc thông tin thiết bị COM.
- Khi không dò được, hiển thị danh sách COM phát hiện thay vì chỉ báo "không tìm thấy mạch".
- Không sửa XAML, thống kê ROM, database hoặc không gian bác sĩ.

## Cách chạy

1. ESP32 đã nạp firmware `ESP32_MPU6500`, bật nguồn.
2. Windows Bluetooth & devices: Pair `ESP32_MPU6500`. Kiểm tra cổng Outgoing SPP (More Bluetooth settings → COM Ports hoặc Device Manager → Ports).
3. Đóng mọi terminal đang giữ cổng SPP. Trong WPF nhấn `Kết nối Bluetooth`.
4. Nếu lỗi, kiểm tra log COM bằng PowerShell: `[System.IO.Ports.SerialPort]::GetPortNames()`.

## Giới hạn

Chưa build/chạy kiểm thử trực tiếp trên Windows và ESP32. Trên một số cấu hình Windows có thể cần chọn COM thủ công vì Windows cung cấp cả Incoming và Outgoing. Dữ liệu góc là góc tương đối theo cảm biến hiện có, không mặc định là góc khuỷu lâm sàng.
