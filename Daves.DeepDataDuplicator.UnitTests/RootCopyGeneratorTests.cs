using Daves.DeepDataDuplicator.Metadata;
using Daves.DeepDataDuplicator.UnitTests.SampleCatalogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace Daves.DeepDataDuplicator.UnitTests
{
    [TestClass]
    public class RootCopyGeneratorTests
    {
        [TestMethod]
        public void GenerateProcedure_Default_ForRootedWorld()
        {
            string procedure = RootCopyGenerator.GenerateProcedure(
                catalog: RootedWorld.Catalog,
                rootTable: RootedWorld.NationsTable);

            Assert.AreEqual(
@"CREATE PROCEDURE [dbo].[CopyNation]
    @id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;

    DECLARE @NationIDPairs TABLE (
        ExistingID INT NOT NULL UNIQUE,
        InsertedID INT NOT NULL UNIQUE
    );

    DECLARE @ProvinceIDPairs TABLE (
        ExistingID INT NOT NULL UNIQUE,
        InsertedID INT NOT NULL UNIQUE
    );

    MERGE INTO [dbo].[Nations] AS Target
    USING (
        SELECT *
        FROM [dbo].[Nations]
        WHERE [ID] = @id
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [Name],
        [FoundedDate])
    VALUES (
        Source.[Name],
        Source.[FoundedDate])
    OUTPUT Source.[ID], Inserted.[ID]
    INTO @NationIDPairs;

    MERGE INTO [dbo].[Provinces] AS Target
    USING (
        SELECT
            copy.*,
            j0.InsertedID j0InsertedID
        FROM [dbo].[Provinces] copy
        JOIN @NationIDPairs j0
            ON copy.[NationID] = j0.ExistingID
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [NationID],
        [Name],
        [Motto])
    VALUES (
        j0InsertedID,
        Source.[Name],
        Source.[Motto])
    OUTPUT Source.[ID], Inserted.[ID]
    INTO @ProvinceIDPairs;

    MERGE INTO [dbo].[Residents] AS Target
    USING (
        SELECT
            copy.*,
            j0.InsertedID j0InsertedID
        FROM [dbo].[Residents] copy
        JOIN @ProvinceIDPairs j0
            ON copy.[ProvinceID] = j0.ExistingID
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [ProvinceID],
        [Name])
    VALUES (
        j0InsertedID,
        Source.[Name]);

    COMMIT TRAN;
END;", procedure);
        }

        [TestMethod]
        public void GenerateProcedure_Customized_ForUnrootedWorld()
        {
            // Note it doesn't really make sense to use root copy on the unrooted world. Compare to the corresponding deep copy test.
            string procedure = RootCopyGenerator.GenerateProcedure(
                catalog: UnrootedWorld.Catalog,
                rootTable: UnrootedWorld.NationsTable,
                procedureName: "RootCopyNation",
                primaryKeyOutputParameterName: "insertedID");

            Assert.AreEqual(
@"CREATE PROCEDURE [dbo].[RootCopyNation]
    @id INT,
    @insertedID INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;

    DECLARE @NationIDPairs TABLE (
        ExistingID INT NOT NULL UNIQUE,
        InsertedID INT NOT NULL UNIQUE
    );

    DECLARE @ProvinceIDPairs TABLE (
        ExistingID INT NOT NULL UNIQUE,
        InsertedID INT NOT NULL UNIQUE
    );

    DECLARE @ResidentIDPairs TABLE (
        ExistingID INT NOT NULL UNIQUE,
        InsertedID INT NOT NULL UNIQUE
    );

    MERGE INTO [dbo].[Nations] AS Target
    USING (
        SELECT *
        FROM [dbo].[Nations]
        WHERE [ID] = @id
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [Name],
        [FoundedDate])
    VALUES (
        Source.[Name],
        Source.[FoundedDate])
    OUTPUT Source.[ID], Inserted.[ID]
    INTO @NationIDPairs;
    SET @insertedID = SCOPE_IDENTITY();

    MERGE INTO [dbo].[Provinces] AS Target
    USING (
        SELECT
            copy.*,
            j0.InsertedID j0InsertedID
        FROM [dbo].[Provinces] copy
        JOIN @NationIDPairs j0
            ON copy.[NationID] = j0.ExistingID
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [NationID],
        [Name],
        [Motto],
        [LeaderResidentID])
    VALUES (
        j0InsertedID,
        Source.[Name],
        Source.[Motto],
        Source.[LeaderResidentID])
    OUTPUT Source.[ID], Inserted.[ID]
    INTO @ProvinceIDPairs;

    MERGE INTO [dbo].[Residents] AS Target
    USING (
        SELECT
            copy.*,
            j0.InsertedID j0InsertedID,
            j1.InsertedID j1InsertedID
        FROM [dbo].[Residents] copy
        JOIN @ProvinceIDPairs j0
            ON copy.[ProvinceID] = j0.ExistingID
        JOIN @NationIDPairs j1
            ON copy.[NationalityNationID] = j1.ExistingID
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [ProvinceID],
        [NationalityNationID],
        [Name],
        [SpouseResidentID],
        [FavoriteProvinceID])
    VALUES (
        j0InsertedID,
        j1InsertedID,
        Source.[Name],
        Source.[SpouseResidentID],
        Source.[FavoriteProvinceID])
    OUTPUT Source.[ID], Inserted.[ID]
    INTO @ResidentIDPairs;

    UPDATE copy
    SET
        copy.[LeaderResidentID] = j0.InsertedID
    FROM [dbo].[Provinces] copy
    JOIN @ResidentIDPairs j0
        ON copy.[LeaderResidentID] = j0.ExistingID
    WHERE copy.[ID] IN (SELECT InsertedID FROM @ProvinceIDPairs);

    UPDATE copy
    SET
        copy.[SpouseResidentID] = COALESCE(j0.InsertedID, copy.[SpouseResidentID]),
        copy.[FavoriteProvinceID] = COALESCE(j1.InsertedID, copy.[FavoriteProvinceID])
    FROM [dbo].[Residents] copy
    LEFT JOIN @ResidentIDPairs j0
        ON copy.[SpouseResidentID] = j0.ExistingID
    LEFT JOIN @ProvinceIDPairs j1
        ON copy.[FavoriteProvinceID] = j1.ExistingID
    WHERE copy.[ID] IN (SELECT InsertedID FROM @ResidentIDPairs);

    COMMIT TRAN;
END;", procedure);
        }

        [TestMethod]
        public void GenerateProcedure_Scoped_ForRootedWorld()
        {
            // Nations is the top-level root, but we can also treat provinces as a root, leaving the nations table unaffected.
            string procedure = RootCopyGenerator.GenerateProcedure(
                catalog: RootedWorld.Catalog,
                rootTable: RootedWorld.ProvincesTable);

            Assert.AreEqual(
@"CREATE PROCEDURE [dbo].[CopyProvince]
    @id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;

    DECLARE @ProvinceIDPairs TABLE (
        ExistingID INT NOT NULL UNIQUE,
        InsertedID INT NOT NULL UNIQUE
    );

    MERGE INTO [dbo].[Provinces] AS Target
    USING (
        SELECT *
        FROM [dbo].[Provinces]
        WHERE [ID] = @id
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [NationID],
        [Name],
        [Motto])
    VALUES (
        Source.[NationID],
        Source.[Name],
        Source.[Motto])
    OUTPUT Source.[ID], Inserted.[ID]
    INTO @ProvinceIDPairs;

    MERGE INTO [dbo].[Residents] AS Target
    USING (
        SELECT
            copy.*,
            j0.InsertedID j0InsertedID
        FROM [dbo].[Residents] copy
        JOIN @ProvinceIDPairs j0
            ON copy.[ProvinceID] = j0.ExistingID
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [ProvinceID],
        [Name])
    VALUES (
        j0InsertedID,
        Source.[Name]);

    COMMIT TRAN;
END;", procedure);
        }

        [TestMethod]
        public void GenerateProcedureBody_Customized_ForRootedWorld()
        {
            var updateParameterNames = new Dictionary<Column, string>
            {
                { RootedWorld.Catalog.FindColumn("Provinces", "Motto"), "toMotto" },
                { RootedWorld.Catalog.FindColumn("Provinces", "Name"), "@toName" },
            };

            string procedure = RootCopyGenerator.GenerateProcedureBody(
                catalog: RootedWorld.Catalog,
                rootTable: RootedWorld.NationsTable,
                primaryKeyParameterName: "existingNationID",
                primaryKeyOutputParameterName: "insertedNationID",
                updateParameterNames: updateParameterNames);

            Assert.AreEqual(@"
    DECLARE @NationIDPairs TABLE (
        ExistingID INT NOT NULL UNIQUE,
        InsertedID INT NOT NULL UNIQUE
    );

    DECLARE @ProvinceIDPairs TABLE (
        ExistingID INT NOT NULL UNIQUE,
        InsertedID INT NOT NULL UNIQUE
    );

    MERGE INTO [dbo].[Nations] AS Target
    USING (
        SELECT *
        FROM [dbo].[Nations]
        WHERE [ID] = @existingNationID
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [Name],
        [FoundedDate])
    VALUES (
        Source.[Name],
        Source.[FoundedDate])
    OUTPUT Source.[ID], Inserted.[ID]
    INTO @NationIDPairs;
    SET @insertedNationID = SCOPE_IDENTITY();

    MERGE INTO [dbo].[Provinces] AS Target
    USING (
        SELECT
            copy.*,
            j0.InsertedID j0InsertedID
        FROM [dbo].[Provinces] copy
        JOIN @NationIDPairs j0
            ON copy.[NationID] = j0.ExistingID
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [NationID],
        [Name],
        [Motto])
    VALUES (
        j0InsertedID,
        @toName,
        @toMotto)
    OUTPUT Source.[ID], Inserted.[ID]
    INTO @ProvinceIDPairs;

    MERGE INTO [dbo].[Residents] AS Target
    USING (
        SELECT
            copy.*,
            j0.InsertedID j0InsertedID
        FROM [dbo].[Residents] copy
        JOIN @ProvinceIDPairs j0
            ON copy.[ProvinceID] = j0.ExistingID
    ) AS Source
    ON 1 = 0
    WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        [ProvinceID],
        [Name])
    VALUES (
        j0InsertedID,
        Source.[Name]);
", procedure);
        }
    }
}
