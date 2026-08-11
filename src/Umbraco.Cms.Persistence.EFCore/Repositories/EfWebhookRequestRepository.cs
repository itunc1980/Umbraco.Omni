using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>EF Core implementation of <see cref="IWebhookRequestRepository" />.</summary>
internal sealed class EfWebhookRequestRepository : IWebhookRequestRepository
{
    private readonly UmbracoDbContext _db;
    public EfWebhookRequestRepository(UmbracoDbContext db) => _db = db;

    public Task<WebhookRequest> CreateAsync(WebhookRequest webhookRequest)
    {
        WebhookRequestDto dto = WebhookRequestFactory.CreateDto(webhookRequest);
        _db.Set<WebhookRequestDto>().Add(dto);
        _db.SaveChanges();
        webhookRequest.Id = dto.Id;
        return Task.FromResult(webhookRequest);
    }

    public Task DeleteAsync(WebhookRequest webhookRequest)
    {
        WebhookRequestDto? dto = _db.Set<WebhookRequestDto>().Find(webhookRequest.Id);
        if (dto != null) { _db.Set<WebhookRequestDto>().Remove(dto); _db.SaveChanges(); }
        return Task.CompletedTask;
    }

    public Task<IEnumerable<WebhookRequest>> GetAllAsync()
    {
        IEnumerable<WebhookRequest> result = _db.Set<WebhookRequestDto>()
            .OrderBy(x => x.Id).ToList()
            .Select(WebhookRequestFactory.CreateModel);
        return Task.FromResult(result);
    }

    public Task<WebhookRequest> UpdateAsync(WebhookRequest webhookRequest)
    {
        WebhookRequestDto dto = WebhookRequestFactory.CreateDto(webhookRequest);
        _db.Set<WebhookRequestDto>().Update(dto);
        _db.SaveChanges();
        return Task.FromResult(webhookRequest);
    }
}
