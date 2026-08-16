using System.Configuration;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Persistence.EFCore.Migrations;

namespace Umbraco.Cms.Persistence.EFCore;

/// <summary>
/// The Entity Framework Core database context for Umbraco CMS.
/// </summary>
/// <remarks>
/// To autogenerate migrations use the following commands
/// and insure the 'src/Umbraco.Web.UI/appsettings.json' have a connection string set with the right provider.
///
/// Create a migration for each provider.
/// <code>dotnet ef migrations add %Name% -s src/Umbraco.Web.UI -p src/Umbraco.Cms.Persistence.EFCore.SqlServer -c UmbracoDbContext</code>
///
/// <code>dotnet ef migrations add %Name% -s src/Umbraco.Web.UI -p src/Umbraco.Cms.Persistence.EFCore.Sqlite -c UmbracoDbContext</code>
///
/// Remove the last migration for each provider.
/// <code>dotnet ef migrations remove -s src/Umbraco.Web.UI -p src/Umbraco.Cms.Persistence.EFCore.SqlServer</code>
///
/// <code>dotnet ef migrations remove -s src/Umbraco.Web.UI -p src/Umbraco.Cms.Persistence.EFCore.Sqlite</code>
///
/// To find documentation about this way of working with the context see
/// https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers?tabs=dotnet-core-cli#using-one-context-type
/// </remarks>
public class UmbracoDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UmbracoDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to be used by the DbContext.</param>
    public UmbracoDbContext(DbContextOptions<UmbracoDbContext> options)
        : base(ConfigureOptions(options))
    { }

    private static DbContextOptions<UmbracoDbContext> ConfigureOptions(DbContextOptions<UmbracoDbContext> options)
    {
        var extensions = options.Extensions.FirstOrDefault() as Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension;
        IServiceProvider? serviceProvider = extensions?.ApplicationServiceProvider;
        serviceProvider ??= StaticServiceProvider.Instance;
        if (serviceProvider == null)
        {
            // If the service provider is null, we cannot resolve the connection string or migration provider.
            throw new InvalidOperationException("The service provider is not configured. Ensure that UmbracoDbContext is registered correctly.");
        }

        IOptionsMonitor<ConnectionStrings>? connectionStringsOptionsMonitor = serviceProvider?.GetRequiredService<IOptionsMonitor<ConnectionStrings>>();

        ConnectionStrings? connectionStrings = connectionStringsOptionsMonitor?.CurrentValue;

        if (string.IsNullOrWhiteSpace(connectionStrings?.ConnectionString))
        {
            ILogger<UmbracoDbContext>? logger = serviceProvider?.GetRequiredService<ILogger<UmbracoDbContext>>();
            logger?.LogCritical("No connection string was found, cannot setup Umbraco EF Core context");

            // we're throwing an exception here to make it abundantly clear that one should never utilize (or have a
            // dependency on) the DbContext before the connection string has been initialized by the installer.
            throw new InvalidOperationException("No connection string was found, cannot setup Umbraco EF Core context");
        }

        IEnumerable<IMigrationProviderSetup>? migrationProviders = serviceProvider?.GetServices<IMigrationProviderSetup>();
        IMigrationProviderSetup? migrationProvider = migrationProviders?.FirstOrDefault(x => x.ProviderName.CompareProviderNames(connectionStrings.ProviderName));

        if (migrationProvider == null && connectionStrings.ProviderName != null)
        {
            throw new InvalidOperationException($"No migration provider found for provider name {connectionStrings.ProviderName}");
        }

        var optionsBuilder = new DbContextOptionsBuilder<UmbracoDbContext>(options);
        migrationProvider?.Setup(optionsBuilder, connectionStrings.ConnectionString);
        return optionsBuilder.Options;
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Register User & User Group DTOs to the EF Core model
        modelBuilder.Entity<UserDto>(entity =>
        {
            entity.ToTable("umbracoUser");
            entity.HasKey(e => e.Id);
            entity.HasMany(u => u.UserGroupDtos)
                  .WithMany()
                  .UsingEntity<System.Collections.Generic.Dictionary<string, object>>(
                      "umbracoUser2UserGroup",
                      r => r.HasOne<UserGroupDto>().WithMany().HasForeignKey("userGroupId"),
                      l => l.HasOne<UserDto>().WithMany().HasForeignKey("userId"));
        });

        modelBuilder.Entity<UserGroupDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup");
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => e.Key);
        });

        modelBuilder.Entity<UserStartNodeDto>(entity =>
        {
            entity.ToTable("umbracoUserStartNode");
            entity.HasKey(e => e.Id);
            entity.HasOne<UserDto>()
                  .WithMany(u => u.UserStartNodeDtos)
                  .HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<UserGroup2AppDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup2App");
            entity.HasKey(e => new { e.UserGroupId, e.AppAlias });
            entity.HasOne<UserGroupDto>()
                  .WithMany(g => g.UserGroup2AppDtos)
                  .HasForeignKey(e => e.UserGroupId);
        });

        modelBuilder.Entity<UserGroup2LanguageDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup2Language");
            entity.HasKey(e => new { e.UserGroupId, e.LanguageId });
            entity.HasOne<UserGroupDto>()
                  .WithMany(g => g.UserGroup2LanguageDtos)
                  .HasForeignKey(e => e.UserGroupId);
        });

        modelBuilder.Entity<UserGroup2PermissionDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup2Permission");
            entity.HasKey(e => e.Id);
            entity.HasOne<UserGroupDto>()
                  .WithMany(g => g.UserGroup2PermissionDtos)
                  .HasForeignKey(e => e.UserGroupKey)
                  .HasPrincipalKey(g => g.Key);
        });

        modelBuilder.Entity<UserGroup2GranularPermissionDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup2GranularPermission");
            entity.HasKey(e => e.Id);
            entity.HasOne<UserGroupDto>()
                  .WithMany(g => g.UserGroup2GranularPermissionDtos)
                  .HasForeignKey(e => e.UserGroupKey)
                  .HasPrincipalKey(g => g.Key);
        });

        modelBuilder.Entity<UserLoginDto>(entity =>
        {
            entity.ToTable("umbracoUserLogin");
            entity.HasKey(e => e.SessionId);
        });

        modelBuilder.Entity<User2ClientIdDto>(entity =>
        {
            entity.ToTable("umbracoUser2ClientId");
            entity.HasKey(e => new { e.UserId, e.ClientId });
        });

        // ── Batch 1: Audit, AuditEntry, Domain, Language, KeyValue ──────────

        modelBuilder.Entity<LogDto>(entity =>
        {
            entity.ToTable("umbracoLog");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<AuditEntryDto>(entity =>
        {
            entity.ToTable("umbracoAudit");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<LanguageDto>(entity =>
        {
            entity.ToTable("umbracoLanguage");
            entity.HasKey(e => e.Id);
            entity.HasOne<LanguageDto>()
                  .WithMany()
                  .HasForeignKey(e => e.FallbackLanguageId)
                  .IsRequired(false);
        });

        modelBuilder.Entity<DomainDto>(entity =>
        {
            entity.ToTable("umbracoDomain");
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.IsoCode); // ResultColumn — not a DB column
            entity.HasOne<LanguageDto>()
                  .WithMany()
                  .HasForeignKey(e => e.DefaultLanguage)
                  .IsRequired(false);
        });

        modelBuilder.Entity<KeyValueDto>(entity =>
        {
            entity.ToTable("umbracoKeyValue");
            entity.HasKey(e => e.Key);
        });

        modelBuilder.Entity<NodeDto>(entity =>
        {
            entity.ToTable("umbracoNode");
            entity.HasKey(e => e.NodeId);
        });

        // ── Batch 2: RedirectUrl, Notifications, Consent, LogViewerQuery, Webhook ──

        modelBuilder.Entity<RedirectUrlDto>(entity =>
        {
            entity.ToTable("umbracoRedirectUrl");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<User2NodeNotifyDto>(entity =>
        {
            entity.ToTable("umbracoUser2NodeNotify");
            entity.HasKey(e => new { e.UserId, e.NodeId, e.Action });
        });

        modelBuilder.Entity<ConsentDto>(entity =>
        {
            entity.ToTable("umbracoConsent");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<LogViewerQueryDto>(entity =>
        {
            entity.ToTable("umbracoLogViewerQuery");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<WebhookDto>(entity =>
        {
            entity.ToTable("umbracoWebhook");
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => e.Key);
        });

        modelBuilder.Entity<Webhook2ContentTypeKeysDto>(entity =>
        {
            entity.ToTable("umbracoWebhook2ContentTypeKeys");
            entity.HasKey(e => new { e.WebhookId, e.ContentTypeKey });
            entity.HasOne<WebhookDto>().WithMany().HasForeignKey(e => e.WebhookId);
        });

        modelBuilder.Entity<Webhook2EventsDto>(entity =>
        {
            entity.ToTable("umbracoWebhook2Events");
            entity.HasKey(e => new { e.WebhookId, e.Event });
            entity.HasOne<WebhookDto>().WithMany().HasForeignKey(e => e.WebhookId);
        });

        modelBuilder.Entity<Webhook2HeadersDto>(entity =>
        {
            entity.ToTable("umbracoWebhook2Headers");
            entity.HasKey(e => new { e.WebhookId, e.Key });
            entity.HasOne<WebhookDto>().WithMany().HasForeignKey(e => e.WebhookId);
        });

        modelBuilder.Entity<WebhookLogDto>(entity =>
        {
            entity.ToTable("umbracoWebhookLog");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<WebhookRequestDto>(entity =>
        {
            entity.ToTable("umbracoWebhookRequest");
            entity.HasKey(e => e.Id);
        });

        // ── Batch 3: ServerRegistration, LongRunningOperation, CacheInstruction, ──
        // ── DocumentUrl, DocumentUrlAlias, TwoFactorLogin, ExternalLogin, UserData ──

        modelBuilder.Entity<ServerRegistrationDto>(entity =>
        {
            entity.ToTable("umbracoServer");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<LongRunningOperationDto>(entity =>
        {
            entity.ToTable("umbracoLongRunningOperation");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<CacheInstructionDto>(entity =>
        {
            entity.ToTable("umbracoCacheInstruction");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<DocumentUrlDto>(entity =>
        {
            entity.ToTable("umbracoDocumentUrl");
            entity.HasKey(e => new { e.UniqueId, e.LanguageId, e.IsDraft, e.UrlSegment });
            entity.Ignore(e => e.NodeId); // NodeId is the PK column name alias — UniqueId is the real FK
        });

        modelBuilder.Entity<DocumentUrlAliasDto>(entity =>
        {
            entity.ToTable("umbracoDocumentUrlAlias");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<TwoFactorLoginDto>(entity =>
        {
            entity.ToTable("umbracoTwoFactorLogin");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<ExternalLoginDto>(entity =>
        {
            entity.ToTable("umbracoExternalLogin");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<ExternalLoginTokenDto>(entity =>
        {
            entity.ToTable("umbracoExternalLoginToken");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.ExternalLoginDto)
                  .WithMany()
                  .HasForeignKey(e => e.ExternalLoginId);
        });

        modelBuilder.Entity<UserDataDto>(entity =>
        {
            entity.ToTable("umbracoUserData");
            entity.HasKey(e => e.Key);
        });

        // ── Faz 2-A: PublicAccess, RelationType, Relation, Dictionary ──────────

        modelBuilder.Entity<AccessDto>(entity =>
        {
            entity.ToTable("umbracoAccess");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<AccessRuleDto>(entity =>
        {
            entity.ToTable("umbracoAccessRule");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<RelationTypeDto>(entity =>
        {
            entity.ToTable("umbracoRelationType");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<RelationDto>(entity =>
        {
            entity.ToTable("umbracoRelation");
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.ParentObjectType);
            entity.Ignore(e => e.ChildObjectType);
        });

        modelBuilder.Entity<DictionaryDto>(entity =>
        {
            entity.ToTable("cmsDictionary");
            entity.HasKey(e => e.PrimaryKey);
        });

        modelBuilder.Entity<LanguageTextDto>(entity =>
        {
            entity.ToTable("cmsLanguageText");
            entity.HasKey(e => e.PrimaryKey);
        });

        // ── Faz 2-B: MemberGroup, Tag ──────────────────────────────────────────

        modelBuilder.Entity<Member2MemberGroupDto>(entity =>
        {
            entity.ToTable("cmsMember2MemberGroup");
            entity.HasKey(e => new { e.Member, e.MemberGroup });
        });

        modelBuilder.Entity<MemberDto>(entity =>
        {
            entity.ToTable("cmsMember");
            entity.HasKey(e => e.NodeId);
        });

        modelBuilder.Entity<TagDto>(entity =>
        {
            entity.ToTable("cmsTags");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<TagRelationshipDto>(entity =>
        {
            entity.ToTable("cmsTagRelationship");
            entity.HasKey(e => new { e.NodeId, e.PropertyTypeId, e.TagId });
        });

        modelBuilder.Entity<PropertyTypeDto>(entity =>
        {
            entity.ToTable("cmsPropertyType");
            entity.HasKey(e => e.Id);
        });

        // ── Faz 2-C: DataType, Template, ContentType ───────────────────────────

        modelBuilder.Entity<DataTypeDto>(entity =>
        {
            entity.ToTable("umbracoDataType");
            entity.HasKey(e => e.NodeId);
        });

        modelBuilder.Entity<ContentTypeDto>(entity =>
        {
            entity.ToTable("cmsContentType");
            entity.HasKey(e => e.PrimaryKey);
        });

        modelBuilder.Entity<TemplateDto>(entity =>
        {
            entity.ToTable("cmsTemplate");
            entity.HasKey(e => e.PrimaryKey);
        });

        // ── Faz 2-D: UserGroup, Junction Tables ────────────────────────────────

        modelBuilder.Entity<UserGroupDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<UserGroup2AppDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup2App");
            entity.HasKey(e => new { e.UserGroupId, e.AppAlias });
        });

        modelBuilder.Entity<UserGroup2LanguageDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup2Language");
            entity.HasKey(e => new { e.UserGroupId, e.LanguageId });
        });

        modelBuilder.Entity<UserGroup2PermissionDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup2Permission");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<UserGroup2GranularPermissionDto>(entity =>
        {
            entity.ToTable("umbracoUserGroup2GranularPermission");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<User2UserGroupDto>(entity =>
        {
            entity.ToTable("umbracoUser2UserGroup");
            entity.HasKey(e => new { e.UserId, e.UserGroupId });
        });

        // ── Batch 3-A: ContentType, MediaType, MemberType Schemas ─────────────

        modelBuilder.Entity<PropertyTypeGroupDto>(entity =>
        {
            entity.ToTable("cmsPropertyTypeGroup");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<ContentTypeAllowedContentTypeDto>(entity =>
        {
            entity.ToTable("cmsContentTypeAllowedContentType");
            entity.HasKey(e => new { e.Id, e.AllowedId });
        });

        modelBuilder.Entity<ContentTypeTemplateDto>(entity =>
        {
            entity.ToTable("cmsDocumentType");
            entity.HasKey(e => new { e.ContentTypeNodeId, e.TemplateNodeId });
        });

        modelBuilder.Entity<ContentType2ContentTypeDto>(entity =>
        {
            entity.ToTable("cmsContentType2ContentType");
            entity.HasKey(e => new { e.ParentId, e.ChildId });
        });

        var providerName = Database.ProviderName;
        bool isPostgreSql = string.Equals(providerName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.OrdinalIgnoreCase);
        bool isOracle = string.Equals(providerName, "Oracle.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);
        bool isMySql = string.Equals(providerName, "Pomelo.EntityFrameworkCore.MySql", StringComparison.OrdinalIgnoreCase);

        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entity.ClrType != null)
            {
                // First ignore ignored properties
                var toIgnore = new System.Collections.Generic.List<string>();
                foreach (var prop in entity.GetProperties())
                {
                    System.Reflection.PropertyInfo? clrProp = entity.ClrType.GetProperty(prop.Name);
                    if (clrProp != null && clrProp.GetCustomAttributes(true).Any(a => 
                        a.GetType().Name == "IgnoreAttribute" || 
                        a.GetType().Name == "ResultColumnAttribute" ||
                        a.GetType().Name == "Ignore" ||
                        a.GetType().Name == "ResultColumn"))
                    {
                        toIgnore.Add(prop.Name);
                    }
                }
                foreach (var propName in toIgnore)
                {
                    modelBuilder.Entity(entity.ClrType).Ignore(propName);
                }

                // Map NPoco Table Name Attribute
                object? npocoTableNameAttr = entity.ClrType.GetCustomAttributes(true)
                    .FirstOrDefault(a => a.GetType().FullName == "NPoco.TableNameAttribute");
                if (npocoTableNameAttr != null)
                {
                    System.Reflection.PropertyInfo? valueProp = npocoTableNameAttr.GetType().GetProperty("Value");
                    string? npocoTableName = valueProp?.GetValue(npocoTableNameAttr) as string;
                    if (!string.IsNullOrWhiteSpace(npocoTableName))
                    {
                        entity.SetTableName(npocoTableName);
                    }
                }

                // Map remaining properties' column names
                foreach (IMutableProperty property in entity.GetProperties())
                {
                    System.Reflection.PropertyInfo? clrProperty = entity.ClrType.GetProperty(property.Name);
                    if (clrProperty != null)
                    {
                        object? npocoColumnAttr = clrProperty.GetCustomAttributes(true)
                            .FirstOrDefault(a => a.GetType().FullName == "NPoco.ColumnAttribute");
                        if (npocoColumnAttr != null)
                        {
                            System.Reflection.PropertyInfo? nameProp = npocoColumnAttr.GetType().GetProperty("Name");
                            string? npocoColumnName = nameProp?.GetValue(npocoColumnAttr) as string;
                            if (!string.IsNullOrWhiteSpace(npocoColumnName))
                            {
                                property.SetColumnName(npocoColumnName);
                            }
                        }
                    }
                }
            }

            // 1. Prefix Table Name (Only if it doesn't already start with the prefix)
            var currentTableName = entity.GetTableName();
            if (currentTableName != null)
            {
                var prefixedTableName = currentTableName.StartsWith(Core.Constants.DatabaseSchema.TableNamePrefix, StringComparison.OrdinalIgnoreCase)
                    ? currentTableName
                    : Core.Constants.DatabaseSchema.TableNamePrefix + currentTableName;

                if (isPostgreSql)
                {
                    entity.SetTableName(ToSnakeCase(prefixedTableName));
                }
                else
                {
                    entity.SetTableName(prefixedTableName);
                }
            }

            // PostgreSQL Specific Mapping: snake_case columns, keys, indexes
            if (isPostgreSql)
            {
                var tableName = entity.GetTableName();
                if (tableName != null)
                {
                    var storeObject = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
                    foreach (IMutableProperty property in entity.GetProperties())
                    {
                        var columnName = property.GetColumnName(storeObject);
                        if (columnName != null)
                        {
                            property.SetColumnName(ToSnakeCase(columnName));
                        }
                    }
                }

                foreach (IMutableKey key in entity.GetKeys())
                {
                    var keyName = key.GetName();
                    if (keyName != null)
                    {
                        key.SetName(ToSnakeCase(keyName));
                    }
                }

                foreach (IMutableForeignKey foreignKey in entity.GetForeignKeys())
                {
                    var constraintName = foreignKey.GetConstraintName();
                    if (constraintName != null)
                    {
                        foreignKey.SetConstraintName(ToSnakeCase(constraintName));
                    }
                }

                foreach (IMutableIndex index in entity.GetIndexes())
                {
                    var indexName = index.GetDatabaseName();
                    if (indexName != null)
                    {
                        index.SetDatabaseName(ToSnakeCase(indexName));
                    }
                }
            }

            // Oracle Specific Mapping: 30 character limit for identifiers
            if (isOracle)
            {
                foreach (IMutableKey key in entity.GetKeys())
                {
                    var keyName = key.GetName();
                    if (keyName?.Length > 30)
                    {
                        key.SetName(TruncateIdentifier(keyName, 30));
                    }
                }

                foreach (IMutableForeignKey foreignKey in entity.GetForeignKeys())
                {
                    var constraintName = foreignKey.GetConstraintName();
                    if (constraintName?.Length > 30)
                    {
                        foreignKey.SetConstraintName(TruncateIdentifier(constraintName, 30));
                    }
                }

                foreach (IMutableIndex index in entity.GetIndexes())
                {
                    var indexName = index.GetDatabaseName();
                    if (indexName?.Length > 30)
                    {
                        index.SetDatabaseName(TruncateIdentifier(indexName, 30));
                    }
                }
            }

            // MySQL / Pomelo Specific Mapping: 64 character limit for keys and indexes
            if (isMySql)
            {
                foreach (IMutableForeignKey foreignKey in entity.GetForeignKeys())
                {
                    var constraintName = foreignKey.GetConstraintName();
                    if (constraintName?.Length > 64)
                    {
                        foreignKey.SetConstraintName(TruncateIdentifier(constraintName, 64));
                    }
                }

                foreach (IMutableIndex index in entity.GetIndexes())
                {
                    var indexName = index.GetDatabaseName();
                    if (indexName?.Length > 64)
                    {
                        index.SetDatabaseName(TruncateIdentifier(indexName, 64));
                    }
                }
            }
        }
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var startUnderscore = input.StartsWith("_");
        var str = System.Text.RegularExpressions.Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        return startUnderscore ? "_" + str : str;
    }

    private static string TruncateIdentifier(string identifier, int maxLength)
    {
        if (string.IsNullOrEmpty(identifier) || identifier.Length <= maxLength)
        {
            return identifier;
        }

        // Generate a deterministic 8-character hex hash from the identifier to preserve uniqueness
        string hash = Math.Abs(identifier.GetHashCode()).ToString("X8");
        int takeLength = maxLength - hash.Length - 1; // leave room for underscore and hash
        if (takeLength < 0)
        {
            takeLength = 0;
        }

        return $"{identifier.Substring(0, takeLength)}_{hash}";
    }
}
