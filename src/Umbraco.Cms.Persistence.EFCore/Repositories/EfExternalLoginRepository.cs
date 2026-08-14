using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IExternalLoginWithKeyRepository" />.</summary>
internal sealed class EfExternalLoginRepository : IExternalLoginWithKeyRepository
{
    private readonly UmbracoDbContext _db;
    public EfExternalLoginRepository(UmbracoDbContext db) => _db = db;

    // ─── IReadRepository<int, IIdentityUserLogin> ───────────────────────────
    public IIdentityUserLogin? Get(int id)
    {
        ExternalLoginDto? dto = _db.Set<ExternalLoginDto>().Find(id);
        return dto == null ? null : ExternalLoginFactory.BuildEntity(dto);
    }

    public IEnumerable<IIdentityUserLogin> GetMany(params int[]? ids)
    {
        IQueryable<ExternalLoginDto> q = _db.Set<ExternalLoginDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.ToList().Select(ExternalLoginFactory.BuildEntity);
    }

    public bool Exists(int id) => _db.Set<ExternalLoginDto>().Any(x => x.Id == id);

    // ─── IWriteRepository<IIdentityUserLogin> ───────────────────────────────
    public void Save(IIdentityUserLogin entity)
    {
        ExternalLoginDto dto = ExternalLoginFactory.BuildDto(entity);
        ExternalLoginDto? existing = _db.Set<ExternalLoginDto>().Find(entity.Id);
        if (existing == null)
        {
            entity.AddingEntity();
            _db.Set<ExternalLoginDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.Id;
        }
        else
        {
            entity.UpdatingEntity();
            existing.LoginProvider = dto.LoginProvider;
            existing.ProviderKey = dto.ProviderKey;
            existing.UserOrMemberKey = dto.UserOrMemberKey;
            existing.UserData = dto.UserData;
            _db.SaveChanges();
        }
        entity.ResetDirtyProperties();
    }

    public void Delete(IIdentityUserLogin entity)
    {
        ExternalLoginDto? dto = _db.Set<ExternalLoginDto>().Find(entity.Id);
        if (dto != null)
        {
            _db.Set<ExternalLoginTokenDto>().RemoveRange(_db.Set<ExternalLoginTokenDto>().Where(t => t.ExternalLoginId == dto.Id));
            _db.Set<ExternalLoginDto>().Remove(dto);
            _db.SaveChanges();
        }
    }

    // ─── IQueryRepository<IIdentityUserLogin> ───────────────────────────────
    public IEnumerable<IIdentityUserLogin> Get(IQuery<IIdentityUserLogin> query)
        => _db.Set<ExternalLoginDto>().ToList().Select(ExternalLoginFactory.BuildEntity);

    public int Count(IQuery<IIdentityUserLogin>? query) => _db.Set<ExternalLoginDto>().Count();

    // ─── IQueryRepository<IIdentityUserToken> ───────────────────────────────
    public IEnumerable<IIdentityUserToken> Get(IQuery<IIdentityUserToken>? query)
    {
        // Load tokens with their parent external login via join
        var tokens = _db.Set<ExternalLoginTokenDto>()
            .Join(_db.Set<ExternalLoginDto>(),
                t => t.ExternalLoginId,
                l => l.Id,
                (t, l) => new { Token = t, Login = l })
            .ToList();
        foreach (var item in tokens)
        {
            item.Token.ExternalLoginDto = item.Login;
            yield return ExternalLoginFactory.BuildEntity(item.Token);
        }
    }

    public int Count(IQuery<IIdentityUserToken>? query)
        => _db.Set<ExternalLoginTokenDto>().Count();

    // ─── IExternalLoginWithKeyRepository ───────────────────────────────────
    public void Save(Guid userOrMemberKey, IEnumerable<IExternalLogin> logins)
    {
        IExternalLogin[] loginArr = logins.ToArray();
        List<ExternalLoginDto> existing = _db.Set<ExternalLoginDto>()
            .Where(x => x.UserOrMemberKey == userOrMemberKey).ToList();

        // remove any logins not in the new set
        List<ExternalLoginDto> toRemove = existing
            .Where(e => !loginArr.Any(l => l.LoginProvider == e.LoginProvider)).ToList();
        if (toRemove.Count > 0)
        {
            int[] removeIds = toRemove.Select(r => r.Id).ToArray();
            _db.Set<ExternalLoginTokenDto>().RemoveRange(_db.Set<ExternalLoginTokenDto>().Where(t => removeIds.Contains(t.ExternalLoginId)));
            _db.Set<ExternalLoginDto>().RemoveRange(toRemove);
        }

        foreach (IExternalLogin login in loginArr)
        {
            ExternalLoginDto? found = existing.FirstOrDefault(e => e.LoginProvider == login.LoginProvider);
            if (found == null)
            {
                ExternalLoginDto dto = ExternalLoginFactory.BuildDto(userOrMemberKey, login);
                _db.Set<ExternalLoginDto>().Add(dto);
            }
            else
            {
                found.ProviderKey = login.ProviderKey;
                found.UserData = login.UserData;
            }
        }
        _db.SaveChanges();
    }

    public void Save(Guid userOrMemberKey, IEnumerable<IExternalLoginToken> tokens)
    {
        IExternalLoginToken[] tokenArr = tokens.ToArray();
        // Find all external logins for this user/member
        List<ExternalLoginDto> userLogins = _db.Set<ExternalLoginDto>()
            .Where(x => x.UserOrMemberKey == userOrMemberKey).ToList();
        int[] loginIds = userLogins.Select(l => l.Id).ToArray();

        List<ExternalLoginTokenDto> existingTokens = _db.Set<ExternalLoginTokenDto>()
            .Where(t => loginIds.Contains(t.ExternalLoginId)).ToList();

        // Group tokens by provider: find parent login
        foreach (IExternalLoginToken token in tokenArr)
        {
            ExternalLoginDto? login = userLogins.FirstOrDefault(l => l.LoginProvider == token.LoginProvider);
            if (login == null) { continue; }

            ExternalLoginTokenDto? existing = existingTokens.FirstOrDefault(
                t => t.ExternalLoginId == login.Id && t.Name == token.Name);
            if (existing == null)
            {
                _db.Set<ExternalLoginTokenDto>().Add(
                    ExternalLoginFactory.BuildDto(login.Id, token));
            }
            else
            {
                existing.Value = token.Value;
            }
        }

        // Remove tokens whose provider-name combo is gone
        var tokenLookup = tokenArr.Select(t => (t.LoginProvider, t.Name)).ToHashSet();
        foreach (ExternalLoginTokenDto old in existingTokens)
        {
            ExternalLoginDto? login = userLogins.FirstOrDefault(l => l.Id == old.ExternalLoginId);
            if (login == null || !tokenLookup.Contains((login.LoginProvider, old.Name)))
            {
                _db.Set<ExternalLoginTokenDto>().Remove(old);
            }
        }

        _db.SaveChanges();
    }

    public void DeleteUserLogins(Guid userOrMemberKey)
    {
        List<ExternalLoginDto> logins = _db.Set<ExternalLoginDto>()
            .Where(x => x.UserOrMemberKey == userOrMemberKey).ToList();
        int[] ids = logins.Select(l => l.Id).ToArray();
        _db.Set<ExternalLoginTokenDto>().RemoveRange(_db.Set<ExternalLoginTokenDto>().Where(t => ids.Contains(t.ExternalLoginId)));
        _db.Set<ExternalLoginDto>().RemoveRange(logins);
        _db.SaveChanges();
    }

    public void DeleteUserLoginsForRemovedProviders(IEnumerable<string> currentLoginProviders)
    {
        string[] providers = currentLoginProviders.ToArray();
        List<ExternalLoginDto> toRemove = _db.Set<ExternalLoginDto>()
            .Where(x => !providers.Contains(x.LoginProvider)).ToList();
        if (toRemove.Count == 0) { return; }
        int[] ids = toRemove.Select(l => l.Id).ToArray();
        _db.Set<ExternalLoginTokenDto>().RemoveRange(_db.Set<ExternalLoginTokenDto>().Where(t => ids.Contains(t.ExternalLoginId)));
        _db.Set<ExternalLoginDto>().RemoveRange(toRemove);
        _db.SaveChanges();
    }
}
