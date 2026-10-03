-- S2-01 Task 3: món hết trong ngày.
-- Trạng thái đã được lưu ở MenuItems.IsSoldOut + SoldOutBusinessDate (usp_SetMenuAvailability)
-- và thực đơn công khai (vw_PublicMenu) chỉ coi là "tạm hết" khi SoldOutBusinessDate = ngày nghiệp vụ hiện tại (UTC+7).
-- Màn hình Quản lý món (usp_ManagementMenu) trước đây đọc thẳng cột IsSoldOut, nên sau nửa đêm
-- (trước khi usp_RunMaintenance chạy) nhân viên vẫn thấy "Tạm hết" trong khi khách thấy món bình thường.
-- Cập nhật để cả hai màn hình dùng cùng một quy tắc.
CREATE OR ALTER PROCEDURE dbo.usp_ManagementMenu @ActorUserId int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';
 DECLARE @today date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
 SELECT Id,Name,Price,
  CONVERT(bit,CASE WHEN IsSoldOut=1 AND SoldOutBusinessDate=@today THEN 1 ELSE 0 END) AS IsSoldOut
 FROM dbo.MenuItems ORDER BY SortOrder,Id;
 SELECT TOP(20) h.ChangedAt,u.UserName,m.Name,h.ChangeDescription
 FROM (
  SELECT ChangedAt,ChangedBy,MenuItemId,CONCAT(N'Đổi giá: ',OldPrice,N' → ',NewPrice,N' đ') AS ChangeDescription FROM dbo.MenuPriceHistory
  UNION ALL
  SELECT ChangedAt,ChangedBy,MenuItemId,CASE WHEN IsSoldOut=1 THEN N'Đánh dấu tạm hết' ELSE N'Đánh dấu còn món' END FROM dbo.MenuAvailabilityEvents
 ) h JOIN dbo.Users u ON u.Id=h.ChangedBy JOIN dbo.MenuItems m ON m.Id=h.MenuItemId
 ORDER BY h.ChangedAt DESC;
END;
GO
