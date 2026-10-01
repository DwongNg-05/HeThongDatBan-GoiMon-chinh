-- Xác minh đăng nhập bằng email: mọi tài khoản trừ Quản lý phải nhập mã gửi qua email
-- sau khi đăng nhập (và trước khi đổi mật khẩu lần đầu). Mỗi phiên đăng nhập xác minh một lần.
IF COL_LENGTH('dbo.Users','Email') IS NULL
 ALTER TABLE dbo.Users ADD Email nvarchar(254) NULL;
GO
IF COL_LENGTH('dbo.LoginSessions','EmailVerifiedAt') IS NULL
 ALTER TABLE dbo.LoginSessions ADD EmailVerifiedAt datetime2(3) NULL;
GO
CREATE TABLE dbo.EmailVerificationCodes (
 Id bigint IDENTITY CONSTRAINT PK_EmailVerificationCodes PRIMARY KEY,
 UserId int NOT NULL CONSTRAINT FK_EmailVerificationCodes_Users REFERENCES dbo.Users(Id),
 SessionId uniqueidentifier NOT NULL CONSTRAINT FK_EmailVerificationCodes_Sessions REFERENCES dbo.LoginSessions(Id),
 Email nvarchar(254) NOT NULL,
 -- Chỉ lưu SHA-256 của mã, không lưu mã gốc.
 CodeHash binary(32) NOT NULL,
 CreatedAt datetime2(3) NOT NULL,
 ExpiresAt datetime2(3) NOT NULL,
 FailedAttempts int NOT NULL CONSTRAINT DF_EmailVerificationCodes_Failed DEFAULT 0,
 ConsumedAt datetime2(3) NULL,
 CONSTRAINT CK_EmailVerificationCodes_Expiry CHECK(ExpiresAt>CreatedAt)
);
CREATE INDEX IX_EmailVerificationCodes_Session ON dbo.EmailVerificationCodes(SessionId,CreatedAt DESC);
CREATE INDEX IX_EmailVerificationCodes_User ON dbo.EmailVerificationCodes(UserId,CreatedAt DESC);
GO
-- Trạng thái xác minh của phiên hiện tại.
CREATE OR ALTER PROCEDURE dbo.usp_EmailVerificationState @UserId int,@SessionId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @now datetime2(3)=SYSUTCDATETIME();
 SELECT u.Email,
        CAST(CASE WHEN s.EmailVerifiedAt IS NOT NULL THEN 1 ELSE 0 END AS bit) AS Verified,
        (SELECT TOP(1) c.CreatedAt FROM dbo.EmailVerificationCodes c WHERE c.SessionId=@SessionId ORDER BY c.CreatedAt DESC) AS LastSentAt,
        (SELECT TOP(1) c.ExpiresAt FROM dbo.EmailVerificationCodes c
          WHERE c.SessionId=@SessionId AND c.ConsumedAt IS NULL AND c.ExpiresAt>@now ORDER BY c.CreatedAt DESC) AS ActiveExpiresAt,
        (SELECT TOP(1) c.Email FROM dbo.EmailVerificationCodes c WHERE c.SessionId=@SessionId ORDER BY c.CreatedAt DESC) AS PendingEmail
 FROM dbo.Users u JOIN dbo.LoginSessions s ON s.UserId=u.Id
 WHERE u.Id=@UserId AND u.IsActive=1 AND s.Id=@SessionId AND s.RevokedAt IS NULL AND s.ExpiresAt>@now;
END;
GO
-- Tạo mã mới (mã cũ của phiên hết hiệu lực). Kết quả: Status 0 = đã tạo, 1 = phải chờ (RetryAfterSeconds),
-- 2 = gửi quá nhiều lần trong 15 phút, 3 = phiên không hợp lệ.
CREATE OR ALTER PROCEDURE dbo.usp_IssueEmailVerificationCode
 @UserId int,@SessionId uniqueidentifier,@Email nvarchar(254),@CodeHash binary(32),
 @ValidMinutes int=10,@CooldownSeconds int=60,@MaxPerWindow int=5
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 BEGIN TRAN;
 DECLARE @now datetime2(3)=SYSUTCDATETIME(),@last datetime2(3),@count int;
 IF NOT EXISTS(SELECT 1 FROM dbo.LoginSessions s WITH(UPDLOCK,HOLDLOCK) JOIN dbo.Users u ON u.Id=s.UserId
               WHERE s.Id=@SessionId AND s.UserId=@UserId AND s.RevokedAt IS NULL AND s.ExpiresAt>@now AND u.IsActive=1)
 BEGIN ROLLBACK; SELECT 3 AS Status, 0 AS RetryAfterSeconds; RETURN; END;
 SELECT @last=MAX(CreatedAt),@count=COUNT(*) FROM dbo.EmailVerificationCodes
 WHERE UserId=@UserId AND CreatedAt>DATEADD(minute,-15,@now);
 IF @last IS NOT NULL AND @last>DATEADD(second,-@CooldownSeconds,@now)
 BEGIN ROLLBACK; SELECT 1 AS Status, @CooldownSeconds-DATEDIFF(second,@last,@now) AS RetryAfterSeconds; RETURN; END;
 IF @count>=@MaxPerWindow
 BEGIN
  DECLARE @oldest datetime2(3)=(SELECT MIN(CreatedAt) FROM (SELECT TOP(@MaxPerWindow) CreatedAt FROM dbo.EmailVerificationCodes
                                 WHERE UserId=@UserId AND CreatedAt>DATEADD(minute,-15,@now) ORDER BY CreatedAt DESC) t);
  ROLLBACK; SELECT 2 AS Status, 900-DATEDIFF(second,@oldest,@now) AS RetryAfterSeconds; RETURN;
 END;
 UPDATE dbo.EmailVerificationCodes SET ConsumedAt=@now WHERE SessionId=@SessionId AND ConsumedAt IS NULL;
 INSERT dbo.EmailVerificationCodes(UserId,SessionId,Email,CodeHash,CreatedAt,ExpiresAt)
 VALUES(@UserId,@SessionId,LTRIM(RTRIM(@Email)),@CodeHash,@now,DATEADD(minute,@ValidMinutes,@now));
 COMMIT;
 SELECT 0 AS Status, 0 AS RetryAfterSeconds;
END;
GO
-- Kiểm tra mã. Status 0 = đúng (phiên được đánh dấu đã xác minh, email được lưu vào tài khoản),
-- 1 = sai (RemainingAttempts), 2 = hết hạn hoặc chưa gửi mã, 3 = sai quá số lần cho phép.
CREATE OR ALTER PROCEDURE dbo.usp_VerifyEmailCode
 @UserId int,@SessionId uniqueidentifier,@CodeHash binary(32),@MaxAttempts int=5
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 BEGIN TRAN;
 DECLARE @now datetime2(3)=SYSUTCDATETIME(),@id bigint,@hash binary(32),@expires datetime2(3),@failed int,@email nvarchar(254);
 SELECT TOP(1) @id=Id,@hash=CodeHash,@expires=ExpiresAt,@failed=FailedAttempts,@email=Email
 FROM dbo.EmailVerificationCodes WITH(UPDLOCK,HOLDLOCK)
 WHERE SessionId=@SessionId AND UserId=@UserId AND ConsumedAt IS NULL ORDER BY CreatedAt DESC;
 IF @id IS NULL OR @expires<=@now
 BEGIN ROLLBACK; SELECT 2 AS Status, 0 AS RemainingAttempts; RETURN; END;
 IF @failed>=@MaxAttempts
 BEGIN ROLLBACK; SELECT 3 AS Status, 0 AS RemainingAttempts; RETURN; END;
 IF @hash<>@CodeHash
 BEGIN
  UPDATE dbo.EmailVerificationCodes SET FailedAttempts=FailedAttempts+1 WHERE Id=@id;
  COMMIT;
  SELECT CASE WHEN @failed+1>=@MaxAttempts THEN 3 ELSE 1 END AS Status, @MaxAttempts-@failed-1 AS RemainingAttempts;
  RETURN;
 END;
 UPDATE dbo.EmailVerificationCodes SET ConsumedAt=@now WHERE Id=@id;
 UPDATE dbo.LoginSessions SET EmailVerifiedAt=@now WHERE Id=@SessionId AND UserId=@UserId AND RevokedAt IS NULL;
 UPDATE dbo.Users SET Email=@email,UpdatedAt=@now WHERE Id=@UserId AND (Email IS NULL OR Email<>@email);
 COMMIT;
 SELECT 0 AS Status, @MaxAttempts AS RemainingAttempts;
END;
GO
GRANT EXECUTE ON dbo.usp_EmailVerificationState TO restaurant_app;
GRANT EXECUTE ON dbo.usp_IssueEmailVerificationCode TO restaurant_app;
GRANT EXECUTE ON dbo.usp_VerifyEmailCode TO restaurant_app;
-- Mã xác minh chỉ được đọc/ghi qua stored procedure.
DENY SELECT, INSERT, UPDATE, DELETE ON dbo.EmailVerificationCodes TO restaurant_app;
GO
