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

/// <summary>EF Core implementation of <see cref="IMemberRepository" />.</summary>
internal sealed class EfMemberRepository : IMemberRepository
{
    private readonly UmbracoDbContext _db;
    private readonly IMemberTypeRepository _memberTypeRepository;
    private static readonly Guid _memberObjectType = CoreConstants.ObjectTypes.Member;

    public EfMemberRepository(UmbracoDbContext db, IMemberTypeRepository memberTypeRepository)
    {
        _db = db;
        _memberTypeRepository = memberTypeRepository;
    }

    public int RecycleBinId => -20;

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IQueryable<NodeDto> MemberNodes()
        => _db.Set<NodeDto>().Where(n => n.NodeObjectType == _memberObjectType);

    private IMember? BuildEntity(NodeDto? node)
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

        MemberDto? memberDto = _db.Set<MemberDto>().Find(node.NodeId);
        if (memberDto == null)
        {
            return null;
        }

        IMemberType? memberType = _memberTypeRepository.Get(contentDto.ContentTypeId);
        if (memberType == null)
        {
            return null;
        }

        var member = new Member(node.Text ?? string.Empty, memberDto.Email ?? string.Empty, memberDto.LoginName ?? string.Empty, memberDto.Password ?? string.Empty, memberType, memberDto.IsApproved)
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
            Email = memberDto.Email ?? string.Empty,
            Username = memberDto.LoginName ?? string.Empty,
            RawPasswordValue = memberDto.Password,
            PasswordConfiguration = memberDto.PasswordConfig,
            SecurityStamp = memberDto.SecurityStampToken,
            FailedPasswordAttempts = memberDto.FailedPasswordAttempts ?? 0,
            IsApproved = memberDto.IsApproved,
            IsLockedOut = memberDto.IsLockedOut,
            LastLoginDate = memberDto.LastLoginDate,
            LastPasswordChangeDate = memberDto.LastPasswordChangeDate,
            LastLockoutDate = memberDto.LastLockoutDate,
            EmailConfirmedDate = memberDto.EmailConfirmedDate,
        };

        ContentVersionDto? version = _db.Set<ContentVersionDto>()
            .Where(v => v.NodeId == node.NodeId && v.Current)
            .OrderByDescending(v => v.Id)
            .FirstOrDefault();

        if (version != null)
        {
            member.VersionId = version.Id;
            member.UpdateDate = version.VersionDate;
        }

        member.ResetDirtyProperties(false);
        return member;
    }

    // ─── IReadRepository<int, IMember> ──────────────────────────────────────
    public IMember? Get(int id)
    {
        NodeDto? node = MemberNodes().FirstOrDefault(n => n.NodeId == id);
        return BuildEntity(node);
    }

    public IEnumerable<IMember> GetMany(params int[]? ids)
    {
        IQueryable<NodeDto> q = MemberNodes();
        if (ids?.Length > 0)
        {
            q = q.Where(n => ids.Contains(n.NodeId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IMember>();
    }

    public bool Exists(int id) => MemberNodes().Any(n => n.NodeId == id);

    // ─── IReadRepository<Guid, IMember> ─────────────────────────────────────
    public IMember? Get(Guid id)
    {
        NodeDto? node = MemberNodes().FirstOrDefault(n => n.UniqueId == id);
        return BuildEntity(node);
    }

    public IEnumerable<IMember> GetMany(params Guid[]? ids)
    {
        IQueryable<NodeDto> q = MemberNodes();
        if (ids?.Length > 0)
        {
            q = q.Where(n => ids.Contains(n.UniqueId));
        }

        return q.ToList().Select(BuildEntity).Where(x => x != null).Cast<IMember>();
    }

    public bool Exists(Guid id) => MemberNodes().Any(n => n.UniqueId == id);

    // ─── Extra Member Methods ───────────────────────────────────────────────
    public int[] GetMemberIds(string[] names)
        => _db.Set<MemberDto>().Where(m => names.Contains(m.LoginName)).Select(m => m.NodeId).ToArray();

    public IMember? GetByUsername(string? username)
    {
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        MemberDto? m = _db.Set<MemberDto>().FirstOrDefault(x => x.LoginName == username);
        return m == null ? null : Get(m.NodeId);
    }

    public IEnumerable<IMember> FindMembersInRole(string roleName, string usernameToMatch, StringPropertyMatchType matchType = StringPropertyMatchType.StartsWith)
        => GetMany(Array.Empty<int>());

    public IEnumerable<IMember> GetByMemberGroup(string groupName) => GetMany(Array.Empty<int>());

    public bool Exists(string username)
        => _db.Set<MemberDto>().Any(m => m.LoginName == username);

    public int GetCountByQuery(IQuery<IMember>? query) => MemberNodes().Count();

    public Task<PagedModel<IMember>> GetPagedByFilterAsync(MemberFilter memberFilter, int skip, int take, Ordering? ordering = null)
    {
        IQueryable<NodeDto> q = MemberNodes().Where(n => !n.Trashed);
        var total = q.Count();
        List<NodeDto> nodes = q.OrderBy(n => n.SortOrder).ThenBy(n => n.NodeId).Skip(skip).Take(take).ToList();
        List<IMember> items = nodes.Select(BuildEntity).Where(x => x != null).Cast<IMember>().ToList();
        return Task.FromResult(new PagedModel<IMember>(total, items));
    }

    public Task UpdateLoginPropertiesAsync(IMember member)
    {
        MemberDto? dto = _db.Set<MemberDto>().Find(member.Id);
        if (dto != null)
        {
            dto.LastLoginDate = member.LastLoginDate;
            if (member is Member m)
            {
                dto.SecurityStampToken = m.SecurityStamp;
            }
            dto.FailedPasswordAttempts = member.FailedPasswordAttempts;
            dto.IsLockedOut = member.IsLockedOut;
            dto.LastLockoutDate = member.LastLockoutDate;
            _db.SaveChanges();
        }

        return Task.CompletedTask;
    }

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(IMember entity)
    {
        if (entity.HasIdentity == false)
        {
            entity.AddingEntity();

            NodeDto nodeDto = new()
            {
                CreateDate = entity.CreateDate,
                Level = short.Parse(entity.Level.ToString(CultureInfo.InvariantCulture)),
                NodeObjectType = _memberObjectType,
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

            MemberDto memberDto = new()
            {
                NodeId = nodeDto.NodeId,
                Email = entity.Email,
                LoginName = entity.Username,
                Password = (entity as Member)?.RawPasswordValue,
                PasswordConfig = (entity as Member)?.PasswordConfiguration,
                SecurityStampToken = (entity as Member)?.SecurityStamp,
                FailedPasswordAttempts = entity.FailedPasswordAttempts,
                IsApproved = entity.IsApproved,
                IsLockedOut = entity.IsLockedOut,
                LastLoginDate = entity.LastLoginDate,
                LastPasswordChangeDate = entity.LastPasswordChangeDate,
                LastLockoutDate = entity.LastLockoutDate,
                EmailConfirmedDate = entity.EmailConfirmedDate,
            };
            _db.Set<MemberDto>().Add(memberDto);
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

            MemberDto? memberDto = _db.Set<MemberDto>().Find(entity.Id);
            if (memberDto != null)
            {
                memberDto.Email = entity.Email;
                memberDto.LoginName = entity.Username;
                memberDto.Password = (entity as Member)?.RawPasswordValue;
                memberDto.PasswordConfig = (entity as Member)?.PasswordConfiguration;
                memberDto.SecurityStampToken = (entity as Member)?.SecurityStamp;
                memberDto.FailedPasswordAttempts = entity.FailedPasswordAttempts;
                memberDto.IsApproved = entity.IsApproved;
                memberDto.IsLockedOut = entity.IsLockedOut;
                memberDto.LastLoginDate = entity.LastLoginDate;
                memberDto.LastPasswordChangeDate = entity.LastPasswordChangeDate;
                memberDto.LastLockoutDate = entity.LastLockoutDate;
                memberDto.EmailConfirmedDate = entity.EmailConfirmedDate;
            }

            ContentVersionDto? currentVersion = _db.Set<ContentVersionDto>()
                .Where(v => v.NodeId == entity.Id && v.Current)
                .FirstOrDefault();

            if (currentVersion != null)
            {
                currentVersion.Text = entity.Name;
                currentVersion.VersionDate = entity.UpdateDate;
            }

            _db.SaveChanges();
        }

        entity.ResetDirtyProperties();
    }

    public void Delete(IMember entity)
    {
        MemberDto? m = _db.Set<MemberDto>().Find(entity.Id);
        if (m != null)
        {
            _db.Set<MemberDto>().Remove(m);
        }

        List<ContentVersionDto> versions = _db.Set<ContentVersionDto>().Where(v => v.NodeId == entity.Id).ToList();
        foreach (ContentVersionDto v in versions)
        {
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
    public IEnumerable<IMember> Get(IQuery<IMember> query) => GetMany(Array.Empty<int>());

    public int Count(IQuery<IMember>? query) => MemberNodes().Count();

    public int Count(string? contentTypeAlias = null)
    {
        if (string.IsNullOrEmpty(contentTypeAlias))
        {
            return MemberNodes().Count();
        }

        ContentTypeDto? ct = _db.Set<ContentTypeDto>().FirstOrDefault(x => x.Alias == contentTypeAlias);
        if (ct == null)
        {
            return 0;
        }

        return _db.Set<ContentDto>().Count(c => c.ContentTypeId == ct.NodeId);
    }

    public int CountChildren(int parentId, string? contentTypeAlias = null)
        => MemberNodes().Count(n => n.ParentId == parentId && !n.Trashed);

    public int CountDescendants(int parentId, string? contentTypeAlias = null)
        => MemberNodes().Count(n => n.Path.Contains($",{parentId},") && !n.Trashed);

    public IEnumerable<IMember> GetPage(
        IQuery<IMember>? query,
        long pageIndex,
        int pageSize,
        out long totalRecords,
        IQuery<IMember>? filter,
        Ordering? ordering)
    {
        IQueryable<NodeDto> q = MemberNodes().Where(n => !n.Trashed);
        totalRecords = q.Count();

        var skip = (int)(pageIndex * pageSize);
        List<NodeDto> nodes = q.OrderBy(n => n.SortOrder).ThenBy(n => n.NodeId).Skip(skip).Take(pageSize).ToList();

        return nodes.Select(BuildEntity).Where(x => x != null).Cast<IMember>();
    }

    // ─── Versions ───────────────────────────────────────────────────────────
    public IEnumerable<IMember> GetAllVersions(int nodeId) => GetMany(nodeId);

    public IEnumerable<IMember> GetAllVersionsSlim(int nodeId, int skip, int take) => GetMany(nodeId);

    public IEnumerable<int> GetVersionIds(int id, int topRows)
        => _db.Set<ContentVersionDto>().Where(v => v.NodeId == id).OrderByDescending(v => v.Id).Take(topRows).Select(v => v.Id).ToList();

    public IMember? GetVersion(int versionId)
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

    public IEnumerable<IMember> GetRecycleBin()
    {
        List<NodeDto> nodes = MemberNodes().Where(n => n.Trashed).ToList();
        return nodes.Select(BuildEntity).Where(x => x != null).Cast<IMember>();
    }

    public ContentDataIntegrityReport CheckDataIntegrity(ContentDataIntegrityReportOptions options)
        => new(new Dictionary<int, ContentDataIntegrityReportEntry>());
}
