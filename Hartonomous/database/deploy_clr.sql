USE HartonomousDB;
GO

-- Enable CLR integration
EXEC sp_configure 'clr enabled', 1;
RECONFIGURE;
GO

-- Set database as trustworthy for CLR assemblies
ALTER DATABASE HartonomousDB SET TRUSTWORTHY ON;
GO

-- Drop existing objects if they exist
IF OBJECT_ID('dbo.usp_SaveComponentWeight') IS NOT NULL
    DROP PROCEDURE dbo.usp_SaveComponentWeight;

IF EXISTS (SELECT * FROM sys.assemblies WHERE name = 'HartonomousDatabaseAssembly')
    DROP ASSEMBLY HartonomousDatabaseAssembly;
GO

-- Create CLR assembly from the compiled DLL
-- Note: Update this path to match your actual project build output
CREATE ASSEMBLY HartonomousDatabaseAssembly
FROM 'E:\projects\Claude\002\Hartonomous\src\Database\Hartonomous.Database\bin\Debug\net48\Hartonomous.Database.dll'
WITH PERMISSION_SET = UNSAFE;
GO

-- Create the CLR stored procedure
CREATE PROCEDURE dbo.usp_SaveComponentWeight
    @componentId UNIQUEIDENTIFIER,
    @data VARBINARY(MAX)
AS EXTERNAL NAME HartonomousDatabaseAssembly.[FileStreamManager].SaveComponentWeight;
GO

-- Model Query Engine functions for neural map capabilities
CREATE FUNCTION dbo.QueryModelBytes(@componentId UNIQUEIDENTIFIER, @offset BIGINT, @length INT)
RETURNS VARBINARY(MAX) AS EXTERNAL NAME HartonomousDatabaseAssembly.[ModelQueryEngine].QueryModelBytes;
GO

CREATE FUNCTION dbo.FindPatternInWeights(@componentId UNIQUEIDENTIFIER, @pattern VARBINARY(MAX), @tolerance FLOAT)
RETURNS BIT AS EXTERNAL NAME HartonomousDatabaseAssembly.[ModelQueryEngine].FindPatternInWeights;
GO

CREATE FUNCTION dbo.GetModelStats(@componentId UNIQUEIDENTIFIER)
RETURNS NVARCHAR(MAX) AS EXTERNAL NAME HartonomousDatabaseAssembly.[ModelQueryEngine].GetModelStats;
GO