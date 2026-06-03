-- =============================================
-- TEXT TO SQL DEMO DATABASE SETUP
-- =============================================



-- STEP 1: CREATE DATABASE
IF NOT EXISTS (
    SELECT name 
    FROM sys.databases 
    WHERE name = 'TextToSqlDemo'
)
BEGIN
    CREATE DATABASE TextToSqlDemo
END
GO



-- STEP 2: USE DATABASE
USE TextToSqlDemo
GO



-- =============================================
-- STEP 3: DROP TABLES IF EXIST
-- =============================================



IF OBJECT_ID('Orders', 'U') IS NOT NULL
    DROP TABLE Orders
GO



IF OBJECT_ID('Customers', 'U') IS NOT NULL
    DROP TABLE Customers
GO



-- =============================================
-- STEP 4: CREATE CUSTOMERS TABLE
-- =============================================



CREATE TABLE Customers
(
    Id INT PRIMARY KEY IDENTITY(1,1),



    Name NVARCHAR(100) NOT NULL,



    Country NVARCHAR(50) NOT NULL,



    CreatedDate DATETIME NOT NULL
)
GO



-- =============================================
-- STEP 5: CREATE ORDERS TABLE
-- =============================================



CREATE TABLE Orders
(
    Id INT PRIMARY KEY IDENTITY(1,1),



    CustomerId INT NOT NULL,



    Amount DECIMAL(18,2) NOT NULL,



    OrderDate DATETIME NOT NULL,



    CONSTRAINT FK_Orders_Customers
    FOREIGN KEY (CustomerId)
    REFERENCES Customers(Id)
)
GO



-- =============================================
-- STEP 6: INSERT SAMPLE CUSTOMERS
-- =============================================



INSERT INTO Customers
(
    Name,
    Country,
    CreatedDate
)
VALUES
('Ankit', 'India', GETDATE()),
('Raj', 'India', DATEADD(DAY, -10, GETDATE())),
('Amit', 'India', DATEADD(DAY, -40, GETDATE())),
('John', 'USA', DATEADD(DAY, -20, GETDATE())),
('David', 'USA', DATEADD(DAY, -30, GETDATE())),
('Sara', 'UK', DATEADD(DAY, -50, GETDATE())),
('Emma', 'Canada', DATEADD(DAY, -60, GETDATE())),
('Michael', 'Germany', DATEADD(DAY, -70, GETDATE())),
('Priya', 'India', DATEADD(DAY, -15, GETDATE())),
('Robert', 'Australia', DATEADD(DAY, -25, GETDATE()))
GO



-- =============================================
-- STEP 7: INSERT SAMPLE ORDERS
-- =============================================



INSERT INTO Orders
(
    CustomerId,
    Amount,
    OrderDate
)
VALUES
(1, 1200.00, GETDATE()),
(1, 800.00, DATEADD(DAY, -2, GETDATE())),
(2, 500.00, DATEADD(DAY, -5, GETDATE())),
(3, 1500.00, DATEADD(DAY, -20, GETDATE())),
(4, 2200.00, DATEADD(DAY, -7, GETDATE())),
(5, 700.00, DATEADD(DAY, -15, GETDATE())),
(6, 950.00, DATEADD(DAY, -8, GETDATE())),
(7, 3000.00, DATEADD(DAY, -3, GETDATE())),
(8, 400.00, DATEADD(DAY, -12, GETDATE())),
(9, 2500.00, DATEADD(DAY, -1, GETDATE())),
(10, 1800.00, DATEADD(DAY, -6, GETDATE())),
(2, 650.00, DATEADD(DAY, -18, GETDATE())),
(3, 1100.00, DATEADD(DAY, -22, GETDATE())),
(5, 2100.00, DATEADD(DAY, -11, GETDATE()))
GO



-- =============================================
-- STEP 8: VERIFY DATA
-- =============================================



PRINT '============================================='
PRINT 'CUSTOMERS DATA'
PRINT '============================================='



SELECT * 
FROM Customers
ORDER BY Id



PRINT '============================================='
PRINT 'ORDERS DATA'
PRINT '============================================='



SELECT * 
FROM Orders
ORDER BY Id



-- =============================================
-- STEP 9: SAMPLE TEST QUERIES
-- =============================================



PRINT '============================================='
PRINT 'TOTAL CUSTOMERS BY COUNTRY'
PRINT '============================================='



SELECT 
    Country,
    COUNT(*) AS TotalCustomers
FROM Customers
GROUP BY Country
ORDER BY TotalCustomers DESC



PRINT '============================================='
PRINT 'TOP CUSTOMERS BY ORDER AMOUNT'
PRINT '============================================='



SELECT TOP 5
    C.Name,
    C.Country,
    SUM(O.Amount) AS TotalOrderAmount
FROM Customers C
INNER JOIN Orders O
    ON C.Id = O.CustomerId
GROUP BY
    C.Name,
    C.Country
ORDER BY TotalOrderAmount DESC



PRINT '============================================='
PRINT 'DATABASE SETUP COMPLETED SUCCESSFULLY'
PRINT '============================================='