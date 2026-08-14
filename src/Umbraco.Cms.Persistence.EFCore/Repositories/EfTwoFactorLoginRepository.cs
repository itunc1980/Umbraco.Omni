using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="ITwoFactorLoginRepository" />.</summary>
internal sealed class EfTwoFactorLoginRepository : ITwoFactorLoginRepository
{
    private readonly UmbracoDbContext _db;
    public EfTwoFactorLoginRepository(UmbracoDbContext db) => _db = db;

    public ITwoFactorLogin? Get(int id)
    {
        TwoFactorLoginDto? dto = _db.Set<TwoFactorLoginDto>().Find(id);
        return dto == null ? null : Map(dto);
    }

    public IEnumerable<ITwoFactorLogin> GetMany(params int[]? ids)
    {
        IQueryable<TwoFactorLoginDto> q = _db.Set<TwoFactorLoginDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.ToList().Select(Map);
    }

    public bool Exists(int id) => _db.Set<TwoFactorLoginDto>().Any(x => x.Id == id);

    public void Save(ITwoFactorLogin entity)
    {
        TwoFactorLoginDto? existing = _db.Set<TwoFactorLoginDto>().Find(entity.Id);
        if (existing == null)
        {
            entity.AddingEntity();
            TwoFactorLoginDto dto = ToDto(entity);
            _db.Set<TwoFactorLoginDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.Id;
        }
        else
        {
            entity.UpdatingEntity();
            existing.ProviderName = entity.ProviderName;
            existing.Secret = entity.Secret;
            existing.UserOrMemberKey = entity.UserOrMemberKey;
            _db.SaveChanges();
        }
        entity.ResetDirtyProperties();
    }

    public void Delete(ITwoFactorLogin entity)
    {
        TwoFactorLoginDto? dto = _db.Set<TwoFactorLoginDto>().Find(entity.Id);
        if (dto != null) { _db.Set<TwoFactorLoginDto>().Remove(dto); _db.SaveChanges(); }
    }

    public IEnumerable<ITwoFactorLogin> Get(IQuery<ITwoFactorLogin> query)
        => _db.Set<TwoFactorLoginDto>().ToList().Select(Map);

    public Task<bool> DeleteUserLoginsAsync(Guid userOrMemberKey)
    {
        List<TwoFactorLoginDto> toDelete = _db.Set<TwoFactorLoginDto>().Where(x => x.UserOrMemberKey == userOrMemberKey).ToList();
        if (toDelete.Count == 0) { return Task.FromResult(false); }
        _db.Set<TwoFactorLoginDto>().RemoveRange(toDelete);
        _db.SaveChanges();
        return Task.FromResult(true);
    }

    public Task<bool> DeleteUserLoginsAsync(Guid userOrMemberKey, string providerName)
    {
        List<TwoFactorLoginDto> toDelete = _db.Set<TwoFactorLoginDto>()
            .Where(x => x.UserOrMemberKey == userOrMemberKey && x.ProviderName == providerName).ToList();
        if (toDelete.Count == 0) { return Task.FromResult(false); }
        _db.Set<TwoFactorLoginDto>().RemoveRange(toDelete);
        _db.SaveChanges();
        return Task.FromResult(true);
    }

    public Task<IEnumerable<ITwoFactorLogin>> GetByUserOrMemberKeyAsync(Guid userOrMemberKey)
    {
        IEnumerable<ITwoFactorLogin> result = _db.Set<TwoFactorLoginDto>()
            .Where(x => x.UserOrMemberKey == userOrMemberKey).ToList().Select(Map);
        return Task.FromResult(result);
    }

    private static ITwoFactorLogin Map(TwoFactorLoginDto dto) =>
        new TwoFactorLogin { Id = dto.Id, ProviderName = dto.ProviderName, Secret = dto.Secret, UserOrMemberKey = dto.UserOrMemberKey };

    private static TwoFactorLoginDto ToDto(ITwoFactorLogin entity) =>
        new() { Id = entity.Id, ProviderName = entity.ProviderName, Secret = entity.Secret, UserOrMemberKey = entity.UserOrMemberKey };
}
