using Umbraco.Cms.Core.Mapping;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="ITrackedReferencesRepository" />.</summary>
internal sealed class EfTrackedReferencesRepository : ITrackedReferencesRepository
{
    private readonly UmbracoDbContext _db;
    private readonly IUmbracoMapper _umbracoMapper;

    public EfTrackedReferencesRepository(UmbracoDbContext db, IUmbracoMapper umbracoMapper)
    {
        _db = db;
        _umbracoMapper = umbracoMapper;
    }

    public IEnumerable<RelationItemModel> GetPagedRelationsForItem(
        Guid key,
        long skip,
        long take,
        bool filterMustBeIsDependency,
        out long totalRecords)
    {
        NodeDto? targetNode = _db.Set<NodeDto>().FirstOrDefault(n => n.UniqueId == key);
        if (targetNode == null)
        {
            totalRecords = 0;
            return Enumerable.Empty<RelationItemModel>();
        }

        return QueryRelationsForNode(targetNode.NodeId, skip, take, filterMustBeIsDependency, out totalRecords);
    }

    public IEnumerable<RelationItemModel> GetPagedRelationsForRecycleBin(
        Guid objectTypeKey,
        long skip,
        long take,
        bool filterMustBeIsDependency,
        out long totalRecords)
    {
        int[] trashedNodeIds = _db.Set<NodeDto>()
            .Where(n => n.NodeObjectType == objectTypeKey && n.Trashed)
            .Select(n => n.NodeId)
            .ToArray();

        if (trashedNodeIds.Length == 0)
        {
            totalRecords = 0;
            return Enumerable.Empty<RelationItemModel>();
        }

        return QueryRelationsForNodes(trashedNodeIds, skip, take, filterMustBeIsDependency, out totalRecords);
    }

    public IEnumerable<RelationItemModel> GetPagedItemsWithRelations(
        ISet<Guid> keys,
        long skip,
        long take,
        bool filterMustBeIsDependency,
        out long totalRecords)
    {
        if (keys.Count == 0)
        {
            totalRecords = 0;
            return Enumerable.Empty<RelationItemModel>();
        }

        int[] nodeIds = _db.Set<NodeDto>()
            .Where(n => keys.Contains(n.UniqueId))
            .Select(n => n.NodeId)
            .ToArray();

        if (nodeIds.Length == 0)
        {
            totalRecords = 0;
            return Enumerable.Empty<RelationItemModel>();
        }

        return QueryRelationsForNodes(nodeIds, skip, take, filterMustBeIsDependency, out totalRecords);
    }

    public IEnumerable<RelationItemModel> GetPagedDescendantsInReferences(
        Guid parentKey,
        long skip,
        long take,
        bool filterMustBeIsDependency,
        out long totalRecords)
    {
        NodeDto? parent = _db.Set<NodeDto>().FirstOrDefault(n => n.UniqueId == parentKey);
        if (parent == null)
        {
            totalRecords = 0;
            return Enumerable.Empty<RelationItemModel>();
        }

        int[] descNodeIds = _db.Set<NodeDto>()
            .Where(n => n.Path.StartsWith(parent.Path + ","))
            .Select(n => n.NodeId)
            .ToArray();

        if (descNodeIds.Length == 0)
        {
            totalRecords = 0;
            return Enumerable.Empty<RelationItemModel>();
        }

        return QueryRelationsForNodes(descNodeIds, skip, take, filterMustBeIsDependency, out totalRecords);
    }

    public Task<PagedModel<Guid>> GetPagedNodeKeysWithDependantReferencesAsync(
        ISet<Guid> keys,
        Guid nodeObjectTypeId,
        long skip,
        long take)
    {
        if (keys.Count == 0)
        {
            return Task.FromResult(new PagedModel<Guid> { Items = Enumerable.Empty<Guid>(), Total = 0 });
        }

        int[] depRelationTypeIds = _db.Set<RelationTypeDto>()
            .Where(rt => rt.IsDependency)
            .Select(rt => rt.Id)
            .ToArray();

        var nodesWithRelations = _db.Set<NodeDto>()
            .Where(n => n.NodeObjectType == nodeObjectTypeId && keys.Contains(n.UniqueId))
            .Where(n => _db.Set<RelationDto>().Any(r =>
                depRelationTypeIds.Contains(r.RelationType) &&
                (r.ChildId == n.NodeId || r.ParentId == n.NodeId)))
            .Select(n => n.UniqueId);

        long total = nodesWithRelations.LongCount();
        List<Guid> items = nodesWithRelations.Skip((int)skip).Take((int)take).ToList();

        return Task.FromResult(new PagedModel<Guid> { Items = items, Total = total });
    }

    // ─── Private Helpers ────────────────────────────────────────────────────
    private IEnumerable<RelationItemModel> QueryRelationsForNode(
        int nodeId,
        long skip,
        long take,
        bool filterMustBeIsDependency,
        out long totalRecords)
        => QueryRelationsForNodes(new[] { nodeId }, skip, take, filterMustBeIsDependency, out totalRecords);

    private IEnumerable<RelationItemModel> QueryRelationsForNodes(
        int[] nodeIds,
        long skip,
        long take,
        bool filterMustBeIsDependency,
        out long totalRecords)
    {
        IQueryable<RelationDto> relQuery = _db.Set<RelationDto>()
            .Where(r => nodeIds.Contains(r.ParentId) || nodeIds.Contains(r.ChildId));

        if (filterMustBeIsDependency)
        {
            int[] depTypeIds = _db.Set<RelationTypeDto>().Where(rt => rt.IsDependency).Select(rt => rt.Id).ToArray();
            relQuery = relQuery.Where(r => depTypeIds.Contains(r.RelationType));
        }

        totalRecords = relQuery.LongCount();
        if (totalRecords == 0)
        {
            return Enumerable.Empty<RelationItemModel>();
        }

        List<RelationDto> pagedRelations = relQuery.Skip((int)skip).Take((int)take).ToList();
        List<RelationItemModel> result = new();

        foreach (RelationDto rel in pagedRelations)
        {
            RelationTypeDto? relType = _db.Set<RelationTypeDto>().Find(rel.RelationType);
            int otherNodeId = nodeIds.Contains(rel.ParentId) ? rel.ChildId : rel.ParentId;
            NodeDto? otherNode = _db.Set<NodeDto>().Find(otherNodeId);

            if (otherNode != null)
            {
                ContentTypeDto? ct = null;
                NodeDto? ctn = null;

                ContentDto? content = _db.Set<ContentDto>().Find(otherNode.NodeId);
                if (content != null)
                {
                    ct = _db.Set<ContentTypeDto>().Find(content.ContentTypeId);
                    if (ct != null)
                    {
                        ctn = _db.Set<NodeDto>().Find(ct.NodeId);
                    }
                }

                result.Add(new RelationItemModel
                {
                    NodeKey = otherNode.UniqueId,
                    NodeName = otherNode.Text ?? string.Empty,
                    NodePublished = true,
                    ContentTypeKey = ctn?.UniqueId ?? Guid.Empty,
                    ContentTypeIcon = ct?.Icon,
                    ContentTypeAlias = ct?.Alias,
                    ContentTypeName = ctn?.Text,
                    RelationTypeName = relType?.Name ?? string.Empty,
                    RelationTypeIsDependency = relType?.IsDependency ?? false,
                    RelationTypeIsBidirectional = relType?.Dual ?? false,
                });
            }
        }

        return result;
    }
}
