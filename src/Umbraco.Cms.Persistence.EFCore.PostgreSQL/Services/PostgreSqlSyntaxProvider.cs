using System.Data;
using System.Diagnostics.CodeAnalysis;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseModelDefinitions;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.PostgreSQL.Services;

public class PostgreSqlSyntaxProvider : SqlSyntaxProviderBase<PostgreSqlSyntaxProvider>
{
    public PostgreSqlSyntaxProvider()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        AutoIncrementDefinition = "SERIAL";
        IntColumnDefinition = "INTEGER";
        LongColumnDefinition = "BIGINT";
        GuidColumnDefinition = "UUID";
        BoolColumnDefinition = "BOOLEAN";
        RealColumnDefinition = "REAL";
        DecimalColumnDefinition = "DECIMAL(38, 6)";
        BlobColumnDefinition = "BYTEA";
        DateTimeColumnDefinition = "TIMESTAMP";
        DateTimeOffsetColumnDefinition = "TIMESTAMP WITH TIME ZONE";
        TimeColumnDefinition = "TIME";
        DateOnlyColumnDefinition = "DATE";
        TimeOnlyColumnDefinition = "TIME";
    }

    public override string ProviderName => "PostgreSQL";

    public override IsolationLevel DefaultIsolationLevel => IsolationLevel.ReadCommitted;

    public override string DbProvider => "Npgsql";

    public override bool TryGetDefaultConstraint(IDatabase db, string? tableName, string columnName, [MaybeNullWhen(false)] out string constraintName)
    {
        constraintName = null;
        return false;
    }

    public override IEnumerable<Tuple<string, string, string>> GetConstraintsPerColumn(IDatabase db)
    {
        // ... (not fully implemented for now)
        return Enumerable.Empty<Tuple<string, string, string>>();
    }

    public override string GetQuotedTableName(string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return string.Empty;
        }

        return $"\"{tableName}\"";
    }

    public override string GetQuotedColumnName(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return string.Empty;
        }

        return $"\"{columnName}\"";
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var startUnderscore = input.StartsWith("_");
        var str = System.Text.RegularExpressions.Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        return startUnderscore ? "_" + str : str;
    }

    public override IEnumerable<Tuple<string, string, string, bool>> GetDefinedIndexes(IDatabase db)
    {
        // For now, return an empty list just to bypass the startup error.
        return new List<Tuple<string, string, string, bool>>();
    }

    protected override string? FormatSystemMethods(SystemMethods systemMethod)
    {
        switch (systemMethod)
        {
            case SystemMethods.NewGuid:
                return "gen_random_uuid()";
            case SystemMethods.CurrentDateTime:
                return "CURRENT_TIMESTAMP";
            case SystemMethods.CurrentUTCDateTime:
                return "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'";
            default:
                return null;
        }
    }

    protected override string FormatIdentity(ColumnDefinition column)
    {
        return string.Empty; // In PostgreSQL, SERIAL handles identity, so we don't append anything here.
    }

    /// <inheritdoc/>
    protected override string FormatType(ColumnDefinition column)
    {
        if (column.IsIdentity)
        {
            if (column.Type == DbType.Int64 || column.PropertyType == typeof(long))
            {
                return "BIGSERIAL";
            }

            return "SERIAL";
        }
        var type = base.FormatType(column);
        if (type.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase))
        {
            return type.Replace("NVARCHAR", "VARCHAR", StringComparison.OrdinalIgnoreCase);
        }
        if (type.StartsWith("NTEXT", StringComparison.OrdinalIgnoreCase))
        {
            return type.Replace("NTEXT", "TEXT", StringComparison.OrdinalIgnoreCase);
        }
        if (type.StartsWith("NCHAR", StringComparison.OrdinalIgnoreCase))
        {
            return type.Replace("NCHAR", "CHAR", StringComparison.OrdinalIgnoreCase);
        }
        if (type.Equals("DATETIME", StringComparison.OrdinalIgnoreCase))
        {
            return "TIMESTAMP";
        }
        return type;
    }

    public override Sql<ISqlContext> SelectTop(Sql<ISqlContext> sql, int top)
    {
        return sql.Append($"LIMIT {top}");
    }

    /// <inheritdoc/>
    public override string StringLengthUnicodeColumnDefinitionFormat => "VARCHAR({0})";

    /// <inheritdoc/>
    public override bool SupportsIdentityInsert()
    {
        return false;
    }

    /// <inheritdoc/>
    protected override string FormatConstraint(ColumnDefinition column)
    {
        return string.Empty;
    }

    /// <inheritdoc/>
    public override string GetSpecialDbType(SpecialDbType dbType)
    {
        if (dbType == SpecialDbType.NCHAR)
        {
            return "CHAR";
        }

        if (dbType == SpecialDbType.NTEXT)
        {
            return "TEXT";
        }

        if (dbType == SpecialDbType.NVARCHARMAX)
        {
            return "TEXT";
        }

        return "VARCHAR";
    }

    /// <inheritdoc/>
    public override string FormatPrimaryKey(TableDefinition table)
    {
        ColumnDefinition? columnDefinition = table.Columns.FirstOrDefault(x => x.IsPrimaryKey);
        if (columnDefinition == null)
        {
            return string.Empty;
        }

        var constraintName = string.IsNullOrEmpty(columnDefinition.PrimaryKeyName)
            ? $"PK_{table.Name}"
            : columnDefinition.PrimaryKeyName;

        var columns = string.IsNullOrEmpty(columnDefinition.PrimaryKeyColumns)
            ? GetQuotedColumnName(columnDefinition.Name)
            : string.Join(", ", columnDefinition.PrimaryKeyColumns
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(GetQuotedColumnName));

        var primaryKeyPart = "PRIMARY KEY";

        return string.Format(
            CreateConstraint,
            GetQuotedTableName(table.Name),
            GetQuotedName(constraintName),
            primaryKeyPart,
            columns);
    }

    /// <inheritdoc/>
    public override string GetIndexType(IndexTypes indexTypes)
    {
        var indexType = string.Empty;

        if (indexTypes == IndexTypes.UniqueClustered || indexTypes == IndexTypes.UniqueNonClustered)
        {
            indexType = "UNIQUE";
        }

        return indexType;
    }

    public override string Format(IndexDefinition index)
    {
        var name = string.IsNullOrEmpty(index.Name)
            ? $"IX_{index.TableName}_{index.ColumnName}"
            : index.Name;

        var columns = index.Columns.Any()
            ? string.Join(",", index.Columns.Select(x => GetQuotedColumnName(x.Name)))
            : GetQuotedColumnName(index.ColumnName);

        if (index.IndexType == IndexTypes.UniqueClustered || index.IndexType == IndexTypes.UniqueNonClustered)
        {
            // PostgreSQL requires a UNIQUE CONSTRAINT to be referenced by a foreign key, 
            // a UNIQUE INDEX is not sufficient.
            var constraintName = string.IsNullOrEmpty(index.Name)
                ? $"UQ_{index.TableName}_{index.ColumnName}"
                : index.Name;

            return $"ALTER TABLE {GetQuotedTableName(index.TableName)} ADD CONSTRAINT {GetQuotedName(constraintName)} UNIQUE ({columns})";
        }

        return string.Format(
            CreateIndex,
            GetIndexType(index.IndexType),
            " ",
            GetQuotedName(name),
            GetQuotedTableName(index.TableName),
            columns);
    }

    /// <inheritdoc/>
    public override void HandleCreateTable(IDatabase database, TableDefinition tableDefinition, bool skipKeysAndIndexes = false)
    {
        var columns = Format(tableDefinition.Columns);
        var primaryKey = FormatPrimaryKey(tableDefinition);
        var foreignKeys = Format(tableDefinition.ForeignKeys);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"CREATE TABLE {GetQuotedTableName(tableDefinition.Name)}");
        sb.AppendLine("(");
        sb.AppendLine(columns);
        sb.AppendLine(");");

        try
        {
            database.Execute(new Sql(sb.ToString()));
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to execute SQL: {sb.ToString()}", ex);
        }

        if (!skipKeysAndIndexes)
        {
            if (!string.IsNullOrEmpty(primaryKey))
            {
                database.Execute(new Sql(primaryKey));
            }

            var indexSql = Format(tableDefinition.Indexes);
            foreach (var sql in indexSql)
            {
                database.Execute(new Sql(sql));
            }

            foreach (var foreignKey in foreignKeys)
            {
                database.Execute(new Sql(foreignKey));
            }
        }
    }

    public override Sql<ISqlContext>.SqlJoinClause<ISqlContext> LeftJoinWithNestedJoin<TDto>(Sql<ISqlContext> sql, Func<Sql<ISqlContext>, Sql<ISqlContext>> nestedJoin, string? alias = null)
    {
        Type type = typeof(TDto);
        var tableName = GetQuotedTableName(type.GetTableName());
        var join = tableName;

        if (alias != null)
        {
            var quotedAlias = GetQuotedTableName(alias);
            join += " " + quotedAlias;
        }

        var nestedSql = new Sql<ISqlContext>(sql.SqlContext);
        nestedSql = nestedJoin(nestedSql);

        Sql<ISqlContext>.SqlJoinClause<ISqlContext> sqlJoin = sql.LeftJoin(join);
        sql.Append(nestedSql);
        return sqlJoin;
    }

    public override IEnumerable<string> GetTablesInSchema(IDatabase db)
    {
        return db.Fetch<string>("SELECT tablename FROM pg_tables WHERE schemaname = 'public'");
    }

    public override string OrderByGuid(string tableName, string columnName)
    {
        return $"{GetQuotedTableName(tableName)}.{GetQuotedColumnName(columnName)}";
    }
}
