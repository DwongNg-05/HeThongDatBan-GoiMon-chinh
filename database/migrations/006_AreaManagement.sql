/*
    S1-06 - QUẢN LÝ KHU VỰC

    Bao gồm:
    1. Thêm khu vực
    2. Sửa khu vực
    3. Ngừng sử dụng
    4. Xóa khu vực nếu chưa có dữ liệu liên quan
    5. Không cho trùng tên sau chuẩn hóa
*/

GO

/* =========================================================
   1. CHUẨN HÓA TÊN
   ========================================================= */

CREATE OR ALTER FUNCTION dbo.fn_NormalizeAreaName
(
    @Name nvarchar(80)
)
RETURNS nvarchar(80)
AS
BEGIN

    DECLARE @Result nvarchar(80);

    SET @Result = ISNULL(@Name, N'');

    -- Đổi tab thành khoảng trắng
    SET @Result = REPLACE(@Result, NCHAR(9), N' ');

    -- Đổi xuống dòng thành khoảng trắng
    SET @Result = REPLACE(@Result, NCHAR(10), N' ');

    SET @Result = REPLACE(@Result, NCHAR(13), N' ');

    -- Xóa khoảng trắng đầu/cuối
    SET @Result = LTRIM(RTRIM(@Result));

    -- Gom nhiều khoảng trắng thành một
    WHILE CHARINDEX(N'  ', @Result) > 0
    BEGIN
        SET @Result = REPLACE(@Result, N'  ', N' ');
    END;

    RETURN @Result;

END;
GO


/* =========================================================
   2. LẤY DANH SÁCH KHU VỰC
   ========================================================= */

CREATE OR ALTER PROCEDURE dbo.usp_ListAreas
AS
BEGIN

    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        SortOrder,
        Notes,
        IsActive
    FROM dbo.Areas
    ORDER BY
        SortOrder,
        Name;

END;
GO


/* =========================================================
   3. LẤY 1 KHU VỰC
   ========================================================= */

CREATE OR ALTER PROCEDURE dbo.usp_GetArea
    @AreaId int
AS
BEGIN

    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        SortOrder,
        Notes,
        IsActive
    FROM dbo.Areas
    WHERE Id = @AreaId;

END;
GO


/* =========================================================
   4. THÊM KHU VỰC
   ========================================================= */

CREATE OR ALTER PROCEDURE dbo.usp_CreateArea
    @ActorUserId int,
    @Name nvarchar(80),
    @SortOrder int,
    @Notes nvarchar(500) = NULL
AS
BEGIN

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.usp_RequirePermission
        @ActorUserId = @ActorUserId,
        @Permission = 'Catalog.Manage';

    DECLARE @NormalizedName nvarchar(80);

    SET @NormalizedName =
        dbo.fn_NormalizeAreaName(@Name);

    IF LEN(@NormalizedName) = 0
    BEGIN
        THROW 51001,
            N'Tên khu vực không được để trống.',
            1;
    END;

    BEGIN TRANSACTION;

    EXEC dbo.usp_LockOperations;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Areas
        WHERE UPPER(dbo.fn_NormalizeAreaName(Name))
            COLLATE Vietnamese_100_CI_AI
            =
            UPPER(@NormalizedName)
            COLLATE Vietnamese_100_CI_AI
    )
    BEGIN
        ROLLBACK TRANSACTION;

        THROW 51002,
            N'Tên khu vực đã tồn tại.',
            1;
    END;

    INSERT INTO dbo.Areas
    (
        Name,
        SortOrder,
        Notes,
        IsActive
    )
    VALUES
    (
        @NormalizedName,
        @SortOrder,
        @Notes,
        1
    );

    COMMIT TRANSACTION;

END;
GO


/* =========================================================
   5. SỬA KHU VỰC
   ========================================================= */

CREATE OR ALTER PROCEDURE dbo.usp_UpdateArea
    @ActorUserId int,
    @AreaId int,
    @Name nvarchar(80),
    @SortOrder int,
    @Notes nvarchar(500) = NULL
AS
BEGIN

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.usp_RequirePermission
        @ActorUserId = @ActorUserId,
        @Permission = 'Catalog.Manage';

    DECLARE @NormalizedName nvarchar(80);

    SET @NormalizedName =
        dbo.fn_NormalizeAreaName(@Name);

    IF LEN(@NormalizedName) = 0
    BEGIN
        THROW 51003,
            N'Tên khu vực không được để trống.',
            1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Areas
        WHERE Id = @AreaId
    )
    BEGIN
        THROW 51004,
            N'Không tìm thấy khu vực.',
            1;
    END;

    BEGIN TRANSACTION;

    EXEC dbo.usp_LockOperations;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Areas
        WHERE Id <> @AreaId
          AND UPPER(dbo.fn_NormalizeAreaName(Name))
              COLLATE Vietnamese_100_CI_AI
              =
              UPPER(@NormalizedName)
              COLLATE Vietnamese_100_CI_AI
    )
    BEGIN
        ROLLBACK TRANSACTION;

        THROW 51005,
            N'Tên khu vực đã tồn tại.',
            1;
    END;

    UPDATE dbo.Areas
    SET
        Name = @NormalizedName,
        SortOrder = @SortOrder,
        Notes = @Notes
    WHERE Id = @AreaId;

    COMMIT TRANSACTION;

END;
GO


/* =========================================================
   6. NGỪNG SỬ DỤNG
   ========================================================= */

CREATE OR ALTER PROCEDURE dbo.usp_DeactivateArea
    @ActorUserId int,
    @AreaId int
AS
BEGIN

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.usp_RequirePermission
        @ActorUserId = @ActorUserId,
        @Permission = 'Catalog.Manage';

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Areas
        WHERE Id = @AreaId
    )
    BEGIN
        THROW 51006,
            N'Không tìm thấy khu vực.',
            1;
    END;

    UPDATE dbo.Areas
    SET IsActive = 0
    WHERE Id = @AreaId;

END;
GO


/* =========================================================
   7. XÓA KHU VỰC
   ========================================================= */

CREATE OR ALTER PROCEDURE dbo.usp_DeleteArea
    @ActorUserId int,
    @AreaId int
AS
BEGIN

    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.usp_RequirePermission
        @ActorUserId = @ActorUserId,
        @Permission = 'Catalog.Manage';

    IF EXISTS
    (
        SELECT 1
        FROM dbo.DiningTables
        WHERE AreaId = @AreaId
    )
    BEGIN
        THROW 51007,
            N'Khu vực đã có bàn nên không thể xóa. Vui lòng ngừng sử dụng khu vực.',
            1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Reservations
        WHERE PreferredAreaId = @AreaId
    )
    BEGIN
        THROW 51008,
            N'Khu vực đã được sử dụng trong lịch đặt bàn nên không thể xóa. Vui lòng ngừng sử dụng khu vực.',
            1;
    END;

    DELETE FROM dbo.Areas
    WHERE Id = @AreaId;

END;
GO
