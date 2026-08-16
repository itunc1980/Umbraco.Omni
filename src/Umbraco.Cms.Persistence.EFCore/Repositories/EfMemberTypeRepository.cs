using System.Globalization;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IMemberTypeRepository" />.</summary>
internal sealed class EfMemberTypeRepository : IMemberTypeRepository
{
    private readonly UmbracoDbContext _db;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly PropertyEditorCollection _editors;
    private readonly IConfigurationEditorJsonSerializer _serializer;
    private readonly IDataValueEditorFactory _dataValueEditorFactory;
    private static readonly Guid _memberTypeObjectType = CoreConstants.ObjectTypes.MemberType;

    public EfMemberTypeRepository(
        UmbracoDbContext db,
        IShortStringHelper shortStringHelper,
        PropertyEditorCollection editors,
        IConfigurationEditorJsonSerializer serializer,
        IDataValueEditorFactory dataValueEditorFactory)
    {
        _db = db;
        _shortStringHelper = shortStringHelper;
        _editors = editors;
        _serializer = serializer;
        _dataValueEditorFactory = dataValueEditorFactory;
    }

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IQueryable<ContentTypeDto> MemberTypeQuery()
    {
        int[] memberTypeNodeIds = _db.Set<NodeDto>()
            .Where(n => n.NodeObjectType == _memberTypeObjectType)
            .Select(n => n.NodeId)
            .ToArray();
        return _db.Set<ContentTypeDto>().Where(c => memberTypeNodeIds.Contains(c.NodeId));
    }

    private IMemberType? BuildEntity(ContentTypeDto dto)
    {
        NodeDto? node = _db.Set<NodeDto>().Find(dto.NodeId);
        if (node == null)
        {
            return null;
        }

        dto.NodeDto = node;
        IMemberType memberType = ContentTypeFactory.BuildMemberTypeEntity(_shortStringHelper, dto);

        // Load property groups & property types
        List<PropertyTypeGroupDto> groupDtos = _db.Set<PropertyTypeGroupDto>()
            .Where(x => x.ContentTypeNodeId == dto.NodeId)
            .ToList();

        foreach (PropertyTypeGroupDto groupDto in groupDtos)
        {
            groupDto.PropertyTypeDtos = _db.Set<PropertyTypeDto>()
                .Where(x => x.PropertyTypeGroupId == groupDto.Id)
                .ToList();

            foreach (PropertyTypeDto ptDto in groupDto.PropertyTypeDtos)
            {
                DataTypeDto? dtDto = _db.Set<DataTypeDto>().Find(ptDto.DataTypeId);
                if (dtDto != null)
                {
                    NodeDto? dtNode = _db.Set<NodeDto>().Find(dtDto.NodeId);
                    if (dtNode != null)
                    {
                        dtDto.NodeDto = dtNode;
                    }

                    ptDto.DataTypeDto = dtDto;
                }
            }
        }

        memberType.PropertyGroups = new PropertyGroupCollection(PropertyGroupFactory.BuildEntity(
            groupDtos,
            isPublishing: false,
            dto.NodeId,
            node.CreateDate,
            node.CreateDate,
            (alias, storageType, propAlias) =>
                new PropertyType(_shortStringHelper, alias ?? "Umbraco.TextBox", storageType, propAlias ?? string.Empty)));

        memberType.ResetDirtyProperties(false);
        return memberType;
    }

    // ─── IReadRepository<int, IMemberType> ───────────────────────────────────
    public IMemberType? Get(int id)
    {
        ContentTypeDto? dto = MemberTypeQuery().FirstOrDefault(x => x.NodeId == id);
        return dto == null ? null : BuildEntity(dto);
    }

    public IEnumerable<IMemberType> GetMany(params int[]? ids)
    {
        IQueryable<ContentTypeDto> q = MemberTypeQuery();
        if (ids?.Length > 0)
        {
            q = q.Where(x => ids.Contains(x.NodeId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IMemberType>();
    }

    public bool Exists(int id) => MemberTypeQuery().Any(x => x.NodeId == id);

    // ─── IReadRepository<Guid, IMemberType> ──────────────────────────────────
    public IMemberType? Get(Guid id)
    {
        NodeDto? node = _db.Set<NodeDto>().FirstOrDefault(n => n.UniqueId == id && n.NodeObjectType == _memberTypeObjectType);
        return node == null ? null : Get(node.NodeId);
    }

    public IEnumerable<IMemberType> GetMany(params Guid[]? ids)
    {
        IQueryable<NodeDto> nodeQ = _db.Set<NodeDto>().Where(n => n.NodeObjectType == _memberTypeObjectType);
        if (ids?.Length > 0)
        {
            nodeQ = nodeQ.Where(n => ids.Contains(n.UniqueId));
        }

        int[] nodeIds = nodeQ.Select(n => n.NodeId).ToArray();
        return GetMany(nodeIds);
    }

    public bool Exists(Guid id)
        => _db.Set<NodeDto>().Any(n => n.UniqueId == id && n.NodeObjectType == _memberTypeObjectType);

    // ─── Extra Methods ──────────────────────────────────────────────────────
    public IMemberType? Get(string alias)
    {
        ContentTypeDto? dto = MemberTypeQuery().FirstOrDefault(x => x.Alias == alias);
        return dto == null ? null : BuildEntity(dto);
    }

    public string GetUniqueAlias(string alias)
    {
        var count = _db.Set<ContentTypeDto>().Count(x => x.Alias != null && x.Alias.StartsWith(alias));
        return count == 0 ? alias : $"{alias}{count}";
    }

    public bool HasContainerInPath(string contentPath) => false;

    public bool HasContainerInPath(params int[] ids) => false;

    public bool HasContentNodes(int id)
        => _db.Set<MemberDto>().Any(m => m.NodeId == id);

    public IEnumerable<Guid> GetAllowedParentKeys(Guid key) => Enumerable.Empty<Guid>();

    public IEnumerable<MoveEventInfo<IMemberType>> Move(IMemberType moving, EntityContainer container)
    {
        moving.ParentId = container.Id;
        moving.Path = string.Concat(container.Path, ",", moving.Id);
        Save(moving);
        return new List<MoveEventInfo<IMemberType>> { new(moving, moving.Path, container.Key) };
    }

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(IMemberType entity)
    {
        if (entity.HasIdentity == false)
        {
            entity.AddingEntity();

            NodeDto nodeDto = new()
            {
                CreateDate = entity.CreateDate,
                Level = short.Parse(entity.Level.ToString(CultureInfo.InvariantCulture)),
                NodeObjectType = _memberTypeObjectType,
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

            nodeDto.Path = $"-1,{nodeDto.NodeId}";
            _db.SaveChanges();

            entity.Id = nodeDto.NodeId;
            entity.Path = nodeDto.Path;

            ContentTypeDto dto = new()
            {
                Alias = entity.Alias,
                AllowAtRoot = entity.AllowedAsRoot,
                AllowedInLibrary = true,
                Description = entity.Description,
                Icon = entity.Icon,
                IsElement = entity.IsElement,
                ListView = entity.ListView,
                NodeId = nodeDto.NodeId,
                Thumbnail = entity.Thumbnail,
                Variations = (byte)entity.Variations,
            };

            _db.Set<ContentTypeDto>().Add(dto);
            _db.SaveChanges();
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
            }

            ContentTypeDto? dto = _db.Set<ContentTypeDto>().FirstOrDefault(x => x.NodeId == entity.Id);
            if (dto != null)
            {
                dto.Alias = entity.Alias;
                dto.AllowAtRoot = entity.AllowedAsRoot;
                dto.Description = entity.Description;
                dto.Icon = entity.Icon;
                dto.IsElement = entity.IsElement;
                dto.ListView = entity.ListView;
                dto.Thumbnail = entity.Thumbnail;
                dto.Variations = (byte)entity.Variations;
            }

            _db.SaveChanges();
        }

        entity.ResetDirtyProperties();
    }

    public void Delete(IMemberType entity)
    {
        ContentTypeDto? dto = _db.Set<ContentTypeDto>().FirstOrDefault(x => x.NodeId == entity.Id);
        if (dto != null)
        {
            _db.Set<ContentTypeDto>().Remove(dto);
        }

        NodeDto? nodeDto = _db.Set<NodeDto>().Find(entity.Id);
        if (nodeDto != null)
        {
            _db.Set<NodeDto>().Remove(nodeDto);
        }

        _db.SaveChanges();
    }

    public IEnumerable<IMemberType> Get(IQuery<IMemberType> query) => GetMany(Array.Empty<int>());

    public int Count(IQuery<IMemberType>? query) => MemberTypeQuery().Count();
}
