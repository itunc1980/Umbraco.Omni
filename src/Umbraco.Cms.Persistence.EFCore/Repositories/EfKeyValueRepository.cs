using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IKeyValueRepository" />.</summary>
internal sealed class EfKeyValueRepository : IKeyValueRepository
{
    private readonly UmbracoDbContext _db;
    public EfKeyValueRepository(UmbracoDbContext db) => _db = db;

    public IReadOnlyDictionary<string, string?>? FindByKeyPrefix(string keyPrefix)
        => _db.Set<KeyValueDto>()
              .Where(x => x.Key != null && x.Key.StartsWith(keyPrefix))
              .ToDictionary(x => x.Key!, x => x.Value);

    public IKeyValue? Get(string? id)
    {
        if (id == null) { return null; }
        KeyValueDto? dto = _db.Set<KeyValueDto>().Find(id);
        return dto == null ? null : Map(dto);
    }

    public IEnumerable<IKeyValue> GetMany(params string[]? ids)
    {
        IQueryable<KeyValueDto> q = _db.Set<KeyValueDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Key)); }
        return q.ToList().Select(Map).Where(x => x != null)!;
    }

    public bool Exists(string id) => _db.Set<KeyValueDto>().Any(x => x.Key == id);

    public void Save(IKeyValue entity)
    {
        KeyValueDto? existing = _db.Set<KeyValueDto>().Find(entity.Identifier);
        if (existing == null)
        {
            _db.Set<KeyValueDto>().Add(new KeyValueDto { Key = entity.Identifier, Value = entity.Value, UpdateDate = entity.UpdateDate });
        }
        else
        {
            existing.Value = entity.Value;
            existing.UpdateDate = entity.UpdateDate;
            _db.Set<KeyValueDto>().Update(existing);
        }
        _db.SaveChanges();
    }

    public void Delete(IKeyValue entity)
    {
        KeyValueDto? dto = _db.Set<KeyValueDto>().Find(entity.Identifier);
        if (dto != null) { _db.Set<KeyValueDto>().Remove(dto); _db.SaveChanges(); }
    }

    private static IKeyValue Map(KeyValueDto dto)
        => new KeyValue { Identifier = dto.Key!, Value = dto.Value, UpdateDate = dto.UpdateDate };
}
