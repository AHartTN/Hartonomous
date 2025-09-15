\-- Hartonomous Database Setup Script v1.0  
\-- This script is designed to be idempotent and safe to run multiple times.

\-- Create the database if it doesn't exist  
IF NOT EXISTS (SELECT \* FROM sys.databases WHERE name \= 'Hartonomous')  
BEGIN  
    CREATE DATABASE Hartonomous;  
END  
GO

USE Hartonomous;  
GO

\-- Create a dedicated login and user for the application to avoid using 'sa'  
IF NOT EXISTS (SELECT \* FROM sys.server\_principals WHERE name \= '${APP\_DB\_USER}')  
BEGIN  
    CREATE LOGIN ${APP\_DB\_USER} WITH PASSWORD \= '${APP\_DB\_PASSWORD}';  
END  
GO

IF NOT EXISTS (SELECT \* FROM sys.database\_principals WHERE name \= '${APP\_DB\_USER}')  
BEGIN  
    CREATE USER ${APP\_DB\_USER} FOR LOGIN ${APP\_DB\_USER};  
END  
GO

\-- Grant necessary permissions  
ALTER ROLE db\_owner ADD MEMBER ${APP\_DB\_USER};  
GO

\-- Enable CDC on the database  
IF (SELECT is\_cdc\_enabled FROM sys.databases WHERE name \= 'Hartonomous') \= 0  
BEGIN  
    EXEC sys.sp\_cdc\_enable\_db;  
END  
GO

\-- Create the Projects table if it doesn't exist  
IF NOT EXISTS (SELECT \* FROM sysobjects WHERE name='Projects' and xtype='U')  
BEGIN  
    CREATE TABLE dbo.Projects (  
        ProjectID UNIQUEIDENTIFIER PRIMARY KEY,  
        ProjectName NVARCHAR(255) NOT NULL,  
        UserPrompt NVARCHAR(MAX) NOT NULL,  
        Status NVARCHAR(50) NOT NULL,  
        CreatedAt DATETIME2 DEFAULT GETUTCDATE()  
    );  
END  
GO

\-- Enable CDC on the Projects table if not already enabled  
IF (SELECT is\_tracked\_by\_cdc FROM sys.tables WHERE name \= 'Projects') \= 0  
BEGIN  
    EXEC sys.sp\_cdc\_enable\_table  
        @source\_schema \= N'dbo',  
        @source\_name   \= N'Projects',  
        @role\_name     \= NULL,  
        @supports\_net\_changes \= 0;  
END  
GO

