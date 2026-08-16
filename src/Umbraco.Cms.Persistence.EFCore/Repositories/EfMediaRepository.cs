using System.Globalization;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IMediaRepository" />.</summary>
internal sealed class EfMediaRepository : IMediaRepository
{
    private readonly UmbracoDbContext _db;
    private readonly IMediaTypeRepository _mediaTypeRepository;
    private static readonly Guid _mediaObjectType = CoreConstants.ObjectTypes.Media;

    public EfMediaRepository(UmbracoDbContext db, IMediaTypeRepository mediaTypeRepository)
    {
        _db = db;
        _mediaTypeRepository = mediaTypeRepository;
    }

    public int RecycleBinId => CoreConstants.System.RecycleBinMedia;

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IQueryable<NodeDto> MediaNodes()
        => _db.Set<NodeDto>().Where(n => n.NodeObjectType == _mediaObjectType);

    private IMedia? BuildEntity(NodeDto? node)
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

        IMediaType? mediaType = _mediaTypeRepository.Get(contentDto.ContentTypeId);
        if (mediaType == null)
        {
            return null;
        }

        var media = new Media(node.Text ?? string.Empty, node.ParentId, mediaType)
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

        ContentVersionDto? version = _db.Set<ContentVersionDto>()
            .Where(v => v.NodeId == node.NodeId && v.Current)
            .OrderByDescending(v => v.Id)
            .FirstOrDefault();

        if (version != null)
        {
            media.VersionId = version.Id;
            media.UpdateDate = version.VersionDate;
            if (version.UserId.HasValue)
            {
                media.WriterId = version.UserId.Value;
            }
        }

        media.ResetDirtyProperties(false);
        return media;
    }

    // ─── IReadRepository<int, IMedia> ───────────────────────────────────────
    public IMedia? Get(int id)
    {
        NodeDto? node = MediaNodes().FirstOrDefault(n => n.NodeId == id);
        return BuildEntity(node);
    }

    public IEnumerable<IMedia> GetMany(params int[]? ids)
    {
        IQueryable<NodeDto> q = MediaNodes();
        if (ids?.Length > 0)
        {
            q = q.Where(n => ids.Contains(n.NodeId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IMedia>();
    }

    public bool Exists(int id) => MediaNodes().Any(n => n.NodeId == id);

    // ─── IReadRepository<Guid, IMedia> ──────────────────────────────────────
    public IMedia? Get(Guid id)
    {
        NodeDto? node = MediaNodes().FirstOrDefault(n => n.UniqueId == id);
        return BuildEntity(node);
    }

    public IEnumerable<IMedia> GetMany(params Guid[]? ids)
    {
        IQueryable<NodeDto> q = MediaNodes();
        if (ids?.Length > 0)
        {
            q = q.Where(n => ids.Contains(n.UniqueId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IMedia>();
    }

    public bool Exists(Guid id) => MediaNodes().Any(n => n.UniqueId == id);

    // ─── Extra Media Methods ────────────────────────────────────────────────
    public IMedia? GetMediaByPath(string mediaPath)
    {
        MediaVersionDto? mv = _db.Set<MediaVersionDto>().FirstOrDefault(m => m.Path == mediaPath);
        if (mv == null)
        {
            return null;
        }

        ContentVersionDto? cv = _db.Set<ContentVersionDto>().Find(mv.Id);
        return cv == null ? null : Get(cv.NodeId);
    }

    public bool RecycleBinSmells() => MediaNodes().Any(n => n.Trashed);

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(IMedia entity)
    {
        if (entity.HasIdentity == false)
        {
            entity.AddingEntity();

            NodeDto nodeDto = new()
            {
                CreateDate = entity.CreateDate,
                Level = short.Parse(entity.Level.ToString(CultureInfo.InvariantCulture)),
                NodeObjectType = _mediaObjectType,
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

            MediaVersionDto mediaVersionDto = new()
            {
                Id = versionDto.Id,
                Path = entity.Path,
            };
            _db.Set<MediaVersionDto>().Add(mediaVersionDto);
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
            }

            _db.SaveChanges();
        }

        entity.ResetDirtyProperties();
    }

    public void Delete(IMedia entity)
    {
        List<ContentVersionDto> versions = _db.Set<ContentVersionDto>().Where(v => v.NodeId == entity.Id).ToList();
        foreach (ContentVersionDto v in versions)
        {
            MediaVersionDto? mv = _db.Set<MediaVersionDto>().Find(v.Id);
            if (mv != null)
            {
                _db.Set<MediaVersionDto>().Remove(mv);
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
    public IEnumerable<IMedia> Get(IQuery<IMedia> query) => GetMany(Array.Empty<int>());

    public int Count(IQuery<IMedia>? query) => MediaNodes().Count();

    public int Count(string? contentTypeAlias = null)
    {
        if (string.IsNullOrEmpty(contentTypeAlias))
        {
            return MediaNodes().Count();
        }

        ContentTypeDto? ct = _db.Set<ContentTypeDto>().FirstOrDefault(x => x.Alias == contentTypeAlias);
        if (ct == null)
        {
            return 0;
        }

        return _db.Set<ContentDto>().Count(c => c.ContentTypeId == ct.NodeId);
    }

    public int CountChildren(int parentId, string? contentTypeAlias = null)
        => MediaNodes().Count(n => n.ParentId == parentId && !n.Trashed);

    public int CountDescendants(int parentId, string? contentTypeAlias = null)
        => MediaNodes().Count(n => n.Path.Contains($",{parentId},") && !n.Trashed);

    public IEnumerable<IMedia> GetPage(
        IQuery<IMedia>? query,
        long pageIndex,
        int pageSize,
        out long totalRecords,
        IQuery<IContent>? filter,
        Ordering? ordering)
    {
        IQueryable<NodeDto> q = MediaNodes().Where(n => !n.Trashed);
        totalRecords = q.Count();

        var skip = (int)(pageIndex * pageSize);
        List<NodeDto> nodes = q.OrderBy(n => n.SortOrder).ThenBy(n => n.NodeId).Skip(skip).Take(pageSize).ToList();

        return nodes.Select(BuildEntity).Where(x => x != null).Cast<IMedia>();
    }

    public IEnumerable<IMedia> GetPage(
        IQuery<IMedia>? query,
        long pageIndex,
        int pageSize,
        out long totalRecords,
        IQuery<IMedia>? filter,
        Ordering? ordering)
    {
        IQueryable<NodeDto> q = MediaNodes().Where(n => !n.Trashed);
        totalRecords = q.Count();

        var skip = (int)(pageIndex * pageSize);
        List<NodeDto> nodes = q.OrderBy(n => n.SortOrder).ThenBy(n => n.NodeId).Skip(skip).Take(pageSize).ToList();

        return nodes.Select(BuildEntity).Where(x => x != null).Cast<IMedia>();
    }

    // ─── Versions ───────────────────────────────────────────────────────────
    public IEnumerable<IMedia> GetAllVersions(int nodeId) => GetMany(nodeId);

    public IEnumerable<IMedia> GetAllVersionsSlim(int nodeId, int skip, int take) => GetMany(nodeId);

    public IEnumerable<int> GetVersionIds(int id, int topRows)
        => _db.Set<ContentVersionDto>().Where(v => v.NodeId == id).OrderByDescending(v => v.Id).Take(topRows).Select(v => v.Id).ToList();

    public IMedia? GetVersion(int versionId)
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

    public IEnumerable<IMedia> GetRecycleBin()
    {
        List<NodeDto> nodes = MediaNodes().Where(n => n.Trashed).ToList();
        return nodes.Select(BuildEntity).Where(x => x != null).Cast<IMedia>();
    }

    public ContentDataIntegrityReport CheckDataIntegrity(ContentDataIntegrityReportOptions options)
        => new(new Dictionary<int, ContentDataIntegrityReportEntry>());
}
