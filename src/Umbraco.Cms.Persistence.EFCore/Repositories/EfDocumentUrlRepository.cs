using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IDocumentUrlRepository" />.</summary>
internal sealed class EfDocumentUrlRepository : IDocumentUrlRepository
{
    private readonly UmbracoDbContext _db;
    public EfDocumentUrlRepository(UmbracoDbContext db) => _db = db;

    public void Save(IEnumerable<PublishedDocumentUrlSegment> publishedDocumentUrlSegments)
    {
        foreach (PublishedDocumentUrlSegment segment in publishedDocumentUrlSegments)
        {
            DocumentUrlDto? existing = _db.Set<DocumentUrlDto>()
                .FirstOrDefault(x =>
                    x.UniqueId == segment.DocumentKey &&
                    x.LanguageId == segment.LanguageId &&
                    x.IsDraft == segment.IsDraft &&
                    x.UrlSegment == segment.UrlSegment);

            if (existing == null)
            {
                _db.Set<DocumentUrlDto>().Add(new DocumentUrlDto
                {
                    UniqueId = segment.DocumentKey,
                    LanguageId = segment.LanguageId,
                    IsDraft = segment.IsDraft,
                    UrlSegment = segment.UrlSegment,
                    IsPrimary = segment.IsPrimary,
                });
            }
            else
            {
                existing.IsPrimary = segment.IsPrimary;
            }
        }
        _db.SaveChanges();
    }

    public IEnumerable<PublishedDocumentUrlSegment> GetAll()
        => _db.Set<DocumentUrlDto>().ToList().Select(Map);

    public void DeleteByDocumentKey(IEnumerable<Guid> select)
    {
        Guid[] keys = select.ToArray();
        _db.Set<DocumentUrlDto>().RemoveRange(_db.Set<DocumentUrlDto>().Where(x => keys.Contains(x.UniqueId)));
        _db.SaveChanges();
    }

    private static PublishedDocumentUrlSegment Map(DocumentUrlDto dto) =>
        new()
        {
            DocumentKey = dto.UniqueId,
            LanguageId = dto.LanguageId,
            IsDraft = dto.IsDraft,
            UrlSegment = dto.UrlSegment,
            IsPrimary = dto.IsPrimary,
        };
}
