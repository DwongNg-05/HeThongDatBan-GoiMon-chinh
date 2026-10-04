-- S2-10 Task 3: gán ảnh mặc định theo nhóm cho các nhóm món chuẩn đã thống nhất.
-- Giữ nguyên đường dẫn do môi trường/PO đã cấu hình; nhóm chưa có mapping sẽ fallback về ảnh chung.
UPDATE dbo.MenuCategories
SET DefaultImagePath = CASE NormalizedName
    WHEN N'KHAI VỊ' THEN N'/images/thuc-don/khai-vi.svg'
    WHEN N'MÓN CHÍNH' THEN N'/images/thuc-don/mon-chinh.svg'
    WHEN N'LẨU' THEN N'/images/thuc-don/lau.svg'
    WHEN N'TRÁNG MIỆNG' THEN N'/images/thuc-don/trang-mieng.svg'
    WHEN N'ĐỒ UỐNG' THEN N'/images/thuc-don/do-uong.svg'
END
WHERE DefaultImagePath IS NULL
  AND NormalizedName IN (N'KHAI VỊ', N'MÓN CHÍNH', N'LẨU', N'TRÁNG MIỆNG', N'ĐỒ UỐNG');
