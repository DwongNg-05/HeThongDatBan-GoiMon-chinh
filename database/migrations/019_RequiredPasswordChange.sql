-- S1-03 restored from 30c23f4, adapted to the shared Users schema.
CREATE OR ALTER PROCEDURE dbo.usp_PasswordChangeState @UserId int
AS
BEGIN
 SET NOCOUNT ON;
 SELECT PasswordHash,MustChangePassword FROM dbo.Users WHERE Id=@UserId AND IsActive=1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ChangeOwnPassword
 @UserId int,@SessionId uniqueidentifier,@ExpectedHash varchar(100),@NewHash varchar(100)
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 BEGIN TRAN;
 -- Use the same session-before-user lock order as session validation.
 DECLARE @valid bit=0,@now datetime2(3)=SYSUTCDATETIME();
 SELECT @valid=1 FROM dbo.LoginSessions WITH(UPDLOCK,HOLDLOCK)
 WHERE Id=@SessionId AND UserId=@UserId AND RevokedAt IS NULL
 AND ExpiresAt>@now AND LastActivityAt>DATEADD(minute,-30,@now);
 IF @valid=0
 BEGIN
  ROLLBACK; SELECT CAST(0 AS bit); RETURN;
 END;
 UPDATE dbo.Users SET PasswordHash=@NewHash,MustChangePassword=0,PasswordChangedAt=@now,SecurityStamp=NEWID(),UpdatedAt=@now
 WHERE Id=@UserId AND IsActive=1 AND PasswordHash COLLATE Latin1_General_100_BIN2=@ExpectedHash COLLATE Latin1_General_100_BIN2;
 IF @@ROWCOUNT=0
 BEGIN
  ROLLBACK; SELECT CAST(0 AS bit); RETURN;
 END;
 UPDATE dbo.LoginSessions SET RevokedAt=@now
 WHERE UserId=@UserId AND Id<>@SessionId AND RevokedAt IS NULL;
 UPDATE dbo.LoginSessions SET LastActivityAt=@now WHERE Id=@SessionId;
 COMMIT;
 SELECT CAST(1 AS bit);
END;
GO
GRANT EXECUTE ON dbo.usp_PasswordChangeState TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ChangeOwnPassword TO restaurant_app;
GO
