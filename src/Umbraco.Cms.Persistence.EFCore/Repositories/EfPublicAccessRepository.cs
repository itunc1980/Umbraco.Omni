using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IPublicAccessRepository" />.</summary>
internal sealed class EfPublicAccessRepository : IPublicAccessRepository
{
    private readonly UmbracoDbContext _db;
    public EfPublicAccessRepository(UmbracoDbContext db) => _db = db;

    public PublicAccessEntry? Get(Guid id)
    {
        AccessDto? dto = LoadWithRules(id);
        return dto == null ? null : PublicAccessEntryFactory.BuildEntity(dto);
    }

    public IEnumerable<PublicAccessEntry> GetMany(params Guid[]? ids)
    {
        IQueryable<AccessDto> q = _db.Set<AccessDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        List<Guid> accessIds = q.Select(x => x.Id).ToList();
        return accessIds.Select(id => LoadWithRules(id)).Where(d => d != null)
                        .Select(d => PublicAccessEntryFactory.BuildEntity(d!));
    }

    public bool Exists(Guid id) => _db.Set<AccessDto>().Any(x => x.Id == id);

    public void Save(PublicAccessEntry entity)
    {
        AccessDto dto = PublicAccessEntryFactory.BuildDto(entity);
        AccessDto? existing = _db.Set<AccessDto>().Find(entity.Key);
        if (existing == null)
        {
            entity.AddingEntity();
            dto.CreateDate = entity.CreateDate;
            dto.UpdateDate = entity.UpdateDate;
            _db.Set<AccessDto>().Add(dto);
            _db.SaveChanges();

            foreach (AccessRuleDto rule in dto.Rules)
            {
                if (rule.Id == Guid.Empty) { rule.Id = Guid.NewGuid(); }
                rule.AccessId = dto.Id;
                _db.Set<AccessRuleDto>().Add(rule);
            }
            _db.SaveChanges();
        }
        else
        {
            entity.UpdatingEntity();
            existing.NodeId = dto.NodeId;
            existing.LoginNodeId = dto.LoginNodeId;
            existing.NoAccessNodeId = dto.NoAccessNodeId;
            existing.UpdateDate = entity.UpdateDate;

            // Handle removed rules
            foreach (Guid removedId in entity.RemovedRules)
            {
                AccessRuleDto? toRemove = _db.Set<AccessRuleDto>().Find(removedId);
                if (toRemove != null) { _db.Set<AccessRuleDto>().Remove(toRemove); }
            }

            // Upsert rules
            foreach (AccessRuleDto rule in dto.Rules)
            {
                AccessRuleDto? existingRule = _db.Set<AccessRuleDto>().Find(rule.Id);
                if (existingRule == null)
                {
                    if (rule.Id == Guid.Empty) { rule.Id = Guid.NewGuid(); }
                    rule.AccessId = existing.Id;
                    _db.Set<AccessRuleDto>().Add(rule);
                }
                else
                {
                    existingRule.RuleValue = rule.RuleValue;
                    existingRule.RuleType = rule.RuleType;
                    existingRule.UpdateDate = rule.UpdateDate;
                }
            }
            _db.SaveChanges();
        }
        entity.ResetDirtyProperties();
    }

    public void Delete(PublicAccessEntry entity)
    {
        List<AccessRuleDto> rules = _db.Set<AccessRuleDto>().Where(r => r.AccessId == entity.Key).ToList();
        _db.Set<AccessRuleDto>().RemoveRange(rules);
        AccessDto? dto = _db.Set<AccessDto>().Find(entity.Key);
        if (dto != null) { _db.Set<AccessDto>().Remove(dto); }
        _db.SaveChanges();
    }

    public IEnumerable<PublicAccessEntry> Get(IQuery<PublicAccessEntry> query)
    {
        List<Guid> ids = _db.Set<AccessDto>().Select(x => x.Id).ToList();
        return ids.Select(id => LoadWithRules(id)).Where(d => d != null)
                  .Select(d => PublicAccessEntryFactory.BuildEntity(d!));
    }

    public int Count(IQuery<PublicAccessEntry>? query) => _db.Set<AccessDto>().Count();

    private AccessDto? LoadWithRules(Guid id)
    {
        AccessDto? dto = _db.Set<AccessDto>().Find(id);
        if (dto == null) { return null; }
        dto.Rules = _db.Set<AccessRuleDto>().Where(r => r.AccessId == id).ToList();
        return dto;
    }
}
