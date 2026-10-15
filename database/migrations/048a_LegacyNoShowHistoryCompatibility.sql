-- Preserve S3-09 feature-branch history when upgrading to release migration 049.
-- Fresh databases do not have this table yet; migration 049 creates its own schema.
IF OBJECT_ID(N'dbo.ReservationNoShowHistory', N'U') IS NOT NULL
   AND COL_LENGTH('dbo.ReservationNoShowHistory','NormalizedPhone') IS NOT NULL
BEGIN
 IF COL_LENGTH('dbo.ReservationNoShowHistory','Phone') IS NULL
  ALTER TABLE dbo.ReservationNoShowHistory ADD Phone varchar(10) NULL;
 IF COL_LENGTH('dbo.ReservationNoShowHistory','TableId') IS NULL
  ALTER TABLE dbo.ReservationNoShowHistory ADD TableId int NULL;
 ALTER TABLE dbo.ReservationNoShowHistory ALTER COLUMN NormalizedPhone nvarchar(100) NULL;
 EXEC(N'UPDATE h SET Phone=COALESCE(h.Phone,CONVERT(varchar(10),h.NormalizedPhone)),
        TableId=COALESCE(h.TableId,r.TableId)
        FROM dbo.ReservationNoShowHistory h LEFT JOIN dbo.Reservations r ON r.Id=h.ReservationId;');
 -- Keep both representations usable by legacy and release readers/writers.
 EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_NoShowHistory_CompatiblePhone
 ON dbo.ReservationNoShowHistory AFTER INSERT,UPDATE AS
 BEGIN
  SET NOCOUNT ON;
  IF TRIGGER_NESTLEVEL(OBJECT_ID(''dbo.tr_NoShowHistory_CompatiblePhone''))>1 RETURN;
  UPDATE h SET Phone=COALESCE(h.Phone,CONVERT(varchar(10),h.NormalizedPhone)),
    NormalizedPhone=COALESCE(h.NormalizedPhone,h.Phone)
  FROM dbo.ReservationNoShowHistory h JOIN inserted i ON i.Id=h.Id
  WHERE h.Phone IS NULL OR h.NormalizedPhone IS NULL;
 END;');
END;
GO
