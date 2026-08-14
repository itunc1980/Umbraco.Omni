using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IServerRegistrationRepository" />.</summary>
internal sealed class EfServerRegistrationRepository : IServerRegistrationRepository
{
    private readonly UmbracoDbContext _db;
    public EfServerRegistrationRepository(UmbracoDbContext db) => _db = db;

    public void ClearCache() { /* no-op: no runtime cache in EF Core impl */ }

    public void DeactiveStaleServers(TimeSpan staleTimeout)
    {
        DateTime timeoutDate = DateTime.UtcNow.Subtract(staleTimeout);
        foreach (ServerRegistrationDto dto in _db.Set<ServerRegistrationDto>()
            .Where(x => x.DateAccessed < timeoutDate).ToList())
        {
            dto.IsActive = false;
            dto.IsSchedulingPublisher = false;
        }
        _db.SaveChanges();
    }

    public IServerRegistration? Get(int id)
    {
        ServerRegistrationDto? dto = _db.Set<ServerRegistrationDto>().Find(id);
        return dto == null ? null : ServerRegistrationFactory.BuildEntity(dto);
    }

    public IEnumerable<IServerRegistration> GetMany(params int[]? ids)
    {
        IQueryable<ServerRegistrationDto> q = _db.Set<ServerRegistrationDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.ToList().Select(x => (IServerRegistration)ServerRegistrationFactory.BuildEntity(x));
    }

    public bool Exists(int id) => _db.Set<ServerRegistrationDto>().Any(x => x.Id == id);

    public void Save(IServerRegistration entity)
    {
        ServerRegistrationDto dto = ServerRegistrationFactory.BuildDto(entity);
        if (entity.Id == 0)
        {
            entity.AddingEntity();
            _db.Set<ServerRegistrationDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.Id;
        }
        else
        {
            entity.UpdatingEntity();
            _db.Set<ServerRegistrationDto>().Update(dto);
            _db.SaveChanges();
        }
        entity.ResetDirtyProperties();
    }

    public void Delete(IServerRegistration entity)
    {
        ServerRegistrationDto? dto = _db.Set<ServerRegistrationDto>().Find(entity.Id);
        if (dto != null) { _db.Set<ServerRegistrationDto>().Remove(dto); _db.SaveChanges(); }
    }

    public IEnumerable<IServerRegistration> Get(IQuery<IServerRegistration> query)
        => _db.Set<ServerRegistrationDto>().ToList().Select(x => (IServerRegistration)ServerRegistrationFactory.BuildEntity(x));

    public int Count(IQuery<IServerRegistration>? query) => _db.Set<ServerRegistrationDto>().Count();
}
