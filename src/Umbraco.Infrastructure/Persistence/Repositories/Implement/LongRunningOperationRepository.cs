using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace Umbraco.Cms.Infrastructure.Persistence.Repositories.Implement;

/// <summary>
/// Repository for managing long-running operations.
/// </summary>
internal class LongRunningOperationRepository : RepositoryBase, ILongRunningOperationRepository
{
    private readonly IJsonSerializer _jsonSerializer;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="LongRunningOperationRepository"/> class.
    /// </summary>
    public LongRunningOperationRepository(
        IJsonSerializer jsonSerializer,
        IScopeAccessor scopeAccessor,
        AppCaches appCaches,
        TimeProvider timeProvider)
        : base(scopeAccessor, appCaches)
    {
        _jsonSerializer = jsonSerializer;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task CreateAsync(LongRunningOperation operation, DateTimeOffset expirationDate)
    {
        LongRunningOperationDto dto = MapEntityToDto(operation, expirationDate);
        await Database.InsertAsync(dto);
    }

    /// <inheritdoc />
    public async Task<LongRunningOperation?> GetAsync(Guid id)
    {
        Sql<ISqlContext> sql = Sql()
            .Select<LongRunningOperationDto>()
            .From<LongRunningOperationDto>()
            .Where<LongRunningOperationDto>(x => x.Id == id);

        LongRunningOperationDto? dto = await Database.FirstOrDefaultAsync<LongRunningOperationDto>(sql);
        return dto == null ? null : MapDtoToEntity(dto);
    }

    /// <inheritdoc />
    public async Task<LongRunningOperation<T>?> GetAsync<T>(Guid id)
    {
        Sql<ISqlContext> sql = Sql()
            .Select<LongRunningOperationDto>()
            .From<LongRunningOperationDto>()
            .Where<LongRunningOperationDto>(x => x.Id == id);

        LongRunningOperationDto? dto = await Database.FirstOrDefaultAsync<LongRunningOperationDto>(sql);
        return dto == null ? null : MapDtoToEntity<T>(dto);
    }

    /// <inheritdoc/>
    public async Task<PagedModel<LongRunningOperation>> GetByTypeAsync(
        string type,
        LongRunningOperationStatus[] statuses,
        int skip,
        int take)
    {
        Sql<ISqlContext> sql = Sql()
            .Select<LongRunningOperationDto>()
            .From<LongRunningOperationDto>()
            .Where<LongRunningOperationDto>(x => x.Type == type);

        if (statuses.Length > 0)
        {
            var includeStale = statuses.Contains(LongRunningOperationStatus.Stale);
            var possibleStaleStatuses = new List<string>
            {
                nameof(LongRunningOperationStatus.Enqueued),
                nameof(LongRunningOperationStatus.Running)
            };
            var statusList = statuses.Except([LongRunningOperationStatus.Stale]).Select(s => s.ToString()).ToList();

            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
            sql = sql.Where<LongRunningOperationDto>(x =>
                (statusList.Contains(x.Status) && (!possibleStaleStatuses.Contains(x.Status) || x.ExpirationDate >= now))
                || (includeStale && possibleStaleStatuses.Contains(x.Status) && x.ExpirationDate < now));
        }

        return await Database.PagedAsync<LongRunningOperationDto, LongRunningOperation>(
            sql,
            skip,
            take,
            sortingAction: sql2 => sql2.OrderBy<LongRunningOperationDto>(x => x.CreateDate),
            mapper: MapDtoToEntity);
    }

    /// <inheritdoc/>
    public async Task<LongRunningOperationStatus?> GetStatusAsync(Guid id)
    {
        Sql<ISqlContext> sql = Sql()
            .Select<LongRunningOperationDto>(x => x.Status)
            .From<LongRunningOperationDto>()
            .Where<LongRunningOperationDto>(x => x.Id == id);

        return (await Database.ExecuteScalarAsync<string>(sql))?.EnumParse<LongRunningOperationStatus>(false);
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(Guid id, LongRunningOperationStatus status, DateTimeOffset expirationTime)
    {
        var tableName = QuoteTableName("umbracoLongRunningOperation");
        var statusCol = QuoteColumnName("status");
        var updateCol = QuoteColumnName("updateDate");
        var expCol = QuoteColumnName("expirationDate");
        var idCol = QuoteColumnName("id");

        Sql<ISqlContext> sql = SqlContext.Sql()
            .Append($"UPDATE {tableName} SET {statusCol} = @0, {updateCol} = @1, {expCol} = @2 WHERE {idCol} = @3",
                status.ToString(), DateTime.UtcNow, expirationTime.DateTime, id);

        await Database.ExecuteAsync(sql);
    }

    /// <inheritdoc/>
    public async Task SetResultAsync<T>(Guid id, T result)
    {
        var tableName = QuoteTableName("umbracoLongRunningOperation");
        var resultCol = QuoteColumnName("result");
        var updateCol = QuoteColumnName("updateDate");
        var idCol = QuoteColumnName("id");

        Sql<ISqlContext> sql = SqlContext.Sql()
            .Append($"UPDATE {tableName} SET {resultCol} = @0, {updateCol} = @1 WHERE {idCol} = @2",
                _jsonSerializer.Serialize(result), DateTime.UtcNow, id);

        await Database.ExecuteAsync(sql);
    }

    /// <inheritdoc/>
    public async Task CleanOperationsAsync(DateTimeOffset olderThan)
    {
        Sql<ISqlContext> sql = Sql()
            .Delete<LongRunningOperationDto>()
            .Where<LongRunningOperationDto>(x => x.UpdateDate < olderThan);

        await Database.ExecuteAsync(sql);
    }

    private LongRunningOperation MapDtoToEntity(LongRunningOperationDto dto) =>
        new()
        {
            Id = dto.Id,
            Type = dto.Type,
            Status = DetermineStatus(dto),
        };

    private LongRunningOperation<T> MapDtoToEntity<T>(LongRunningOperationDto dto) =>
        new()
        {
            Id = dto.Id,
            Type = dto.Type,
            Status = DetermineStatus(dto),
            Result = dto.Result == null ? default : _jsonSerializer.Deserialize<T>(dto.Result),
        };

    private static LongRunningOperationDto MapEntityToDto(LongRunningOperation entity, DateTimeOffset expirationTime) =>
        new()
        {
            Id = entity.Id,
            Type = entity.Type,
            Status = entity.Status.ToString(),
            CreateDate = DateTime.UtcNow,
            UpdateDate = DateTime.UtcNow,
            ExpirationDate = expirationTime.UtcDateTime,
        };

    private LongRunningOperationStatus DetermineStatus(LongRunningOperationDto dto)
    {
        LongRunningOperationStatus status = dto.Status.EnumParse<LongRunningOperationStatus>(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (status is LongRunningOperationStatus.Enqueued or LongRunningOperationStatus.Running
            && now.UtcDateTime >= dto.ExpirationDate)
        {
            status = LongRunningOperationStatus.Stale;
        }

        return status;
    }
}
