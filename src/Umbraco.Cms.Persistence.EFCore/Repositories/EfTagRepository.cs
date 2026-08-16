using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="ITagRepository" />.</summary>
internal sealed class EfTagRepository : ITagRepository
{
    private readonly UmbracoDbContext _db;
    public EfTagRepository(UmbracoDbContext db) => _db = db;

    // ─── IReadRepository<int, ITag> ─────────────────────────────────────────
    public ITag? Get(int id)
    {
        TagDto? dto = _db.Set<TagDto>().Find(id);
        return dto == null ? null : TagFactory.BuildEntity(dto);
    }

    public IEnumerable<ITag> GetMany(params int[]? ids)
    {
        IQueryable<TagDto> q = _db.Set<TagDto>();
        if (ids?.Length > 0) { q = q.Where(x => ids.Contains(x.Id)); }
        return q.ToList().Select(TagFactory.BuildEntity);
    }

    public bool Exists(int id) => _db.Set<TagDto>().Any(x => x.Id == id);

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(ITag entity)
    {
        TagDto dto = TagFactory.BuildDto(entity);
        TagDto? existing = entity.HasIdentity ? _db.Set<TagDto>().Find(entity.Id) : null;
        if (existing == null)
        {
            entity.AddingEntity();
            _db.Set<TagDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.Id;
        }
        else
        {
            entity.UpdatingEntity();
            existing.Text = dto.Text;
            existing.Group = dto.Group;
            existing.LanguageId = dto.LanguageId;
            _db.SaveChanges();
        }
        entity.ResetDirtyProperties();
    }

    public void Delete(ITag entity)
    {
        // Remove all relationships first
        List<TagRelationshipDto> rels = _db.Set<TagRelationshipDto>().Where(r => r.TagId == entity.Id).ToList();
        _db.Set<TagRelationshipDto>().RemoveRange(rels);
        TagDto? dto = _db.Set<TagDto>().Find(entity.Id);
        if (dto != null) { _db.Set<TagDto>().Remove(dto); }
        _db.SaveChanges();
    }

    public IEnumerable<ITag> Get(IQuery<ITag> query)
        => _db.Set<TagDto>().ToList().Select(TagFactory.BuildEntity);

    public int Count(IQuery<ITag>? query) => _db.Set<TagDto>().Count();

    // ─── Assign/Remove Tags ──────────────────────────────────────────────────
    public void Assign(int contentId, int propertyTypeId, IEnumerable<ITag> tags, bool replaceTags = true)
    {
        List<ITag> tagList = tags.ToList();

        if (replaceTags)
        {
            // Delete existing relationships for this content+propertyType
            List<TagRelationshipDto> existing = _db.Set<TagRelationshipDto>()
                .Where(r => r.NodeId == contentId && r.PropertyTypeId == propertyTypeId)
                .ToList();
            _db.Set<TagRelationshipDto>().RemoveRange(existing);
            _db.SaveChanges();
        }

        foreach (ITag tag in tagList)
        {
            // Ensure tag exists (upsert by text+group+languageId)
            TagDto? existingTag = _db.Set<TagDto>()
                .FirstOrDefault(t => t.Text == tag.Text && t.Group == tag.Group && t.LanguageId == tag.LanguageId);

            int tagId;
            if (existingTag == null)
            {
                var dto = TagFactory.BuildDto(tag);
                _db.Set<TagDto>().Add(dto);
                _db.SaveChanges();
                tagId = dto.Id;
                if (tag.HasIdentity == false) { /* newly saved */ }
            }
            else
            {
                tagId = existingTag.Id;
            }

            // Upsert relationship
            bool relExists = _db.Set<TagRelationshipDto>()
                .Any(r => r.NodeId == contentId && r.PropertyTypeId == propertyTypeId && r.TagId == tagId);

            if (!relExists)
            {
                _db.Set<TagRelationshipDto>().Add(new TagRelationshipDto
                {
                    NodeId = contentId,
                    PropertyTypeId = propertyTypeId,
                    TagId = tagId,
                });
            }
        }
        _db.SaveChanges();
    }

    public void Remove(int contentId, int propertyTypeId, IEnumerable<ITag> tags)
    {
        List<string> tagTexts = tags.Select(t => t.Text).ToList();
        int[] tagIds = _db.Set<TagDto>().Where(t => tagTexts.Contains(t.Text)).Select(t => t.Id).ToArray();
        List<TagRelationshipDto> rels = _db.Set<TagRelationshipDto>()
            .Where(r => r.NodeId == contentId && r.PropertyTypeId == propertyTypeId && tagIds.Contains(r.TagId))
            .ToList();
        _db.Set<TagRelationshipDto>().RemoveRange(rels);
        _db.SaveChanges();
    }

    public void RemoveAll(int contentId)
    {
        List<TagRelationshipDto> rels = _db.Set<TagRelationshipDto>().Where(r => r.NodeId == contentId).ToList();
        _db.Set<TagRelationshipDto>().RemoveRange(rels);
        _db.SaveChanges();
    }

    public void RemoveAll(int contentId, int propertyTypeId)
    {
        List<TagRelationshipDto> rels = _db.Set<TagRelationshipDto>()
            .Where(r => r.NodeId == contentId && r.PropertyTypeId == propertyTypeId)
            .ToList();
        _db.Set<TagRelationshipDto>().RemoveRange(rels);
        _db.SaveChanges();
    }

    // ─── Tagged entity queries ───────────────────────────────────────────────
    public TaggedEntity? GetTaggedEntityByKey(Guid key)
    {
        NodeDto? node = _db.Set<NodeDto>().FirstOrDefault(n => n.UniqueId == key);
        return node == null ? null : BuildTaggedEntity(node.NodeId);
    }

    public TaggedEntity? GetTaggedEntityById(int id)
        => BuildTaggedEntity(id);

    private TaggedEntity? BuildTaggedEntity(int nodeId)
    {
        List<TagRelationshipDto> rels = _db.Set<TagRelationshipDto>().Where(r => r.NodeId == nodeId).ToList();
        if (rels.Count == 0) { return null; }

        List<TaggedProperty> props = rels
            .GroupBy(r => r.PropertyTypeId)
            .Select(g =>
            {
                PropertyTypeDto? pt = _db.Set<PropertyTypeDto>().Find(g.Key);
                IEnumerable<ITag> ptTags = g.Select(r =>
                {
                    TagDto? td = _db.Set<TagDto>().Find(r.TagId);
                    return td == null ? null : TagFactory.BuildEntity(td);
                }).Where(t => t != null).Cast<ITag>();
                return new TaggedProperty(g.Key, pt?.Alias, ptTags);
            }).ToList();

        return new TaggedEntity(nodeId, props);
    }

    public IEnumerable<TaggedEntity> GetTaggedEntitiesByTagGroup(TaggableObjectTypes objectType, string group, string? culture = null)
    {
        IQueryable<TagDto> tagQ = _db.Set<TagDto>().Where(t => t.Group == group);
        if (culture != null)
        {
            int? langId = GetLanguageId(culture);
            tagQ = langId.HasValue ? tagQ.Where(t => t.LanguageId == langId) : tagQ.Where(t => t.LanguageId == null);
        }
        int[] tagIds = tagQ.Select(t => t.Id).ToArray();
        return GetTaggedEntitiesByTagIds(objectType, tagIds);
    }

    public IEnumerable<TaggedEntity> GetTaggedEntitiesByTag(TaggableObjectTypes objectType, string tag, string? group = null, string? culture = null)
    {
        IQueryable<TagDto> tagQ = _db.Set<TagDto>().Where(t => t.Text == tag);
        if (group != null) { tagQ = tagQ.Where(t => t.Group == group); }
        if (culture != null)
        {
            int? langId = GetLanguageId(culture);
            tagQ = langId.HasValue ? tagQ.Where(t => t.LanguageId == langId) : tagQ.Where(t => t.LanguageId == null);
        }
        int[] tagIds = tagQ.Select(t => t.Id).ToArray();
        return GetTaggedEntitiesByTagIds(objectType, tagIds);
    }

    private IEnumerable<TaggedEntity> GetTaggedEntitiesByTagIds(TaggableObjectTypes objectType, int[] tagIds)
    {
        if (tagIds.Length == 0) { return Enumerable.Empty<TaggedEntity>(); }

        IQueryable<int> nodeIdQ = _db.Set<TagRelationshipDto>()
            .Where(r => tagIds.Contains(r.TagId))
            .Select(r => r.NodeId)
            .Distinct();

        if (objectType != TaggableObjectTypes.All)
        {
            Guid objTypeGuid = GetNodeObjectType(objectType);
            nodeIdQ = nodeIdQ.Where(nid => _db.Set<NodeDto>().Any(n => n.NodeId == nid && n.NodeObjectType == objTypeGuid));
        }

        return nodeIdQ.ToList().Select(id => BuildTaggedEntity(id)).Where(e => e != null).Cast<TaggedEntity>();
    }

    public IEnumerable<ITag> GetTagsForEntityType(TaggableObjectTypes objectType, string? group = null, string? culture = null)
    {
        Guid? objTypeGuid = objectType != TaggableObjectTypes.All ? GetNodeObjectType(objectType) : null;

        IQueryable<int> nodeIdQ = _db.Set<NodeDto>()
            .Where(n => objTypeGuid == null || n.NodeObjectType == objTypeGuid)
            .Select(n => n.NodeId);

        IQueryable<int> tagIdQ = _db.Set<TagRelationshipDto>()
            .Where(r => nodeIdQ.Contains(r.NodeId))
            .Select(r => r.TagId)
            .Distinct();

        IQueryable<TagDto> tagQ = _db.Set<TagDto>().Where(t => tagIdQ.Contains(t.Id));
        if (group != null) { tagQ = tagQ.Where(t => t.Group == group); }
        if (culture != null)
        {
            int? langId = GetLanguageId(culture);
            tagQ = langId.HasValue ? tagQ.Where(t => t.LanguageId == langId) : tagQ.Where(t => t.LanguageId == null);
        }
        return tagQ.ToList().Select(TagFactory.BuildEntity);
    }

    public IEnumerable<ITag> GetTagsForEntity(int contentId, string? group = null, string? culture = null)
    {
        int[] tagIds = _db.Set<TagRelationshipDto>()
            .Where(r => r.NodeId == contentId)
            .Select(r => r.TagId).ToArray();
        return FilterTags(tagIds, group, culture);
    }

    public IEnumerable<ITag> GetTagsForEntity(Guid contentId, string? group = null, string? culture = null)
    {
        int? nodeId = _db.Set<NodeDto>().Where(n => n.UniqueId == contentId).Select(n => (int?)n.NodeId).FirstOrDefault();
        if (nodeId == null) { return Enumerable.Empty<ITag>(); }
        return GetTagsForEntity(nodeId.Value, group, culture);
    }

    public IEnumerable<ITag> GetTagsForProperty(int contentId, string propertyTypeAlias, string? group = null, string? culture = null)
    {
        int? propTypeId = _db.Set<PropertyTypeDto>()
            .Where(p => p.Alias == propertyTypeAlias)
            .Select(p => (int?)p.Id).FirstOrDefault();
        if (propTypeId == null) { return Enumerable.Empty<ITag>(); }

        int[] tagIds = _db.Set<TagRelationshipDto>()
            .Where(r => r.NodeId == contentId && r.PropertyTypeId == propTypeId)
            .Select(r => r.TagId).ToArray();
        return FilterTags(tagIds, group, culture);
    }

    public IEnumerable<ITag> GetTagsForProperty(Guid contentId, string propertyTypeAlias, string? group = null, string? culture = null)
    {
        int? nodeId = _db.Set<NodeDto>().Where(n => n.UniqueId == contentId).Select(n => (int?)n.NodeId).FirstOrDefault();
        if (nodeId == null) { return Enumerable.Empty<ITag>(); }
        return GetTagsForProperty(nodeId.Value, propertyTypeAlias, group, culture);
    }

    // ─── Private helpers ────────────────────────────────────────────────────
    private IEnumerable<ITag> FilterTags(int[] tagIds, string? group, string? culture)
    {
        if (tagIds.Length == 0) { return Enumerable.Empty<ITag>(); }
        IQueryable<TagDto> q = _db.Set<TagDto>().Where(t => tagIds.Contains(t.Id));
        if (group != null) { q = q.Where(t => t.Group == group); }
        if (culture != null)
        {
            int? langId = GetLanguageId(culture);
            q = langId.HasValue ? q.Where(t => t.LanguageId == langId) : q.Where(t => t.LanguageId == null);
        }
        return q.ToList().Select(TagFactory.BuildEntity);
    }

    private int? GetLanguageId(string isoCode)
        => _db.Set<LanguageDto>().Where(l => l.IsoCode == isoCode).Select(l => (int?)l.Id).FirstOrDefault();

    private static Guid GetNodeObjectType(TaggableObjectTypes type) => type switch
    {
        TaggableObjectTypes.Content => CoreConstants.ObjectTypes.Document,
        TaggableObjectTypes.Media => CoreConstants.ObjectTypes.Media,
        TaggableObjectTypes.Member => CoreConstants.ObjectTypes.Member,
        TaggableObjectTypes.Element => CoreConstants.ObjectTypes.Document,
        _ => Guid.Empty,
    };
}
