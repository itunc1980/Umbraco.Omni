using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="ICacheInstructionRepository" />.</summary>
internal sealed class EfCacheInstructionRepository : ICacheInstructionRepository
{
    private readonly UmbracoDbContext _db;
    public EfCacheInstructionRepository(UmbracoDbContext db) => _db = db;

    public int CountAll() => _db.Set<CacheInstructionDto>().Count();

    public int CountPendingInstructions(int lastId)
        => _db.Set<CacheInstructionDto>().Count(x => x.Id > lastId);

    public int GetMaxId()
    {
        if (!_db.Set<CacheInstructionDto>().Any()) { return 0; }
        return _db.Set<CacheInstructionDto>().Max(x => x.Id);
    }

    public bool Exists(int id) => _db.Set<CacheInstructionDto>().Any(x => x.Id == id);

    public void Add(CacheInstruction cacheInstruction)
    {
        CacheInstructionDto dto = CacheInstructionFactory.BuildDto(cacheInstruction);
        _db.Set<CacheInstructionDto>().Add(dto);
        _db.SaveChanges();
    }

    public IEnumerable<CacheInstruction> GetPendingInstructions(int lastId, int maxNumberToRetrieve)
        => _db.Set<CacheInstructionDto>()
            .Where(x => x.Id > lastId)
            .OrderBy(x => x.Id)
            .Take(maxNumberToRetrieve)
            .ToList()
            .Select(CacheInstructionFactory.BuildEntity);

    public void DeleteInstructionsOlderThan(DateTime pruneDate)
    {
        _db.Set<CacheInstructionDto>().RemoveRange(
            _db.Set<CacheInstructionDto>().Where(x => x.UtcStamp < pruneDate));
        _db.SaveChanges();
    }
}
