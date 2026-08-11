using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IWebhookLogRepository" />.</summary>
internal sealed class EfWebhookLogRepository : IWebhookLogRepository
{
    private readonly UmbracoDbContext _db;
    public EfWebhookLogRepository(UmbracoDbContext db) => _db = db;

    public Task CreateAsync(WebhookLog log)
    {
        WebhookLogDto dto = WebhookLogFactory.CreateDto(log);
        _db.Set<WebhookLogDto>().Add(dto);
        _db.SaveChanges();
        log.Id = dto.Id;
        return Task.CompletedTask;
    }

    public Task<PagedModel<WebhookLog>> GetPagedAsync(int skip, int take)
    {
        long total = _db.Set<WebhookLogDto>().LongCount();
        List<WebhookLogDto> dtos = _db.Set<WebhookLogDto>().OrderByDescending(x => x.Date).Skip(skip).Take(take).ToList();
        return Task.FromResult(new PagedModel<WebhookLog>
        {
            Items = dtos.Select(WebhookLogFactory.DtoToEntity),
            Total = total,
        });
    }

    public Task<IEnumerable<WebhookLog>> GetOlderThanDate(DateTime date)
    {
        IEnumerable<WebhookLog> result = _db.Set<WebhookLogDto>()
            .Where(x => x.Date < date).ToList()
            .Select(WebhookLogFactory.DtoToEntity);
        return Task.FromResult(result);
    }

    public Task DeleteByIds(int[] ids)
    {
        _db.Set<WebhookLogDto>().RemoveRange(_db.Set<WebhookLogDto>().Where(x => ids.Contains(x.Id)));
        _db.SaveChanges();
        return Task.CompletedTask;
    }
}
