-- Bỏ màn hình "Món trong ngày" của Bếp (/Kitchen/Dishes, /Kitchen/SoldOut): báo món tạm hết / bán lại
-- đã gộp vào Quản lý món (/Dishes), chỉ Quản lý dùng. Thu hẹp quyền trong database cho khớp:
-- Bếp không còn Menu.Availability, nên gọi thẳng usp_SetMenuAvailability bằng tài khoản Bếp cũng bị từ chối (51001).
-- Chỉ xoá cặp vai trò–quyền; không đụng dữ liệu món. Chạy lại vẫn an toàn.
DELETE rp
FROM dbo.RolePermissions rp
JOIN dbo.Roles r ON r.Id=rp.RoleId
JOIN dbo.Permissions p ON p.Id=rp.PermissionId
WHERE r.Code='Kitchen' AND p.Code='Menu.Availability';
GO
