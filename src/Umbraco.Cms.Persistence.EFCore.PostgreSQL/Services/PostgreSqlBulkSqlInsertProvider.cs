using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Umbraco.Cms.Persistence.EFCore.PostgreSQL.Services;

public class PostgreSqlBulkSqlInsertProvider : IBulkSqlInsertProvider
{
    /// <inheritdoc/>
    public string ProviderName => "PostgreSQL";

    /// <inheritdoc/>
    public int BulkInsertRecords<T>(IUmbracoDatabase database, IEnumerable<T> records)
    {
        T[] recordsA = records.ToArray();
        if (recordsA.Length == 0)
        {
            return 0;
        }

        PocoData? pocoData = database.PocoDataFactory.ForType(typeof(T)) ?? throw new InvalidOperationException("Could not find PocoData for " + typeof(T));

        return BulkInsertRecordsPostgreSql(database, pocoData, recordsA);
    }

    private static int BulkInsertRecordsPostgreSql<T>(IUmbracoDatabase database, PocoData pocoData, IEnumerable<T> records)
    {
        var count = 0;
        var inTrans = database.InTransaction;

        if (!inTrans)
        {
            database.BeginTransaction();
        }

        foreach (T record in records)
        {
            database.Insert(record);
            count++;
        }

        if (!inTrans)
        {
            database.CompleteTransaction();
        }

        return count;
    }
}
