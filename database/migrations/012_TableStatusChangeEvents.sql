CREATE TABLE dbo.TableStatusChangeEvents (
 Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TableStatusChangeEvents PRIMARY KEY,
 TableId int NOT NULL,
 TableCode varchar(20) NOT NULL,
 AreaName nvarchar(80) NOT NULL,
 Capacity int NOT NULL,
 PreviousStatus varchar(20) NOT NULL,
 Status varchar(20) NOT NULL,
 ChangedAtUtc datetime2(3) NOT NULL,
 RecordedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_TableStatusChangeEvents_RecordedAtUtc DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_TableStatusChangeEvents_Id ON dbo.TableStatusChangeEvents(Id)
 INCLUDE(TableCode,AreaName,Capacity,PreviousStatus,Status,ChangedAtUtc);
GRANT SELECT ON dbo.TableStatusChangeEvents TO restaurant_app;
GO

CREATE OR ALTER TRIGGER dbo.tr_DiningTables_StatusChanged
ON dbo.DiningTables
WITH EXECUTE AS OWNER
AFTER UPDATE
AS
BEGIN
 SET NOCOUNT ON;

 INSERT dbo.TableStatusChangeEvents(TableId,TableCode,AreaName,Capacity,PreviousStatus,Status,ChangedAtUtc)
 SELECT i.Id,i.Code,a.Name,i.MaxCapacity,d.Status,i.Status,i.StatusChangedAt
 FROM inserted i
 JOIN deleted d ON d.Id=i.Id
 JOIN dbo.Areas a ON a.Id=i.AreaId
 WHERE i.Status<>d.Status;
END;
GO
