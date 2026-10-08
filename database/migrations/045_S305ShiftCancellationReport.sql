CREATE OR ALTER PROCEDURE dbo.usp_ShiftCancellationReport @ActorUserId int,@ShiftId bigint=NULL
AS
BEGIN
 SET NOCOUNT ON;
 -- Reports.Read alone is insufficient: cashiers hold it for other reports.
 IF NOT EXISTS(SELECT 1 FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE u.Id=@ActorUserId AND u.IsActive=1 AND r.Code='Manager')
  THROW 51510,N'Chỉ quản lý được xem nhật ký huỷ món theo ca.',1;
 IF @ShiftId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Shifts WHERE Id=@ShiftId)
  THROW 51511,N'Ca được chọn không tồn tại.',1;
 IF @ShiftId IS NULL SELECT TOP(1) @ShiftId=Id FROM dbo.Shifts ORDER BY OpenedAt DESC,Id DESC;
 SELECT Id,Name,OpenedAt,ClosedAt,Status FROM dbo.Shifts ORDER BY OpenedAt DESC,Id DESC;
 SELECT @ShiftId AS SelectedShiftId;
 SELECT e.Id AS EventId,s.Id AS SessionId,b.Id AS BatchId,b.BatchNumber,i.Id AS OrderItemId,t.Code AS TableCode,
  i.ItemName,i.Quantity,i.UnitPrice,i.LineTotal,e.ActorUserId,u.FullName,u.UserName,e.OccurredAt,
  i.CancelReason,i.CancellationNote,e.FromStatus,i.ChargeWhenCancelled
 FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId
 JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId
 JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId LEFT JOIN dbo.Users u ON u.Id=e.ActorUserId
 WHERE s.ShiftId=@ShiftId AND e.ToStatus='Cancelled'
 ORDER BY e.OccurredAt,e.Id;
END;
GO
GRANT EXECUTE ON dbo.usp_ShiftCancellationReport TO restaurant_app;
GO
