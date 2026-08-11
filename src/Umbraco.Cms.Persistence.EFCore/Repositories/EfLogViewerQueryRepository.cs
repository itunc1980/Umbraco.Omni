using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="ILogViewerQueryRepository" />.</summary>
internal sealed class EfLogViewerQueryRepository : ILogViewerQueryRepository
{
    private readonly UmbracoDbContext _db;
    public EfLogViewerQueryRepository(UmbracoDbContext db) => _db = db;

    public ILogViewerQuery? GetByName(string name)
    {
        LogViewerQueryDto? dto = _db.Set<LogViewerQueryDto>().FirstOrDefault(x => x.Name == name);
        return dto == null ? null : Map(dto);
    }

    public ILogViewerQuery? Get(int id)
    {
        LogViewerQueryDto? dto = _db.Set<LogViewerQueryDto>().Find(id);
        return dto == null ? null : Map(dto);
    }

    public IEnumerable<ILogViewerQuery> GetMany(params int[]? ids)
    {
        IQueryable<LogViewerQueryDto> q = _db.Set<LogViewerQueryDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.OrderBy(x => x.Name).ToList().Select(Map);
    }

    public bool Exists(int id) => _db.Set<LogViewerQueryDto>().Any(x => x.Id == id);

    public void Save(ILogViewerQuery entity)
    {
        LogViewerQueryDto dto = new() { Id = entity.Id, Name = entity.Name, Query = entity.Query };
        if (entity.Id == 0)
        {
            _db.Set<LogViewerQueryDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.Id;
        }
        else
        {
            _db.Set<LogViewerQueryDto>().Update(dto);
            _db.SaveChanges();
        }
    }

    public void Delete(ILogViewerQuery entity)
    {
        LogViewerQueryDto? dto = _db.Set<LogViewerQueryDto>().Find(entity.Id);
        if (dto != null) { _db.Set<LogViewerQueryDto>().Remove(dto); _db.SaveChanges(); }
    }

    public IEnumerable<ILogViewerQuery> Get(IQuery<ILogViewerQuery> query)
        => _db.Set<LogViewerQueryDto>().OrderBy(x => x.Name).ToList().Select(Map);

    public int Count(IQuery<ILogViewerQuery>? query) => _db.Set<LogViewerQueryDto>().Count();

    private static ILogViewerQuery Map(LogViewerQueryDto dto)
    {
        var entity = new LogViewerQuery(dto.Name, dto.Query ?? string.Empty);
        entity.Id = dto.Id;
        return entity;
    }
}
