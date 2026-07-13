# Umbraco Omni - EF Core Persistence Migration & Handover Summary

This document serves as a comprehensive technical summary of the database layer migration for Umbraco Omni. It outlines the design patterns, codebase modifications, dynamic reflection bridges, test suites, and hotfixes implemented to transition from a SQL Server-centric NPoco persistence architecture to a database-agnostic **Entity Framework Core (EF Core)** persistency model.

It is designed to give developers and subsequent AI agents immediate context to understand, maintain, or extend this architecture.

---

## 1. Technical Context & Migration Objectives

The persistence layer of Umbraco Omni has been migrated to support **five major Relational Database Management Systems (RDBMS)** from a single codebase:
1. **Microsoft SQL Server (MSSQL)**
2. **PostgreSQL**
3. **SQLite**
4. **MySQL (Pomelo)**
5. **Oracle**

To achieve this without bloating the core binaries or creating tight compile-time coupling with proprietary database drivers, the architecture utilizes **Dependency Injection, lazy options resolution, and Reflection-based dynamic assembly loading** at runtime.

---

## 2. Core Architectural Components

### A. Dynamic Database Selection Layer (DI)
- **Class/Extension:** [UmbracoFlexibleDataStoresExtensions.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/Extensions/UmbracoFlexibleDataStoresExtensions.cs)
- **Role:** Reads `ConnectionStrings` at startup, validates the database provider, and registers `UmbracoDbContext` dynamically. PostgreSQL, MySQL, and Oracle EF Core drivers are loaded via reflection using their respective assembly signatures.

### B. Generic Repository & Unit of Work (EF Core)
- **Interfaces & Implementations:** 
  - [IEfRepository<TEntity>](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/Repositories/IEfRepository.cs) / [EfRepository<TEntity>](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/Repositories/EfRepository.cs)
  - [IEfUnitOfWork](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/UnitOfWork/IEfUnitOfWork.cs) / [EfUnitOfWork](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/UnitOfWork/EfUnitOfWork.cs)
- **Role:** Decouples business logic from EF Core's concrete `DbContext` class. It manages entity lifecycles and handles database transaction orchestrations (`BeginTransactionAsync`, `CommitTransactionAsync`, `RollbackTransactionAsync`) agnostically.

### C. Dynamic NPoco-to-EF Core Reflection Bridge
- **Class/Method:** [UmbracoDbContext.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/UmbracoDbContext.cs) -> `OnModelCreating`
- **Role:** Automates column/table translations. Instead of statically configuring database schemas for hundreds of legacy DTO classes, a yansıma (reflection) loop runs dynamically during model creation to:
  1. **Map Custom Columns:** Resolves NPoco `[Column("columnName")]` properties and binds them as EF Core column names.
  2. **Map Custom Tables:** Resolves NPoco `[TableName("tableName")]` attributes and configures table mappings.
  3. **Ignore Non-Database Properties:** Finds properties decorated with NPoco `[ResultColumn]` or `[Ignore]` attributes (such as `UserCount` in `UserGroupDto`) and instructs EF Core to ignore them to prevent mapping or insertion errors.
  4. **Normalize Schema Names:** Converts all identifiers (tables, columns, indexes, foreign keys) to lowercase `snake_case` if running PostgreSQL, and deterministically truncates long constraint/index names to a 30-character limit (Oracle) or a 64-character limit (MySQL) using hash-based suffixes to prevent naming collision.

### D. High-Performance Querying Service
- **Class:** [UmbracoHierarchyQueryService.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/Services/UmbracoHierarchyQueryService.cs)
- **Role:** Provides optimized data access for highly repetitive, hierarchical content requests. Employs `EF.CompileAsyncQuery` to cache C# expression trees, `.AsNoTracking()` to bypass EF Core tracker overhead, and `.AsSplitQuery()` to prevent Cartesian product explosions during relational JOINs.

---

## 3. Handover/Verification Test Suite

- **Location:** [UmbracoOmniDatabaseTests.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/tests/Umbraco.Tests.Integration/Umbraco.Persistence.EFCore/DbContext/UmbracoOmniDatabaseTests.cs)
- **Design:**
  - Standardized NUnit database integration test checking user persistence, grouping, and retrieval.
  - Registers a custom `TestMigrationProviderSetup` that handles dynamic sqlite/postgresql assembly configurations.
  - Executes real database CRUD transactions under Unit of Work orchestration and queries the results using the compiled query service.

### Verification Commands
To compile and execute this test suite, run the following commands:
```bash
# Build the test assembly
dotnet build tests/Umbraco.Tests.Integration/Umbraco.Tests.Integration.csproj

# Run database integration tests
dotnet test tests/Umbraco.Tests.Integration/Umbraco.Tests.Integration.csproj --filter "FullyQualifiedName=Umbraco.Cms.Tests.Integration.Umbraco.Persistence.EFCore.DbContext.UmbracoOmniDatabaseTests"
```

---

## 4. Boot-time Connection Validation Hotfix

### The Problem
During Umbraco's boot sequence, `UmbracoDatabaseFactory` invokes `DbConnectionExtensions.IsConnectionAvailable` to verify connectivity. Because the dependency injection context (`StaticServiceProvider.Instance`) is not yet initialized at early boot, it was impossible to resolve the configured database provider from `appsettings.json`. As a result, the code defaulted to SQL Server's `SqlClientFactory` and crashed with `System.ArgumentException: Keyword not supported: 'host'` when parsing a PostgreSQL connection string.

### The Solution
We refactored `IsConnectionAvailable` inside [DbConnectionExtensions.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Infrastructure/Persistence/DbConnectionExtensions.cs) to be completely independent of configuration and DI services. It parses keywords inside the `connectionString` argument directly to resolve the target database engine:
- `host=` and `port=` -> PostgreSQL (`NpgsqlFactory.Instance`)
- `data source=` with `.db`/`.sqlite` -> SQLite (`SqliteFactory.Instance`)
- `server=` and `port=` with `uid=`/`user=` -> MySQL (`MySqlConnectorFactory.Instance` / `MySqlClientFactory.Instance`)
- `oracle` or specific data sources -> Oracle (`OracleClientFactory.Instance`)

This resolved all boot-time connection exceptions and enables the application to boot cleanly.

---

## 5. NPoco Legacy Layer Bypass for Non-SqlServer/Sqlite Providers

### The Problem
Umbraco's boot process initializes `UmbracoDatabaseFactory`, which uses the legacy NPoco ORM layer. This layer has hardcoded provider registrations (`ISqlSyntaxProvider`, `IBulkSqlInsertProvider`, `IDatabaseCreator`) keyed to **only** `"Microsoft.Data.SqlClient"` and `"Microsoft.Data.Sqlite"` provider names. When a PostgreSQL, MySQL, or Oracle provider name is configured, three critical failures occur:
1. `DbProviderFactoryCreator.GetSqlSyntaxProvider("PostgreSQL")` → `InvalidOperationException` (unknown provider key).
2. `DbProviderFactories.GetFactory("PostgreSQL")` → `ArgumentException` (unregistered factory).
3. `SqlServerSyntaxProvider.GetUpdatedDatabaseType()` → Tries to open a `Microsoft.Data.SqlClient.SqlConnection` with a PostgreSQL connection string containing `host=` → `ArgumentException: Keyword not supported: 'host'`.

### The Solution (Multi-layered)

#### A. `appsettings.json` ProviderName Key Fix
- **File:** [appsettings.json](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Web.UI/appsettings.json)
- **Change:** Renamed `"ProviderName"` to `"umbracoDbDSN_ProviderName"` to match Umbraco's `_ProviderName` postfix convention (`ConfigurationExtensions.ProviderNamePostfix`).
- **Impact:** Without this fix, `ConfigureConnectionStrings.Configure()` cannot find the provider name and defaults to `"Microsoft.Data.SqlClient"`.

#### B. `ConfigureConnectionStrings` Auto-Detection
- **File:** [ConfigureConnectionStrings.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Core/Configuration/ConfigureConnectionStrings.cs)
- **Change:** Added connection string pattern analysis as a fallback when `providerName` is null. If the connection string contains `host=`, `port=`, and `username=`/`password=`, it is auto-detected as `"PostgreSQL"`.
- **Impact:** Safety net — even if `_ProviderName` key is missing, PostgreSQL connections are correctly identified.

#### C. `UmbracoDatabaseFactory.Initialize()` Graceful Degradation
- **File:** [UmbracoDatabaseFactory.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Infrastructure/Persistence/UmbracoDatabaseFactory.cs)
- **Changes:**
  1. `DbProviderFactory` property: Added `try/catch` around `_dbProviderFactoryCreator.CreateFactory()` with a reflection-based fallback (`ResolveProviderFactoryByReflection`) that resolves `NpgsqlFactory`, `MySqlConnectorFactory`, or `OracleClientFactory` at runtime.
  2. `Initialize()` method: Wrapped `GetSqlSyntaxProvider()` and `CreateBulkSqlInsertProvider()` calls in `try/catch` blocks. When the provider is unrecognized, it falls back to SQLite's syntax provider (least SQL Server-specific) and skips `GetUpdatedDatabaseType()` entirely to prevent connection string incompatibility errors.
- **Impact:** The legacy NPoco layer initializes without crashing for any provider, while EF Core handles actual database operations.

#### D. `SqlServerSyntaxProvider.GetUpdatedDatabaseType()` Guard
- **File:** [SqlServerSyntaxProvider.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.SqlServer/Services/SqlServerSyntaxProvider.cs)
- **Change:** Added connection string pattern analysis at the top of `GetUpdatedDatabaseType()`. If the connection string matches PostgreSQL, SQLite, or MySQL patterns, the method returns immediately without attempting to open a `SqlConnection`.
- **Impact:** Even if the SqlServer syntax provider is selected as a fallback, it won't crash when encountering non-SqlServer connection strings.

---

## 6. File Location Reference Directory

| Technical Layer | Primary Source File Path |
| :--- | :--- |
| **Flexible DB Selector** | [UmbracoFlexibleDataStoresExtensions.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/Extensions/UmbracoFlexibleDataStoresExtensions.cs) |
| **Model Configurator** | [UmbracoDbContext.OnModelCreating](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/UmbracoDbContext.cs) |
| **Unit of Work** | [EfUnitOfWork.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/UnitOfWork/EfUnitOfWork.cs) |
| **Generic Repository** | [EfRepository.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/Repositories/EfRepository.cs) |
| **Compiled Query Service**| [UmbracoHierarchyQueryService.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.EFCore/Services/UmbracoHierarchyQueryService.cs) |
| **Integration Test Suite**| [UmbracoOmniDatabaseTests.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/tests/Umbraco.Tests.Integration/Umbraco.Persistence.EFCore/DbContext/UmbracoOmniDatabaseTests.cs) |
| **Connection Hotfix** | [DbConnectionExtensions.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Infrastructure/Persistence/DbConnectionExtensions.cs) |
| **NPoco Bypass** | [UmbracoDatabaseFactory.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Infrastructure/Persistence/UmbracoDatabaseFactory.cs) |
| **SqlServer Guard** | [SqlServerSyntaxProvider.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Cms.Persistence.SqlServer/Services/SqlServerSyntaxProvider.cs) |
| **Provider Config** | [ConfigureConnectionStrings.cs](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/src/Umbraco.Core/Configuration/ConfigureConnectionStrings.cs) |
| **Architecture ADRs** | [UMBRACO_OMNI_ARCHITECTURE.md](file:///Users/tunc/Source/Umbraco%20Hydra/Umbraco.Omni/UMBRACO_OMNI_ARCHITECTURE.md) |

