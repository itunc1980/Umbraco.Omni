using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IMemberGroupRepository" />.</summary>
/// <remarks>
///     MemberGroup entities are stored in the <c>umbracoNode</c> table, filtered by
///     <c>NodeObjectType == Constants.ObjectTypes.MemberGroup</c>.
/// </remarks>
internal sealed class EfMemberGroupRepository : IMemberGroupRepository
{
    private readonly UmbracoDbContext _db;
    private static readonly Guid _memberGroupObjectType = CoreConstants.ObjectTypes.MemberGroup;

    public EfMemberGroupRepository(UmbracoDbContext db) => _db = db;

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IQueryable<NodeDto> MemberGroupNodes()
        => _db.Set<NodeDto>().Where(n => n.NodeObjectType == _memberGroupObjectType);

    // ─── IReadRepository<int, IMemberGroup> ─────────────────────────────────
    public IMemberGroup? Get(int id)
    {
        NodeDto? dto = MemberGroupNodes().FirstOrDefault(n => n.NodeId == id);
        return dto == null ? null : MemberGroupFactory.BuildEntity(dto);
    }

    public IEnumerable<IMemberGroup> GetMany(params int[]? ids)
    {
        IQueryable<NodeDto> q = MemberGroupNodes();
        if (ids?.Length > 0) { q = q.Where(n => ids.Contains(n.NodeId)); }
        return q.ToList().Select(MemberGroupFactory.BuildEntity);
    }

    public bool Exists(int id) => MemberGroupNodes().Any(n => n.NodeId == id);

    // ─── IMemberGroupRepository extras ──────────────────────────────────────
    public IMemberGroup? Get(Guid uniqueId)
    {
        NodeDto? dto = MemberGroupNodes().FirstOrDefault(n => n.UniqueId == uniqueId);
        return dto == null ? null : MemberGroupFactory.BuildEntity(dto);
    }

    public IMemberGroup? GetByName(string? name)
    {
        if (string.IsNullOrEmpty(name)) { return null; }
        NodeDto? dto = MemberGroupNodes().FirstOrDefault(n => n.Text == name);
        return dto == null ? null : MemberGroupFactory.BuildEntity(dto);
    }

    public IMemberGroup? CreateIfNotExists(string roleName)
    {
        IMemberGroup? existing = GetByName(roleName);
        if (existing != null) { return null; }

        var group = new MemberGroup { Name = roleName };
        Save(group);
        return group;
    }

    public IEnumerable<IMemberGroup> GetMemberGroupsForMember(int memberId)
    {
        int[] groupIds = _db.Set<Member2MemberGroupDto>()
            .Where(m => m.Member == memberId)
            .Select(m => m.MemberGroup)
            .ToArray();
        return MemberGroupNodes()
            .Where(n => groupIds.Contains(n.NodeId))
            .ToList()
            .Select(MemberGroupFactory.BuildEntity);
    }

    public IEnumerable<IMemberGroup> GetMemberGroupsForMember(string? username)
    {
        if (string.IsNullOrEmpty(username)) { return Enumerable.Empty<IMemberGroup>(); }
        MemberDto? member = _db.Set<MemberDto>()
            .Join(_db.Set<NodeDto>(), m => m.NodeId, n => n.NodeId, (m, n) => new { m, n })
            .Where(x => x.m.LoginName == username)
            .Select(x => x.m)
            .FirstOrDefault();
        if (member == null) { return Enumerable.Empty<IMemberGroup>(); }
        return GetMemberGroupsForMember(member.NodeId);
    }

    public void ReplaceRoles(int[] memberIds, string[] roleNames)
        => AssignRolesInternal(memberIds, roleNames, replace: true);

    public void AssignRoles(int[] memberIds, string[] roleNames)
        => AssignRolesInternal(memberIds, roleNames, replace: false);

    public void DissociateRoles(int[] memberIds, string[] roleNames)
    {
        int[] roleIds = MemberGroupNodes()
            .Where(n => roleNames.Contains(n.Text))
            .Select(n => n.NodeId)
            .ToArray();
        List<Member2MemberGroupDto> toDelete = _db.Set<Member2MemberGroupDto>()
            .Where(x => memberIds.Contains(x.Member) && roleIds.Contains(x.MemberGroup))
            .ToList();
        _db.Set<Member2MemberGroupDto>().RemoveRange(toDelete);
        _db.SaveChanges();
    }

    // ─── IWriteRepository ───────────────────────────────────────────────────
    public void Save(IMemberGroup entity)
    {
        NodeDto dto = MemberGroupFactory.BuildDto(entity);
        NodeDto? existing = entity.HasIdentity ? MemberGroupNodes().FirstOrDefault(n => n.NodeId == entity.Id) : null;

        if (existing == null)
        {
            entity.AddingEntity();
            if (dto.UniqueId == Guid.Empty) { dto.UniqueId = Guid.NewGuid(); }
            _db.Set<NodeDto>().Add(dto);
            _db.SaveChanges();
            entity.Id = dto.NodeId;

            // Update path after getting the id
            dto.Path = $"-1,{dto.NodeId}";
            _db.SaveChanges();
        }
        else
        {
            entity.UpdatingEntity();
            existing.Text = dto.Text;
            existing.UniqueId = dto.UniqueId;
            _db.SaveChanges();
        }
        entity.ResetDirtyProperties();
    }

    public void Delete(IMemberGroup entity)
    {
        // Remove member-group associations
        List<Member2MemberGroupDto> links = _db.Set<Member2MemberGroupDto>()
            .Where(x => x.MemberGroup == entity.Id).ToList();
        _db.Set<Member2MemberGroupDto>().RemoveRange(links);

        // Remove node
        NodeDto? dto = MemberGroupNodes().FirstOrDefault(n => n.NodeId == entity.Id);
        if (dto != null) { _db.Set<NodeDto>().Remove(dto); }
        _db.SaveChanges();
    }

    public IEnumerable<IMemberGroup> Get(IQuery<IMemberGroup> query)
        => MemberGroupNodes().ToList().Select(MemberGroupFactory.BuildEntity);

    public int Count(IQuery<IMemberGroup>? query) => MemberGroupNodes().Count();

    // ─── Private helpers ────────────────────────────────────────────────────
    private void AssignRolesInternal(int[] memberIds, string[] roleNames, bool replace)
    {
        memberIds = memberIds.Distinct().ToArray();

        // Create missing roles
        List<string?> existingNames = MemberGroupNodes()
            .Where(n => roleNames.Contains(n.Text))
            .Select(n => n.Text)
            .ToList();
        string[] missing = roleNames
            .Except(existingNames.Where(n => n != null).Cast<string>(), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        foreach (string role in missing)
        {
            var group = new MemberGroup { Name = role };
            Save(group);
        }

        // Get all relevant role node-ids by name
        Dictionary<string, int> roleIdByName = MemberGroupNodes()
            .Where(n => roleNames.Contains(n.Text) && n.Text != null)
            .ToDictionary(n => n.Text!, n => n.NodeId, StringComparer.InvariantCultureIgnoreCase);

        if (replace)
        {
            // Delete all assignments for these members
            List<Member2MemberGroupDto> toDelete = _db.Set<Member2MemberGroupDto>()
                .Where(x => memberIds.Contains(x.Member))
                .ToList();
            _db.Set<Member2MemberGroupDto>().RemoveRange(toDelete);
            _db.SaveChanges();

            // Assign all requested roles
            foreach (int memberId in memberIds)
            {
                foreach (int groupId in roleIdByName.Values)
                {
                    _db.Set<Member2MemberGroupDto>().Add(new Member2MemberGroupDto { Member = memberId, MemberGroup = groupId });
                }
            }
            _db.SaveChanges();
        }
        else
        {
            // Get currently assigned
            int[] allGroupIds = roleIdByName.Values.ToArray();
            List<Member2MemberGroupDto> current = _db.Set<Member2MemberGroupDto>()
                .Where(x => memberIds.Contains(x.Member) && allGroupIds.Contains(x.MemberGroup))
                .ToList();

            foreach (int memberId in memberIds)
            {
                HashSet<int> alreadyAssigned = current
                    .Where(x => x.Member == memberId)
                    .Select(x => x.MemberGroup)
                    .ToHashSet();

                foreach (KeyValuePair<string, int> role in roleIdByName)
                {
                    if (!alreadyAssigned.Contains(role.Value))
                    {
                        _db.Set<Member2MemberGroupDto>().Add(new Member2MemberGroupDto { Member = memberId, MemberGroup = role.Value });
                    }
                }
            }
            _db.SaveChanges();
        }
    }
}
