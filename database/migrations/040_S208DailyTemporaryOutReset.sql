-- S2-08 Task 3: tự đưa mọi món đang "Tạm hết" về "Còn món" lúc 00:00 (Asia/Ho_Chi_Minh, UTC+7, không đổi giờ mùa hè).
-- Thay usp_ResetTemporarilyOutMenuItems (migration 007):
--   * @ScheduledFor phải đúng 00:00 giờ Việt Nam (lưu UTC = 17:00 ngày hôm trước) và không ở tương lai.
--   * Chỉ đặt lại món bị báo tạm hết TRƯỚC mốc 00:00 đó. Khi web khởi động muộn (sau nửa đêm) và chạy bù,
--     món Bếp vừa báo tạm hết sáng hôm nay (sau 00:00) được giữ nguyên.
--   * Món đang còn hàng không bị đụng tới (không cập nhật, không ghi nhật ký).
--   * Mỗi ngày chỉ chạy một lần (dbo.ScheduledJobRuns, JobName='TemporaryOutReset'); chạy lại không đổi gì.
--   * Nhật ký (đã chốt với PO): mỗi món được đặt lại ghi MỘT dòng vào dbo.MenuTemporaryOutEvents,
--     ChangedBy = NULL (hiển thị "Hệ thống – tự đặt lại 00:00"), trạng thái Tạm hết → Còn món, thời điểm = lúc chạy.
CREATE OR ALTER PROCEDURE dbo.usp_ResetTemporarilyOutMenuItems @ScheduledFor datetime2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @local datetime2(3)=DATEADD(hour,7,@ScheduledFor);
    IF @ScheduledFor IS NULL OR CONVERT(time(3),@local)<>'00:00:00'
        THROW 51090,N'Mốc đặt lại phải đúng 00:00 giờ Việt Nam (Asia/Ho_Chi_Minh).',1;
    IF @ScheduledFor>SYSUTCDATETIME()
        THROW 51091,N'Chưa tới 00:00 của ngày cần đặt lại.',1;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        IF EXISTS (SELECT 1 FROM dbo.ScheduledJobRuns WITH (UPDLOCK,HOLDLOCK)
                   WHERE JobName='TemporaryOutReset' AND ScheduledFor=@ScheduledFor AND Status='Succeeded')
        BEGIN
            COMMIT;
            SELECT 0;
            RETURN;
        END;
        IF NOT EXISTS (SELECT 1 FROM dbo.ScheduledJobRuns WHERE JobName='TemporaryOutReset' AND ScheduledFor=@ScheduledFor)
            INSERT dbo.ScheduledJobRuns(JobName,ScheduledFor,Status) VALUES('TemporaryOutReset',@ScheduledFor,'Running');
        ELSE
            UPDATE dbo.ScheduledJobRuns SET Status='Running',StartedAt=SYSUTCDATETIME(),CompletedAt=NULL,Error=NULL
             WHERE JobName='TemporaryOutReset' AND ScheduledFor=@ScheduledFor;

        DECLARE @now datetime2(3)=SYSUTCDATETIME();
        DECLARE @reset TABLE(MenuItemId int PRIMARY KEY);
        UPDATE m SET IsTemporarilyOut=0, UpdatedAt=@now
          OUTPUT inserted.Id INTO @reset(MenuItemId)
        FROM dbo.MenuItems m
        WHERE m.IsTemporarilyOut=1
          AND NOT EXISTS (SELECT 1 FROM dbo.MenuTemporaryOutEvents e
                          WHERE e.MenuItemId=m.Id AND e.IsTemporarilyOut=1 AND e.ChangedAt>=@ScheduledFor);
        INSERT dbo.MenuTemporaryOutEvents(MenuItemId,OldIsTemporarilyOut,IsTemporarilyOut,ChangedBy,ChangedAt)
            SELECT MenuItemId,1,0,NULL,@now FROM @reset;
        UPDATE dbo.ScheduledJobRuns SET Status='Succeeded',CompletedAt=SYSUTCDATETIME()
         WHERE JobName='TemporaryOutReset' AND ScheduledFor=@ScheduledFor;
        COMMIT;
        SELECT COUNT(*) FROM @reset;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_ResetTemporarilyOutMenuItems TO restaurant_app;
GO
