using System.Data;
using Microsoft.EntityFrameworkCore;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IDomainRepository" />.</summary>
internal sealed class EfDomainRepository : IDomainRepository
{
    private readonly UmbracoDbContext _db;
    public EfDomainRepository(UmbracoDbContext db) => _db = db;

    public IDomain? GetByName(string domainName)
        => GetAll(true).FirstOrDefault(x => x.DomainName.InvariantEquals(domainName));

    public bool Exists(string domainName)
        => _db.Set<DomainDto>().Any(x => x.DomainName == domainName);

    public IEnumerable<IDomain> GetAll(bool includeWildcards)
    {
        IQueryable<DomainDto> q = _db.Set<DomainDto>().OrderBy(d => d.SortOrder);
        if (!includeWildcards)
        {
            q = q.Where(d => d.DomainName != null && !d.DomainName.StartsWith("*") && d.DomainName != string.Empty);
        }
        return q.ToList().Select(DomainFactory.BuildEntity);
    }

    public IEnumerable<IDomain> GetAssignedDomains(int contentId, bool includeWildcards)
    {
        IQueryable<DomainDto> q = _db.Set<DomainDto>()
            .Where(x => x.RootStructureId == contentId)
            .OrderBy(d => d.SortOrder);
        if (!includeWildcards)
        {
            q = q.Where(d => d.DomainName != null && !d.DomainName.StartsWith("*") && d.DomainName != string.Empty);
        }
        return q.ToList().Select(DomainFactory.BuildEntity);
    }

    public IDomain? Get(int id)
    {
        DomainDto? dto = _db.Set<DomainDto>().FirstOrDefault(x => x.Id == id);
        return dto == null ? null : DomainFactory.BuildEntity(dto);
    }

    public IEnumerable<IDomain> GetMany(params int[]? ids)
    {
        IQueryable<DomainDto> q = _db.Set<DomainDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.OrderBy(d => d.SortOrder).ToList().Select(DomainFactory.BuildEntity);
    }

    public bool Exists(int id) => _db.Set<DomainDto>().Any(x => x.Id == id);

    public void Save(IDomain entity)
    {
        if (entity.Id == 0) { PersistNew(entity); }
        else { PersistUpdated(entity); }
    }

    public void Delete(IDomain entity)
    {
        DomainDto? dto = _db.Set<DomainDto>().Find(entity.Id);
        if (dto != null) { _db.Set<DomainDto>().Remove(dto); _db.SaveChanges(); }
    }

    public IEnumerable<IDomain> Get(IQuery<IDomain> query)
        => throw new NotSupportedException("This repository does not support generic query.");

    public int Count(IQuery<IDomain>? query)
        => _db.Set<DomainDto>().Count();

    private void PersistNew(IDomain entity)
    {
        if (_db.Set<DomainDto>().Any(x => x.DomainName == entity.DomainName))
            throw new DuplicateNameException($"The domain name {entity.DomainName} is already assigned.");
        if (entity.RootContentId.HasValue && !_db.Set<NodeDto>().Any(x => x.NodeId == entity.RootContentId.Value))
            throw new NullReferenceException($"No content exists with id {entity.RootContentId.Value}.");
        if (entity.LanguageId.HasValue && !_db.Set<LanguageDto>().Any(x => x.Id == entity.LanguageId.Value))
            throw new NullReferenceException($"No language exists with id {entity.LanguageId.Value}.");

        entity.AddingEntity();
        entity.SortOrder = GetNewSortOrder(entity.RootContentId, entity.IsWildcard);
        DomainDto dto = DomainFactory.BuildDto(entity);
        _db.Set<DomainDto>().Add(dto);
        _db.SaveChanges();
        entity.Id = dto.Id;

        if (entity.LanguageId.HasValue)
        {
            ((UmbracoDomain)entity).LanguageIsoCode =
                _db.Set<LanguageDto>().Where(x => x.Id == entity.LanguageId.Value).Select(x => x.IsoCode).FirstOrDefault();
        }
        entity.ResetDirtyProperties();
    }

    private void PersistUpdated(IDomain entity)
    {
        if (_db.Set<DomainDto>().Any(x => x.DomainName == entity.DomainName && x.Id != entity.Id))
            throw new DuplicateNameException($"The domain name {entity.DomainName} is already assigned.");
        if (entity.RootContentId.HasValue && !_db.Set<NodeDto>().Any(x => x.NodeId == entity.RootContentId.Value))
            throw new NullReferenceException($"No content exists with id {entity.RootContentId.Value}.");
        if (entity.LanguageId.HasValue && !_db.Set<LanguageDto>().Any(x => x.Id == entity.LanguageId.Value))
            throw new NullReferenceException($"No language exists with id {entity.LanguageId.Value}.");

        entity.UpdatingEntity();
        DomainDto dto = DomainFactory.BuildDto(entity);
        _db.Set<DomainDto>().Update(dto);
        _db.SaveChanges();

        if (entity.WasPropertyDirty("LanguageId") && entity.LanguageId.HasValue)
        {
            ((UmbracoDomain)entity).LanguageIsoCode =
                _db.Set<LanguageDto>().Where(x => x.Id == entity.LanguageId.Value).Select(x => x.IsoCode).FirstOrDefault();
        }
        entity.ResetDirtyProperties();
    }

    private int GetNewSortOrder(int? rootContentId, bool isWildcard)
    {
        if (isWildcard) { return -1; }
        int? max = _db.Set<DomainDto>()
            .Where(x => x.RootStructureId == rootContentId
                        && x.DomainName != null && x.DomainName != string.Empty
                        && !x.DomainName.StartsWith("*"))
            .Select(x => (int?)x.SortOrder).Max();
        return (max ?? -1) + 1;
    }
}
