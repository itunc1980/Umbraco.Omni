using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="INodeCountRepository" />.</summary>
internal sealed class EfNodeCountRepository : INodeCountRepository
{
    private readonly UmbracoDbContext _db;
    public EfNodeCountRepository(UmbracoDbContext db) => _db = db;

    public int GetNodeCount(Guid nodeType)
        => _db.Set<NodeDto>().Count(x => x.NodeObjectType == nodeType && x.Trashed == false);

    public int GetMediaCount()
    {
        // umbracoMedia ObjectType GUID constant
        var mediaGuid = new Guid("B796F64C-1F99-4FFB-B886-4BF4BC011A9C");
        return _db.Set<NodeDto>().Count(x => x.NodeObjectType == mediaGuid && x.Trashed == false);
    }
}
