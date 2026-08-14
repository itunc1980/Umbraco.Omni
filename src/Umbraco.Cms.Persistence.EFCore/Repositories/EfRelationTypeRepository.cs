using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IRelationTypeRepository" />.</summary>
internal sealed class EfRelationTypeRepository : IRelationTypeRepository
{
    private readonly UmbracoDbContext _db;
    public EfRelationTypeRepository(UmbracoDbContext db) => _db = db;

    // ─── IReadRepository<int, IRelationType> ────────────────────────────────
    public IRelationType? Get(int id)
    {
        RelationTypeDto? dto = _db.Set<RelationTypeDto>().Find(id);
        return dto == null ? null : RelationTypeFactory.BuildEntity(dto);
    }

    // ─── IReadRepository<Guid, IRelationType> ───────────────────────────────
    public IRelationType? Get(Guid id)
    {
        RelationTypeDto? dto = _db.Set<RelationTypeDto>().FirstOrDefault(x => x.UniqueId == id);
        return dto == null ? null : RelationTypeFactory.BuildEntity(dto);
    }

    public IEnumerable<IRelationType> GetMany(params Guid[]? ids)
    {
        IQueryable<RelationTypeDto> q = _db.Set<RelationTypeDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.UniqueId)); }
        return q.ToList().Select(RelationTypeFactory.BuildEntity);
    }

    public bool Exists(Guid id) => _db.Set<RelationTypeDto>().Any(x => x.UniqueId == id);

    public IEnumerable<IRelationType> GetMany(params int[]? ids)
    {
        IQueryable<RelationTypeDto> q = _db.Set<RelationTypeDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.ToList().Select(RelationTypeFactory.BuildEntity);
    }

    public bool Exists(int id) => _db.Set<RelationTypeDto>().Any(x => x.Id == id);

    public void Save(IRelationType entity)
    {
        RelationTypeDto dto = RelationTypeFactory.BuildDto(entity);
        RelationTypeDto? existing = _db.Set<RelationTypeDto>().Find(entity.Id);
        if (existing == null)
        {
            entity.AddingEntity();
            if (dto.UniqueId == Guid.Empty) { dto.UniqueId = entity.Key == Guid.Empty ? Guid.NewGuid() : entity.Key; }
            _db.Set<RelationTypeDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.Id;
        }
        else
        {
            entity.UpdatingEntity();
            existing.Alias = dto.Alias;
            existing.Name = dto.Name;
            existing.Dual = dto.Dual;
            existing.IsDependency = dto.IsDependency;
            existing.ParentObjectType = dto.ParentObjectType;
            existing.ChildObjectType = dto.ChildObjectType;
            _db.SaveChanges();
        }
        entity.ResetDirtyProperties();
    }

    public void Delete(IRelationType entity)
    {
        RelationTypeDto? dto = _db.Set<RelationTypeDto>().Find(entity.Id);
        if (dto != null) { _db.Set<RelationTypeDto>().Remove(dto); _db.SaveChanges(); }
    }

    public IEnumerable<IRelationType> Get(IQuery<IRelationType> query)
        => _db.Set<RelationTypeDto>().ToList().Select(RelationTypeFactory.BuildEntity);

    public int Count(IQuery<IRelationType>? query) => _db.Set<RelationTypeDto>().Count();
}
