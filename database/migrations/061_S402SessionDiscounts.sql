-- S4-02 Task 3: giảm giá được áp dụng trước khi chốt hoá đơn.
-- Chỉ giữ một giảm giá đang hiệu lực cho mỗi phiên; mọi thay đổi vào AuditLogs.
IF OBJECT_ID(N'dbo.SessionDiscounts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SessionDiscounts (
        Id bigint IDENTITY PRIMARY KEY,
        SessionId bigint NOT NULL UNIQUE REFERENCES dbo.DiningSessions(Id),
        DiscountType varchar(10) NOT NULL CHECK (DiscountType IN ('Amount','Percent')),
        DiscountValue decimal(18,2) NOT NULL CHECK (DiscountValue > 0),
        DiscountAmount decimal(18,0) NOT NULL CHECK (DiscountAmount > 0),
        Reason nvarchar(500) NOT NULL,
        AppliedBy int NOT NULL REFERENCES dbo.Users(Id),
        AppliedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedBy int NOT NULL REFERENCES dbo.Users(Id),
        UpdatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        RowVersion rowversion NOT NULL
    );
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_SetSessionDiscount
    @SessionId bigint,
    @DiscountType varchar(10),
    @DiscountValue decimal(18,2),
    @Reason nvarchar(500),
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Payments.Manage';

        SET @Reason = LTRIM(RTRIM(@Reason));
        IF @DiscountType NOT IN ('Amount','Percent') OR @DiscountValue <= 0
            OR (@DiscountType='Percent' AND @DiscountValue > 50)
            OR (@DiscountType='Amount' AND @DiscountValue <> FLOOR(@DiscountValue))
            THROW 51070, N'Giảm giá phải là số dương và không vượt quá 50%.', 1;
        IF LEN(@Reason) < 5
            THROW 51071, N'Lý do giảm giá cần có ít nhất 5 ký tự.', 1;

        DECLARE @subtotal decimal(18,0), @amount decimal(18,0), @oldType varchar(10), @oldValue decimal(18,2), @oldAmount decimal(18,0);
        SELECT @subtotal=COALESCE(SUM(i.LineTotal),0)
        FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId
        WHERE (s.Id=@SessionId OR s.BillingSessionId=@SessionId)
          AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1);

        IF @subtotal IS NULL OR NOT EXISTS (
            SELECT 1 FROM dbo.DiningSessions s JOIN dbo.Shifts sh ON sh.Id=s.ShiftId
            WHERE s.Id=@SessionId AND s.BillingSessionId IS NULL AND s.Status IN ('Open','AwaitingPayment') AND sh.Status='Open')
            THROW 51072, N'Chỉ được giảm giá cho phiên đang mở trong ca đang mở.', 1;

        SET @amount=CASE @DiscountType WHEN 'Amount' THEN @DiscountValue ELSE ROUND(@subtotal*@DiscountValue/100,0) END;
        IF @amount > @subtotal*0.5
            THROW 51073, N'Giảm giá tối đa 50% tổng tiền món.', 1;

        SELECT @oldType=DiscountType,@oldValue=DiscountValue,@oldAmount=DiscountAmount
        FROM dbo.SessionDiscounts WITH (UPDLOCK,HOLDLOCK) WHERE SessionId=@SessionId;

        IF @oldType IS NULL
        BEGIN
            INSERT dbo.SessionDiscounts(SessionId,DiscountType,DiscountValue,DiscountAmount,Reason,AppliedBy,UpdatedBy)
            VALUES(@SessionId,@DiscountType,@DiscountValue,@amount,@Reason,@ActorUserId,@ActorUserId);
            INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,Reason,NewValues)
            VALUES(@ActorUserId,'SessionDiscountApplied','DiningSession',CONVERT(varchar(60),@SessionId),@Reason,
                   CONCAT('{"type":"',@DiscountType,'","value":',@DiscountValue,',"amount":',@amount,'}'));
        END
        ELSE
        BEGIN
            UPDATE dbo.SessionDiscounts SET DiscountType=@DiscountType,DiscountValue=@DiscountValue,DiscountAmount=@amount,
                   Reason=@Reason,UpdatedBy=@ActorUserId,UpdatedAt=SYSUTCDATETIME() WHERE SessionId=@SessionId;
            INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,Reason,OldValues,NewValues)
            VALUES(@ActorUserId,'SessionDiscountChanged','DiningSession',CONVERT(varchar(60),@SessionId),@Reason,
                   CONCAT('{"type":"',@oldType,'","value":',@oldValue,',"amount":',@oldAmount,'}'),
                   CONCAT('{"type":"',@DiscountType,'","value":',@DiscountValue,',"amount":',@amount,'}'));
        END;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_RemoveSessionDiscount @SessionId bigint,@ActorUserId int
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Payments.Manage';
        IF NOT EXISTS (SELECT 1 FROM dbo.DiningSessions s JOIN dbo.Shifts sh ON sh.Id=s.ShiftId
            WHERE s.Id=@SessionId AND s.BillingSessionId IS NULL AND s.Status IN ('Open','AwaitingPayment') AND sh.Status='Open')
            THROW 51072, N'Chỉ được gỡ giảm giá cho phiên đang mở trong ca đang mở.', 1;
        IF NOT EXISTS(SELECT 1 FROM dbo.SessionDiscounts WHERE SessionId=@SessionId)
            THROW 51074, N'Phiên này chưa có giảm giá để gỡ.', 1;
        DELETE FROM dbo.SessionDiscounts WHERE SessionId=@SessionId;
        INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,Reason)
        VALUES(@ActorUserId,'SessionDiscountRemoved','DiningSession',CONVERT(varchar(60),@SessionId),N'Gỡ giảm giá trước khi thanh toán');
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
