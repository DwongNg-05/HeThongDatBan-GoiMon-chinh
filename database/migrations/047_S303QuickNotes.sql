-- S3-03 Task 2: lựa chọn ghi chú nhanh ("Ít cay", "Không hành", "Không mỡ") cho từng dòng món.
--
-- Lựa chọn nhanh lưu ở cột riêng dbo.OrderItems.QuickNotes (ví dụ N'Ít cay, Không hành'), tách khỏi ghi chú tự do
-- dbo.OrderItems.Notes (tối đa 200 ký tự) để lựa chọn nhanh không chiếm chỗ của ghi chú tự do.
-- usp_SubmitOrder nhận thêm thuộc tính "QuickNotes" (chuỗi, tối đa 100 ký tự) cho mỗi phần tử @ItemsJson;
-- giỏ cũ không gửi thuộc tính này vẫn chạy như trước (QuickNotes = NULL). Mỗi phần tử vẫn là một dòng OrderItems riêng.
-- Chạy lại an toàn.

IF COL_LENGTH(N'dbo.OrderItems', N'QuickNotes') IS NULL
    ALTER TABLE dbo.OrderItems ADD QuickNotes nvarchar(100) NULL;
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
   IF NOT EXISTS(SELECT 1 FROM dbo.OrderBatches WHERE RequestId=@RequestId AND SessionId=@SessionId)
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
    OR EXISTS(SELECT 1 FROM @items WHERE MenuItemId IS NULL OR Quantity IS NULL OR Quantity NOT BETWEEN 1 AND 99 OR LEN(Notes)>200 OR LEN(QuickNotes)>100)
    THROW 51027,N'Số lượng hoặc ghi chú món không hợp lệ.',1;
   IF EXISTS(SELECT 1 FROM @items i LEFT JOIN dbo.MenuItems m ON m.Id=i.MenuItemId LEFT JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
    WHERE m.Id IS NULL OR m.IsActive=0 OR c.IsActive=0 OR m.IsTemporarilyOut=1
     OR (m.IsSoldOut=1 AND m.SoldOutBusinessDate=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))
    THROW 51028,N'Có món đã hết hoặc ngừng bán. Vui lòng kiểm tra lại giỏ món.',1;
   DECLARE @number int=(SELECT COALESCE(MAX(BatchNumber),0)+1 FROM dbo.OrderBatches WHERE SessionId=@SessionId);
   INSERT dbo.OrderBatches(SessionId,BatchNumber,RequestId,CreatedBy,GuestSessionId) VALUES(@SessionId,@number,@RequestId,@ActorUserId,@guest);
   DECLARE @batch bigint=SCOPE_IDENTITY();
   INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,Notes,QuickNotes,EstimatedPrepMinutes)
    SELECT @batch,m.Id,@table,m.Name,m.Unit,m.Price,i.Quantity,i.Notes,i.QuickNotes,m.EstimatedPrepMinutes FROM @items i JOIN dbo.MenuItems m ON m.Id=i.MenuItemId;
   INSERT dbo.OrderItemEvents(OrderItemId,ToStatus,ActorUserId) SELECT Id,'Pending',@ActorUserId FROM dbo.OrderItems WHERE BatchId=@batch;
   INSERT dbo.Notifications(Kind,Title,Body,TargetRoleId,EntityType,EntityId)
    VALUES('NewOrder',N'Có phiếu gọi món mới',CONCAT(N'Bàn ',(SELECT Code FROM dbo.DiningTables WHERE Id=@table)),3,'OrderBatch',@batch);
   SELECT @batch AS BatchId;
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
