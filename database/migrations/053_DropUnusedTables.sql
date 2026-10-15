-- Dọn database: xoá các bảng không còn dùng tới, đồng bộ với mã nguồn hiện tại.
--   * dbo.LoginAttempts   : bản đầu của nhật ký đăng nhập; đăng nhập/khoá tài khoản dùng dbo.LoginFailures,
--                           dbo.LoginLockoutSubjects (007) và dbo.SecurityAuditLogs (020). Không còn code nào ghi/đọc.
--   * dbo.RefreshTokens   : dành cho đăng nhập bằng token; website dùng cookie + dbo.LoginSessions (008). Không còn code nào dùng.
--   * dbo.NotificationReceipts : chưa từng có thủ tục hay code nào ghi/đọc.
-- usp_RunMaintenance được viết lại, bỏ hai câu dọn dẹp LoginAttempts / RefreshTokens (phần còn lại giữ nguyên như 003).
-- An toàn dữ liệu: chỉ xoá bảng khi bảng TRỐNG; còn dữ liệu thì giữ lại và báo để kiểm tra tay. Chạy lại vẫn an toàn.

CREATE OR ALTER PROCEDURE dbo.usp_RunMaintenance
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @now datetime2(3)=SYSUTCDATETIME(),@date date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
  INSERT dbo.MenuAvailabilityEvents(MenuItemId,IsSoldOut,BusinessDate)
    SELECT Id,0,@date FROM dbo.MenuItems WHERE IsSoldOut=1 AND (SoldOutBusinessDate<@date OR SoldOutBusinessDate IS NULL);
  UPDATE dbo.MenuItems SET IsSoldOut=0,SoldOutBusinessDate=@date WHERE IsSoldOut=1 AND (SoldOutBusinessDate<@date OR SoldOutBusinessDate IS NULL);
  UPDATE t SET Status=CASE WHEN EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status='Confirmed'
    AND r.StartsAt<=DATEADD(minute,30,@now) AND r.EndsAt>@now) THEN 'Reserved' ELSE 'Available' END
   FROM dbo.DiningTables t WHERE t.Status IN ('Available','Reserved');
  INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey)
   SELECT r.Id,'BookingReminder',r.Email,N'Nhắc lịch đặt bàn',
    (SELECT r.Code,r.StartsAt,r.GuestCount,r.TableId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),CONCAT('BookingReminder:',r.Id)
    FROM dbo.Reservations r WHERE r.Status='Confirmed' AND r.Email IS NOT NULL
    AND r.StartsAt>@now AND r.StartsAt<=DATEADD(minute,(SELECT ReminderLeadMinutes FROM dbo.RestaurantSettings WHERE Id=1),@now)
    AND NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox o WHERE o.DedupeKey=CONCAT('BookingReminder:',r.Id));
  UPDATE o SET Status='Cancelled' FROM dbo.EmailOutbox o JOIN dbo.Reservations r ON r.Id=o.ReservationId
    WHERE o.MessageType='BookingReminder' AND o.Status IN ('Pending','Processing') AND (r.Status<>'Confirmed' OR r.StartsAt<=@now);
  DELETE dbo.LookupAttempts WHERE AttemptedAt<DATEADD(day,-1,@now);
  -- Physically delete expired booking personal data; retain financial/order snapshots.
  DECLARE @old TABLE(Id bigint PRIMARY KEY);
  INSERT @old SELECT r.Id FROM dbo.Reservations r WHERE r.EndsAt<DATEADD(month,-12,@now)
   AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions s WHERE s.ReservationId=r.Id AND s.Status<>'Closed');
  UPDATE s SET ReservationId=NULL FROM dbo.DiningSessions s JOIN @old o ON o.Id=s.ReservationId;
  DELETE e FROM dbo.EmailOutbox e JOIN @old o ON o.Id=e.ReservationId;
  DELETE e FROM dbo.ReservationEvents e JOIN @old o ON o.Id=e.ReservationId;
  DELETE r FROM dbo.Reservations r JOIN @old o ON o.Id=r.Id;
  INSERT dbo.ScheduledJobRuns(JobName,ScheduledFor,CompletedAt,Status) VALUES('Maintenance',@now,SYSUTCDATETIME(),'Succeeded');
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

DECLARE @t sysname, @sql nvarchar(400), @rows bigint;
DECLARE unused_tables CURSOR LOCAL STATIC READ_ONLY FORWARD_ONLY FOR
    SELECT v.name FROM (VALUES (N'NotificationReceipts'), (N'RefreshTokens'), (N'LoginAttempts')) AS v(name)
    WHERE OBJECT_ID(N'dbo.' + QUOTENAME(v.name), N'U') IS NOT NULL;
OPEN unused_tables;
FETCH NEXT FROM unused_tables INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @rows = SUM(p.rows) FROM sys.partitions p
    WHERE p.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@t)) AND p.index_id IN (0, 1);
    IF COALESCE(@rows, 0) = 0
    BEGIN
        SET @sql = N'DROP TABLE dbo.' + QUOTENAME(@t) + N';';
        EXEC sys.sp_executesql @sql;
        PRINT N'Đã xoá bảng không dùng: dbo.' + @t;
    END
    ELSE
        PRINT N'Giữ lại dbo.' + @t + N' vì còn ' + CAST(@rows AS nvarchar(20)) + N' dòng dữ liệu; kiểm tra tay trước khi xoá.';
    FETCH NEXT FROM unused_tables INTO @t;
END;
CLOSE unused_tables;
DEALLOCATE unused_tables;
GO
