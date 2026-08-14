using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IDictionaryRepository" />.</summary>
internal sealed class EfDictionaryRepository : IDictionaryRepository
{
    private readonly UmbracoDbContext _db;
    public EfDictionaryRepository(UmbracoDbContext db) => _db = db;

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IDictionaryItem BuildWithTranslations(DictionaryDto dto)
    {
        IDictionaryItem item = DictionaryItemFactory.BuildEntity(dto);
        item.Translations = LoadTranslations(dto.UniqueId);
        return item;
    }

    private IEnumerable<IDictionaryTranslation> LoadTranslations(Guid uniqueId)
    {
        List<LanguageTextDto> texts = _db.Set<LanguageTextDto>().Where(t => t.UniqueId == uniqueId).ToList();
        List<IDictionaryTranslation> result = new();
        foreach (LanguageTextDto text in texts)
        {
            LanguageDto? lang = _db.Set<LanguageDto>().Find(text.LanguageId);
            if (lang != null)
            {
                ILanguage langEntity = LanguageFactory.BuildEntity(lang, null);
                result.Add(DictionaryTranslationFactory.BuildEntity(text, uniqueId, langEntity));
            }
        }
        return result;
    }

    // ─── IReadRepository<int, IDictionaryItem> ──────────────────────────────
    public IDictionaryItem? Get(int id)
    {
        DictionaryDto? dto = _db.Set<DictionaryDto>().Find(id);
        return dto == null ? null : BuildWithTranslations(dto);
    }

    // ─── IDictionaryRepository extra lookups ────────────────────────────────
    public IDictionaryItem? Get(Guid uniqueId)
    {
        DictionaryDto? dto = _db.Set<DictionaryDto>().FirstOrDefault(x => x.UniqueId == uniqueId);
        return dto == null ? null : BuildWithTranslations(dto);
    }

    public IDictionaryItem? Get(string key)
    {
        DictionaryDto? dto = _db.Set<DictionaryDto>().FirstOrDefault(x => x.Key == key);
        return dto == null ? null : BuildWithTranslations(dto);
    }

    public IEnumerable<IDictionaryItem> GetMany(params int[]? ids)
    {
        IQueryable<DictionaryDto> q = _db.Set<DictionaryDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.PrimaryKey)); }
        return q.ToList().Select(BuildWithTranslations);
    }

    public IEnumerable<IDictionaryItem> GetMany(params Guid[] uniqueIds)
    {
        if (uniqueIds.Length == 0) { return _db.Set<DictionaryDto>().ToList().Select(BuildWithTranslations); }
        return _db.Set<DictionaryDto>().Where(x => uniqueIds.Contains(x.UniqueId)).ToList().Select(BuildWithTranslations);
    }

    public IEnumerable<IDictionaryItem> GetManyByKeys(params string[] keys)
    {
        if (keys.Length == 0) { return _db.Set<DictionaryDto>().ToList().Select(BuildWithTranslations); }
        return _db.Set<DictionaryDto>().Where(x => keys.Contains(x.Key)).ToList().Select(BuildWithTranslations);
    }

    public IEnumerable<IDictionaryItem> GetDictionaryItemDescendants(Guid? parentId, string? filter = null)
    {
        IQueryable<DictionaryDto> q = _db.Set<DictionaryDto>().Where(x => x.Parent == parentId);
        if (!string.IsNullOrEmpty(filter)) { q = q.Where(x => x.Key.Contains(filter)); }
        List<DictionaryDto> all = q.ToList();
        // Recursively load descendants
        List<DictionaryDto> result = new(all);
        foreach (DictionaryDto item in all)
        {
            result.AddRange(GetDescendantsRecursive(item.UniqueId, filter));
        }
        return result.Select(BuildWithTranslations);
    }

    private IEnumerable<DictionaryDto> GetDescendantsRecursive(Guid parentUniqueId, string? filter)
    {
        IQueryable<DictionaryDto> q = _db.Set<DictionaryDto>().Where(x => x.Parent == parentUniqueId);
        if (!string.IsNullOrEmpty(filter)) { q = q.Where(x => x.Key.Contains(filter)); }
        List<DictionaryDto> children = q.ToList();
        List<DictionaryDto> all = new(children);
        foreach (DictionaryDto child in children)
        {
            all.AddRange(GetDescendantsRecursive(child.UniqueId, filter));
        }
        return all;
    }

    public Dictionary<string, Guid> GetDictionaryItemKeyMap()
        => _db.Set<DictionaryDto>().ToDictionary(x => x.Key, x => x.UniqueId);

    public bool Exists(int id) => _db.Set<DictionaryDto>().Any(x => x.PrimaryKey == id);

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(IDictionaryItem entity)
    {
        DictionaryDto dto = DictionaryItemFactory.BuildDto(entity);
        DictionaryDto? existing = _db.Set<DictionaryDto>().Find(entity.Id);

        if (existing == null)
        {
            entity.AddingEntity();
            if (dto.UniqueId == Guid.Empty) { dto.UniqueId = entity.Key == Guid.Empty ? Guid.NewGuid() : entity.Key; }
            _db.Set<DictionaryDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.PrimaryKey;
        }
        else
        {
            entity.UpdatingEntity();
            existing.Key = dto.Key;
            existing.Parent = dto.Parent;
            _db.SaveChanges();
        }

        // Upsert translations
        if (entity.Translations != null)
        {
            IDictionary<string, ILanguage> langs = _db.Set<LanguageDto>()
                .Where(l => l.IsoCode != null).ToList()
                .ToDictionary(l => l.IsoCode!, l => (ILanguage)LanguageFactory.BuildEntity(l, null));

            foreach (IDictionaryTranslation translation in entity.Translations)
            {
                LanguageTextDto textDto = DictionaryTranslationFactory.BuildDto(translation, dto.UniqueId, langs);
                LanguageTextDto? existingText = _db.Set<LanguageTextDto>()
                    .FirstOrDefault(t => t.UniqueId == dto.UniqueId && t.LanguageId == textDto.LanguageId);
                if (existingText == null)
                {
                    _db.Set<LanguageTextDto>().Add(textDto);
                }
                else
                {
                    existingText.Value = textDto.Value;
                }
            }
            _db.SaveChanges();
        }

        entity.ResetDirtyProperties();
    }

    public void Delete(IDictionaryItem entity)
    {
        _db.Set<LanguageTextDto>().RemoveRange(_db.Set<LanguageTextDto>().Where(t => t.UniqueId == entity.Key));
        DictionaryDto? dto = _db.Set<DictionaryDto>().Find(entity.Id);
        if (dto != null) { _db.Set<DictionaryDto>().Remove(dto); }
        _db.SaveChanges();
    }

    public IEnumerable<IDictionaryItem> Get(IQuery<IDictionaryItem> query)
        => _db.Set<DictionaryDto>().ToList().Select(BuildWithTranslations);

    public int Count(IQuery<IDictionaryItem>? query) => _db.Set<DictionaryDto>().Count();
}
