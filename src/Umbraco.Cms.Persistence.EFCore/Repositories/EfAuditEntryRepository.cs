using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IAuditEntryRepository" />.</summary>
internal sealed class EfAuditEntryRepository : IAuditEntryRepository
{
    private readonly UmbracoDbContext _db;
    public EfAuditEntryRepository(UmbracoDbContext db) => _db = db;

    public IEnumerable<IAuditEntry> GetPage(long pageIndex, int pageCount, out long records)
    {
        IQueryable<AuditEntryDto> q = _db.Set<AuditEntryDto>().OrderByDescending(x => x.EventDate);
        records = q.LongCount();
        return q.Skip((int)(pageIndex * pageCount)).Take(pageCount).ToList().Select(AuditEntryFactory.BuildEntity);
    }

    public IAuditEntry? Get(int id)
    {
        AuditEntryDto? dto = _db.Set<AuditEntryDto>().Find(id);
        return dto == null ? null : AuditEntryFactory.BuildEntity(dto);
    }

    public IEnumerable<IAuditEntry> GetMany(params int[]? ids)
    {
        IQueryable<AuditEntryDto> q = _db.Set<AuditEntryDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.ToList().Select(AuditEntryFactory.BuildEntity);
    }

    public bool Exists(int id) => _db.Set<AuditEntryDto>().Any(x => x.Id == id);

    public void Save(IAuditEntry entity)
    {
        entity.AddingEntity();
        AuditEntryDto dto = AuditEntryFactory.BuildDto(entity);
        _db.Set<AuditEntryDto>().Add(dto);
        _db.SaveChanges();
        entity.Id = dto.Id;
        entity.ResetDirtyProperties();
    }

    public void Delete(IAuditEntry entity)
        => throw new NotSupportedException("Audit entries cannot be deleted.");

    public IEnumerable<IAuditEntry> Get(IQuery<IAuditEntry> query)
        => _db.Set<AuditEntryDto>().ToList().Select(AuditEntryFactory.BuildEntity);

    public int Count(IQuery<IAuditEntry>? query)
        => _db.Set<AuditEntryDto>().Count();
}
