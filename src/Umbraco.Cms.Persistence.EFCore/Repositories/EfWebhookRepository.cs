using Umbraco.Extensions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IWebhookRepository" />.</summary>
internal sealed class EfWebhookRepository : IWebhookRepository
{
    private readonly UmbracoDbContext _db;
    public EfWebhookRepository(UmbracoDbContext db) => _db = db;

    public Task<PagedModel<IWebhook>> GetAllAsync(int skip, int take)
    {
        long total = _db.Set<WebhookDto>().LongCount();
        List<WebhookDto> dtos = _db.Set<WebhookDto>().OrderBy(x => x.Id).Skip(skip).Take(take).ToList();
        return Task.FromResult(new PagedModel<IWebhook>
        {
            Items = dtos.Select(BuildEntity),
            Total = total,
        });
    }

    public Task<IWebhook> CreateAsync(IWebhook webhook)
    {
        webhook.AddingEntity();
        WebhookDto dto = WebhookFactory.BuildDto(webhook);
        _db.Set<WebhookDto>().Add(dto);
        _db.SaveChanges();
        webhook.Id = dto.Id;
        InsertManyToOne(webhook);
        return Task.FromResult(webhook);
    }

    public Task<IWebhook?> GetAsync(Guid key)
    {
        WebhookDto? dto = _db.Set<WebhookDto>().FirstOrDefault(x => x.Key == key);
        return Task.FromResult<IWebhook?>(dto == null ? null : BuildEntity(dto));
    }

    public Task<PagedModel<IWebhook>> GetByIdsAsync(IEnumerable<Guid> keys)
    {
        Guid[] keyArr = keys.ToArray();
        List<WebhookDto> dtos = _db.Set<WebhookDto>().Where(x => keyArr.Contains(x.Key)).ToList();
        return Task.FromResult(new PagedModel<IWebhook> { Items = dtos.Select(BuildEntity), Total = dtos.Count });
    }

    public Task<PagedModel<IWebhook>> GetByAliasAsync(string alias)
    {
        List<WebhookDto> dtos = _db.Set<WebhookDto>()
            .Where(w => _db.Set<Webhook2EventsDto>().Any(e => e.WebhookId == w.Id && e.Event == alias))
            .ToList();
        return Task.FromResult(new PagedModel<IWebhook> { Items = dtos.Select(BuildEntity), Total = dtos.Count });
    }

    public Task DeleteAsync(IWebhook webhook)
    {
        WebhookDto? dto = _db.Set<WebhookDto>().FirstOrDefault(x => x.Key == webhook.Key);
        if (dto != null) { _db.Set<WebhookDto>().Remove(dto); _db.SaveChanges(); }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IWebhook webhook)
    {
        webhook.UpdatingEntity();
        WebhookDto dto = WebhookFactory.BuildDto(webhook);
        _db.Set<WebhookDto>().Update(dto);
        DeleteManyToOne(dto.Id);
        InsertManyToOne(webhook);
        _db.SaveChanges();
        return Task.CompletedTask;
    }

    private IWebhook BuildEntity(WebhookDto dto)
    {
        List<Webhook2ContentTypeKeysDto> keys = _db.Set<Webhook2ContentTypeKeysDto>().Where(x => x.WebhookId == dto.Id).ToList();
        List<Webhook2EventsDto> events = _db.Set<Webhook2EventsDto>().Where(x => x.WebhookId == dto.Id).ToList();
        List<Webhook2HeadersDto> headers = _db.Set<Webhook2HeadersDto>().Where(x => x.WebhookId == dto.Id).ToList();
        return WebhookFactory.BuildEntity(dto, keys, events, headers);
    }

    private void DeleteManyToOne(int webhookId)
    {
        _db.Set<Webhook2ContentTypeKeysDto>().RemoveRange(_db.Set<Webhook2ContentTypeKeysDto>().Where(x => x.WebhookId == webhookId));
        _db.Set<Webhook2EventsDto>().RemoveRange(_db.Set<Webhook2EventsDto>().Where(x => x.WebhookId == webhookId));
        _db.Set<Webhook2HeadersDto>().RemoveRange(_db.Set<Webhook2HeadersDto>().Where(x => x.WebhookId == webhookId));
        _db.SaveChanges();
    }

    private void InsertManyToOne(IWebhook webhook)
    {
        _db.Set<Webhook2ContentTypeKeysDto>().AddRange(WebhookFactory.BuildEntityKey2WebhookDto(webhook));
        _db.Set<Webhook2EventsDto>().AddRange(WebhookFactory.BuildEvent2WebhookDto(webhook));
        _db.Set<Webhook2HeadersDto>().AddRange(WebhookFactory.BuildHeaders2WebhookDtos(webhook));
        _db.SaveChanges();
    }
}
