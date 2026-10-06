-- Add temporary out state and audit history for existing databases.
IF COL_LENGTH('dbo.MenuItems','IsTemporarilyOut') IS NULL
    ALTER TABLE dbo.MenuItems ADD IsTemporarilyOut bit NOT NULL
        CONSTRAINT DF_MenuItems_IsTemporarilyOut DEFAULT 0;
GO

IF OBJECT_ID('dbo.MenuTemporaryOutEvents','U') IS NULL
BEGIN
    CREATE TABLE dbo.MenuTemporaryOutEvents (
        Id bigint IDENTITY PRIMARY KEY,
        MenuItemId int NOT NULL REFERENCES dbo.MenuItems(Id),
        OldIsTemporarilyOut bit NULL,
        IsTemporarilyOut bit NOT NULL,
        ChangedBy int NULL REFERENCES dbo.Users(Id),
        ChangedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID('dbo.MenuTemporaryOutEvents')
      AND name='IX_MenuTemporaryOutEvents_ItemChangedAt'
)
    CREATE INDEX IX_MenuTemporaryOutEvents_ItemChangedAt
        ON dbo.MenuTemporaryOutEvents(MenuItemId,ChangedAt DESC,Id DESC);
GO

CREATE OR ALTER PROCEDURE dbo.usp_SetMenuTemporarilyOut
    @MenuItemId int,
    @IsTemporarilyOut bit,
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId,'Menu.Availability';

        IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE Id=@MenuItemId)
            THROW 51040,N'Món không tồn tại.',1;

        DECLARE @old bit = (SELECT IsTemporarilyOut FROM dbo.MenuItems WHERE Id=@MenuItemId);
        IF @old<>@IsTemporarilyOut
        BEGIN
            UPDATE dbo.MenuItems
            SET IsTemporarilyOut=@IsTemporarilyOut,UpdatedAt=SYSUTCDATETIME()
            WHERE Id=@MenuItemId;
            INSERT dbo.MenuTemporaryOutEvents(MenuItemId,OldIsTemporarilyOut,IsTemporarilyOut,ChangedBy)
            VALUES(@MenuItemId,@old,@IsTemporarilyOut,@ActorUserId);
        END;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
