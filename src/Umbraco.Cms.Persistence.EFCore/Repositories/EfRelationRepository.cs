using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;


namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IRelationRepository" />.</summary>
internal sealed class EfRelationRepository : IRelationRepository
{
    private readonly UmbracoDbContext _db;
    public EfRelationRepository(UmbracoDbContext db) => _db = db;

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IRelationType? LoadRelationType(int id)
    {
        RelationTypeDto? dto = _db.Set<RelationTypeDto>().Find(id);
        return dto == null ? null : RelationTypeFactory.BuildEntity(dto);
    }

    private IRelation? BuildEntity(RelationDto dto)
    {
        IRelationType? rt = LoadRelationType(dto.RelationType);
        return rt == null ? null : RelationFactory.BuildEntity(dto, rt);
    }

    // ─── IReadRepository<int, IRelation> ────────────────────────────────────
    public IRelation? Get(int id)
    {
        RelationDto? dto = _db.Set<RelationDto>().Find(id);
        return dto == null ? null : BuildEntity(dto);
    }

    public IEnumerable<IRelation> GetMany(params int[]? ids)
    {
        IQueryable<RelationDto> q = _db.Set<RelationDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IRelation>();
    }

    public bool Exists(int id) => _db.Set<RelationDto>().Any(x => x.Id == id);

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(IRelation entity)
    {
        RelationDto dto = RelationFactory.BuildDto(entity);
        RelationDto? existing = _db.Set<RelationDto>().Find(entity.Id);
        if (existing == null)
        {
            entity.AddingEntity();
            dto.Datetime = entity.CreateDate;
            _db.Set<RelationDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.Id;
        }
        else
        {
            entity.UpdatingEntity();
            existing.Comment = dto.Comment;
            existing.Datetime = dto.Datetime;
            _db.SaveChanges();
        }
        entity.ResetDirtyProperties();
    }

    public void Save(IEnumerable<IRelation> relations)
    {
        foreach (IRelation r in relations) { Save(r); }
    }

    public void SaveBulk(IEnumerable<ReadOnlyRelation> relations)
    {
        foreach (ReadOnlyRelation r in relations)
        {
            RelationDto dto = RelationFactory.BuildDto(r);
            _db.Set<RelationDto>().Add(dto);
        }
        _db.SaveChanges();
    }

    public void Delete(IRelation entity)
    {
        RelationDto? dto = _db.Set<RelationDto>().Find(entity.Id);
        if (dto != null) { _db.Set<RelationDto>().Remove(dto); _db.SaveChanges(); }
    }

    public void DeleteByParent(int parentId, params string[] relationTypeAliases)
    {
        IQueryable<RelationDto> q = _db.Set<RelationDto>().Where(x => x.ParentId == parentId);
        if (relationTypeAliases.Length > 0)
        {
            int[] typeIds = _db.Set<RelationTypeDto>()
                .Where(t => relationTypeAliases.Contains(t.Alias))
                .Select(t => t.Id).ToArray();
            q = q.Where(x => typeIds.Contains(x.RelationType));
        }
        _db.Set<RelationDto>().RemoveRange(q);
        _db.SaveChanges();
    }

    // ─── IQueryRepository ───────────────────────────────────────────────────
    public IEnumerable<IRelation> Get(IQuery<IRelation> query)
        => _db.Set<RelationDto>().ToList().Select(BuildEntity).Where(x => x != null).Cast<IRelation>();

    public int Count(IQuery<IRelation>? query) => _db.Set<RelationDto>().Count();

    // ─── Paged queries ──────────────────────────────────────────────────────
    public IEnumerable<IRelation> GetPagedRelationsByQuery(
        IQuery<IRelation>? query,
        long pageIndex,
        int pageSize,
        out long totalRecords,
        Ordering? ordering)
    {
        IQueryable<RelationDto> q = _db.Set<RelationDto>();
        totalRecords = q.LongCount();
        List<RelationDto> page = q
            .OrderBy(x => x.Id)
            .Skip((int)(pageIndex * pageSize))
            .Take(pageSize)
            .ToList();
        return page.Select(BuildEntity).Where(x => x != null).Cast<IRelation>();
    }

    public IEnumerable<IUmbracoEntity> GetPagedParentEntitiesByChildId(
        int childId, long pageIndex, int pageSize, out long totalRecords, params Guid[] entityTypes)
    {
        IQueryable<int> parentIds = _db.Set<RelationDto>().Where(x => x.ChildId == childId).Select(x => x.ParentId);
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(n => parentIds.Contains(n.NodeId));
        if (entityTypes.Length > 0) { q = q.Where(n => entityTypes.Contains(n.NodeObjectType!.Value)); }
        totalRecords = q.LongCount();
        return q.Skip((int)(pageIndex * pageSize)).Take(pageSize).ToList()
                .Select(n => (IUmbracoEntity)new EntitySlim { Id = n.NodeId, Key = n.UniqueId, Name = n.Text, Trashed = n.Trashed });
    }

    public IEnumerable<IUmbracoEntity> GetPagedChildEntitiesByParentId(
        int parentId, long pageIndex, int pageSize, out long totalRecords, params Guid[] entityTypes)
    {
        IQueryable<int> childIds = _db.Set<RelationDto>().Where(x => x.ParentId == parentId).Select(x => x.ChildId);
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(n => childIds.Contains(n.NodeId));
        if (entityTypes.Length > 0) { q = q.Where(n => entityTypes.Contains(n.NodeObjectType!.Value)); }
        totalRecords = q.LongCount();
        return q.Skip((int)(pageIndex * pageSize)).Take(pageSize).ToList()
                .Select(n => (IUmbracoEntity)new EntitySlim { Id = n.NodeId, Key = n.UniqueId, Name = n.Text, Trashed = n.Trashed });
    }

    public Task<PagedModel<IRelation>> GetPagedByChildKeyAsync(Guid childKey, int skip, int take, string? relationTypeAlias)
    {
        // Resolve child node id by key
        int? childId = _db.Set<NodeDto>().Where(n => n.UniqueId == childKey).Select(n => (int?)n.NodeId).FirstOrDefault();
        if (childId == null)
        {
            return Task.FromResult(new PagedModel<IRelation> { Items = Enumerable.Empty<IRelation>(), Total = 0 });
        }

        IQueryable<RelationDto> q = _db.Set<RelationDto>().Where(x => x.ChildId == childId);
        if (!string.IsNullOrEmpty(relationTypeAlias))
        {
            int[] typeIds = _db.Set<RelationTypeDto>()
                .Where(t => t.Alias == relationTypeAlias)
                .Select(t => t.Id).ToArray();
            q = q.Where(x => typeIds.Contains(x.RelationType));
        }

        long total = q.LongCount();
        IEnumerable<IRelation> items = q.OrderBy(x => x.Id).Skip(skip).Take(take).ToList()
            .Select(BuildEntity).Where(x => x != null).Cast<IRelation>();

        return Task.FromResult(new PagedModel<IRelation> { Items = items, Total = total });
    }
}
