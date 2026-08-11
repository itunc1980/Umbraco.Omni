using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IConsentRepository" />.</summary>
internal sealed class EfConsentRepository : IConsentRepository
{
    private readonly UmbracoDbContext _db;
    public EfConsentRepository(UmbracoDbContext db) => _db = db;

    public void ClearCurrent(string source, string context, string action)
    {
        foreach (ConsentDto dto in _db.Set<ConsentDto>()
            .Where(x => x.Source == source && x.Context == context && x.Action == action && x.Current)
            .ToList())
        {
            dto.Current = false;
        }
        _db.SaveChanges();
    }

    public IConsent? Get(int id)
    {
        ConsentDto? dto = _db.Set<ConsentDto>().Find(id);
        return dto == null ? null : ConsentFactory.BuildEntities([dto]).FirstOrDefault();
    }

    public IEnumerable<IConsent> GetMany(params int[]? ids)
    {
        IQueryable<ConsentDto> q = _db.Set<ConsentDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return ConsentFactory.BuildEntities(q.OrderBy(x => x.Source).ThenBy(x => x.Context).ThenBy(x => x.Action).ToList());
    }

    public bool Exists(int id) => _db.Set<ConsentDto>().Any(x => x.Id == id);

    public void Save(IConsent entity)
    {
        ConsentDto dto = new()
        {
            Id = entity.Id,
            Current = entity.Current,
            CreateDate = entity.CreateDate,
            Source = entity.Source,
            Context = entity.Context,
            Action = entity.Action,
            State = (int)entity.State,
            Comment = entity.Comment,
        };
        if (entity.Id == 0)
        {
            entity.AddingEntity();
            _db.Set<ConsentDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.Id;
        }
        else
        {
            entity.UpdatingEntity();
            _db.Set<ConsentDto>().Update(dto);
            _db.SaveChanges();
        }
        entity.ResetDirtyProperties();
    }

    public void Delete(IConsent entity)
    {
        ConsentDto? dto = _db.Set<ConsentDto>().Find(entity.Id);
        if (dto != null) { _db.Set<ConsentDto>().Remove(dto); _db.SaveChanges(); }
    }

    public IEnumerable<IConsent> Get(IQuery<IConsent> query)
        => ConsentFactory.BuildEntities(_db.Set<ConsentDto>().OrderBy(x => x.Source).ThenBy(x => x.Context).ThenBy(x => x.Action).ToList());

    public int Count(IQuery<IConsent>? query) => _db.Set<ConsentDto>().Count();
}
