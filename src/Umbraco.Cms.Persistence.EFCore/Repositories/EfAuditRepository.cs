using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IAuditRepository" />.</summary>
internal sealed class EfAuditRepository : IAuditRepository
{
    private readonly UmbracoDbContext _db;
    public EfAuditRepository(UmbracoDbContext db) => _db = db;

    public IEnumerable<IAuditItem> Get(AuditType type, IQuery<IAuditItem> query)
    {
        var typeStr = type.ToString();
        IQueryable<LogDto> q = _db.Set<LogDto>().Where(x => x.Header == typeStr);
        q = ApplyQuery(q, query);
        return AuditItemFactory.BuildEntities(q.ToList());
    }

    public void CleanLogs(int maximumAgeOfLogsInMinutes)
    {
        DateTime cutoff = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(maximumAgeOfLogsInMinutes));
        string[] headers = ["open", "system"];
        List<LogDto> toDelete = _db.Set<LogDto>()
            .Where(x => x.Datestamp < cutoff && headers.Contains(x.Header))
            .ToList();
        _db.Set<LogDto>().RemoveRange(toDelete);
        _db.SaveChanges();
    }

    public IEnumerable<IAuditItem> GetPagedResultsByQuery(
        IQuery<IAuditItem> query,
        long pageIndex,
        int pageSize,
        out long totalRecords,
        Direction orderDirection,
        AuditType[]? auditTypeFilter,
        IQuery<IAuditItem>? customFilter)
    {
        auditTypeFilter ??= [];
        IQueryable<LogDto> q = ApplyQuery(_db.Set<LogDto>().AsQueryable(), query);
        if (auditTypeFilter.Length > 0)
        {
            string[] typeStrings = auditTypeFilter.Select(t => t.ToString()).ToArray();
            q = q.Where(x => typeStrings.Contains(x.Header));
        }
        if (customFilter != null) { q = ApplyQuery(q, customFilter); }
        q = orderDirection == Direction.Ascending
            ? q.OrderBy(x => x.Datestamp)
            : q.OrderByDescending(x => x.Datestamp);
        totalRecords = q.LongCount();
        List<LogDto> items = q.Skip((int)(pageIndex * pageSize)).Take(pageSize).ToList();
        return AuditItemFactory.BuildEntities(items);
    }

    public IAuditItem? Get(int id)
    {
        LogDto? dto = _db.Set<LogDto>().Find(id);
        return dto == null ? null : AuditItemFactory.BuildEntity(dto);
    }

    public IEnumerable<IAuditItem> GetMany(params int[]? ids)
    {
        IQueryable<LogDto> q = _db.Set<LogDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return AuditItemFactory.BuildEntities(q.ToList());
    }

    public bool Exists(int id) => _db.Set<LogDto>().Any(x => x.Id == id);

    public void Save(IAuditItem entity)
    {
        LogDto dto = AuditItemFactory.BuildDto(entity);
        _db.Set<LogDto>().Add(dto);
        _db.SaveChanges();
    }

    public void Delete(IAuditItem entity)
    {
        LogDto? dto = _db.Set<LogDto>().Find(entity.Id);
        if (dto != null) { _db.Set<LogDto>().Remove(dto); _db.SaveChanges(); }
    }

    public IEnumerable<IAuditItem> Get(IQuery<IAuditItem> query)
        => AuditItemFactory.BuildEntities(ApplyQuery(_db.Set<LogDto>().AsQueryable(), query).ToList());

    public int Count(IQuery<IAuditItem>? query)
    {
        IQueryable<LogDto> q = _db.Set<LogDto>().AsQueryable();
        if (query != null) { q = ApplyQuery(q, query); }
        return q.Count();
    }

    private static IQueryable<LogDto> ApplyQuery(IQueryable<LogDto> q, IQuery<IAuditItem> query)
    {
        foreach ((string clause, object[] args) in query.GetWhereClauses())
        {
            if (clause.Contains("userId", StringComparison.OrdinalIgnoreCase) && args.Length > 0 && args[0] is int userId)
                q = q.Where(x => x.UserId == userId);
            else if (clause.Contains("entityType", StringComparison.OrdinalIgnoreCase) && args.Length > 0 && args[0] is string et)
                q = q.Where(x => x.EntityType == et);
            else if (clause.Contains("entityId", StringComparison.OrdinalIgnoreCase) && args.Length > 0 && args[0] is int eid)
                q = q.Where(x => x.NodeId == eid);
        }
        return q;
    }
}
