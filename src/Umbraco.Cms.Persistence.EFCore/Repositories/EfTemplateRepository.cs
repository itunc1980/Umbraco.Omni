using System.Text;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Core.Constants;
using File = Umbraco.Cms.Core.Models.File;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="ITemplateRepository" />.</summary>
internal sealed class EfTemplateRepository : ITemplateRepository
{
    private readonly UmbracoDbContext _db;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly IFileSystem? _viewsFileSystem;
    private readonly IViewHelper _viewHelper;
    private readonly IOptionsMonitor<RuntimeSettings> _runtimeSettings;
    private static readonly Guid _templateObjectType = CoreConstants.ObjectTypes.Template;

    public EfTemplateRepository(
        UmbracoDbContext db,
        IShortStringHelper shortStringHelper,
        FileSystems fileSystems,
        IViewHelper viewHelper,
        IOptionsMonitor<RuntimeSettings> runtimeSettings)
    {
        _db = db;
        _shortStringHelper = shortStringHelper;
        _viewsFileSystem = fileSystems.MvcViewsFileSystem;
        _viewHelper = viewHelper;
        _runtimeSettings = runtimeSettings;
    }

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IQueryable<TemplateDto> TemplateQuery() => _db.Set<TemplateDto>();

    private IEnumerable<IUmbracoEntity> GetAxisDefinitions(params TemplateDto[] templates)
    {
        int[] parentIds = templates.Select(x => x.NodeDto.ParentId).Where(id => id > 0).Distinct().ToArray();
        int[] nodeIds = templates.Select(x => x.NodeId).ToArray();

        var matches = _db.Set<TemplateDto>()
            .Join(_db.Set<NodeDto>(), t => t.NodeId, n => n.NodeId, (t, n) => new { t.NodeId, n.ParentId, t.Alias })
            .Where(x => parentIds.Contains(x.NodeId) || nodeIds.Contains(x.ParentId))
            .ToList();

        return matches.Select(x => new EntitySlim { Id = x.NodeId, ParentId = x.ParentId, Name = x.Alias });
    }

    private ITemplate? BuildEntity(TemplateDto dto, IUmbracoEntity[]? axisDefinitions = null)
    {
        NodeDto? node = _db.Set<NodeDto>().Find(dto.NodeId);
        if (node == null)
        {
            return null;
        }

        dto.NodeDto = node;
        axisDefinitions ??= GetAxisDefinitions(dto).ToArray();

        Template template = TemplateFactory.BuildEntity(
            _shortStringHelper,
            dto,
            axisDefinitions,
            file => GetFileContent((Template)file, false));

        if (dto.NodeDto.ParentId > 0)
        {
            IUmbracoEntity? layoutTemplate = axisDefinitions.FirstOrDefault(x => x.Id == dto.NodeDto.ParentId);
            if (layoutTemplate != null)
            {
                template.LayoutTemplateAlias = layoutTemplate.Name;
                template.LayoutTemplateId = new Lazy<int>(() => dto.NodeDto.ParentId);
            }
        }

        return template;
    }

    // ─── IReadRepository<int, ITemplate> ────────────────────────────────────
    public ITemplate? Get(int id)
    {
        TemplateDto? dto = TemplateQuery().FirstOrDefault(x => x.NodeId == id);
        return dto == null ? null : BuildEntity(dto);
    }

    public IEnumerable<ITemplate> GetMany(params int[]? ids)
    {
        IQueryable<TemplateDto> q = TemplateQuery();
        if (ids?.Length > 0)
        {
            q = q.Where(x => ids.Contains(x.NodeId));
        }

        List<TemplateDto> dtos = q.ToList();
        foreach (TemplateDto d in dtos)
        {
            NodeDto? n = _db.Set<NodeDto>().Find(d.NodeId);
            if (n != null)
            {
                d.NodeDto = n;
            }
        }

        IUmbracoEntity[] axis = GetAxisDefinitions(dtos.ToArray()).ToArray();
        return dtos.Select(d => BuildEntity(d, axis)).Where(x => x != null).Cast<ITemplate>();
    }

    public bool Exists(int id) => TemplateQuery().Any(x => x.NodeId == id);

    // ─── IReadRepository<Guid, ITemplate> ───────────────────────────────────
    public ITemplate? Get(Guid id)
    {
        NodeDto? node = _db.Set<NodeDto>().FirstOrDefault(n => n.UniqueId == id && n.NodeObjectType == _templateObjectType);
        if (node == null)
        {
            return null;
        }

        TemplateDto? dto = TemplateQuery().FirstOrDefault(x => x.NodeId == node.NodeId);
        return dto == null ? null : BuildEntity(dto);
    }

    public IEnumerable<ITemplate> GetMany(params Guid[]? ids)
    {
        IQueryable<NodeDto> nodeQ = _db.Set<NodeDto>().Where(n => n.NodeObjectType == _templateObjectType);
        if (ids?.Length > 0)
        {
            nodeQ = nodeQ.Where(n => ids.Contains(n.UniqueId));
        }

        int[] nodeIds = nodeQ.Select(n => n.NodeId).ToArray();
        return GetMany(nodeIds);
    }

    public bool Exists(Guid id)
        => _db.Set<NodeDto>().Any(n => n.UniqueId == id && n.NodeObjectType == _templateObjectType);

    // ─── ITemplateRepository Specific ───────────────────────────────────────
    public ITemplate? Get(string? alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return null;
        }

        TemplateDto? dto = TemplateQuery().FirstOrDefault(x => x.Alias == alias);
        return dto == null ? null : BuildEntity(dto);
    }

    public IEnumerable<ITemplate> GetAll(params string[] aliases)
    {
        if (aliases.Length == 0)
        {
            return GetMany(Array.Empty<int>());
        }

        List<TemplateDto> dtos = TemplateQuery().Where(x => aliases.Contains(x.Alias)).ToList();
        return dtos.Select(d => BuildEntity(d)).Where(x => x != null).Cast<ITemplate>();
    }

    public IEnumerable<ITemplate> GetChildren(int layoutTemplateId)
    {
        int[] childNodeIds = _db.Set<NodeDto>()
            .Where(n => n.ParentId == layoutTemplateId && n.NodeObjectType == _templateObjectType)
            .Select(n => n.NodeId)
            .ToArray();
        return GetMany(childNodeIds);
    }

    public IEnumerable<ITemplate> GetDescendants(int layoutTemplateId)
    {
        NodeDto? parent = _db.Set<NodeDto>().Find(layoutTemplateId);
        if (parent == null)
        {
            return Enumerable.Empty<ITemplate>();
        }

        int[] descNodeIds = _db.Set<NodeDto>()
            .Where(n => n.NodeObjectType == _templateObjectType && n.Path.StartsWith(parent.Path + ","))
            .Select(n => n.NodeId)
            .ToArray();
        return GetMany(descNodeIds);
    }

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(ITemplate entity)
    {
        var template = (Template)entity;
        if (template.HasIdentity == false)
        {
            template.AddingEntity();

            NodeDto nodeDto = new()
            {
                CreateDate = template.CreateDate,
                Level = 1,
                NodeObjectType = _templateObjectType,
                ParentId = template.LayoutTemplateId?.Value ?? 0,
                Path = template.Path,
                Text = template.Name,
                Trashed = false,
                UniqueId = template.Key == Guid.Empty ? Guid.NewGuid() : template.Key,
            };

            _db.Set<NodeDto>().Add(nodeDto);
            _db.SaveChanges();

            nodeDto.Path = $"-1,{nodeDto.NodeId}";
            _db.SaveChanges();

            template.Id = nodeDto.NodeId;
            template.Path = nodeDto.Path;

            TemplateDto dto = new()
            {
                Alias = template.Alias,
                NodeId = nodeDto.NodeId,
            };

            _db.Set<TemplateDto>().Add(dto);
            _db.SaveChanges();
        }
        else
        {
            template.UpdatingEntity();

            NodeDto? nodeDto = _db.Set<NodeDto>().Find(template.Id);
            if (nodeDto != null)
            {
                nodeDto.Text = template.Name;
                nodeDto.ParentId = template.LayoutTemplateId?.Value ?? 0;
                nodeDto.Path = template.Path;
            }

            TemplateDto? dto = _db.Set<TemplateDto>().FirstOrDefault(x => x.NodeId == template.Id);
            if (dto != null)
            {
                dto.Alias = template.Alias;
            }

            _db.SaveChanges();
        }

        // Save view file if content is dirty
        if (template.IsPropertyDirty("Content"))
        {
            SaveFile(template, template.Content);
        }

        template.ResetDirtyProperties();
    }

    public void Delete(ITemplate entity)
    {
        TemplateDto? dto = _db.Set<TemplateDto>().FirstOrDefault(x => x.NodeId == entity.Id);
        if (dto != null)
        {
            _db.Set<TemplateDto>().Remove(dto);
        }

        NodeDto? nodeDto = _db.Set<NodeDto>().Find(entity.Id);
        if (nodeDto != null)
        {
            _db.Set<NodeDto>().Remove(nodeDto);
        }

        _db.SaveChanges();

        // Delete view file
        if (_viewsFileSystem != null && !string.IsNullOrWhiteSpace(entity.VirtualPath) && _viewsFileSystem.FileExists(entity.VirtualPath))
        {
            _viewsFileSystem.DeleteFile(entity.VirtualPath);
        }
    }

    public IEnumerable<ITemplate> Get(IQuery<ITemplate> query) => GetMany(Array.Empty<int>());

    public int Count(IQuery<ITemplate>? query) => TemplateQuery().Count();

    // ─── IFileRepository ────────────────────────────────────────────────────
    public Stream GetFileContentStream(string filepath)
    {
        IFileSystem? fileSystem = GetFileSystem(filepath);
        if (fileSystem?.FileExists(filepath) == false)
        {
            return Stream.Null;
        }

        try
        {
            return fileSystem!.OpenFile(filepath);
        }
        catch
        {
            return Stream.Null;
        }
    }

    public void SetFileContent(string filepath, Stream content)
        => GetFileSystem(filepath)?.AddFile(filepath, content, true);

    public long GetFileSize(string filename)
    {
        IFileSystem? fileSystem = GetFileSystem(filename);
        if (fileSystem?.FileExists(filename) == false)
        {
            return -1;
        }

        try
        {
            return fileSystem!.GetSize(filename);
        }
        catch
        {
            return -1;
        }
    }

    private IFileSystem? GetFileSystem(string path) => _viewsFileSystem;

    private string? GetFileContent(Template template, bool setPath)
    {
        if (string.IsNullOrWhiteSpace(template.VirtualPath))
        {
            template.VirtualPath = _viewHelper.ViewPath(template.Alias);
        }

        if (_viewsFileSystem?.FileExists(template.VirtualPath) != true)
        {
            return string.Empty;
        }

        using Stream stream = _viewsFileSystem.OpenFile(template.VirtualPath);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private void SaveFile(ITemplate template, string? content)
    {
        if (string.IsNullOrWhiteSpace(template.VirtualPath))
        {
            template.VirtualPath = _viewHelper.ViewPath(template.Alias);
        }

        if (_viewsFileSystem != null && content != null)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            _viewsFileSystem.AddFile(template.VirtualPath, stream, true);
        }
    }
}
