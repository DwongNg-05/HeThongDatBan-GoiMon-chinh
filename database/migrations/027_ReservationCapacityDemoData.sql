-- S2-02 Task 5: keep one development table that can demonstrate the 20-guest boundary.
IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE IsActive=1 AND MaxCapacity>=20)
BEGIN
    DECLARE @areaId int = (SELECT Id FROM dbo.Areas WHERE NormalizedName=N'TRONG NHÀ');
    IF @areaId IS NULL
    BEGIN
        INSERT dbo.Areas(Name,SortOrder,IsActive) VALUES(N'Trong nhà',10,1);
        SET @areaId = SCOPE_IDENTITY();
    END;
    IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE Code='DEMO-20')
        INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,TableType,Status,IsActive,SortOrder)
        VALUES(@areaId,'DEMO-20',1,20,'Standard','Available',1,999);
END;
GO
