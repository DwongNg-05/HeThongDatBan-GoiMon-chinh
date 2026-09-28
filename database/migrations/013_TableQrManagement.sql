-- S1-07: public table-QR token support.
-- The stored hash remains the authorization secret; PublicToken is the value encoded in the QR.
IF COL_LENGTH('dbo.TableQrCodes', 'PublicToken') IS NULL
BEGIN
    ALTER TABLE dbo.TableQrCodes ADD PublicToken varchar(64) NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.TableQrCodes')
      AND name = 'CK_TableQrCodes_PublicToken'
)
BEGIN
    ALTER TABLE dbo.TableQrCodes WITH CHECK
    ADD CONSTRAINT CK_TableQrCodes_PublicToken CHECK
    (
        PublicToken IS NULL
        OR
        (
            LEN(PublicToken) BETWEEN 16 AND 64
            AND DATALENGTH(PublicToken) = LEN(PublicToken)
            AND PublicToken COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Za-z0-9_-]%'
        )
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.TableQrCodes')
      AND name = 'UX_TableQrCodes_PublicToken'
)
BEGIN
    CREATE UNIQUE INDEX UX_TableQrCodes_PublicToken
        ON dbo.TableQrCodes(PublicToken)
        WHERE PublicToken IS NOT NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_RotateTableQr
    @TableId int,
    @TokenHash binary(32),
    @ActorUserId int,
    @PublicToken varchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Catalog.Manage';

        IF @PublicToken IS NOT NULL
        BEGIN
            IF LEN(@PublicToken) NOT BETWEEN 16 AND 64
               OR DATALENGTH(@PublicToken) <> LEN(@PublicToken)
               OR @PublicToken COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Za-z0-9_-]%'
                THROW 51031, N'Mã QR công khai không hợp lệ.', 1;

            IF @TokenHash IS NULL
               OR HASHBYTES('SHA2_256', CONVERT(varbinary(64), @PublicToken)) <> @TokenHash
                THROW 51032, N'Mã hash QR không khớp với mã QR công khai.', 1;
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE Id = @TableId AND IsActive = 1)
            THROW 51021, N'Bàn không hoạt động.', 1;

        -- These statements and the insert are one transaction. A failed insert (for example,
        -- an accidental token collision) rolls back the revocations as well.
        UPDATE gs
        SET RevokedAt = SYSUTCDATETIME()
        FROM dbo.GuestSessions gs
        JOIN dbo.TableQrCodes q ON q.Id = gs.TableQrCodeId
        WHERE q.TableId = @TableId AND gs.RevokedAt IS NULL;

        UPDATE dbo.TableQrCodes
        SET RevokedAt = SYSUTCDATETIME()
        WHERE TableId = @TableId AND RevokedAt IS NULL;

        INSERT dbo.TableQrCodes(TableId, TokenHash, PublicToken, CreatedBy)
        VALUES(@TableId, @TokenHash, @PublicToken, @ActorUserId);

        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
