using System.Collections.Concurrent;
using System.Globalization;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IEntityRepository" />.</summary>
internal sealed class EfEntityRepository : IEntityRepository
{
    private readonly UmbracoDbContext _db;

    public EfEntityRepository(UmbracoDbContext db)
    {
        _db = db;
    }

    // ─── Entity Construction Helpers ────────────────────────────────────────
    private IEntitySlim? BuildEntity(NodeDto? node)
    {
        if (node == null)
        {
            return null;
        }

        Guid objectType = node.NodeObjectType ?? Guid.Empty;
        var hasChildren = _db.Set<NodeDto>().Any(x => x.ParentId == node.NodeId);

        if (objectType == CoreConstants.ObjectTypes.Document || objectType == CoreConstants.ObjectTypes.DocumentBlueprint)
        {
            var doc = new DocumentEntitySlim
            {
                Id = node.NodeId,
                Key = node.UniqueId,
                Name = node.Text,
                CreatorId = node.UserId ?? 0,
                ParentId = node.ParentId,
                Level = node.Level,
                Path = node.Path,
                SortOrder = node.SortOrder,
                Trashed = node.Trashed,
                CreateDate = node.CreateDate,
                UpdateDate = node.CreateDate,
                HasChildren = hasChildren,
                NodeObjectType = objectType,
            };

            ContentDto? content = _db.Set<ContentDto>().Find(node.NodeId);
            if (content != null)
            {
                ContentTypeDto? ct = _db.Set<ContentTypeDto>().Find(content.ContentTypeId);
                if (ct != null)
                {
                    doc.ContentTypeAlias = ct.Alias ?? string.Empty;
                    doc.ContentTypeIcon = ct.Icon;
                    doc.ContentTypeThumbnail = ct.Thumbnail;
                    doc.Variations = (ContentVariation)ct.Variations;
                    NodeDto? ctNode = _db.Set<NodeDto>().Find(ct.NodeId);
                    if (ctNode != null)
                    {
                        doc.ContentTypeKey = ctNode.UniqueId;
                    }
                }
            }

            return doc;
        }

        if (objectType == CoreConstants.ObjectTypes.Media)
        {
            var media = new MediaEntitySlim
            {
                Id = node.NodeId,
                Key = node.UniqueId,
                Name = node.Text,
                CreatorId = node.UserId ?? 0,
                ParentId = node.ParentId,
                Level = node.Level,
                Path = node.Path,
                SortOrder = node.SortOrder,
                Trashed = node.Trashed,
                CreateDate = node.CreateDate,
                UpdateDate = node.CreateDate,
                HasChildren = hasChildren,
                NodeObjectType = objectType,
            };

            ContentDto? content = _db.Set<ContentDto>().Find(node.NodeId);
            if (content != null)
            {
                ContentTypeDto? ct = _db.Set<ContentTypeDto>().Find(content.ContentTypeId);
                if (ct != null)
                {
                    media.ContentTypeAlias = ct.Alias ?? string.Empty;
                    media.ContentTypeIcon = ct.Icon;
                    media.ContentTypeThumbnail = ct.Thumbnail;
                    NodeDto? ctNode = _db.Set<NodeDto>().Find(ct.NodeId);
                    if (ctNode != null)
                    {
                        media.ContentTypeKey = ctNode.UniqueId;
                    }
                }
            }

            return media;
        }

        if (objectType == CoreConstants.ObjectTypes.Member)
        {
            var member = new MemberEntitySlim
            {
                Id = node.NodeId,
                Key = node.UniqueId,
                Name = node.Text,
                CreatorId = node.UserId ?? 0,
                ParentId = node.ParentId,
                Level = node.Level,
                Path = node.Path,
                SortOrder = node.SortOrder,
                Trashed = node.Trashed,
                CreateDate = node.CreateDate,
                UpdateDate = node.CreateDate,
                HasChildren = hasChildren,
                NodeObjectType = objectType,
            };

            ContentDto? content = _db.Set<ContentDto>().Find(node.NodeId);
            if (content != null)
            {
                ContentTypeDto? ct = _db.Set<ContentTypeDto>().Find(content.ContentTypeId);
                if (ct != null)
                {
                    member.ContentTypeAlias = ct.Alias ?? string.Empty;
                    member.ContentTypeIcon = ct.Icon;
                    member.ContentTypeThumbnail = ct.Thumbnail;
                    NodeDto? ctNode = _db.Set<NodeDto>().Find(ct.NodeId);
                    if (ctNode != null)
                    {
                        member.ContentTypeKey = ctNode.UniqueId;
                    }
                }
            }

            return member;
        }

        return new EntitySlim
        {
            Id = node.NodeId,
            Key = node.UniqueId,
            Name = node.Text,
            CreatorId = node.UserId ?? 0,
            ParentId = node.ParentId,
            Level = node.Level,
            Path = node.Path,
            SortOrder = node.SortOrder,
            Trashed = node.Trashed,
            CreateDate = node.CreateDate,
            UpdateDate = node.CreateDate,
            HasChildren = hasChildren,
            NodeObjectType = objectType,
        };
    }

    // ─── Single Getters ─────────────────────────────────────────────────────
    public IEntitySlim? Get(int id)
    {
        NodeDto? node = _db.Set<NodeDto>().Find(id);
        return BuildEntity(node);
    }

    public IEntitySlim? Get(Guid key)
    {
        NodeDto? node = _db.Set<NodeDto>().FirstOrDefault(x => x.UniqueId == key);
        return BuildEntity(node);
    }

    public IEntitySlim? Get(int id, Guid objectTypeId)
    {
        NodeDto? node = _db.Set<NodeDto>().FirstOrDefault(x => x.NodeId == id && x.NodeObjectType == objectTypeId);
        return BuildEntity(node);
    }

    public IEntitySlim? Get(Guid key, Guid objectTypeId)
    {
        NodeDto? node = _db.Set<NodeDto>().FirstOrDefault(x => x.UniqueId == key && x.NodeObjectType == objectTypeId);
        return BuildEntity(node);
    }

    // ─── Multi Getters ──────────────────────────────────────────────────────
    public IEnumerable<IEntitySlim> GetAll(Guid objectType, params int[] ids)
    {
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(x => x.NodeObjectType == objectType);
        if (ids.Length > 0)
        {
            q = q.Where(x => ids.Contains(x.NodeId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IEntitySlim>();
    }

    public IEnumerable<IEntitySlim> GetAll(IEnumerable<Guid> objectTypes, params int[] ids)
    {
        Guid[] types = objectTypes.ToArray();
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(x => types.Contains(x.NodeObjectType!.Value));
        if (ids.Length > 0)
        {
            q = q.Where(x => ids.Contains(x.NodeId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IEntitySlim>();
    }

    public IEnumerable<IEntitySlim> GetAll(Guid objectType, params Guid[] keys)
    {
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(x => x.NodeObjectType == objectType);
        if (keys.Length > 0)
        {
            q = q.Where(x => keys.Contains(x.UniqueId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IEntitySlim>();
    }

    public IEnumerable<IEntitySlim> GetAll(IEnumerable<Guid> objectTypes, params Guid[] keys)
    {
        Guid[] types = objectTypes.ToArray();
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(x => types.Contains(x.NodeObjectType!.Value));
        if (keys.Length > 0)
        {
            q = q.Where(x => keys.Contains(x.UniqueId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IEntitySlim>();
    }

    // ─── Siblings ───────────────────────────────────────────────────────────
    public IEnumerable<IEntitySlim> GetSiblings(
        ISet<Guid> objectTypes,
        Guid targetKey,
        int before,
        int after,
        IQuery<IUmbracoEntity>? filter,
        Ordering ordering,
        out long totalBefore,
        out long totalAfter)
    {
        totalBefore = 0;
        totalAfter = 0;
        NodeDto? target = _db.Set<NodeDto>().FirstOrDefault(x => x.UniqueId == targetKey);
        if (target == null)
        {
            return Enumerable.Empty<IEntitySlim>();
        }

        List<NodeDto> siblings = _db.Set<NodeDto>()
            .Where(x => x.ParentId == target.ParentId && objectTypes.Contains(x.NodeObjectType!.Value) && !x.Trashed)
            .OrderBy(x => x.SortOrder)
            .ToList();

        int targetIndex = siblings.FindIndex(x => x.UniqueId == targetKey);
        if (targetIndex < 0)
        {
            return Enumerable.Empty<IEntitySlim>();
        }

        totalBefore = targetIndex;
        totalAfter = siblings.Count - 1 - targetIndex;

        var startIndex = Math.Max(0, targetIndex - before);
        var count = Math.Min(siblings.Count - startIndex, before + 1 + after);

        return siblings.Skip(startIndex).Take(count).Select(BuildEntity).Where(x => x != null).Cast<IEntitySlim>();
    }

    public IEnumerable<IEntitySlim> GetTrashedSiblings(
        ISet<Guid> objectTypes,
        Guid targetKey,
        int before,
        int after,
        IQuery<IUmbracoEntity>? filter,
        Ordering ordering,
        out long totalBefore,
        out long totalAfter)
    {
        totalBefore = 0;
        totalAfter = 0;
        NodeDto? target = _db.Set<NodeDto>().FirstOrDefault(x => x.UniqueId == targetKey);
        if (target == null)
        {
            return Enumerable.Empty<IEntitySlim>();
        }

        List<NodeDto> siblings = _db.Set<NodeDto>()
            .Where(x => x.ParentId == target.ParentId && objectTypes.Contains(x.NodeObjectType!.Value) && x.Trashed)
            .OrderBy(x => x.SortOrder)
            .ToList();

        int targetIndex = siblings.FindIndex(x => x.UniqueId == targetKey);
        if (targetIndex < 0)
        {
            return Enumerable.Empty<IEntitySlim>();
        }

        totalBefore = targetIndex;
        totalAfter = siblings.Count - 1 - targetIndex;

        var startIndex = Math.Max(0, targetIndex - before);
        var count = Math.Min(siblings.Count - startIndex, before + 1 + after);

        return siblings.Skip(startIndex).Take(count).Select(BuildEntity).Where(x => x != null).Cast<IEntitySlim>();
    }

    // ─── Query & Paging ─────────────────────────────────────────────────────
    public IEnumerable<IEntitySlim> GetByQuery(IQuery<IUmbracoEntity> query)
        => _db.Set<NodeDto>().Take(100).ToList().Select(BuildEntity).Where(x => x != null).Cast<IEntitySlim>();

    public IEnumerable<IEntitySlim> GetByQuery(IQuery<IUmbracoEntity> query, Guid objectType)
        => _db.Set<NodeDto>().Where(x => x.NodeObjectType == objectType).Take(100).ToList().Select(BuildEntity).Where(x => x != null).Cast<IEntitySlim>();

    public IEnumerable<IEntitySlim> GetPagedResultsByQuery(
        IQuery<IUmbracoEntity> query,
        ISet<Guid> objectTypes,
        long pageIndex,
        int pageSize,
        out long totalRecords,
        IQuery<IUmbracoEntity>? filter,
        Ordering? ordering)
    {
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(x => objectTypes.Contains(x.NodeObjectType!.Value));
        totalRecords = q.Count();

        var skip = (int)(pageIndex * pageSize);
        List<NodeDto> pagedNodes = q.OrderBy(x => x.SortOrder).ThenBy(x => x.NodeId).Skip(skip).Take(pageSize).ToList();

        return pagedNodes.Select(BuildEntity).Where(x => x != null).Cast<IEntitySlim>();
    }

    public int CountByQuery(IQuery<IUmbracoEntity> query, IEnumerable<Guid> objectTypes, IQuery<IUmbracoEntity>? filter)
    {
        Guid[] types = objectTypes.ToArray();
        return _db.Set<NodeDto>().Count(x => types.Contains(x.NodeObjectType!.Value));
    }

    // ─── Paths & Keys ───────────────────────────────────────────────────────
    public IEnumerable<TreeEntityPath> GetAllPaths(Guid objectType, params int[]? ids)
    {
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(x => x.NodeObjectType == objectType);
        if (ids?.Length > 0)
        {
            q = q.Where(x => ids.Contains(x.NodeId));
        }

        return q.Select(x => new TreeEntityPath
        {
            Id = x.NodeId,
            Key = x.UniqueId,
            Path = x.Path,
        }).ToList();
    }

    public IEnumerable<TreeEntityPath> GetAllPaths(Guid objectType, params Guid[] keys)
    {
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(x => x.NodeObjectType == objectType);
        if (keys.Length > 0)
        {
            q = q.Where(x => keys.Contains(x.UniqueId));
        }

        return q.Select(x => new TreeEntityPath
        {
            Id = x.NodeId,
            Key = x.UniqueId,
            Path = x.Path,
        }).ToList();
    }

    public IEnumerable<TreeEntityPath> GetAllPaths(Guid[] objectTypes, params Guid[] keys)
    {
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(x => objectTypes.Contains(x.NodeObjectType!.Value));
        if (keys.Length > 0)
        {
            q = q.Where(x => keys.Contains(x.UniqueId));
        }

        return q.Select(x => new TreeEntityPath
        {
            Id = x.NodeId,
            Key = x.UniqueId,
            Path = x.Path,
        }).ToList();
    }

    // ─── Object Types & Existence ───────────────────────────────────────────
    public UmbracoObjectTypes GetObjectType(int id)
    {
        NodeDto? node = _db.Set<NodeDto>().Find(id);
        if (node?.NodeObjectType == null)
        {
            return UmbracoObjectTypes.Unknown;
        }

        return ObjectTypes.GetUmbracoObjectType(node.NodeObjectType.Value);
    }

    public UmbracoObjectTypes GetObjectType(Guid key)
    {
        NodeDto? node = _db.Set<NodeDto>().FirstOrDefault(x => x.UniqueId == key);
        if (node?.NodeObjectType == null)
        {
            return UmbracoObjectTypes.Unknown;
        }

        return ObjectTypes.GetUmbracoObjectType(node.NodeObjectType.Value);
    }

    public int ReserveId(Guid key)
    {
        NodeDto? existing = _db.Set<NodeDto>().FirstOrDefault(x => x.UniqueId == key);
        if (existing != null)
        {
            return existing.NodeId;
        }

        NodeDto node = new()
        {
            UniqueId = key,
            Text = "reserved",
            CreateDate = DateTime.UtcNow,
            Level = 1,
            ParentId = -1,
            Path = "-1",
            SortOrder = 0,
            Trashed = false,
        };

        _db.Set<NodeDto>().Add(node);
        _db.SaveChanges();
        return node.NodeId;
    }

    public bool Exists(int id) => _db.Set<NodeDto>().Any(x => x.NodeId == id);

    public bool Exists(Guid key) => _db.Set<NodeDto>().Any(x => x.UniqueId == key);

    public bool Exists(IEnumerable<Guid> keys)
    {
        Guid[] keyArray = keys.ToArray();
        return _db.Set<NodeDto>().Count(x => keyArray.Contains(x.UniqueId)) == keyArray.Length;
    }

    public bool Exists(Guid key, Guid objectType)
        => _db.Set<NodeDto>().Any(x => x.UniqueId == key && x.NodeObjectType == objectType);

    public bool Exists(int id, Guid objectType)
        => _db.Set<NodeDto>().Any(x => x.NodeId == id && x.NodeObjectType == objectType);
}
