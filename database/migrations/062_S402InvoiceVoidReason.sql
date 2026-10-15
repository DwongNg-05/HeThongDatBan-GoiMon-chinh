-- S4-02 Task 4: ràng buộc lý do huỷ đủ rõ ràng ở máy chủ, không chỉ ở giao diện.
CREATE OR ALTER PROCEDURE dbo.usp_VoidInvoice @InvoiceId bigint,@Reason nvarchar(500),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Invoices.Void';
  SET @Reason=LTRIM(RTRIM(@Reason));
  IF LEN(@Reason)<5 THROW 51055,N'Lý do huỷ hoá đơn cần có ít nhất 5 ký tự.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.Invoices i JOIN dbo.Shifts s ON s.Id=i.ShiftId WHERE i.Id=@InvoiceId AND i.Status='Paid' AND s.Status='Open')
   THROW 51056,N'Hoá đơn không hợp lệ, đã huỷ hoặc ca đã chốt. Quản lý phải mở lại ca trước.',1;
  UPDATE dbo.Invoices SET Status='Voided',VoidedBy=@ActorUserId,VoidedAt=SYSUTCDATETIME(),VoidReason=@Reason WHERE Id=@InvoiceId;
  INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,Reason) VALUES(@ActorUserId,'InvoiceVoided','Invoice',@InvoiceId,@Reason);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
