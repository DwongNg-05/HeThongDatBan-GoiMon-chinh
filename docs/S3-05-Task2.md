# S3-05 Task 2 — Báo cáo nhật ký huỷ món theo ca

Triển khai trong HeThongDatBan-GoiMon-chinh.

Quy tắc PO đã chốt: ca của đơn là DiningSessions.ShiftId; chỉ huỷ thành công được báo cáo, không ghi yêu cầu bị từ chối/thất bại. Chọn được ca mở và ca đóng, mặc định ca có thời điểm mở gần nhất.

Quản lý vào menu **Báo cáo cuối ca** hoặc /ShiftReports. Chọn ca, xem đơn/lần gọi, phiên/bàn, mã dòng món, tên món, số lượng, giá trị, người thực hiện, giờ Việt Nam (UTC+7), lý do/ghi chú, cách tính tiền và số tiền tính. Huỷ trước chế biến không tính tiền; các lần quản lý huỷ sau chế biến có sẵn vẫn tính tiền. Chưa cung cấp thao tác quản lý huỷ sau chế biến trên giao diện.

Controller yêu cầu Manager; thủ tục SQL kiểm tra lại tài khoản còn hoạt động và đúng vai trò Manager. Phục vụ/Bếp/Thu ngân không thấy menu và bị từ chối khi gõ URL màn hình hoặc API trực tiếp. Mỗi sự kiện thành công chỉ xuất hiện một lần; retry Task 1 không tạo thêm nhật ký.

Thông báo riêng khi chưa có ca hoặc ca không có huỷ. Lỗi tải báo cáo có thao tác Tải lại và giữ ca đang chọn; phản hồi cũ không ghi đè lựa chọn mới.

Migration: 045_S305ShiftCancellationReport.sql.

Kiểm thử:
- Build solution, chạy DbTool verify-shift-cancellations với RM_CONNECTION_STRING. Tạo database thử riêng và tự xoá; kiểm tra Task 1 và Task 2, đủ trường, đúng ca, ngày huỷ khác ngày ca, ca mở/đóng, mặc định, huỷ tính tiền/không tính tiền, không trùng và quyền SQL/HTTP.
- node --test tools/tests/shift-cancellation-report.test.cjs tools/tests/pending-order-cancellation.test.cjs.
- AreaTests kiểm tra hồi quy giao diện điều hướng và phân quyền.

Demo phát triển: DbTool seed-shift-cancellation-demo tạo hai ca đã đóng: DEMO S3-05 - Nhật ký huỷ món có 3 dòng không tính tiền (200.000 đồng) và 1 dòng vẫn tính tiền (60.000 đồng), DEMO S3-05 - Ca không có huỷ kiểm tra rỗng. Seed dùng thủ tục huỷ thật, chạy lặp không tạo thêm ca/nhật ký, không đổi trạng thái bàn hoặc đơn đang phục vụ. Chỉ chạy trong database phát triển/kiểm thử.
