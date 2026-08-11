using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Notifications;
using Umbraco.Cms.Persistence.EFCore.Repositories;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Composition;

/// <summary>
/// Composer that registers Entity Framework Core services and configurations for Umbraco.
/// </summary>
public class UmbracoEFCoreComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IEFCoreMigrationExecutor, EFCoreMigrationExecutor>();

        builder.AddNotificationAsyncHandler<DatabaseSchemaAndDataCreatedNotification, EFCoreCreateTablesNotificationHandler>();
        builder.AddNotificationAsyncHandler<UnattendedInstallNotification, EFCoreCreateTablesNotificationHandler>();

        // Register dynamic flexible data stores based on appsettings configuration
        builder.Services.AddUmbracoFlexibleDataStores(builder.Config);

        // Register EF Core UserRepository (PoC)
        builder.Services.AddUnique<IUserRepository, EfUserRepository>(ServiceLifetime.Singleton);

        // ── Batch 1: Audit, AuditEntry, Domain, Language, KeyValue ──────────
        builder.Services.AddUnique<IAuditRepository, EfAuditRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<IAuditEntryRepository, EfAuditEntryRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<IDomainRepository, EfDomainRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<ILanguageRepository, EfLanguageRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<IKeyValueRepository, EfKeyValueRepository>(ServiceLifetime.Singleton);

        // ── Batch 2: RedirectUrl, Notifications, Webhook, Consent, LogViewerQuery, NodeCount ──
        builder.Services.AddUnique<IRedirectUrlRepository, EfRedirectUrlRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<INotificationsRepository, EfNotificationsRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<IWebhookRepository, EfWebhookRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<IWebhookLogRepository, EfWebhookLogRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<IWebhookRequestRepository, EfWebhookRequestRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<IConsentRepository, EfConsentRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<ILogViewerQueryRepository, EfLogViewerQueryRepository>(ServiceLifetime.Singleton);
        builder.Services.AddUnique<INodeCountRepository, EfNodeCountRepository>(ServiceLifetime.Singleton);

        builder.Services.AddOpenIddict()

            // Register the OpenIddict core components.
            .AddCore(options =>
            {
                options
                    .UseEntityFrameworkCore()
                    .UseDbContext<UmbracoDbContext>();
            });
    }
}


/// <summary>
/// Notification handler that creates EF Core database tables after schema creation or unattended install.
/// </summary>
public class EFCoreCreateTablesNotificationHandler : INotificationAsyncHandler<DatabaseSchemaAndDataCreatedNotification>, INotificationAsyncHandler<UnattendedInstallNotification>
{
    private readonly IEFCoreMigrationExecutor _iefCoreMigrationExecutor;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreCreateTablesNotificationHandler"/> class.
    /// </summary>
    /// <param name="iefCoreMigrationExecutor">The EF Core migration executor.</param>
    public EFCoreCreateTablesNotificationHandler(IEFCoreMigrationExecutor iefCoreMigrationExecutor)
    {
        _iefCoreMigrationExecutor = iefCoreMigrationExecutor;
    }

    /// <inheritdoc />
    public async Task HandleAsync(UnattendedInstallNotification notification, CancellationToken cancellationToken)
    {
        await HandleAsync();
    }

    /// <inheritdoc />
    public async Task HandleAsync(DatabaseSchemaAndDataCreatedNotification notification, CancellationToken cancellationToken)
    {
        if (notification.RequiresUpgrade is false)
        {
            await HandleAsync();
        }
    }

    private async Task HandleAsync()
    {
        await _iefCoreMigrationExecutor.ExecuteAllMigrationsAsync();
    }
}
