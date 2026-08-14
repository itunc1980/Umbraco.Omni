using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Querying;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IUserDataRepository" />.</summary>
internal sealed class EfUserDataRepository : IUserDataRepository
{
    private readonly UmbracoDbContext _db;
    public EfUserDataRepository(UmbracoDbContext db) => _db = db;

    public Task<IUserData?> GetAsync(Guid key)
    {
        UserDataDto? dto = _db.Set<UserDataDto>().Find(key);
        return Task.FromResult(dto == null ? null : (IUserData?)Map(dto));
    }

    public Task<PagedModel<IUserData>> GetAsync(int skip, int take, IUserDataFilter? filter = null)
    {
        IQueryable<UserDataDto> q = _db.Set<UserDataDto>();
        if (filter != null)
        {
            if (filter.UserKeys?.Count > 0)
            {
                Guid[] keys = filter.UserKeys.ToArray();
                q = q.Where(x => keys.Contains(x.UserKey));
            }
            if (filter.Groups?.Count > 0)
            {
                string[] groups = filter.Groups.ToArray();
                q = q.Where(x => groups.Contains(x.Group));
            }
            if (filter.Identifiers?.Count > 0)
            {
                string[] identifiers = filter.Identifiers.ToArray();
                q = q.Where(x => identifiers.Contains(x.Identifier));
            }
        }

        long total = q.LongCount();
        IEnumerable<IUserData> items = q.OrderBy(x => x.Key).Skip(skip).Take(take).ToList().Select(Map);
        return Task.FromResult(new PagedModel<IUserData> { Items = items, Total = total });
    }

    public Task<IUserData> Save(IUserData userData)
    {
        UserDataDto dto = ToDto(userData);
        if (dto.Key == Guid.Empty) { dto.Key = Guid.NewGuid(); }
        _db.Set<UserDataDto>().Add(dto);
        _db.SaveChanges();
        userData.Key = dto.Key;
        return Task.FromResult(userData);
    }

    public Task<IUserData> Update(IUserData userData)
    {
        UserDataDto? existing = _db.Set<UserDataDto>().Find(userData.Key);
        if (existing != null)
        {
            existing.Group = userData.Group;
            existing.Identifier = userData.Identifier;
            existing.Value = userData.Value;
            _db.SaveChanges();
        }
        return Task.FromResult(userData);
    }

    public Task Delete(IUserData userData)
    {
        UserDataDto? dto = _db.Set<UserDataDto>().Find(userData.Key);
        if (dto != null) { _db.Set<UserDataDto>().Remove(dto); _db.SaveChanges(); }
        return Task.CompletedTask;
    }

    private static IUserData Map(UserDataDto dto) =>
        new UserData { Key = dto.Key, UserKey = dto.UserKey, Group = dto.Group, Identifier = dto.Identifier, Value = dto.Value };

    private static UserDataDto ToDto(IUserData entity) =>
        new() { Key = entity.Key, UserKey = entity.UserKey, Group = entity.Group, Identifier = entity.Identifier, Value = entity.Value };
}
