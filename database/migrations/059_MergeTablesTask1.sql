-- Explicit adjacency is configured by a manager; never infer it from table codes.
CREATE TABLE dbo.TableAdjacencies (
 TableId int NOT NULL REFERENCES dbo.DiningTables(Id),
 AdjacentTableId int NOT NULL REFERENCES dbo.DiningTables(Id),
 PRIMARY KEY(TableId,AdjacentTableId), CHECK(TableId<AdjacentTableId)
);
GO
ALTER TABLE dbo.OrderBatches ADD OriginSessionId bigint NULL REFERENCES dbo.DiningSessions(Id);
GO
UPDATE dbo.OrderBatches SET OriginSessionId=SessionId;
ALTER TABLE dbo.OrderBatches ALTER COLUMN OriginSessionId bigint NOT NULL;
GO
-- Upgrade existing groups as well: a member's old batches must join the common list.
;WITH moved AS (
 SELECT b.Id, s.BillingSessionId AS RootId,
  (SELECT COALESCE(MAX(r.BatchNumber),0) FROM dbo.OrderBatches r WHERE r.SessionId=s.BillingSessionId)
    + ROW_NUMBER() OVER(PARTITION BY s.BillingSessionId ORDER BY b.SessionId,b.BatchNumber,b.Id) AS NewNumber
 FROM dbo.OrderBatches b JOIN dbo.DiningSessions s ON s.Id=b.SessionId
 WHERE s.BillingSessionId IS NOT NULL
)
UPDATE b SET SessionId=m.RootId,BatchNumber=m.NewNumber
FROM dbo.OrderBatches b JOIN moved m ON m.Id=b.Id;
GO

CREATE OR ALTER TRIGGER dbo.tr_OrderItems_KeepOrigin ON dbo.OrderItems AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT Id,OriginalTableId FROM inserted EXCEPT SELECT Id,OriginalTableId FROM deleted)
  THROW 51151,N'Bàn gốc của dòng món đã gửi không được thay đổi.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_SetTableAdjacency @TableId int,@AdjacentTableId int,@IsAdjacent bit,@ActorUserId int AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';
  IF @TableId=@AdjacentTableId OR (SELECT COUNT(*) FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId WHERE t.Id IN(@TableId,@AdjacentTableId) AND t.IsActive=1 AND a.IsActive=1)<>2
   OR (SELECT COUNT(DISTINCT AreaId) FROM dbo.DiningTables WHERE Id IN(@TableId,@AdjacentTableId))<>1
   THROW 51152,N'Chọn hai bàn khác nhau, còn hoạt động trong cùng khu vực.',1;
  DECLARE @a int=CASE WHEN @TableId<@AdjacentTableId THEN @TableId ELSE @AdjacentTableId END,
          @b int=CASE WHEN @TableId<@AdjacentTableId THEN @AdjacentTableId ELSE @TableId END;
  IF @IsAdjacent=1 AND NOT EXISTS(SELECT 1 FROM dbo.TableAdjacencies WHERE TableId=@a AND AdjacentTableId=@b)
   INSERT dbo.TableAdjacencies VALUES(@a,@b);
  IF @IsAdjacent=0 DELETE dbo.TableAdjacencies WHERE TableId=@a AND AdjacentTableId=@b;
  INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,NewValues)
   VALUES(@ActorUserId,'SetTableAdjacency','DiningTable',@a,CONCAT('{"adjacentTableId":',@b,',"isAdjacent":',CONVERT(int,@IsAdjacent),'}'));
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_MergeSessions @MainSessionId bigint,@ChildSessionId bigint,@ActorUserId int,
 @ExpectedMainVersion binary(8)=NULL,@ExpectedChildVersion binary(8)=NULL AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';
  IF @MainSessionId=@ChildSessionId OR (SELECT COUNT(*) FROM dbo.DiningSessions WITH(UPDLOCK,HOLDLOCK) WHERE Id IN(@MainSessionId,@ChildSessionId) AND Status='Open' AND BillingSessionId IS NULL)<>2
   OR EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE BillingSessionId IN(@MainSessionId,@ChildSessionId))
   THROW 51153,N'Bàn đã gộp hoặc không còn đang phục vụ. Vui lòng tải lại thông tin bàn.',1;
  IF (@ExpectedMainVersion IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@MainSessionId AND RowVersion=@ExpectedMainVersion))
   OR (@ExpectedChildVersion IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@ChildSessionId AND RowVersion=@ExpectedChildVersion))
   THROW 51153,N'Trạng thái bàn vừa thay đổi. Vui lòng tải lại và xác nhận lại.',1;
  IF EXISTS(SELECT 1 FROM dbo.Invoices WHERE SessionId IN(@MainSessionId,@ChildSessionId))
   THROW 51153,N'Phiên đã có hoá đơn. Vui lòng tải lại thông tin bàn.',1;
  IF (SELECT COUNT(*) FROM dbo.SessionTables WHERE SessionId IN(@MainSessionId,@ChildSessionId) AND ReleasedAt IS NULL)<>2
   OR (SELECT COUNT(DISTINCT SessionId) FROM dbo.SessionTables WHERE SessionId IN(@MainSessionId,@ChildSessionId) AND ReleasedAt IS NULL)<>2
   THROW 51153,N'Chỉ gộp hai bàn độc lập đang phục vụ. Vui lòng tải lại.',1;
  DECLARE @a int,@b int;
  SELECT @a=TableId FROM dbo.SessionTables WHERE SessionId=@MainSessionId AND ReleasedAt IS NULL;
  SELECT @b=TableId FROM dbo.SessionTables WHERE SessionId=@ChildSessionId AND ReleasedAt IS NULL;
  IF (SELECT COUNT(*) FROM dbo.DiningTables t WITH(UPDLOCK,HOLDLOCK) JOIN dbo.Areas a ON a.Id=t.AreaId WHERE t.Id IN(@a,@b) AND t.Status='Serving' AND t.IsActive=1 AND a.IsActive=1)<>2
   THROW 51153,N'Bàn không còn đang phục vụ. Vui lòng tải lại thông tin bàn.',1;
  IF (SELECT COUNT(DISTINCT AreaId) FROM dbo.DiningTables WHERE Id IN(@a,@b))<>1
   THROW 51154,N'Chỉ gộp hai bàn cùng khu vực.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.TableAdjacencies WHERE TableId=CASE WHEN @a<@b THEN @a ELSE @b END AND AdjacentTableId=CASE WHEN @a<@b THEN @b ELSE @a END)
   THROW 51155,N'Hai bàn không được cấu hình liền nhau. Vui lòng tải lại thông tin bàn.',1;
  IF (SELECT COUNT(DISTINCT ShiftId) FROM dbo.DiningSessions WHERE Id IN(@MainSessionId,@ChildSessionId))<>1
   OR EXISTS(SELECT 1 FROM dbo.DiningSessions s JOIN dbo.Shifts h ON h.Id=s.ShiftId WHERE s.Id IN(@MainSessionId,@ChildSessionId) AND h.Status<>'Open')
   THROW 51156,N'Hai bàn phải thuộc cùng ca đang mở.',1;
  -- Move ownership without copying any order line or changing its price/origin.
  DECLARE @offset int=(SELECT COALESCE(MAX(BatchNumber),0) FROM dbo.OrderBatches WHERE SessionId=@MainSessionId);
  UPDATE dbo.OrderBatches SET SessionId=@MainSessionId,BatchNumber=BatchNumber+@offset WHERE SessionId=@ChildSessionId;
  UPDATE dbo.DiningSessions SET BillingSessionId=@MainSessionId WHERE Id=@ChildSessionId;
  UPDATE dbo.DiningSessions SET PaymentRequestedAt=NULL WHERE Id=@MainSessionId;
  INSERT dbo.SessionMerges(MainSessionId,ChildSessionId,MergedBy) VALUES(@MainSessionId,@ChildSessionId,@ActorUserId);
  INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,NewValues)
   VALUES(@ActorUserId,'MergeSessions','DiningSession',@MainSessionId,CONCAT('{"childSessionId":',@ChildSessionId,'}'));
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_SubmitOrder @SessionId bigint,@RequestId uniqueidentifier,@ItemsJson nvarchar(max),
 @ActorUserId int=NULL,@GuestTokenHash binary(32)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  DECLARE @guest bigint,@root bigint,@table int;
  IF @ActorUserId IS NOT NULL EXEC dbo.usp_RequirePermission @ActorUserId,'Orders.Manage';
  ELSE
  BEGIN
   SELECT @guest=g.Id FROM dbo.GuestSessions g JOIN dbo.TableQrCodes q ON q.Id=g.TableQrCodeId
    WHERE g.TokenHash=@GuestTokenHash AND g.SessionId=@SessionId AND g.RevokedAt IS NULL
     AND g.ExpiresAt>SYSUTCDATETIME() AND q.RevokedAt IS NULL;
   IF @guest IS NULL THROW 51023,N'Phiên gọi món đã hết hạn.',1;
  END;
  IF EXISTS(SELECT 1 FROM dbo.OrderBatches WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.OrderBatches WHERE RequestId=@RequestId AND OriginSessionId=@SessionId)
    THROW 51024,N'Mã yêu cầu thuộc phiên khác.',1;
   SELECT Id AS BatchId FROM dbo.OrderBatches WHERE RequestId=@RequestId;
  END
  ELSE
  BEGIN
   SELECT @root=COALESCE(BillingSessionId,Id) FROM dbo.DiningSessions WHERE Id=@SessionId AND Status='Open';
   IF @root IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@root AND Status='Open')
    THROW 51025,N'Phiên đã đóng hoặc đang chờ thanh toán.',1;
   SELECT @table=TableId FROM dbo.SessionTables WHERE SessionId=@SessionId AND ReleasedAt IS NULL;
   IF ISJSON(@ItemsJson)<>1 OR LEFT(LTRIM(@ItemsJson),1)<>'[' THROW 51026,N'Danh sách món không hợp lệ.',1;
   DECLARE @items TABLE(MenuItemId int,Quantity int,Notes nvarchar(max),QuickNotes nvarchar(max));
   INSERT @items SELECT MenuItemId,Quantity,Notes,NULLIF(LTRIM(RTRIM(QuickNotes)),N'')
    FROM OPENJSON(@ItemsJson) WITH(MenuItemId int,Quantity int,Notes nvarchar(max),QuickNotes nvarchar(max));
   IF NOT EXISTS(SELECT 1 FROM @items) OR (SELECT COUNT(*) FROM @items)>100
    OR EXISTS(SELECT 1 FROM @items WHERE MenuItemId IS NULL OR Quantity IS NULL OR Quantity NOT BETWEEN 1 AND 20 OR LEN(Notes)>200 OR LEN(QuickNotes)>100)
    THROW 51027,N'Số lượng mỗi món phải là số nguyên từ 1 đến 20 phần; ghi chú không được vượt giới hạn.',1;
   IF EXISTS(SELECT 1 FROM @items i LEFT JOIN dbo.MenuItems m ON m.Id=i.MenuItemId LEFT JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
    WHERE m.Id IS NULL OR m.IsActive=0 OR c.IsActive=0 OR m.IsTemporarilyOut=1
     OR (m.IsSoldOut=1 AND m.SoldOutBusinessDate=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))
    THROW 51028,N'Có món đã hết hoặc ngừng bán. Vui lòng kiểm tra lại giỏ món.',1;
   DECLARE @number int=(SELECT COALESCE(MAX(BatchNumber),0)+1 FROM dbo.OrderBatches WHERE SessionId=@root);
   INSERT dbo.OrderBatches(SessionId,OriginSessionId,BatchNumber,RequestId,CreatedBy,GuestSessionId) VALUES(@root,@SessionId,@number,@RequestId,@ActorUserId,@guest);
   DECLARE @batch bigint=SCOPE_IDENTITY();
   INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,Notes,QuickNotes,EstimatedPrepMinutes)
    SELECT @batch,m.Id,@table,m.Name,m.Unit,m.Price,i.Quantity,i.Notes,i.QuickNotes,m.EstimatedPrepMinutes FROM @items i JOIN dbo.MenuItems m ON m.Id=i.MenuItemId;
   INSERT dbo.OrderItemEvents(OrderItemId,ToStatus,ActorUserId) SELECT Id,'Pending',@ActorUserId FROM dbo.OrderItems WHERE BatchId=@batch;
   INSERT dbo.Notifications(Kind,Title,Body,TargetRoleId,EntityType,EntityId)
    VALUES('NewOrder',N'Có phiếu gọi món mới',CONCAT(N'Bàn ',(SELECT Code FROM dbo.DiningTables WHERE Id=@table)),3,'OrderBatch',@batch);
   SELECT @batch AS BatchId;
  END
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO


CREATE OR ALTER PROCEDURE dbo.usp_UnmergeSession @ChildSessionId bigint,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';

  DECLARE @main bigint=(SELECT BillingSessionId FROM dbo.DiningSessions WHERE Id=@ChildSessionId AND Status='Open');
  IF @main IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@main AND Status='Open')
   THROW 51038,N'Chỉ huỷ gộp khi cả nhóm đang phục vụ và chưa thanh toán.',1;
  IF EXISTS(SELECT 1 FROM dbo.OrderBatches WHERE OriginSessionId=@ChildSessionId AND SessionId=@main)
   THROW 51157,N'Phiên có món đã chuyển về phiên chính; chức năng huỷ gộp chưa được hỗ trợ.',1;
  UPDATE dbo.DiningSessions SET BillingSessionId=NULL WHERE Id=@ChildSessionId;
  UPDATE dbo.SessionMerges SET UndoneAt=SYSUTCDATETIME(),UndoneBy=@ActorUserId WHERE ChildSessionId=@ChildSessionId AND UndoneAt IS NULL;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

GRANT SELECT ON dbo.TableAdjacencies TO restaurant_app;
GRANT EXECUTE ON dbo.usp_SetTableAdjacency TO restaurant_app;
GO
