-- S2-07: every SQL status change gets one UTC timestamp and one sync event.
CREATE OR ALTER TRIGGER dbo.tr_DiningTables_StatusChanged
ON dbo.DiningTables
WITH EXECUTE AS OWNER
AFTER UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE i.Status<>d.Status) RETURN;
 DECLARE @changedAt datetime2(3)=SYSUTCDATETIME();
 UPDATE t SET StatusChangedAt=@changedAt
 FROM dbo.DiningTables t JOIN inserted i ON i.Id=t.Id JOIN deleted d ON d.Id=i.Id
 WHERE i.Status<>d.Status;
 INSERT dbo.TableStatusChangeEvents(TableId,TableCode,AreaName,Capacity,PreviousStatus,Status,ChangedAtUtc)
 SELECT i.Id,i.Code,a.Name,i.MaxCapacity,d.Status,i.Status,@changedAt
 FROM inserted i JOIN deleted d ON d.Id=i.Id JOIN dbo.Areas a ON a.Id=i.AreaId
 WHERE i.Status<>d.Status;
END;
GO
