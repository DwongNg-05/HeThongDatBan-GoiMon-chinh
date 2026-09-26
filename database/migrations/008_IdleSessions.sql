CREATE TABLE dbo.LoginSessions (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 UserId int NOT NULL REFERENCES dbo.Users(Id),
 CreatedAt datetime2(3) NOT NULL,
 LastActivityAt datetime2(3) NOT NULL,
 ExpiresAt datetime2(3) NOT NULL,
 RevokedAt datetime2(3) NULL,
 CHECK(ExpiresAt>CreatedAt)
);
CREATE INDEX IX_LoginSessions_User ON dbo.LoginSessions(UserId,RevokedAt);
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateLoginSession @Id uniqueidentifier,@UserId int
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @now datetime2(3)=SYSUTCDATETIME();
 INSERT dbo.LoginSessions(Id,UserId,CreatedAt,LastActivityAt,ExpiresAt)
 SELECT @Id,Id,@now,@now,DATEADD(day,14,@now) FROM dbo.Users WHERE Id=@UserId AND IsActive=1;
 IF @@ROWCOUNT=0 THROW 51204,N'Cannot create session for an inactive account.',1;
END;
GO
-- Validate on every authenticated request. Touch only after a valid protected action.
CREATE OR ALTER PROCEDURE dbo.usp_CheckLoginSession
 @Id uniqueidentifier,@UserId int,@Touch bit=0
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 BEGIN TRAN;
 DECLARE @last datetime2(3),@expires datetime2(3),@revoked datetime2(3),@now datetime2(3),@valid bit=0;
 SELECT @last=LastActivityAt,@expires=ExpiresAt,@revoked=RevokedAt
 FROM dbo.LoginSessions WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id AND UserId=@UserId;
 SET @now=SYSUTCDATETIME();
 IF @last>DATEADD(minute,-30,@now) AND @expires>@now AND @revoked IS NULL
    AND EXISTS(SELECT 1 FROM dbo.Users WHERE Id=@UserId AND IsActive=1)
 BEGIN
  SET @valid=1;
  IF @Touch=1 UPDATE dbo.LoginSessions SET LastActivityAt=@now WHERE Id=@Id AND UserId=@UserId;
 END
 ELSE
  UPDATE dbo.LoginSessions SET RevokedAt=COALESCE(RevokedAt,@now) WHERE Id=@Id AND UserId=@UserId;
 COMMIT;
 SELECT @valid;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_RevokeLoginSession @Id uniqueidentifier,@UserId int
AS
BEGIN
 SET NOCOUNT ON;
 UPDATE dbo.LoginSessions SET RevokedAt=COALESCE(RevokedAt,SYSUTCDATETIME()) WHERE Id=@Id AND UserId=@UserId;
END;
GO
GRANT EXECUTE ON dbo.usp_CreateLoginSession TO restaurant_app;
GRANT EXECUTE ON dbo.usp_CheckLoginSession TO restaurant_app;
GRANT EXECUTE ON dbo.usp_RevokeLoginSession TO restaurant_app;
GO
