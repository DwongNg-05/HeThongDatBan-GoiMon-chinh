-- S2-06 Task 2. Rejection immediately releases a Pending table hold.
CREATE OR ALTER PROCEDURE dbo.usp_RejectReservation
 @ReservationId bigint,@Reason nvarchar(100),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
  IF @Reason IS NULL OR NOT EXISTS(SELECT 1 FROM (VALUES(N'NoTable'),(N'OutsideHours'),(N'Unreachable')) reasons(Code)
    WHERE Code COLLATE Latin1_General_100_BIN2=@Reason COLLATE Latin1_General_100_BIN2 AND DATALENGTH(Code)=DATALENGTH(@Reason))
   THROW 51010,N'Vui lòng chọn một trong ba lý do từ chối hợp lệ.',1;
  DECLARE @tableId int,@found bit=0;
  SELECT @tableId=TableId,@found=1 FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId AND Status='Pending';
  IF @found=0
   THROW 51011,N'Chỉ có thể từ chối lượt đặt đang chờ. Lượt đặt đã được xử lý hoặc không tồn tại.',1;
  UPDATE dbo.Reservations SET Status='Rejected',RejectionReason=@Reason,TableId=NULL WHERE Id=@ReservationId;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,OldTableId,ActorUserId,Reason)
   VALUES(@ReservationId,'Pending','Rejected',@tableId,@ActorUserId,@Reason);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
GRANT EXECUTE ON dbo.usp_RejectReservation TO restaurant_app;
GO
