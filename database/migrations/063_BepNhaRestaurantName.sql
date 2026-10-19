-- Đổi tên quán trong cấu hình chung từ dữ liệu mẫu "Nhà hàng Demo" (migration 005) thành "Bếp Nhà".
-- Tên này hiện trên email đặt bàn / nhắc lịch, hoá đơn tạm tính và các màn hình lấy tên quán từ dbo.RestaurantSettings.
-- Chỉ đổi khi vẫn còn tên mẫu, không ghi đè tên quán đã được đặt khác.
UPDATE dbo.RestaurantSettings
SET Name = N'Bếp Nhà'
WHERE Id = 1 AND (Name IS NULL OR LTRIM(RTRIM(Name)) IN (N'', N'Nhà hàng Demo'));
