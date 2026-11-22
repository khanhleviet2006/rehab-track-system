# ESP32 thường + MPU6050 qua Bluetooth Classic SPP

## Phần cứng
Chỉ sử dụng với **ESP32 có Bluetooth Classic** (ví dụ ESP32-WROOM-32/ESP32 DevKit V1), không phải ESP32-C3 hoặc ESP32-S3. Kết nối MPU6050: VCC → 3V3, GND → GND, SDA → GPIO21, SCL → GPIO22.

## Nạp firmware
Mở `firmware/ESP32_ROM_SPP_MPU6050/ESP32_ROM_SPP_MPU6050.ino` bằng Arduino IDE. Chọn **ESP32 Dev Module**, nạp firmware, mở Serial Monitor 115200. Đặt cảm biến bất động ở tư thế gốc khi khởi động; firmware coi tư thế đó là 0°. Cần nhận được `#SENSOR_READY` và các dòng góc dạng `12.3`. Có thể gửi ký tự `Z` qua ứng dụng terminal Bluetooth để đặt lại zero tại góc hiện tại.

**Lưu ý quan trọng:** Firmware xác thực thanh ghi WHO_AM_I phải là 0x68 trước khi truyền dữ liệu; nếu log là 0x98 hoặc giá trị khác, kiểm tra loại IC và dây kết nối, không bỏ qua kiểm tra nhận dạng chỉ để hiện góc. Một MPU6050 không đủ đo góc khớp khuỷu thực nếu cả cánh tay trên và dưới cùng chuyển động.

## Ghép đôi với Windows
1. Xóa ghép đôi BLE cũ (nếu có); ghép đôi thiết bị `ESP32_ROM_SPP` trong Settings → Bluetooth & devices.
2. Mở Device Manager → Ports (COM & LPT), tìm `Standard Serial over Bluetooth link (COMx)`. Windows có thể tạo cổng Incoming và Outgoing; ứng dụng sẽ dò dữ liệu.
3. Đóng Bluetooth serial terminal/Arduino Serial Monitor đang chiếm cổng COM trước khi mở RehabTrack.
4. Mở ứng dụng WPF, bấm **Kết nối Bluetooth**.

## Cách sửa trong WPF
`MainWindow.xaml.cs` đã được sửa trong nhánh này:
- Tự động dò COM với thời gian chờ đủ để thiết bị Bluetooth SPP khởi động.
- Chấp nhận marker `#ESP32_ROM_SPP`, đồng thời tương thích firmware cũ truyền số góc mỗi dòng.
- Chờ Windows giải phóng cổng sau khi dò, rồi mở lại với ReadTimeout.

Không thay đổi database, báo cáo, biểu đồ, không gian bác sĩ hoặc tệp XAML.

## Kiểm tra & giới hạn
Chưa có kiểm thử thực tế trên máy Windows và phần cứng. Cần build bằng Visual Studio và kiểm tra COM, chuyển động cảm biến, độ ổn định khi tắt/mở ESP32. Nếu PC không tạo cổng Bluetooth SPP, không thể giải quyết chỉ bằng sửa hàm `SerialPort`. Nếu bạn đang dùng mã BLE/NimBLE trên ESP32, phải nạp firmware Classic SPP này; bản WPF hiện tại không có client BLE/GATT.
