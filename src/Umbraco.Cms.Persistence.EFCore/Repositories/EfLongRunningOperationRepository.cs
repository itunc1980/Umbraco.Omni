using System.Text.Json;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="ILongRunningOperationRepository" />.</summary>
internal sealed class EfLongRunningOperationRepository : ILongRunningOperationRepository
{
    private readonly UmbracoDbContext _db;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly TimeProvider _timeProvider;

    public EfLongRunningOperationRepository(
        UmbracoDbContext db,
        IJsonSerializer jsonSerializer,
        TimeProvider timeProvider)
    {
        _db = db;
        _jsonSerializer = jsonSerializer;
        _timeProvider = timeProvider;
    }

    public Task CreateAsync(LongRunningOperation operation, DateTimeOffset expirationDate)
    {
        LongRunningOperationDto dto = MapEntityToDto(operation, expirationDate);
        _db.Set<LongRunningOperationDto>().Add(dto);
        _db.SaveChanges();
        return Task.CompletedTask;
    }

    public Task<LongRunningOperation?> GetAsync(Guid id)
    {
        LongRunningOperationDto? dto = _db.Set<LongRunningOperationDto>().Find(id);
        return Task.FromResult(dto == null ? null : MapDtoToEntity(dto));
    }

    public Task<LongRunningOperation<T>?> GetAsync<T>(Guid id)
    {
        LongRunningOperationDto? dto = _db.Set<LongRunningOperationDto>().Find(id);
        return Task.FromResult(dto == null ? null : MapDtoToEntity<T>(dto));
    }

    public Task<PagedModel<LongRunningOperation>> GetByTypeAsync(
        string type, LongRunningOperationStatus[] statuses, int skip, int take)
    {
        IQueryable<LongRunningOperationDto> q = _db.Set<LongRunningOperationDto>().Where(x => x.Type == type);

        if (statuses.Length > 0)
        {
            bool includeStale = statuses.Contains(LongRunningOperationStatus.Stale);
            var plainStatuses = statuses
                .Where(s => s != LongRunningOperationStatus.Stale)
                .Select(s => s.ToString()).ToList();

            if (includeStale)
            {
                // stale = Enqueued/Running that have expired
                DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
                q = q.Where(x => plainStatuses.Contains(x.Status)
                    || ((x.Status == nameof(LongRunningOperationStatus.Enqueued)
                         || x.Status == nameof(LongRunningOperationStatus.Running))
                        && x.ExpirationDate <= now));
            }
            else
            {
                q = q.Where(x => plainStatuses.Contains(x.Status));
            }
        }

        long total = q.LongCount();
        IEnumerable<LongRunningOperation> items = q.OrderBy(x => x.CreateDate).Skip(skip).Take(take).ToList().Select(MapDtoToEntity);
        return Task.FromResult(new PagedModel<LongRunningOperation> { Items = items, Total = total });
    }

    public Task<LongRunningOperationStatus?> GetStatusAsync(Guid id)
    {
        LongRunningOperationDto? dto = _db.Set<LongRunningOperationDto>().Find(id);
        if (dto == null) { return Task.FromResult<LongRunningOperationStatus?>(null); }
        return Task.FromResult<LongRunningOperationStatus?>(DetermineStatus(dto));
    }

    public Task UpdateStatusAsync(Guid id, LongRunningOperationStatus status, DateTimeOffset expirationDate)
    {
        LongRunningOperationDto? dto = _db.Set<LongRunningOperationDto>().Find(id);
        if (dto != null)
        {
            dto.Status = status.ToString();
            dto.UpdateDate = DateTime.UtcNow;
            dto.ExpirationDate = expirationDate.UtcDateTime;
            _db.SaveChanges();
        }
        return Task.CompletedTask;
    }

    public Task SetResultAsync<T>(Guid id, T result)
    {
        LongRunningOperationDto? dto = _db.Set<LongRunningOperationDto>().Find(id);
        if (dto != null)
        {
            dto.Result = _jsonSerializer.Serialize(result);
            dto.UpdateDate = DateTime.UtcNow;
            _db.SaveChanges();
        }
        return Task.CompletedTask;
    }

    public Task CleanOperationsAsync(DateTimeOffset olderThan)
    {
        DateTime cutoff = olderThan.UtcDateTime;
        _db.Set<LongRunningOperationDto>().RemoveRange(
            _db.Set<LongRunningOperationDto>().Where(x => x.UpdateDate < cutoff));
        _db.SaveChanges();
        return Task.CompletedTask;
    }

    private LongRunningOperation MapDtoToEntity(LongRunningOperationDto dto) =>
        new() { Id = dto.Id, Type = dto.Type, Status = DetermineStatus(dto) };

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
