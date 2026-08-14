using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IDocumentUrlAliasRepository" />.</summary>
internal sealed class EfDocumentUrlAliasRepository : IDocumentUrlAliasRepository
{
    private readonly UmbracoDbContext _db;
    public EfDocumentUrlAliasRepository(UmbracoDbContext db) => _db = db;

    public void Save(IEnumerable<PublishedDocumentUrlAlias> aliases)
    {
        PublishedDocumentUrlAlias[] aliasArr = aliases.ToArray();

        // Collect all document keys in this batch
        Guid[] docKeys = aliasArr.Select(a => a.DocumentKey).Distinct().ToArray();

        // Remove existing aliases for these documents
        _db.Set<DocumentUrlAliasDto>().RemoveRange(
            _db.Set<DocumentUrlAliasDto>().Where(x => docKeys.Contains(x.UniqueId)));
        _db.SaveChanges();

        // Insert all provided aliases
        foreach (PublishedDocumentUrlAlias alias in aliasArr)
        {
            _db.Set<DocumentUrlAliasDto>().Add(new DocumentUrlAliasDto
            {
                UniqueId = alias.DocumentKey,
                LanguageId = alias.LanguageId,
                Alias = alias.Alias,
            });
        }
        _db.SaveChanges();
    }

    public IEnumerable<PublishedDocumentUrlAlias> GetAll()
        => _db.Set<DocumentUrlAliasDto>().ToList().Select(Map);

    public void DeleteByDocumentKey(IEnumerable<Guid> documentKeys)
    {
        Guid[] keys = documentKeys.ToArray();
        _db.Set<DocumentUrlAliasDto>().RemoveRange(_db.Set<DocumentUrlAliasDto>().Where(x => keys.Contains(x.UniqueId)));
        _db.SaveChanges();
    }

    public IEnumerable<DocumentUrlAliasRaw> GetAllDocumentUrlAliases()
    {
        // Returns raw alias data from the umbracoUrlAlias property stored in DocumentUrlAliasDto
        return _db.Set<DocumentUrlAliasDto>().ToList().Select(x => new DocumentUrlAliasRaw
        {
            DocumentKey = x.UniqueId,
            LanguageId = x.LanguageId,
            AliasValue = x.Alias,
        });
    }

    private static PublishedDocumentUrlAlias Map(DocumentUrlAliasDto dto) =>
        new() { DocumentKey = dto.UniqueId, LanguageId = dto.LanguageId, Alias = dto.Alias };
}
