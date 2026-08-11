using System.Security.Cryptography;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IRedirectUrlRepository" />.</summary>
internal sealed class EfRedirectUrlRepository : IRedirectUrlRepository
{
    private readonly UmbracoDbContext _db;
    public EfRedirectUrlRepository(UmbracoDbContext db) => _db = db;

    public IRedirectUrl? Get(string url, Guid contentKey, string? culture)
    {
        var urlHash = url.GenerateHash<SHA1>();
        RedirectUrlDto? dto = _db.Set<RedirectUrlDto>()
            .FirstOrDefault(x => x.Url == url && x.UrlHash == urlHash && x.ContentKey == contentKey && x.Culture == culture);
        return dto == null ? null : Map(dto);
    }

    public void DeleteAll()
    {
        _db.Set<RedirectUrlDto>().RemoveRange(_db.Set<RedirectUrlDto>().ToList());
        _db.SaveChanges();
    }

    public void DeleteContentUrls(Guid contentKey)
    {
        _db.Set<RedirectUrlDto>().RemoveRange(_db.Set<RedirectUrlDto>().Where(x => x.ContentKey == contentKey));
        _db.SaveChanges();
    }

    public void Delete(Guid id)
    {
        RedirectUrlDto? dto = _db.Set<RedirectUrlDto>().FirstOrDefault(x => x.Id == id);
        if (dto != null) { _db.Set<RedirectUrlDto>().Remove(dto); _db.SaveChanges(); }
    }

    public IRedirectUrl? GetMostRecentUrl(string url)
    {
        var urlHash = url.GenerateHash<SHA1>();
        RedirectUrlDto? dto = _db.Set<RedirectUrlDto>()
            .Where(x => x.Url == url && x.UrlHash == urlHash)
            .OrderByDescending(x => x.CreateDateUtc).FirstOrDefault();
        return dto == null ? null : Map(dto);
    }

    public async Task<IRedirectUrl?> GetMostRecentUrlAsync(string url)
        => await Task.FromResult(GetMostRecentUrl(url));

    public IRedirectUrl? GetMostRecentUrl(string url, string culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) { return GetMostRecentUrl(url); }
        var urlHash = url.GenerateHash<SHA1>();
        RedirectUrlDto? dto = _db.Set<RedirectUrlDto>()
            .Where(x => x.Url == url && x.UrlHash == urlHash)
            .OrderByDescending(x => x.CreateDateUtc).ToList()
            .FirstOrDefault(x => culture.InvariantEquals(x.Culture));
        return dto == null ? null : Map(dto);
    }

    public async Task<IRedirectUrl?> GetMostRecentUrlAsync(string url, string culture)
        => await Task.FromResult(GetMostRecentUrl(url, culture));

    public IEnumerable<IRedirectUrl> GetContentUrls(Guid contentKey)
        => _db.Set<RedirectUrlDto>().Where(x => x.ContentKey == contentKey)
              .OrderByDescending(x => x.CreateDateUtc).ToList().Select(Map);

    public IEnumerable<IRedirectUrl> GetAllUrls(long pageIndex, int pageSize, out long total)
    {
        IQueryable<RedirectUrlDto> q = _db.Set<RedirectUrlDto>().OrderByDescending(x => x.CreateDateUtc);
        total = q.LongCount();
        return q.Skip((int)(pageIndex * pageSize)).Take(pageSize).ToList().Select(Map);
    }

    public IEnumerable<IRedirectUrl> GetAllUrls(int rootContentId, long pageIndex, int pageSize, out long total)
    {
        // Simplified: returns paged redirect URLs without deep node-hierarchy filtering
        IQueryable<RedirectUrlDto> q = _db.Set<RedirectUrlDto>().OrderByDescending(x => x.CreateDateUtc);
        total = q.LongCount();
        return q.Skip((int)(pageIndex * pageSize)).Take(pageSize).ToList().Select(Map);
    }

    public IEnumerable<IRedirectUrl> SearchUrls(string searchTerm, long pageIndex, int pageSize, out long total)
    {
        IQueryable<RedirectUrlDto> q = _db.Set<RedirectUrlDto>()
            .Where(x => x.Url != null && x.Url.Contains(searchTerm))
            .OrderByDescending(x => x.CreateDateUtc);
        total = q.LongCount();
        return q.Skip((int)(pageIndex * pageSize)).Take(pageSize).ToList().Select(Map);
    }

    // IReadRepository<Guid, IRedirectUrl> — entity.Id is int (EntityBase), Key is Guid
    public IRedirectUrl? Get(Guid id)
    {
        // The Guid here refers to RedirectUrlDto.Id (the DTO primary key which is also the model's Key)
        RedirectUrlDto? dto = _db.Set<RedirectUrlDto>().FirstOrDefault(x => x.Id == id);
        return dto == null ? null : Map(dto);
    }

    public IEnumerable<IRedirectUrl> GetMany(params Guid[]? ids)
    {
        IQueryable<RedirectUrlDto> q = _db.Set<RedirectUrlDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.ToList().Select(Map);
    }

    public bool Exists(Guid id) => _db.Set<RedirectUrlDto>().Any(x => x.Id == id);

    public void Save(IRedirectUrl entity)
    {
        var urlHash = entity.Url.GenerateHash<SHA1>();
        Guid dtoId = entity.Key == Guid.Empty ? Guid.NewGuid() : entity.Key;
        RedirectUrlDto? existing = _db.Set<RedirectUrlDto>().FirstOrDefault(x => x.Id == dtoId);
        if (existing == null)
        {
            var dto = new RedirectUrlDto
            {
                Id = dtoId,
                ContentKey = entity.ContentKey,
                Url = entity.Url,
                UrlHash = urlHash,
                Culture = entity.Culture,
                CreateDateUtc = entity.CreateDateUtc,
            };
            _db.Set<RedirectUrlDto>().Add(dto);
        }
        else
        {
            existing.Url = entity.Url;
            existing.UrlHash = urlHash;
            existing.ContentKey = entity.ContentKey;
            _db.Set<RedirectUrlDto>().Update(existing);
        }
        _db.SaveChanges();
        entity.Key = dtoId;
    }

    public void Delete(IRedirectUrl entity) => Delete(entity.Key);

    public IEnumerable<IRedirectUrl> Get(IQuery<IRedirectUrl> query)
        => throw new NotSupportedException();

    public int Count(IQuery<IRedirectUrl>? query) => _db.Set<RedirectUrlDto>().Count();

    private static IRedirectUrl Map(RedirectUrlDto dto) => new RedirectUrl
    {
        Key = dto.Id,
        ContentKey = dto.ContentKey,
        Url = dto.Url,
        Culture = dto.Culture,
        CreateDateUtc = dto.CreateDateUtc,
    };
}
