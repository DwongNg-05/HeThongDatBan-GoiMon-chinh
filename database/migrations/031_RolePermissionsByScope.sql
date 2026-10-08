-- S1-04 (Task 1–4): đồng bộ quyền trong database với phân quyền ở máy chủ web (Security/AppRoles.cs, docs/S1-04-TongHop.md).
-- Sau Task 1 và Task 2, web chặn chặt hơn bảng dbo.RolePermissions gốc (005_ReferenceData.sql):
--   * Phục vụ chỉ làm sơ đồ bàn, đặt bàn, gọi món → bỏ Menu.Availability (báo tạm hết), Kitchen.Read (màn hình bếp),
--     Payments.Read (hoá đơn).
--   * Bếp và Thu ngân bị chặn hẳn ở đặt bàn → bỏ Reservations.Read.
-- Nhờ vậy thủ tục SQL (ví dụ usp_SetMenuAvailability kiểm tra Menu.Availability) cũng từ chối đúng như web.
-- Chỉ xoá các cặp vai trò–quyền; không đổi danh sách quyền, không đụng dữ liệu nghiệp vụ. Chạy lại vẫn an toàn.
DELETE rp
FROM dbo.RolePermissions rp
JOIN dbo.Roles r ON r.Id=rp.RoleId
JOIN dbo.Permissions p ON p.Id=rp.PermissionId
WHERE (r.Code='Waiter' AND p.Code IN ('Menu.Availability','Kitchen.Read','Payments.Read'))
   OR (r.Code IN ('Kitchen','Cashier') AND p.Code='Reservations.Read');
GO
