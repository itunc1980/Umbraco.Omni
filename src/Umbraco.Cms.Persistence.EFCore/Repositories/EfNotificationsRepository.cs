using System.Diagnostics.CodeAnalysis;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="INotificationsRepository" />.</summary>
internal sealed class EfNotificationsRepository : INotificationsRepository
{
    private readonly UmbracoDbContext _db;
    public EfNotificationsRepository(UmbracoDbContext db) => _db = db;

    public IEnumerable<Notification> GetUsersNotifications(IEnumerable<int> userIds, string? action, IEnumerable<int> nodeIds, Guid objectType)
    {
        int[] userIdArr = userIds.ToArray();
        int[] nodeIdArr = nodeIds.ToArray();
        IQueryable<User2NodeNotifyDto> q = _db.Set<User2NodeNotifyDto>()
            .Where(x => userIdArr.Contains(x.UserId))
            .Where(x => nodeIdArr.Length == 0 || nodeIdArr.Contains(x.NodeId));
        if (action != null) { q = q.Where(x => x.Action == action); }
        return q.ToList().Select(x => new Notification(x.NodeId, x.UserId, x.Action ?? string.Empty, objectType));
    }

    public IEnumerable<Notification> GetUserNotifications(IUser user)
    {
        return _db.Set<User2NodeNotifyDto>()
            .Where(x => x.UserId == user.Id).OrderBy(x => x.NodeId).ToList()
            .Select(d =>
            {
                NodeDto? node = _db.Set<NodeDto>().Find(d.NodeId);
                return new Notification(d.NodeId, d.UserId, d.Action ?? string.Empty, node?.NodeObjectType ?? Guid.Empty);
            });
    }

    public IEnumerable<Notification> GetEntityNotifications(IEntity entity)
    {
        NodeDto? node = _db.Set<NodeDto>().Find(entity.Id);
        Guid objectType = node?.NodeObjectType ?? Guid.Empty;
        return _db.Set<User2NodeNotifyDto>().Where(x => x.NodeId == entity.Id).OrderBy(x => x.NodeId).ToList()
            .Select(d => new Notification(d.NodeId, d.UserId, d.Action ?? string.Empty, objectType));
    }

    public int DeleteNotifications(IEntity entity)
    {
        List<User2NodeNotifyDto> toDelete = _db.Set<User2NodeNotifyDto>().Where(x => x.NodeId == entity.Id).ToList();
        _db.Set<User2NodeNotifyDto>().RemoveRange(toDelete);
        _db.SaveChanges();
        return toDelete.Count;
    }

    public int DeleteNotifications(IUser user)
    {
        List<User2NodeNotifyDto> toDelete = _db.Set<User2NodeNotifyDto>().Where(x => x.UserId == user.Id).ToList();
        _db.Set<User2NodeNotifyDto>().RemoveRange(toDelete);
        _db.SaveChanges();
        return toDelete.Count;
    }

    public int DeleteNotifications(IUser user, IEntity entity)
    {
        List<User2NodeNotifyDto> toDelete = _db.Set<User2NodeNotifyDto>()
            .Where(x => x.NodeId == entity.Id && x.UserId == user.Id).ToList();
        _db.Set<User2NodeNotifyDto>().RemoveRange(toDelete);
        _db.SaveChanges();
        return toDelete.Count;
    }

    public IEnumerable<Notification> SetNotifications(IUser user, IEntity entity, string[] actions)
    {
        DeleteNotifications(user, entity);
        var created = new List<Notification>();
        foreach (string action in actions)
        {
            if (TryCreateNotification(user, entity, action, out Notification? n)) { created.Add(n); }
        }
        return created;
    }

    public bool TryCreateNotification(IUser user, IEntity entity, string action, [NotNullWhen(true)] out Notification? notification)
    {
        NodeDto? node = _db.Set<NodeDto>().Find(entity.Id);
        if (node == null) { notification = null; return false; }
        var dto = new User2NodeNotifyDto { Action = action, NodeId = entity.Id, UserId = user.Id };
        _db.Set<User2NodeNotifyDto>().Add(dto);
        _db.SaveChanges();
        notification = new Notification(dto.NodeId, dto.UserId, dto.Action, node.NodeObjectType ?? Guid.Empty);
        return true;
    }
}
