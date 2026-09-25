INSERT dbo.RestaurantSettings(Id,Name,Address,Phone,Email) VALUES(1,N'Nhà hàng Demo',N'Địa chỉ mẫu — thay trước khi triển khai','0280000000',NULL);
INSERT dbo.OpeningHours(DayOfWeek,OpensAt,ClosesAt) VALUES
 (1,'08:00','23:00'),(2,'08:00','23:00'),(3,'08:00','23:00'),(4,'08:00','23:00'),(5,'08:00','23:00'),(6,'08:00','23:00'),(7,'08:00','23:00');
INSERT dbo.Roles(Id,Code,Name) VALUES(1,'Manager',N'Quản lý'),(2,'Waiter',N'Phục vụ'),(3,'Kitchen',N'Bếp'),(4,'Cashier',N'Thu ngân');
INSERT dbo.Permissions(Code,Description) VALUES
 ('Users.Manage',N'Quản lý tài khoản'),('Catalog.Manage',N'Quản lý khu vực, bàn, thực đơn'),
 ('Catalog.Read',N'Xem danh mục'),('Menu.Availability',N'Đánh dấu món tạm hết'),
 ('Reservations.Manage',N'Xác nhận, từ chối và xử lý đặt bàn'),('Reservations.Read',N'Xem đặt bàn'),
 ('Sessions.Manage',N'Đón khách, gộp bàn, báo dọn bàn'),('Orders.Manage',N'Thêm và huỷ món đang chờ'),
 ('Orders.Read',N'Xem order'),('Orders.Serve',N'Xác nhận mang món ra'),
 ('Orders.CancelPrepared',N'Huỷ món đã chế biến có tính tiền'),('Kitchen.Manage',N'Chuyển trạng thái chế biến'),
 ('Kitchen.Read',N'Xem màn hình bếp'),('Payments.Manage',N'Thanh toán và chốt ca'),
 ('Payments.Read',N'Xem hoá đơn'),('Invoices.Void',N'Huỷ hoá đơn đã chốt'),
 ('Shifts.Reopen',N'Mở lại ca đã đóng'),('Reports.Read',N'Xem báo cáo'),('Audit.Read',N'Xem nhật ký');
-- Manager may read the kitchen screen but kitchen state changes remain Kitchen's duty.
INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT 1,Id FROM dbo.Permissions WHERE Code<>'Kitchen.Manage';
INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT 2,Id FROM dbo.Permissions WHERE Code IN
 ('Catalog.Read','Menu.Availability','Reservations.Manage','Reservations.Read','Sessions.Manage','Orders.Manage','Orders.Read','Orders.Serve','Kitchen.Read','Payments.Read');
INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT 3,Id FROM dbo.Permissions WHERE Code IN
 ('Catalog.Read','Menu.Availability','Reservations.Read','Orders.Read','Kitchen.Read','Kitchen.Manage');
INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT 4,Id FROM dbo.Permissions WHERE Code IN
 ('Catalog.Read','Reservations.Read','Orders.Read','Payments.Manage','Payments.Read','Reports.Read');
GO
