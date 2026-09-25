CREATE OR ALTER TRIGGER dbo.tr_Reservations_NoOverlap ON dbo.Reservations AFTER INSERT,UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 -- HOLDLOCK uses serializable key-range locks, including direct SQL writes.
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.Reservations r WITH(UPDLOCK,HOLDLOCK,INDEX(IX_Reservations_TableTime))
  ON r.TableId=i.TableId AND r.Id<>i.Id AND r.StartsAt<i.EndsAt AND r.EndsAt>i.StartsAt
  WHERE i.Status IN ('Confirmed','Arrived') AND r.Status IN ('Confirmed','Arrived'))
 THROW 51100,N'Không được đặt trùng một bàn trong các khung giờ giao nhau.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_AuditLogs_AppendOnly ON dbo.AuditLogs INSTEAD OF UPDATE,DELETE
AS
BEGIN
 THROW 51101,N'Nhật ký chỉ được thêm, không được sửa hoặc xoá.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_InvoiceLines_Immutable ON dbo.InvoiceLines INSTEAD OF UPDATE,DELETE
AS
BEGIN
 THROW 51102,N'Không được sửa hoặc xoá dòng hoá đơn đã chốt.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_Payments_Immutable ON dbo.Payments INSTEAD OF UPDATE,DELETE
AS
BEGIN
 THROW 51103,N'Không được sửa hoặc xoá thanh toán đã ghi nhận.',1;
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_Invoices_Immutable ON dbo.Invoices AFTER UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted) THROW 51104,N'Không được xoá hoá đơn.',1;
 IF UPDATE(SessionId) OR UPDATE(ShiftId) OR UPDATE(InvoiceNumber) OR UPDATE(BusinessDate) OR UPDATE(RequestId)
  OR UPDATE(RestaurantName) OR UPDATE(RestaurantAddress) OR UPDATE(RestaurantPhone) OR UPDATE(TableLabels)
  OR UPDATE(GuestCount) OR UPDATE(SessionOpenedAt) OR UPDATE(IssuedAt) OR UPDATE(IssuedBy)
  OR UPDATE(Subtotal) OR UPDATE(DiscountType) OR UPDATE(DiscountValue) OR UPDATE(DiscountAmount) OR UPDATE(DiscountReason)
  THROW 51105,N'Nội dung hoá đơn đã chốt không được thay đổi.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id JOIN dbo.Shifts s ON s.Id=i.ShiftId
  WHERE d.Status<>'Paid' OR i.Status<>'Voided' OR s.Status<>'Open')
  THROW 51106,N'Chỉ huỷ hoá đơn đã thanh toán trong ca đang mở.',1;
END;
GO
CREATE OR ALTER VIEW dbo.vw_PublicMenu AS
 SELECT m.Id,m.CategoryId,c.Name AS CategoryName,c.SortOrder AS CategorySortOrder,m.Name,m.Price,m.Unit,m.Description,
  m.EstimatedPrepMinutes,COALESCE(m.ImagePath,c.DefaultImagePath) AS ImagePath,m.SortOrder,
  CONVERT(bit,CASE WHEN m.IsSoldOut=1 AND m.SoldOutBusinessDate=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) THEN 1 ELSE 0 END) AS IsSoldOut
 FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId WHERE m.IsActive=1 AND c.IsActive=1;
GO
CREATE OR ALTER VIEW dbo.vw_SessionTotals AS
 SELECT s.Id AS SessionId,COALESCE(s.BillingSessionId,s.Id) AS BillingSessionId,
  COALESCE(SUM(CASE WHEN i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1 THEN i.LineTotal ELSE 0 END),0) AS Subtotal
 FROM dbo.DiningSessions s LEFT JOIN dbo.OrderBatches b ON b.SessionId=s.Id LEFT JOIN dbo.OrderItems i ON i.BatchId=b.Id
 GROUP BY s.Id,s.BillingSessionId;
GO
CREATE OR ALTER VIEW dbo.vw_TableMap AS
 SELECT t.Id,t.Code,t.AreaId,a.Name AS AreaName,t.MinCapacity,t.MaxCapacity,t.TableType,t.Status,
  t.PositionX,t.PositionY,t.StatusChangedAt,st.SessionId,s.BillingSessionId,s.OpenedAt,s.GuestCount,s.Status AS SessionStatus,
  COALESCE(tot.Subtotal,0) AS Subtotal,
  CONVERT(bit,CASE WHEN t.Status='Cleaning' AND t.StatusChangedAt<DATEADD(minute,-10,SYSUTCDATETIME()) THEN 1 ELSE 0 END) AS CleaningOverdue,
  upcoming.Id AS UpcomingReservationId,upcoming.CustomerName AS UpcomingCustomerName,upcoming.StartsAt AS UpcomingStartsAt,
  CONVERT(bit,CASE WHEN upcoming.Id IS NOT NULL AND COALESCE(upcoming.HoldExtendedUntil,DATEADD(minute,15,upcoming.StartsAt))<SYSUTCDATETIME() THEN 1 ELSE 0 END) AS NoShowWarning
 FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
 LEFT JOIN dbo.SessionTables st ON st.TableId=t.Id AND st.ReleasedAt IS NULL
 LEFT JOIN dbo.DiningSessions s ON s.Id=st.SessionId
 LEFT JOIN dbo.vw_SessionTotals tot ON tot.SessionId=s.Id
 OUTER APPLY(SELECT TOP(1) r.Id,r.CustomerName,r.StartsAt,r.HoldExtendedUntil FROM dbo.Reservations r
  WHERE r.TableId=t.Id AND r.Status='Confirmed' ORDER BY r.StartsAt) upcoming
 WHERE t.IsActive=1 AND a.IsActive=1;
GO
CREATE OR ALTER VIEW dbo.vw_KitchenQueue AS
 SELECT i.Id AS OrderItemId,b.Id AS BatchId,b.SessionId,b.BatchNumber,t.Code AS TableCode,i.ItemName,i.Quantity,i.Notes,
  i.Status,i.SubmittedAt,i.PreparingAt,i.ReadyAt,i.EstimatedPrepMinutes,
  CONVERT(bit,CASE WHEN i.Status IN ('Pending','Preparing') AND i.SubmittedAt<DATEADD(minute,-15,SYSUTCDATETIME()) THEN 1 ELSE 0 END) AS KitchenOverdue,
  CONVERT(bit,CASE WHEN i.Status='Ready' AND i.ReadyAt<DATEADD(minute,-5,SYSUTCDATETIME()) THEN 1 ELSE 0 END) AS ServingOverdue
 FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
 WHERE i.Status IN ('Pending','Preparing','Ready');
GO
CREATE OR ALTER VIEW dbo.vw_ReservationList AS
 SELECT r.Id,r.Code,r.CustomerName,STUFF(r.Phone,4,4,'****') AS MaskedPhone,r.GuestCount,r.PreferredAreaId,r.TableId,
  r.StartsAt,r.EndsAt,r.Status,r.HoldExtendedUntil,
  (SELECT COUNT(*) FROM dbo.Reservations h WHERE h.Phone=r.Phone AND h.Status='NoShow' AND h.NoShowAt>=DATEADD(day,-90,SYSUTCDATETIME())) AS RecentNoShowCount
 FROM dbo.Reservations r;
GO
CREATE OR ALTER VIEW dbo.vw_DailyRevenue AS
 SELECT i.BusinessDate,COUNT_BIG(*) AS InvoiceCount,SUM(i.Total) AS Revenue,SUM(i.Subtotal) AS GrossRevenue,
  SUM(i.DiscountAmount) AS Discounts,SUM(CONVERT(bigint,i.GuestCount)) AS GuestCount,
  AVG(CONVERT(decimal(18,2),i.Total)) AS AverageInvoice,
  SUM(CASE WHEN p.Method='Cash' THEN p.Amount ELSE 0 END) AS CashRevenue,
  SUM(CASE WHEN p.Method='Transfer' THEN p.Amount ELSE 0 END) AS TransferRevenue
 FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id WHERE i.Status='Paid' GROUP BY i.BusinessDate;
GO
CREATE OR ALTER VIEW dbo.vw_HourlyRevenue AS
 SELECT BusinessDate,DATEPART(hour,DATEADD(hour,7,IssuedAt)) AS LocalHour,COUNT_BIG(*) AS InvoiceCount,SUM(Total) AS Revenue
 FROM dbo.Invoices WHERE Status='Paid' GROUP BY BusinessDate,DATEPART(hour,DATEADD(hour,7,IssuedAt));
GO
CREATE OR ALTER PROCEDURE dbo.usp_ReportTopItems @FromDate date,@ToDate date,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Reports.Read';
 IF @ToDate<@FromDate OR DATEDIFF(day,@FromDate,@ToDate)>91 THROW 51107,N'Khoảng báo cáo tối đa 92 ngày.',1;
 -- Revenue is gross item revenue before invoice-level discounts, explicitly labeled.
 SELECT TOP(10) l.MenuItemId,m.Name,m.IsActive,SUM(l.Quantity) AS Quantity,SUM(l.LineTotal) AS GrossRevenue
 FROM dbo.InvoiceLines l JOIN dbo.Invoices i ON i.Id=l.InvoiceId JOIN dbo.MenuItems m ON m.Id=l.MenuItemId
 WHERE i.Status='Paid' AND i.BusinessDate BETWEEN @FromDate AND @ToDate AND l.IsChargedCancellation=0
 GROUP BY l.MenuItemId,m.Name,m.IsActive ORDER BY Quantity DESC,l.MenuItemId;
 SELECT TOP(10) l.MenuItemId,m.Name,m.IsActive,SUM(l.Quantity) AS Quantity,SUM(l.LineTotal) AS GrossRevenue
 FROM dbo.InvoiceLines l JOIN dbo.Invoices i ON i.Id=l.InvoiceId JOIN dbo.MenuItems m ON m.Id=l.MenuItemId
 WHERE i.Status='Paid' AND i.BusinessDate BETWEEN @FromDate AND @ToDate AND l.IsChargedCancellation=0
 GROUP BY l.MenuItemId,m.Name,m.IsActive ORDER BY GrossRevenue DESC,l.MenuItemId;
 WITH Reasons AS (
  SELECT i.MenuItemId,i.CancelReason,SUM(i.Quantity) AS CancelledQuantity,
   ROW_NUMBER() OVER(PARTITION BY i.MenuItemId ORDER BY SUM(i.Quantity) DESC,i.CancelReason) AS rn
  FROM dbo.OrderItems i WHERE i.Status='Cancelled' AND CONVERT(date,DATEADD(hour,7,i.CancelledAt)) BETWEEN @FromDate AND @ToDate
  GROUP BY i.MenuItemId,i.CancelReason
 ), Totals AS (SELECT MenuItemId,SUM(CancelledQuantity) AS CancelledQuantity FROM Reasons GROUP BY MenuItemId)
 SELECT TOP(10) t.MenuItemId,m.Name,t.CancelledQuantity,r.CancelReason AS MostCommonReason
 FROM Totals t JOIN Reasons r ON r.MenuItemId=t.MenuItemId AND r.rn=1 JOIN dbo.MenuItems m ON m.Id=t.MenuItemId
 ORDER BY t.CancelledQuantity DESC,t.MenuItemId;
END;
GO
-- Application role: no blanket db_owner/db_datawriter. Authentication/CRUD services need
-- additional narrowly scoped procedures; these are deliberately not exposed as public APIs.
CREATE ROLE restaurant_app;
GRANT SELECT ON dbo.vw_PublicMenu TO restaurant_app;
GRANT EXECUTE ON dbo.usp_CreateReservation TO restaurant_app;
GRANT EXECUTE ON dbo.usp_CancelReservation TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ConfirmReservation TO restaurant_app;
GRANT EXECUTE ON dbo.usp_RejectReservation TO restaurant_app;
GRANT EXECUTE ON dbo.usp_MarkNoShow TO restaurant_app;
GRANT EXECUTE ON dbo.usp_OpenShift TO restaurant_app;
GRANT EXECUTE ON dbo.usp_OpenSession TO restaurant_app;
GRANT EXECUTE ON dbo.usp_RotateTableQr TO restaurant_app;
GRANT EXECUTE ON dbo.usp_OpenGuestSession TO restaurant_app;
GRANT EXECUTE ON dbo.usp_SubmitOrder TO restaurant_app;
GRANT EXECUTE ON dbo.usp_TransitionOrderItem TO restaurant_app;
GRANT EXECUTE ON dbo.usp_CancelOrderItem TO restaurant_app;
GRANT EXECUTE ON dbo.usp_SetPaymentState TO restaurant_app;
GRANT EXECUTE ON dbo.usp_MergeSessions TO restaurant_app;
GRANT EXECUTE ON dbo.usp_UnmergeSession TO restaurant_app;
GRANT EXECUTE ON dbo.usp_UpdateMenuPrice TO restaurant_app;
GRANT EXECUTE ON dbo.usp_SetMenuAvailability TO restaurant_app;
GRANT EXECUTE ON dbo.usp_Checkout TO restaurant_app;
GRANT EXECUTE ON dbo.usp_CleanTable TO restaurant_app;
GRANT EXECUTE ON dbo.usp_CloseShift TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ReopenShift TO restaurant_app;
GRANT EXECUTE ON dbo.usp_VoidInvoice TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ReportTopItems TO restaurant_app;
CREATE ROLE restaurant_worker;
GRANT EXECUTE ON dbo.usp_RunMaintenance TO restaurant_worker;
GRANT EXECUTE ON dbo.usp_ClaimEmail TO restaurant_worker;
GRANT EXECUTE ON dbo.usp_CompleteEmail TO restaurant_worker;
GO
