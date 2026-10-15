-- S3-09 Task 3: cảnh báo khi tạo đặt bàn cho số điện thoại có ít nhất 3 lần khách không tới trong 90 ngày.
--
-- Quy tắc (đề xuất, cần PO chốt — docs/S3-09-Task3.md):
--  * Nguồn: dbo.ReservationNoShowHistory (migration 049) — mỗi lượt đặt tối đa một dòng, số điện thoại đã chuẩn hoá
--    (10 chữ số, đầu 0), không mất khi lượt đặt bị xoá.
--  * Cửa sổ 90 ngày TRƯỢT, tính ngược từ đúng thời điểm kiểm tra: NoShowAt trong [@NowUtc − 90 ngày, @NowUtc].
--    Mốc biên được TÍNH: lần không tới đúng 90 ngày × 24 giờ trước vẫn đếm; sớm hơn 1 ms thì không.
--  * Ngưỡng cảnh báo: >= 3 lần (hằng số trong mã C#: PhoneNoShowRules.WarningThreshold).
-- Chạy lại an toàn.

CREATE OR ALTER FUNCTION dbo.fn_PhoneNoShowCount(@Phone varchar(10), @NowUtc datetime2(3))
RETURNS TABLE
AS
RETURN
    SELECT COUNT(*) AS NoShowCount, MAX(h.NoShowAt) AS LastNoShowAt
    FROM dbo.ReservationNoShowHistory h
    WHERE h.Phone = @Phone
      AND h.NoShowAt >= DATEADD(day, -90, @NowUtc)
      AND h.NoShowAt <= @NowUtc;
GO

GRANT SELECT ON dbo.fn_PhoneNoShowCount TO restaurant_app;
GO
