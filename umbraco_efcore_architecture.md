# Database-Agnostic EF Core Architecture for Umbraco CMS

This document outlines the complete architectural design and code implementation for migrating the database layer of Umbraco CMS from NPoco (micro-ORM, SQL Server-centric) to a high-performance, **Database-Agnostic Entity Framework Core** architecture. This setup natively supports **Microsoft SQL Server, PostgreSQL, MySQL (Pomelo), Oracle, and SQLite** from a single codebase.

---

## 1. Enterprise Repository and Unit of Work Patterns

To decouple business logic from EF Core specifics while maintaining high performance and CQRS compatibility, we define clean asynchronous Repository and Unit of Work abstractions.

### IRepository.cs
Exposes standard CRUD operations and an `IQueryable<TEntity>` query root to facilitate projection, filtering, and custom specification patterns in CQRS handlers.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Umbraco.Core.Persistence.Repositories
{
    public interface IRepository<TEntity> where TEntity : class
    {
        /// <summary>
        /// Exposes a queryable root for the entity. By default, it disables tracking for read-only query performance.
        /// </summary>
        IQueryable<TEntity> Query(bool asNoTracking = true);

        Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);
        Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
        
        Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
        Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
        
        void Update(TEntity entity);
        void Delete(TEntity entity);
        void DeleteRange(IEnumerable<TEntity> entities);
        
        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
        Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    }
}
```

### IUnitOfWork.cs
Coordinates transactions across multiple repositories and manages the lifecycle of the underlying `DbContext`.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Umbraco.Core.Persistence.UnitOfWork
{
    public interface IUnitOfWork : IDisposable, IAsyncDisposable
    {
        IRepository<TEntity> Repository<TEntity>() where TEntity : class;
        
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
        
        Task BeginTransactionAsync(CancellationToken cancellationToken = default);
        Task CommitTransactionAsync(CancellationToken cancellationToken = default);
        Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
    }
}
```

### EFRepository.cs
Concrete EF Core repository implementation.

```csharp
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Core.Persistence.Repositories;

namespace Umbraco.Infrastructure.Persistence.Repositories
{
    public class EFRepository<TEntity> : IRepository<TEntity> where TEntity : class
    {
        protected readonly DbContext Context;
        protected readonly DbSet<TEntity> DbSet;

        public EFRepository(DbContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            DbSet = context.Set<TEntity>();
        }

        public IQueryable<TEntity> Query(bool asNoTracking = true)
        {
            return asNoTracking ? DbSet.AsNoTracking() : DbSet;
        }

        public async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        {
            return await DbSet.FindAsync(new object[] { id }, cancellationToken);
        }

        public async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await DbSet.ToListAsync(cancellationToken);
        }

        public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await DbSet.AddAsync(entity, cancellationToken);
        }

        public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await DbSet.AddRangeAsync(entities, cancellationToken);
        }

        public void Update(TEntity entity)
        {
            DbSet.Update(entity);
        }

        public void Delete(TEntity entity)
        {
            DbSet.Remove(entity);
        }

        public void DeleteRange(IEnumerable<TEntity> entities)
        {
            DbSet.RemoveRange(entities);
        }

        public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await DbSet.AnyAsync(predicate, cancellationToken);
        }

        public async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await DbSet.CountAsync(predicate, cancellationToken);
        }
    }
}
```

### EFUnitOfWork.cs
Concrete Unit of Work implementation. Repositories are cached in a thread-safe dictionary to maintain scope integrity during a single request lifecycle.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Core.Persistence.Repositories;
using Umbraco.Core.Persistence.UnitOfWork;
using Umbraco.Infrastructure.Persistence.Repositories;

namespace Umbraco.Infrastructure.Persistence.UnitOfWork
{
    public class EFUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
    {
        private readonly TContext _context;
        private IDbContextTransaction? _transaction;
        private readonly ConcurrentDictionary<Type, object> _repositories;
        private bool _disposed;

        public EFUnitOfWork(TContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _repositories = new ConcurrentDictionary<Type, object>();
        }

        public IRepository<TEntity> Repository<TEntity>() where TEntity : class
        {
            return (IRepository<TEntity>)_repositories.GetOrAdd(typeof(TEntity), _ => new EFRepository<TEntity>(_context));
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_transaction != null) return;
            _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        }

        public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await SaveChangesAsync(cancellationToken);
                if (_transaction != null)
                {
                    await _transaction.CommitAsync(cancellationToken);
                }
            }
            catch
            {
                await RollbackTransactionAsync(cancellationToken);
                throw;
            }
            finally
            {
                await DisposeTransactionAsync();
            }
        }

        public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync(cancellationToken);
                await DisposeTransactionAsync();
            }
        }

        private async Task DisposeTransactionAsync()
        {
            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _transaction?.Dispose();
                    _context.Dispose();
                }
                _disposed = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                if (_transaction != null)
                {
                    await _transaction.DisposeAsync();
                }
                await _context.DisposeAsync();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
```

---

## 2. Dynamic Model Customization and Provider-Specific Configuration

The `UmbracoDbContext` dynamically configures entity metadata based on the active provider (`Database.ProviderName`). This resolves PostgreSQL naming constraints, Oracle identifier rules, SQLite data type limits, and MySQL indexing behaviors.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Umbraco.Core.Domain.Entities; // Example Namespace

namespace Umbraco.Infrastructure.Persistence
{
    public class UmbracoDbContext : DbContext
    {
        public DbSet<ContentNode> ContentNodes => Set<ContentNode>();
        public DbSet<PropertyData> PropertyData => Set<PropertyData>();
        public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

        public UmbracoDbContext(DbContextOptions<UmbracoDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply standard Fluent API configurations from current assembly
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(UmbracoDbContext).Assembly);

            var providerName = Database.ProviderName;

            if (providerName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                ConfigurePostgreSqlNamingConventions(modelBuilder);
            }
            else if (providerName == "Oracle.EntityFrameworkCore")
            {
                ConfigureOracleSpecifics(modelBuilder);
            }
            else if (providerName == "Pomelo.EntityFrameworkCore.MySql")
            {
                ConfigureMySqlSpecifics(modelBuilder);
            }
            else if (providerName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                ConfigureSqliteSpecifics(modelBuilder);
            }
        }

        /// <summary>
        /// PostgreSQL is strictly case-sensitive and prefers lowercase snake_case schema.
        /// </summary>
        private void ConfigurePostgreSqlNamingConventions(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                // Table names to snake_case
                entity.SetTableName(entity.GetTableName()?.ToSnakeCase());

                // Column names
                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(property.GetColumnName()?.ToSnakeCase());
                }

                // Primary Key constraints
                foreach (var key in entity.GetKeys())
                {
                    key.SetName(key.GetName()?.ToSnakeCase());
                }

                // Foreign Key constraints
                foreach (var fk in entity.GetForeignKeys())
                {
                    fk.SetConstraintName(fk.GetConstraintName()?.ToSnakeCase());
                }

                // Indexes
                foreach (var index in entity.GetIndexes())
                {
                    index.SetDatabaseName(index.GetDatabaseName()?.ToSnakeCase());
                }
            }
        }

        /// <summary>
        /// Oracle limits identifiers to 30 characters in older versions, lacks boolean types,
        /// and prefers UPPERCASE schema conventions.
        /// </summary>
        private void ConfigureOracleSpecifics(ModelBuilder modelBuilder)
        {
            var booleanConverter = new BoolToZeroOneConverter<int>();

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                // Enforce uppercase table names and trim length to prevent 30-char limit overflow
                var tableName = entity.GetTableName()?.ToUpperInvariant();
                if (tableName?.Length > 30)
                {
                    tableName = tableName.Substring(0, 30);
                }
                entity.SetTableName(tableName);

                foreach (var property in entity.GetProperties())
                {
                    // Enforce uppercase column names
                    var columnName = property.GetColumnName()?.ToUpperInvariant();
                    if (columnName?.Length > 30)
                    {
                        columnName = columnName.Substring(0, 30);
                    }
                    property.SetColumnName(columnName);

                    // Map System.Boolean to Oracle's NUMBER(1,0) using ValueConverter
                    if (property.ClrType == typeof(bool) || property.ClrType == typeof(bool?))
                    {
                        property.SetValueConverter(booleanConverter);
                        property.SetColumnType("NUMBER(1,0)");
                    }
                }
            }
        }

        /// <summary>
        /// MySQL (Pomelo) limits index key prefix lengths. We restrict column lengths for indexed strings.
        /// </summary>
        private void ConfigureMySqlSpecifics(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                // Avoid indexing huge string columns without length configuration
                foreach (var index in entity.GetIndexes())
                {
                    foreach (var property in index.Properties)
                    {
                        if (property.ClrType == typeof(string))
                        {
                            // Reduce default string column length for index stability
                            property.SetMaxLength(255); 
                        }
                    }
                }
            }
        }

        /// <summary>
        /// SQLite lacks native support for DateTimeOffset types and requires converters to store them as strings.
        /// </summary>
        private void ConfigureSqliteSpecifics(ModelBuilder modelBuilder)
        {
            var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, string>(
                d => d.ToString("O"), // ISO-8601 representation
                s => DateTimeOffset.Parse(s));

            var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, string>(
                d => d.HasValue ? d.Value.ToString("O") : string.Empty,
                s => string.IsNullOrEmpty(s) ? null : DateTimeOffset.Parse(s));

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(dateTimeOffsetConverter);
                        property.SetColumnType("TEXT");
                    }
                    else if (property.ClrType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(nullableDateTimeOffsetConverter);
                        property.SetColumnType("TEXT");
                    }
                }
            }
        }
    }

    public static class StringNamingExtensions
    {
        public static string ToSnakeCase(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            var startWithUnderscore = input.StartsWith("_");
            var result = Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2");
            
            return (startWithUnderscore ? "_" : "") + result.ToLowerInvariant();
        }
    }
}
```

---

## 3. Query Conversions and Maximum Performance Optimizations

To completely remove raw SQL dependencies (such as SQL Server-specific Common Table Expressions - CTE) while matching NPoco speed, we leverage EF Core's performance features.

### Tree Hierarchy Retrieval Stratejisi
NPoco queries often use hierarchical recursive CTEs for Content Nodes. To make this database-agnostic, we store a path value (e.g. `-1,1024,1035,1112`). A standard database-agnostic string prefix check maps efficiently to B-Tree index scans.

We implement **Compiled Queries** to bypass query compilation overhead, **Query Splitting** to eliminate Cartesian explosion on children collections, and **AsNoTracking** for read-only caching bypass.

### Optimized Query Code Example

```csharp
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Core.Domain.Entities;

namespace Umbraco.Infrastructure.Persistence.Queries
{
    public class UmbracoContentQueryService
    {
        private readonly UmbracoDbContext _context;

        // Compiled Query definition for fast, database-agnostic subtree retrieval
        private static readonly Func<UmbracoDbContext, string, IAsyncEnumerable<ContentNodeDto>> _getSubtreeCompiledQuery =
            EF.CompileAsyncQuery((UmbracoDbContext context, string pathPrefix) =>
                context.ContentNodes
                    .AsNoTracking()
                    .Where(n => n.Path.StartsWith(pathPrefix))
                    .OrderBy(n => n.Level)
                    .ThenBy(n => n.SortOrder)
                    .Select(n => new ContentNodeDto
                    {
                        Id = n.Id,
                        ParentId = n.ParentId,
                        Name = n.Name,
                        Level = n.Level,
                        Path = n.Path
                    }));

        public UmbracoContentQueryService(UmbracoDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Fetches a high-performance content hierarchy.
        /// </summary>
        public async Task<List<ContentNodeDto>> GetSubtreeAsync(int rootId)
        {
            // First retrieve the root node's path prefix
            var rootNode = await _context.ContentNodes
                .AsNoTracking()
                .Select(n => new { n.Id, n.Path })
                .FirstOrDefaultAsync(n => n.Id == rootId);

            if (rootNode == null) return new List<ContentNodeDto>();

            var pathPrefix = rootNode.Path + ",";
            var results = new List<ContentNodeDto>();

            // Query compilation bypass (Compiled Query execution)
            await foreach (var node in _getSubtreeCompiledQuery(_context, pathPrefix))
            {
                results.Add(node);
            }

            return results;
        }

        /// <summary>
        /// Fetches detailed node data, resolving Cartesian product issues through Query Splitting.
        /// </summary>
        public async Task<List<ContentNode>> GetDetailedNodesInSubtreeAsync(string pathPrefix, CancellationToken cancellationToken = default)
        {
            return await _context.ContentNodes
                .AsNoTracking()
                .AsSplitQuery() // Splitting: Prevents performance drop from massive JOINs
                .Include(n => n.PropertyData)
                .Include(n => n.Permissions)
                .Where(n => n.Path.StartsWith(pathPrefix))
                .ToListAsync(cancellationToken);
        }
    }

    public class ContentNodeDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; }
        public string Path { get; set; } = string.Empty;
    }
}
```

---

## 4. Multi-Provider Database Migrations Strategy

Because each database provider interprets database schemas with unique SQL dialects (e.g. `identity` vs `sequence`, `text` vs `nvarchar(max)`), **the best practice is to isolate migrations per provider in separate directories**. 

We keep a single DbContext, but generate migrations targeting SQL Server, PostgreSQL, MySQL, Oracle, and SQLite separately.

### Migration Directory Structure
```text
Umbraco.Infrastructure/
└── Persistence/
    ├── Migrations/
    │   ├── SqlServer/       <-- MSSQL Migrations
    │   ├── PostgreSQL/      <-- PostgreSQL Migrations
    │   ├── MySql/           <-- MySQL Migrations
    │   ├── Oracle/          <-- Oracle Migrations
    │   └── Sqlite/          <-- SQLite Migrations
```

### Design-Time DbContext Factory
To facilitate CLI generation of migrations per provider without changing system code, we write an `IDesignTimeDbContextFactory`.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System;

namespace Umbraco.Infrastructure.Persistence
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UmbracoDbContext>
    {
        public UmbracoDbContext CreateDbContext(string[] args)
        {
            // We retrieve target provider from design-time arguments or environment variables
            var provider = Environment.GetEnvironmentVariable("EF_PROVIDER")?.ToUpperInvariant() ?? "SQLSERVER";
            var optionsBuilder = new DbContextOptionsBuilder<UmbracoDbContext>();

            const string dummyConnectionString = "Server=localhost;Database=DummyDb;User Id=sa;Password=Password123;";

            switch (provider)
            {
                case "POSTGRESQL":
                    optionsBuilder.UseNpgsql(dummyConnectionString, 
                        b => b.MigrationsAssembly("Umbraco.Infrastructure"));
                    break;
                case "MYSQL":
                    optionsBuilder.UseMySql(dummyConnectionString, new MySqlServerVersion(new Version(8, 0)),
                        b => b.MigrationsAssembly("Umbraco.Infrastructure"));
                    break;
                case "ORACLE":
                    optionsBuilder.UseOracle(dummyConnectionString, 
                        b => b.MigrationsAssembly("Umbraco.Infrastructure"));
                    break;
                case "SQLITE":
                    optionsBuilder.UseSqlite("Data Source=dummy.db", 
                        b => b.MigrationsAssembly("Umbraco.Infrastructure"));
                    break;
                case "SQLSERVER":
                default:
                    optionsBuilder.UseSqlServer(dummyConnectionString, 
                        b => b.MigrationsAssembly("Umbraco.Infrastructure"));
                    break;
            }

            return new UmbracoDbContext(optionsBuilder.Options);
        }
    }
}
```

### CLI Command Matrix for Adding Migrations

Use these CLI commands to build schema changes for each database. The target directory is automatically set via the `--output-dir` flag.

```bash
# Generate SQL Server Migration
export EF_PROVIDER=SQLSERVER
dotnet ef migrations add InitialSqlServer --context UmbracoDbContext --output-dir Persistence/Migrations/SqlServer --project Umbraco.Infrastructure

# Generate PostgreSQL Migration
export EF_PROVIDER=POSTGRESQL
dotnet ef migrations add InitialPostgres --context UmbracoDbContext --output-dir Persistence/Migrations/PostgreSQL --project Umbraco.Infrastructure

# Generate MySQL Migration
export EF_PROVIDER=MYSQL
dotnet ef migrations add InitialMySql --context UmbracoDbContext --output-dir Persistence/Migrations/MySql --project Umbraco.Infrastructure

# Generate Oracle Migration
export EF_PROVIDER=ORACLE
dotnet ef migrations add InitialOracle --context UmbracoDbContext --output-dir Persistence/Migrations/Oracle --project Umbraco.Infrastructure

# Generate SQLite Migration
export EF_PROVIDER=SQLITE
dotnet ef migrations add InitialSqlite --context UmbracoDbContext --output-dir Persistence/Migrations/Sqlite --project Umbraco.Infrastructure
```

---

## 5. Flexible Database Registration Layer (IoC Configuration)

To load and register configurations based on the `appsettings.json` selection, we write an extension method `AddUmbracoFlexibleDataStores()`. It registers connection pooling, sets up retry resilience, and maps the active migrations assembly dynamically.

### Configuration Structure (`appsettings.json`)
```json
{
  "Umbraco": {
    "Database": {
      "Provider": "PostgreSQL",
      "ConnectionString": "Host=127.0.0.1;Database=umbraco_hydra;Username=umbraco_admin;Password=SecretPassword"
    }
  }
}
```

### ServiceCollectionExtensions.cs
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Umbraco.Core.Persistence.UnitOfWork;
using Umbraco.Infrastructure.Persistence;
using Umbraco.Infrastructure.Persistence.UnitOfWork;

namespace Umbraco.Infrastructure.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddUmbracoFlexibleDataStores(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            var provider = configuration["Umbraco:Database:Provider"]?.Trim().ToUpperInvariant() 
                           ?? "SQLSERVER";
            var connectionString = configuration["Umbraco:Database:ConnectionString"] 
                                   ?? throw new InvalidOperationException("Connection string is missing.");

            // Use AddDbContextPool for improved connection recycling and high throughput
            services.AddDbContextPool<UmbracoDbContext>(options =>
            {
                switch (provider)
                {
                    case "POSTGRESQL":
                        options.UseNpgsql(connectionString, sqlOptions =>
                        {
                            sqlOptions.MigrationsAssembly(typeof(UmbracoDbContext).Assembly.FullName);
                            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory");
                            sqlOptions.EnableRetryOnFailure(
                                maxRetryCount: 5,
                                maxRetryDelay: TimeSpan.FromSeconds(30),
                                errorCodesToAdd: null);
                        });
                        break;

                    case "MYSQL":
                        options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0)), sqlOptions =>
                        {
                            sqlOptions.MigrationsAssembly(typeof(UmbracoDbContext).Assembly.FullName);
                            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory");
                            sqlOptions.EnableRetryOnFailure(
                                maxRetryCount: 5,
                                maxRetryDelay: TimeSpan.FromSeconds(30),
                                errorCodesToAdd: null);
                        });
                        break;

                    case "ORACLE":
                        options.UseOracle(connectionString, sqlOptions =>
                        {
                            sqlOptions.MigrationsAssembly(typeof(UmbracoDbContext).Assembly.FullName);
                            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory");
                            sqlOptions.EnableRetryOnFailure(
                                maxRetryCount: 5,
                                maxRetryDelay: TimeSpan.FromSeconds(30),
                                errorCodesToAdd: null);
                        });
                        break;

                    case "SQLITE":
                        options.UseSqlite(connectionString, sqlOptions =>
                        {
                            sqlOptions.MigrationsAssembly(typeof(UmbracoDbContext).Assembly.FullName);
                            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory");
                            // SQLite does not support execution strategies (no retry needed for simple files)
                        });
                        break;

                    case "SQLSERVER":
                    default:
                        options.UseSqlServer(connectionString, sqlOptions =>
                        {
                            sqlOptions.MigrationsAssembly(typeof(UmbracoDbContext).Assembly.FullName);
                            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory");
                            sqlOptions.EnableRetryOnFailure(
                                maxRetryCount: 5,
                                maxRetryDelay: TimeSpan.FromSeconds(30),
                                errorNumbersToAdd: null);
                        });
                        break;
                }
            });

            // Register Repository and Unit of Work abstractions
            services.AddScoped<IUnitOfWork, EFUnitOfWork<UmbracoDbContext>>();
            // Generic repositories can be registered dynamically if needed, or mapped via dependency injection
            services.AddScoped(typeof(IRepository<>), typeof(EFRepository<>));

            return services;
        }
    }
}
```
