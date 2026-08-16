using System.Collections.Immutable;
using System.Globalization;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IDocumentRepository" />.</summary>
internal sealed class EfDocumentRepository : IDocumentRepository
{
    private readonly UmbracoDbContext _db;
    private readonly IContentTypeRepository _contentTypeRepository;
    private readonly ITemplateRepository _templateRepository;
    private static readonly Guid _documentObjectType = CoreConstants.ObjectTypes.Document;

    public EfDocumentRepository(
        UmbracoDbContext db,
        IContentTypeRepository contentTypeRepository,
        ITemplateRepository templateRepository)
    {
        _db = db;
        _contentTypeRepository = contentTypeRepository;
        _templateRepository = templateRepository;
    }

    public int RecycleBinId => CoreConstants.System.RecycleBinContent;

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IQueryable<NodeDto> DocumentNodes()
        => _db.Set<NodeDto>().Where(n => n.NodeObjectType == _documentObjectType);

    private IContent? BuildEntity(NodeDto? node)
    {
        if (node == null)
        {
            return null;
        }

        ContentDto? contentDto = _db.Set<ContentDto>().Find(node.NodeId);
        if (contentDto == null)
        {
            return null;
        }

        IContentType? contentType = _contentTypeRepository.Get(contentDto.ContentTypeId);
        if (contentType == null)
        {
            return null;
        }

        var content = new Content(node.Text ?? string.Empty, node.ParentId, contentType)
        {
            Id = node.NodeId,
            Key = node.UniqueId,
            CreateDate = node.CreateDate,
            UpdateDate = node.CreateDate,
            CreatorId = node.UserId ?? 0,
            Level = node.Level,
            Path = node.Path,
            SortOrder = node.SortOrder,
            Trashed = node.Trashed,
        };

        // Load current version
        ContentVersionDto? version = _db.Set<ContentVersionDto>()
            .Where(v => v.NodeId == node.NodeId && v.Current)
            .OrderByDescending(v => v.Id)
            .FirstOrDefault();

        if (version != null)
        {
            content.VersionId = version.Id;
            content.UpdateDate = version.VersionDate;
            if (version.UserId.HasValue)
            {
                content.WriterId = version.UserId.Value;
            }

            DocumentVersionDto? docVersion = _db.Set<DocumentVersionDto>().Find(version.Id);
            if (docVersion?.TemplateId != null)
            {
                content.TemplateId = docVersion.TemplateId.Value;
            }
        }

        content.ResetDirtyProperties(false);
        return content;
    }

    // ─── IReadRepository<int, IContent> ─────────────────────────────────────
    public IContent? Get(int id)
    {
        NodeDto? node = DocumentNodes().FirstOrDefault(n => n.NodeId == id);
        return BuildEntity(node);
    }

    public IEnumerable<IContent> GetMany(params int[]? ids)
    {
        IQueryable<NodeDto> q = DocumentNodes();
        if (ids?.Length > 0)
        {
            q = q.Where(n => ids.Contains(n.NodeId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IContent>();
    }

    public bool Exists(int id) => DocumentNodes().Any(n => n.NodeId == id);

    // ─── IReadRepository<Guid, IContent> ────────────────────────────────────
    public IContent? Get(Guid id)
    {
        NodeDto? node = DocumentNodes().FirstOrDefault(n => n.UniqueId == id);
        return BuildEntity(node);
    }

    public IEnumerable<IContent> GetMany(params Guid[]? ids)
    {
        IQueryable<NodeDto> q = DocumentNodes();
        if (ids?.Length > 0)
        {
            q = q.Where(n => ids.Contains(n.UniqueId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IContent>();
    }

    public bool Exists(Guid id) => DocumentNodes().Any(n => n.UniqueId == id);

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(IContent entity)
    {
        if (entity.HasIdentity == false)
        {
            entity.AddingEntity();

            NodeDto nodeDto = new()
            {
                CreateDate = entity.CreateDate,
                Level = short.Parse(entity.Level.ToString(CultureInfo.InvariantCulture)),
                NodeObjectType = _documentObjectType,
                ParentId = entity.ParentId,
                Path = entity.Path,
                SortOrder = entity.SortOrder,
                Text = entity.Name,
                Trashed = false,
                UniqueId = entity.Key == Guid.Empty ? Guid.NewGuid() : entity.Key,
                UserId = entity.CreatorId,
            };

            _db.Set<NodeDto>().Add(nodeDto);
            _db.SaveChanges();

            nodeDto.Path = string.Concat(entity.ParentId == -1 ? "-1" : entity.Path, ",", nodeDto.NodeId);
            _db.SaveChanges();

            entity.Id = nodeDto.NodeId;
            entity.Path = nodeDto.Path;

            ContentDto contentDto = new()
            {
                NodeId = nodeDto.NodeId,
                ContentTypeId = entity.ContentTypeId,
            };
            _db.Set<ContentDto>().Add(contentDto);

            ContentVersionDto versionDto = new()
            {
                NodeId = nodeDto.NodeId,
                Current = true,
                Text = entity.Name,
                UserId = entity.CreatorId,
                VersionDate = entity.CreateDate,
            };
            _db.Set<ContentVersionDto>().Add(versionDto);
            _db.SaveChanges();

            DocumentVersionDto docVersionDto = new()
            {
                Id = versionDto.Id,
                Published = entity.Published,
                TemplateId = entity.TemplateId,
            };
            _db.Set<DocumentVersionDto>().Add(docVersionDto);
            _db.SaveChanges();

            entity.VersionId = versionDto.Id;
        }
        else
        {
            entity.UpdatingEntity();

            NodeDto? nodeDto = _db.Set<NodeDto>().Find(entity.Id);
            if (nodeDto != null)
            {
                nodeDto.Text = entity.Name;
                nodeDto.ParentId = entity.ParentId;
                nodeDto.Path = entity.Path;
                nodeDto.SortOrder = entity.SortOrder;
                nodeDto.Trashed = entity.Trashed;
            }

            ContentVersionDto? currentVersion = _db.Set<ContentVersionDto>()
                .Where(v => v.NodeId == entity.Id && v.Current)
                .FirstOrDefault();

            if (currentVersion != null)
            {
                currentVersion.Text = entity.Name;
                currentVersion.VersionDate = entity.UpdateDate;
                currentVersion.UserId = entity.WriterId;

                DocumentVersionDto? docVersionDto = _db.Set<DocumentVersionDto>().Find(currentVersion.Id);
                if (docVersionDto != null)
                {
                    docVersionDto.Published = entity.Published;
                    docVersionDto.TemplateId = entity.TemplateId;
                }
            }

            _db.SaveChanges();
        }

        entity.ResetDirtyProperties();
    }

    public void Delete(IContent entity)
    {
        List<ContentVersionDto> versions = _db.Set<ContentVersionDto>().Where(v => v.NodeId == entity.Id).ToList();
        foreach (ContentVersionDto v in versions)
        {
            DocumentVersionDto? dv = _db.Set<DocumentVersionDto>().Find(v.Id);
            if (dv != null)
            {
                _db.Set<DocumentVersionDto>().Remove(dv);
            }

            _db.Set<PropertyDataDto>().RemoveRange(_db.Set<PropertyDataDto>().Where(p => p.VersionId == v.Id));
            _db.Set<ContentVersionDto>().Remove(v);
        }

        ContentDto? c = _db.Set<ContentDto>().Find(entity.Id);
        if (c != null)
        {
            _db.Set<ContentDto>().Remove(c);
        }

        NodeDto? n = _db.Set<NodeDto>().Find(entity.Id);
        if (n != null)
        {
            _db.Set<NodeDto>().Remove(n);
        }

        _db.SaveChanges();
    }

    // ─── Query & Paging ─────────────────────────────────────────────────────
    public IEnumerable<IContent> Get(IQuery<IContent> query) => GetMany(Array.Empty<int>());

    public int Count(IQuery<IContent>? query) => DocumentNodes().Count();

    public int Count(string? contentTypeAlias = null)
    {
        if (string.IsNullOrEmpty(contentTypeAlias))
        {
            return DocumentNodes().Count();
        }

        ContentTypeDto? ct = _db.Set<ContentTypeDto>().FirstOrDefault(x => x.Alias == contentTypeAlias);
        if (ct == null)
        {
            return 0;
        }

        return _db.Set<ContentDto>().Count(c => c.ContentTypeId == ct.NodeId);
    }

    public int CountChildren(int parentId, string? contentTypeAlias = null)
        => DocumentNodes().Count(n => n.ParentId == parentId && !n.Trashed);

    public int CountDescendants(int parentId, string? contentTypeAlias = null)
        => DocumentNodes().Count(n => n.Path.Contains($",{parentId},") && !n.Trashed);

    public IEnumerable<IContent> GetPage(
        IQuery<IContent>? query,
        long pageIndex,
        int pageSize,
        out long totalRecords,
        IQuery<IContent>? filter,
        Ordering? ordering)
        => GetPage(query, pageIndex, pageSize, out totalRecords, null, filter, ordering, true);

    public IEnumerable<IContent> GetPage(
        IQuery<IContent>? query,
        long pageIndex,
        int pageSize,
        out long totalRecords,
        string[]? propertyAliases,
        IQuery<IContent>? filter,
        Ordering? ordering,
        bool loadTemplates)
    {
        IQueryable<NodeDto> q = DocumentNodes().Where(n => !n.Trashed);
        totalRecords = q.Count();

        var skip = (int)(pageIndex * pageSize);
        List<NodeDto> nodes = q.OrderBy(n => n.SortOrder).ThenBy(n => n.NodeId).Skip(skip).Take(pageSize).ToList();

        return nodes.Select(BuildEntity).Where(x => x != null).Cast<IContent>();
    }

    // ─── Versions ───────────────────────────────────────────────────────────
    public IEnumerable<IContent> GetAllVersions(int nodeId) => GetMany(nodeId);

    public IEnumerable<IContent> GetAllVersionsSlim(int nodeId, int skip, int take) => GetMany(nodeId);

    public IEnumerable<int> GetVersionIds(int id, int topRows)
        => _db.Set<ContentVersionDto>().Where(v => v.NodeId == id).OrderByDescending(v => v.Id).Take(topRows).Select(v => v.Id).ToList();

    public IContent? GetVersion(int versionId)
    {
        ContentVersionDto? v = _db.Set<ContentVersionDto>().Find(versionId);
        return v == null ? null : Get(v.NodeId);
    }

    public void DeleteVersion(int versionId)
    {
        ContentVersionDto? v = _db.Set<ContentVersionDto>().Find(versionId);
        if (v != null)
        {
            _db.Set<ContentVersionDto>().Remove(v);
            _db.SaveChanges();
        }
    }

    public void DeleteVersions(int nodeId, DateTime versionDate)
    {
        List<ContentVersionDto> versions = _db.Set<ContentVersionDto>()
            .Where(v => v.NodeId == nodeId && v.VersionDate < versionDate && !v.Current)
            .ToList();
        _db.Set<ContentVersionDto>().RemoveRange(versions);
        _db.SaveChanges();
    }

    public IEnumerable<IContent> GetRecycleBin()
    {
        List<NodeDto> nodes = DocumentNodes().Where(n => n.Trashed).ToList();
        return nodes.Select(BuildEntity).Where(x => x != null).Cast<IContent>();
    }

    public bool RecycleBinSmells() => DocumentNodes().Any(n => n.Trashed);

    public ContentDataIntegrityReport CheckDataIntegrity(ContentDataIntegrityReportOptions options)
        => new(new Dictionary<int, ContentDataIntegrityReportEntry>());

    // ─── IPublishableContentRepository ──────────────────────────────────────
    public ContentScheduleCollection GetContentSchedule(int contentId) => new();

    public void PersistContentSchedule(IPublishableContentBase content, ContentScheduleCollection schedule) { }

    public void ClearSchedule(DateTime date) { }

    public void ClearSchedule(DateTime date, ContentScheduleAction action) { }

    public bool HasContentForExpiration(DateTime date) => false;

    public bool HasContentForRelease(DateTime date) => false;

    public IEnumerable<IContent> GetContentForExpiration(DateTime date) => Enumerable.Empty<IContent>();

    public IEnumerable<IContent> GetContentForRelease(DateTime date) => Enumerable.Empty<IContent>();

    public int CountPublished(string? contentTypeAlias = null)
        => _db.Set<DocumentVersionDto>().Count(d => d.Published);

    public bool IsPathPublished(IContent? content) => content != null && content.Published;

    public IDictionary<int, IEnumerable<ContentSchedule>> GetContentSchedulesByIds(int[] contentIds)
        => ImmutableDictionary<int, IEnumerable<ContentSchedule>>.Empty;

    // ─── Permissions ────────────────────────────────────────────────────────
    public void ReplaceContentPermissions(EntityPermissionSet permissionSet) { }

    public void AssignEntityPermission(IContent entity, string permission, IEnumerable<int> groupIds) { }

    public EntityPermissionCollection GetPermissionsForEntity(int entityId) => new();

    public void AddOrUpdatePermissions(ContentPermissionSet permission) { }
}
