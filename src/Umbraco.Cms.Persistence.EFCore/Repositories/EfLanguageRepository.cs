using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="ILanguageRepository" />.</summary>
internal sealed class EfLanguageRepository : ILanguageRepository
{
    private readonly UmbracoDbContext _db;
    private readonly ILogger<EfLanguageRepository> _logger;
    private readonly Dictionary<string, int> _codeIdMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _idCodeMap = new();
    private bool _mapLoaded;

    public EfLanguageRepository(UmbracoDbContext db, ILogger<EfLanguageRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public ILanguage? GetByIsoCode(string isoCode)
    {
        EnsureMapLoaded();
        return _codeIdMap.TryGetValue(isoCode, out int id) ? Get(id) : null;
    }

    public int? GetIdByIsoCode(string? isoCode, bool throwOnNotFound = true)
    {
        if (isoCode == null) { return null; }
        EnsureMapLoaded();
        lock (_codeIdMap)
        {
            if (_codeIdMap.TryGetValue(isoCode, out int id)) { return id; }
        }
        if (throwOnNotFound)
            throw new ArgumentException($"Code {isoCode} does not correspond to an existing language.", nameof(isoCode));
        return null;
    }

    public string? GetIsoCodeById(int? id, bool throwOnNotFound = true)
    {
        if (id == null) { return null; }
        EnsureMapLoaded();
        lock (_codeIdMap)
        {
            if (_idCodeMap.TryGetValue(id.Value, out string? code)) { return code; }
        }
        if (throwOnNotFound)
            throw new ArgumentException($"Id {id} does not correspond to an existing language.", nameof(id));
        return null;
    }

    public string GetDefaultIsoCode() => GetDefault().IsoCode;
    public int? GetDefaultId() => GetDefault().Id;

    public string[] GetIsoCodesByIds(ICollection<int> ids, bool throwOnNotFound = true)
    {
        var result = new string[ids.Count];
        if (!ids.Any()) { return result; }
        EnsureMapLoaded();
        lock (_codeIdMap)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids.ElementAt(i);
                if (_idCodeMap.TryGetValue(id, out string? code)) { result[i] = code; }
                else if (throwOnNotFound)
                    throw new ArgumentException($"Id {id} does not correspond to an existing language.", nameof(ids));
            }
        }
        return result;
    }

    public ILanguage? Get(int id)
    {
        LanguageDto? dto = _db.Set<LanguageDto>().Find(id);
        return dto == null ? null : BuildWithFallback(dto);
    }

    public IEnumerable<ILanguage> GetMany(params int[]? ids)
    {
        IQueryable<LanguageDto> q = _db.Set<LanguageDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.OrderBy(x => x.Id).ToList().Select(BuildWithFallback);
    }

    public bool Exists(int id) => _db.Set<LanguageDto>().Any(x => x.Id == id);

    public void Save(ILanguage entity)
    {
        if (entity.Id == 0) { PersistNew(entity); }
        else { PersistUpdated(entity); }
    }

    public void Delete(ILanguage entity)
    {
        // Cannot delete default language
        LanguageDto? defaultLang = _db.Set<LanguageDto>().FirstOrDefault(x => x.IsDefault);
        if (defaultLang?.Id == entity.Id)
            throw new InvalidOperationException($"Cannot delete the default language ({entity.IsoCode}).");

        // Clear fallback references
        foreach (LanguageDto lang in _db.Set<LanguageDto>().Where(x => x.FallbackLanguageId == entity.Id).ToList())
        {
            lang.FallbackLanguageId = null;
        }

        LanguageDto? dto = _db.Set<LanguageDto>().Find(entity.Id);
        if (dto != null) { _db.Set<LanguageDto>().Remove(dto); }
        _db.SaveChanges();
        InvalidateMap();
    }

    public IEnumerable<ILanguage> Get(IQuery<ILanguage> query)
        => _db.Set<LanguageDto>().OrderBy(x => x.Id).ToList().Select(BuildWithFallback);

    public int Count(IQuery<ILanguage>? query)
        => _db.Set<LanguageDto>().Count();

    private ILanguage GetDefault()
    {
        List<LanguageDto> all = _db.Set<LanguageDto>().OrderBy(x => x.Id).ToList();
        LanguageDto? def = all.FirstOrDefault(x => x.IsDefault);
        if (def == null)
        {
            _logger.LogWarning("There is no default language.");
            def = all.First();
        }
        return BuildWithFallback(def);
    }

    private void EnsureMapLoaded()
    {
        if (_mapLoaded) { return; }
        lock (_codeIdMap)
        {
            if (_mapLoaded) { return; }
            _codeIdMap.Clear(); _idCodeMap.Clear();
            foreach (LanguageDto dto in _db.Set<LanguageDto>().ToList())
            {
                if (!string.IsNullOrEmpty(dto.IsoCode))
                {
                    _codeIdMap[dto.IsoCode] = dto.Id;
                    _idCodeMap[dto.Id] = dto.IsoCode;
                }
            }
            _mapLoaded = true;
        }
    }

    private void InvalidateMap() { lock (_codeIdMap) { _mapLoaded = false; } }

    private ILanguage BuildWithFallback(LanguageDto dto)
    {
        string? fallback = null;
        if (dto.FallbackLanguageId.HasValue) { _idCodeMap.TryGetValue(dto.FallbackLanguageId.Value, out fallback); }
        return LanguageFactory.BuildEntity(dto, fallback);
    }

    private int? GetFallbackId(ILanguage entity)
    {
        if (entity.FallbackIsoCode.IsNullOrWhiteSpace()) { return null; }
        _codeIdMap.TryGetValue(entity.FallbackIsoCode, out int id);
        return id == 0 ? null : id;
    }

    private void PersistNew(ILanguage entity)
    {
        if (entity.IsoCode.IsNullOrWhiteSpace() || entity.CultureName.IsNullOrWhiteSpace())
            throw new InvalidOperationException("Cannot save a language without an ISO code and a culture name.");
        EnsureMapLoaded();
        entity.AddingEntity();
        if (entity.IsDefault)
        {
            foreach (LanguageDto lang in _db.Set<LanguageDto>().Where(x => x.IsDefault).ToList())
                lang.IsDefault = false;
        }
        LanguageDto dto = LanguageFactory.BuildDto(entity, GetFallbackId(entity));
        _db.Set<LanguageDto>().Add(dto);
        _db.SaveChanges();
        entity.Id = dto.Id;
        entity.ResetDirtyProperties();
        InvalidateMap();
    }

    private void PersistUpdated(ILanguage entity)
    {
        if (entity.IsoCode.IsNullOrWhiteSpace() || entity.CultureName.IsNullOrWhiteSpace())
            throw new InvalidOperationException("Cannot save a language without an ISO code and a culture name.");
        EnsureMapLoaded();
        entity.UpdatingEntity();
        if (entity.IsDefault)
        {
            foreach (LanguageDto lang in _db.Set<LanguageDto>().Where(x => x.IsDefault && x.Id != entity.Id).ToList())
                lang.IsDefault = false;
        }
        else
        {
            LanguageDto? def = _db.Set<LanguageDto>().FirstOrDefault(x => x.IsDefault);
            if (def?.Id == entity.Id)
                throw new InvalidOperationException($"Cannot save the default language ({entity.IsoCode}) as non-default.");
        }
        if (entity.IsPropertyDirty(nameof(ILanguage.IsoCode)))
        {
            if (_db.Set<LanguageDto>().Any(x => x.IsoCode == entity.IsoCode && x.Id != entity.Id))
                throw new InvalidOperationException($"Cannot update the language to a new culture: {entity.IsoCode} since it is already assigned.");
        }
        LanguageDto dto = LanguageFactory.BuildDto(entity, GetFallbackId(entity));
        _db.Set<LanguageDto>().Update(dto);
        _db.SaveChanges();
        entity.ResetDirtyProperties();
        InvalidateMap();
    }
}
