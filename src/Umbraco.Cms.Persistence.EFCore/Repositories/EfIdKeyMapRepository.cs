using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IIdKeyMapRepository" />.</summary>
internal sealed class EfIdKeyMapRepository : IIdKeyMapRepository
{
    private readonly UmbracoDbContext _db;
    public EfIdKeyMapRepository(UmbracoDbContext db) => _db = db;

    public int? GetIdForKey(Guid key, UmbracoObjectTypes umbracoObjectType)
    {
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(n => n.UniqueId == key);
        if (umbracoObjectType != UmbracoObjectTypes.Unknown)
        {
            Guid typeGuid = GetNodeObjectTypeGuid(umbracoObjectType);
            q = q.Where(n => n.NodeObjectType == typeGuid);
        }
        return q.Select(n => (int?)n.NodeId).FirstOrDefault();
    }

    public Guid? GetIdForKey(int id, UmbracoObjectTypes umbracoObjectType)
    {
        IQueryable<NodeDto> q = _db.Set<NodeDto>().Where(n => n.NodeId == id);
        if (umbracoObjectType != UmbracoObjectTypes.Unknown)
        {
            Guid typeGuid = GetNodeObjectTypeGuid(umbracoObjectType);
            q = q.Where(n => n.NodeObjectType == typeGuid);
        }
        return q.Select(n => n.UniqueId).FirstOrDefault();
    }

    private static Guid GetNodeObjectTypeGuid(UmbracoObjectTypes umbracoObjectType)
    {
        Guid guid = umbracoObjectType.GetGuid();
        if (guid == Guid.Empty)
        {
            throw new NotSupportedException("Unsupported object type (" + umbracoObjectType + ").");
        }
        return guid;
    }
}
