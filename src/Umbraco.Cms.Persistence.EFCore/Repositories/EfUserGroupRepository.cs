using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Models.Membership.Permissions;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Cms.Infrastructure.Persistence.Mappers;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IUserGroupRepository" />.</summary>
internal sealed class EfUserGroupRepository : IUserGroupRepository
{
    private readonly UmbracoDbContext _db;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly IDictionary<string, IPermissionMapper> _permissionMappers;

    public EfUserGroupRepository(
        UmbracoDbContext db,
        IShortStringHelper shortStringHelper,
        IEnumerable<IPermissionMapper> permissionMappers)
    {
        _db = db;
        _shortStringHelper = shortStringHelper;
        _permissionMappers = permissionMappers.ToDictionary(x => x.Context);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────
    private UserGroupDto? LoadDto(int id)
    {
        UserGroupDto? dto = _db.Set<UserGroupDto>().Find(id);
        if (dto != null)
        {
            PopulateChildren(dto);
        }

        return dto;
    }

    private void PopulateChildren(UserGroupDto dto)
    {
        dto.UserGroup2AppDtos = _db.Set<UserGroup2AppDto>().Where(x => x.UserGroupId == dto.Id).ToList();
        dto.UserGroup2LanguageDtos = _db.Set<UserGroup2LanguageDto>().Where(x => x.UserGroupId == dto.Id).ToList();
        dto.UserGroup2PermissionDtos = _db.Set<UserGroup2PermissionDto>().Where(x => x.UserGroupKey == dto.Key).ToList();
        dto.UserGroup2GranularPermissionDtos = _db.Set<UserGroup2GranularPermissionDto>().Where(x => x.UserGroupKey == dto.Key).ToList();
        dto.UserCount = _db.Set<User2UserGroupDto>().Count(x => x.UserGroupId == dto.Id);
    }

    private IUserGroup? BuildEntity(UserGroupDto? dto)
        => dto == null ? null : UserGroupFactory.BuildEntity(_shortStringHelper, dto, _permissionMappers);

    // ─── IReadRepository<int, IUserGroup> ───────────────────────────────────
    public IUserGroup? Get(int id) => BuildEntity(LoadDto(id));

    public IEnumerable<IUserGroup> GetMany(params int[]? ids)
    {
        IQueryable<UserGroupDto> q = _db.Set<UserGroupDto>();
        if (ids?.Length > 0)
        {
            q = q.Where(x => ids.Contains(x.Id));
        }

        List<UserGroupDto> dtos = q.ToList();
        foreach (UserGroupDto d in dtos)
        {
            PopulateChildren(d);
        }

        return dtos.Select(d => BuildEntity(d)).Where(x => x != null).Cast<IUserGroup>();
    }

    public bool Exists(int id) => _db.Set<UserGroupDto>().Any(x => x.Id == id);

    // ─── IUserGroupRepository Extra Get / Exists ────────────────────────────
    public IUserGroup? Get(string alias)
    {
        UserGroupDto? dto = _db.Set<UserGroupDto>().FirstOrDefault(x => x.Alias == alias);
        if (dto != null)
        {
            PopulateChildren(dto);
        }

        return BuildEntity(dto);
    }

    public IUserGroup? Get(Guid key)
    {
        UserGroupDto? dto = _db.Set<UserGroupDto>().FirstOrDefault(x => x.Key == key);
        if (dto != null)
        {
            PopulateChildren(dto);
        }

        return BuildEntity(dto);
    }

    public IEnumerable<IUserGroup> GetMany(params Guid[]? keys)
    {
        IQueryable<UserGroupDto> q = _db.Set<UserGroupDto>();
        if (keys?.Length > 0)
        {
            q = q.Where(x => keys.Contains(x.Key));
        }

        List<UserGroupDto> dtos = q.ToList();
        foreach (UserGroupDto d in dtos)
        {
            PopulateChildren(d);
        }

        return dtos.Select(d => BuildEntity(d)).Where(x => x != null).Cast<IUserGroup>();
    }

    public IEnumerable<IUserGroup> GetMany(params string[]? aliases)
    {
        IQueryable<UserGroupDto> q = _db.Set<UserGroupDto>();
        if (aliases?.Length > 0)
        {
            q = q.Where(x => aliases.Contains(x.Alias));
        }

        List<UserGroupDto> dtos = q.ToList();
        foreach (UserGroupDto d in dtos)
        {
            PopulateChildren(d);
        }

        return dtos.Select(d => BuildEntity(d)).Where(x => x != null).Cast<IUserGroup>();
    }

    public bool Exists(string alias) => _db.Set<UserGroupDto>().Any(x => x.Alias == alias);

    public bool Exists(Guid id) => _db.Set<UserGroupDto>().Any(x => x.Key == id);

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(IUserGroup entity)
    {
        var group = (UserGroup)entity;
        UserGroupDto dto = UserGroupFactory.BuildDto(group);

        if (group.HasIdentity == false)
        {
            group.AddingEntity();
            dto.CreateDate = group.CreateDate;
            dto.UpdateDate = group.UpdateDate;
            if (dto.Key == Guid.Empty)
            {
                dto.Key = Guid.NewGuid();
            }

            _db.Set<UserGroupDto>().Add(dto);
            _db.SaveChanges();
            group.Id = dto.Id;
            group.Key = dto.Key;
        }
        else
        {
            group.UpdatingEntity();
            UserGroupDto? existing = _db.Set<UserGroupDto>().Find(group.Id);
            if (existing != null)
            {
                existing.Alias = dto.Alias;
                existing.Name = dto.Name;
                existing.Description = dto.Description;
                existing.Icon = dto.Icon;
                existing.HasAccessToAllLanguages = dto.HasAccessToAllLanguages;
                existing.StartContentId = dto.StartContentId;
                existing.StartMediaId = dto.StartMediaId;
                existing.StartElementId = dto.StartElementId;
                existing.UpdateDate = group.UpdateDate;
            }

            // Remove existing relationships
            _db.Set<UserGroup2AppDto>().RemoveRange(_db.Set<UserGroup2AppDto>().Where(x => x.UserGroupId == group.Id));
            _db.Set<UserGroup2LanguageDto>().RemoveRange(_db.Set<UserGroup2LanguageDto>().Where(x => x.UserGroupId == group.Id));
            _db.Set<UserGroup2PermissionDto>().RemoveRange(_db.Set<UserGroup2PermissionDto>().Where(x => x.UserGroupKey == group.Key));
            _db.Set<UserGroup2GranularPermissionDto>().RemoveRange(_db.Set<UserGroup2GranularPermissionDto>().Where(x => x.UserGroupKey == group.Key));
            _db.SaveChanges();
        }

        // Add relationships
        foreach (UserGroup2AppDto app in dto.UserGroup2AppDtos)
        {
            app.UserGroupId = group.Id;
            _db.Set<UserGroup2AppDto>().Add(app);
        }

        foreach (UserGroup2LanguageDto lang in dto.UserGroup2LanguageDtos)
        {
            lang.UserGroupId = group.Id;
            _db.Set<UserGroup2LanguageDto>().Add(lang);
        }

        foreach (UserGroup2PermissionDto perm in dto.UserGroup2PermissionDtos)
        {
            perm.UserGroupKey = group.Key;
            _db.Set<UserGroup2PermissionDto>().Add(perm);
        }

        foreach (UserGroup2GranularPermissionDto gperm in dto.UserGroup2GranularPermissionDtos)
        {
            gperm.UserGroupKey = group.Key;
            _db.Set<UserGroup2GranularPermissionDto>().Add(gperm);
        }

        _db.SaveChanges();
        group.ResetDirtyProperties();
    }

    public void Delete(IUserGroup entity)
    {
        _db.Set<UserGroup2AppDto>().RemoveRange(_db.Set<UserGroup2AppDto>().Where(x => x.UserGroupId == entity.Id));
        _db.Set<UserGroup2LanguageDto>().RemoveRange(_db.Set<UserGroup2LanguageDto>().Where(x => x.UserGroupId == entity.Id));
        _db.Set<UserGroup2PermissionDto>().RemoveRange(_db.Set<UserGroup2PermissionDto>().Where(x => x.UserGroupKey == entity.Key));
        _db.Set<UserGroup2GranularPermissionDto>().RemoveRange(_db.Set<UserGroup2GranularPermissionDto>().Where(x => x.UserGroupKey == entity.Key));
        _db.Set<User2UserGroupDto>().RemoveRange(_db.Set<User2UserGroupDto>().Where(x => x.UserGroupId == entity.Id));

        UserGroupDto? dto = _db.Set<UserGroupDto>().Find(entity.Id);
        if (dto != null)
        {
            _db.Set<UserGroupDto>().Remove(dto);
        }

        _db.SaveChanges();
    }

    public IEnumerable<IUserGroup> Get(IQuery<IUserGroup> query) => GetMany(Array.Empty<int>());

    public int Count(IQuery<IUserGroup>? query) => _db.Set<UserGroupDto>().Count();

    // ─── IUserGroupRepository Specific ──────────────────────────────────────
    public IEnumerable<IUserGroup> GetGroupsAssignedToSection(string sectionAlias)
    {
        int[] groupIds = _db.Set<UserGroup2AppDto>()
            .Where(x => x.AppAlias == sectionAlias)
            .Select(x => x.UserGroupId)
            .ToArray();
        return GetMany(groupIds);
    }

    public void AddOrUpdateGroupWithUsers(IUserGroup userGroup, int[]? userIds)
    {
        Save(userGroup);
        if (userIds != null)
        {
            _db.Set<User2UserGroupDto>().RemoveRange(
                _db.Set<User2UserGroupDto>().Where(x => x.UserGroupId == userGroup.Id));
            foreach (var userId in userIds)
            {
                _db.Set<User2UserGroupDto>().Add(new User2UserGroupDto
                {
                    UserId = userId,
                    UserGroupId = userGroup.Id,
                });
            }

            _db.SaveChanges();
        }
    }

    public EntityPermissionCollection GetPermissions(int[] groupIds, params int[] entityIds)
    {
        var groups = GetMany(groupIds).Cast<IReadOnlyUserGroup>().ToArray();
        return GetPermissions(groups, fallbackToDefaultPermissions: false, entityIds);
    }

    public IEnumerable<IUserGroup> GetAllWithUsers() => GetMany(Array.Empty<int>());

    public IEnumerable<IUserGroup> GetUserGroupsWithUserCounts(params int[] groupIds)
        => GetMany(groupIds);

    public IEnumerable<IUserGroup> GetUserGroupsWithUserCounts(params string[] groupAliases)
        => GetMany(groupAliases);

    public EntityPermissionCollection GetPermissions(IReadOnlyUserGroup[]? groups, bool fallbackToDefaultPermissions, params int[] nodeIds)
    {
        var result = new EntityPermissionCollection();
        if (groups == null || groups.Length == 0)
        {
            return result;
        }

        foreach (IReadOnlyUserGroup group in groups)
        {
            if (nodeIds.Length == 0)
            {
                result.Add(new EntityPermission(group.Id, 0, group.Permissions));
            }
            else
            {
                foreach (var nodeId in nodeIds)
                {
                    result.Add(new EntityPermission(group.Id, nodeId, group.Permissions));
                }
            }
        }

        return result;
    }

    public void ReplaceGroupPermissions(int groupId, ISet<string> permissions, params int[] entityIds)
    {
        UserGroupDto? group = _db.Set<UserGroupDto>().Find(groupId);
        if (group == null)
        {
            return;
        }

        _db.Set<UserGroup2PermissionDto>().RemoveRange(
            _db.Set<UserGroup2PermissionDto>().Where(x => x.UserGroupKey == group.Key));

        foreach (var perm in permissions)
        {
            _db.Set<UserGroup2PermissionDto>().Add(new UserGroup2PermissionDto
            {
                UserGroupKey = group.Key,
                Permission = perm,
            });
        }

        _db.SaveChanges();
    }

    public void AssignGroupPermission(int groupId, string permission, params int[] entityIds)
    {
        UserGroupDto? group = _db.Set<UserGroupDto>().Find(groupId);
        if (group == null)
        {
            return;
        }

        bool exists = _db.Set<UserGroup2PermissionDto>().Any(x => x.UserGroupKey == group.Key && x.Permission == permission);
        if (!exists)
        {
            _db.Set<UserGroup2PermissionDto>().Add(new UserGroup2PermissionDto
            {
                UserGroupKey = group.Key,
                Permission = permission,
            });
            _db.SaveChanges();
        }
    }
}
