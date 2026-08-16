using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IDataTypeRepository" />.</summary>
internal sealed class EfDataTypeRepository : IDataTypeRepository
{
    private readonly UmbracoDbContext _db;
    private readonly PropertyEditorCollection _editors;
    private readonly IConfigurationEditorJsonSerializer _serializer;
    private readonly IDataValueEditorFactory _dataValueEditorFactory;
    private readonly ILogger<IDataType> _dataTypeLogger;
    private static readonly Guid _dataTypeObjectType = CoreConstants.ObjectTypes.DataType;

    public EfDataTypeRepository(
        UmbracoDbContext db,
        PropertyEditorCollection editors,
        ILoggerFactory loggerFactory,
        IConfigurationEditorJsonSerializer serializer,
        IDataValueEditorFactory dataValueEditorFactory)
    {
        _db = db;
        _editors = editors;
        _serializer = serializer;
        _dataValueEditorFactory = dataValueEditorFactory;
        _dataTypeLogger = loggerFactory.CreateLogger<IDataType>();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IQueryable<DataTypeDto> DataTypeQuery()
        => _db.Set<DataTypeDto>();

    private IDataType? BuildEntity(DataTypeDto dto)
    {
        NodeDto? node = _db.Set<NodeDto>().Find(dto.NodeId);
        if (node == null)
        {
            return null;
        }

        dto.NodeDto = node;
        return DataTypeFactory.BuildEntity(dto, _editors, _dataTypeLogger, _serializer, _dataValueEditorFactory);
    }

    // ─── IReadRepository<int, IDataType> ────────────────────────────────────
    public IDataType? Get(int id)
    {
        DataTypeDto? dto = DataTypeQuery().FirstOrDefault(x => x.NodeId == id);
        return dto == null ? null : BuildEntity(dto);
    }

    public IEnumerable<IDataType> GetMany(params int[]? ids)
    {
        IQueryable<DataTypeDto> q = DataTypeQuery();
        if (ids?.Length > 0)
        {
            q = q.Where(x => ids.Contains(x.NodeId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IDataType>();
    }

    public bool Exists(int id) => DataTypeQuery().Any(x => x.NodeId == id);

    // ─── IReadRepository<Guid, IDataType> ───────────────────────────────────
    public IDataType? Get(Guid id)
    {
        NodeDto? node = _db.Set<NodeDto>().FirstOrDefault(n => n.UniqueId == id && n.NodeObjectType == _dataTypeObjectType);
        if (node == null)
        {
            return null;
        }

        DataTypeDto? dto = DataTypeQuery().FirstOrDefault(x => x.NodeId == node.NodeId);
        return dto == null ? null : BuildEntity(dto);
    }

    public IEnumerable<IDataType> GetMany(params Guid[]? ids)
    {
        IQueryable<NodeDto> nodeQ = _db.Set<NodeDto>().Where(n => n.NodeObjectType == _dataTypeObjectType);
        if (ids?.Length > 0)
        {
            nodeQ = nodeQ.Where(n => ids.Contains(n.UniqueId));
        }

        int[] nodeIds = nodeQ.Select(n => n.NodeId).ToArray();
        return DataTypeQuery().Where(x => nodeIds.Contains(x.NodeId)).ToList()
            .Select(BuildEntity).Where(x => x != null).Cast<IDataType>();
    }

    public bool Exists(Guid id)
        => _db.Set<NodeDto>().Any(n => n.UniqueId == id && n.NodeObjectType == _dataTypeObjectType);

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(IDataType entity)
    {
        if (entity.HasIdentity == false)
        {
            PersistNewItem(entity);
        }
        else
        {
            PersistUpdatedItem(entity);
        }
    }

    private void PersistNewItem(IDataType entity)
    {
        entity.AddingEntity();

        DataTypeDto dto = DataTypeFactory.BuildDto(entity, _serializer);
        NodeDto nodeDto = dto.NodeDto;

        NodeDto? parent = _db.Set<NodeDto>().Find(entity.ParentId);
        var level = parent != null ? parent.Level + 1 : 1;
        var sortOrder = _db.Set<NodeDto>().Count(x => x.ParentId == entity.ParentId && x.NodeObjectType == _dataTypeObjectType);

        nodeDto.Path = parent != null ? parent.Path : "-1";
        nodeDto.Level = short.Parse(level.ToString(CultureInfo.InvariantCulture));
        nodeDto.SortOrder = sortOrder;

        _db.Set<NodeDto>().Add(nodeDto);
        _db.SaveChanges();

        nodeDto.Path = string.Concat(nodeDto.Path, ",", nodeDto.NodeId);
        _db.SaveChanges();

        entity.Id = nodeDto.NodeId;
        entity.Path = nodeDto.Path;
        entity.SortOrder = sortOrder;
        entity.Level = level;

        dto.NodeId = nodeDto.NodeId;
        _db.Set<DataTypeDto>().Add(dto);
        _db.SaveChanges();

        entity.ResetDirtyProperties();
    }

    private void PersistUpdatedItem(IDataType entity)
    {
        entity.UpdatingEntity();

        NodeDto? nodeDto = _db.Set<NodeDto>().Find(entity.Id);
        if (nodeDto != null)
        {
            nodeDto.Level = short.Parse(entity.Level.ToString(CultureInfo.InvariantCulture));
            nodeDto.ParentId = entity.ParentId;
            nodeDto.Path = entity.Path;
            nodeDto.SortOrder = entity.SortOrder;
            nodeDto.Text = entity.Name;
            nodeDto.Trashed = entity.Trashed;
            nodeDto.UserId = entity.CreatorId;
            nodeDto.UniqueId = entity.Key;
        }

        DataTypeDto? dto = _db.Set<DataTypeDto>().Find(entity.Id);
        if (dto != null)
        {
            dto.EditorAlias = entity.EditorAlias;
            dto.EditorUiAlias = entity.EditorUiAlias;
            dto.DbType = entity.DatabaseType.ToString();
            dto.Configuration = entity.Editor?.GetConfigurationEditor().ToDatabase(entity.ConfigurationData, _serializer);
        }

        _db.SaveChanges();
        entity.ResetDirtyProperties();
    }

    public void Delete(IDataType entity)
    {
        DataTypeDto? dto = _db.Set<DataTypeDto>().Find(entity.Id);
        if (dto != null)
        {
            _db.Set<DataTypeDto>().Remove(dto);
        }

        NodeDto? nodeDto = _db.Set<NodeDto>().Find(entity.Id);
        if (nodeDto != null)
        {
            _db.Set<NodeDto>().Remove(nodeDto);
        }

        _db.SaveChanges();
    }

    public IEnumerable<IDataType> Get(IQuery<IDataType> query)
        => DataTypeQuery().ToList().Select(BuildEntity).Where(x => x != null).Cast<IDataType>();

    public int Count(IQuery<IDataType>? query) => DataTypeQuery().Count();

    // ─── IDataTypeRepository Specific Methods ───────────────────────────────
    public IEnumerable<MoveEventInfo<IDataType>> Move(IDataType toMove, EntityContainer? container)
    {
        var parentId = container?.Id ?? CoreConstants.System.Root;
        var moveInfo = new List<MoveEventInfo<IDataType>>
        {
            new(toMove, toMove.Path, toMove.ParentId),
        };

        var origPath = toMove.Path;
        toMove.ParentId = parentId;
        toMove.Path = string.Concat(container == null ? parentId.ToInvariantString() : container.Path, ",", toMove.Id);
        Save(toMove);

        // Update all descendants
        List<NodeDto> descendantNodes = _db.Set<NodeDto>()
            .Where(n => n.NodeObjectType == _dataTypeObjectType && n.Path.StartsWith(origPath + ","))
            .OrderBy(n => n.Level)
            .ToList();

        IDataType lastParent = toMove;
        foreach (NodeDto descNode in descendantNodes)
        {
            DataTypeDto? descDto = _db.Set<DataTypeDto>().Find(descNode.NodeId);
            if (descDto != null)
            {
                IDataType? descEntity = BuildEntity(descDto);
                if (descEntity != null)
                {
                    moveInfo.Add(new MoveEventInfo<IDataType>(descEntity, descEntity.Path, descEntity.ParentId));
                    descEntity.ParentId = lastParent.Id;
                    descEntity.Path = string.Concat(lastParent.Path, ",", descEntity.Id);
                    Save(descEntity);
                }
            }
        }

        return moveInfo;
    }

    public IReadOnlyDictionary<Udi, IEnumerable<string>> FindUsages(int id)
    {
        if (id == default)
        {
            return new Dictionary<Udi, IEnumerable<string>>();
        }

        var propTypes = _db.Set<PropertyTypeDto>()
            .Where(pt => pt.DataTypeId == id)
            .Join(_db.Set<NodeDto>(), pt => pt.ContentTypeId, n => n.NodeId, (pt, n) => new
            {
                pt.Alias,
                n.UniqueId,
                n.NodeObjectType
            })
            .ToList();

        return propTypes
            .GroupBy(x => x.UniqueId)
            .ToDictionary(
                g => (Udi)new GuidUdi(CoreConstants.UdiEntityType.DocumentType, g.Key).EnsureClosed(),
                g => (IEnumerable<string>)g.Select(p => p.Alias).ToList());
    }

    public IReadOnlyDictionary<Udi, IEnumerable<string>> FindListViewUsages(int id)
    {
        var usages = new Dictionary<Udi, IEnumerable<string>>();
        if (id == default)
        {
            return usages;
        }

        IDataType? dataType = Get(id);
        if (dataType is null || dataType.EditorAlias.Equals(CoreConstants.PropertyEditors.Aliases.ListView) is false)
        {
            return usages;
        }

        var contentTypes = _db.Set<ContentTypeDto>()
            .Where(ct => ct.ListView == dataType.Key)
            .Join(_db.Set<NodeDto>(), ct => ct.NodeId, n => n.NodeId, (ct, n) => new
            {
                n.UniqueId,
                n.Text
            })
            .ToList();

        return contentTypes.ToDictionary(
            x => (Udi)new GuidUdi(CoreConstants.UdiEntityType.DocumentType, x.UniqueId).EnsureClosed(),
            x => (IEnumerable<string>)new[] { x.Text ?? string.Empty });
    }
}
