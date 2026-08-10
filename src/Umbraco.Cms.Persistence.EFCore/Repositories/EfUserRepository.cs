using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Persistence;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence.Factories;
using Umbraco.Cms.Infrastructure.Persistence.Mappers;
using Umbraco.Cms.Persistence.EFCore.Scoping;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>
/// EF Core based implementation of the <see cref="IUserRepository"/> interface.
/// Registered as a Singleton to match legacy UserRepository lifetime, resolving DbContext dynamically.
/// </summary>
public class EfUserRepository : IUserRepository
{
    private readonly IEFCoreScopeAccessor<UmbracoDbContext> _efCoreScopeAccessor;
    private readonly IServiceProvider _serviceProvider;
    private readonly GlobalSettings _globalSettings;
    private readonly SecuritySettings _securitySettings;
    private readonly IRuntimeState _runtimeState;
    private readonly IDictionary<string, IPermissionMapper> _permissionMappers;
    private readonly IJsonSerializer _jsonSerializer;
    private string? _passwordConfigJson;

    public EfUserRepository(
        IEFCoreScopeAccessor<UmbracoDbContext> efCoreScopeAccessor,
        IServiceProvider serviceProvider,
        IOptions<GlobalSettings> globalSettings,
        IOptions<SecuritySettings> securitySettings,
        IRuntimeState runtimeState,
        IEnumerable<IPermissionMapper> permissionMappers,
        IJsonSerializer jsonSerializer)
    {
        _efCoreScopeAccessor = efCoreScopeAccessor ?? throw new ArgumentNullException(nameof(efCoreScopeAccessor));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _globalSettings = globalSettings?.Value ?? throw new ArgumentNullException(nameof(globalSettings));
        _securitySettings = securitySettings?.Value ?? throw new ArgumentNullException(nameof(securitySettings));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _permissionMappers = permissionMappers.ToDictionary(x => x.Context);
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
    }

    private UmbracoDbContext GetContext()
    {
        var ambientScope = _efCoreScopeAccessor.AmbientScope as EFCoreScope<UmbracoDbContext>;
        if (ambientScope != null)
        {
            var field = typeof(EFCoreScope<UmbracoDbContext>).GetField("_dbContext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var dbContext = field?.GetValue(ambientScope) as UmbracoDbContext;
            if (dbContext != null)
            {
                return dbContext;
            }
        }

        // Return a transient context from the service provider if no ambient scope exists
        return _serviceProvider.GetRequiredService<UmbracoDbContext>();
    }

    private string DefaultPasswordConfigJson
    {
        get
        {
            if (_passwordConfigJson == null)
            {
                var passwordConfig = new PersistedPasswordSettings
                {
                    HashAlgorithm = _securitySettings.UserPassword.HashAlgorithmType
                };
                _passwordConfigJson = _jsonSerializer.Serialize(passwordConfig);
            }
            return _passwordConfigJson;
        }
    }

    private IQueryable<UserDto> GetUserQuery(UmbracoDbContext context)
    {
        return context.Set<UserDto>()
            .Include(u => u.UserStartNodeDtos)
            .Include(u => u.UserGroupDtos)
                .ThenInclude(g => g.UserGroup2AppDtos)
            .Include(u => u.UserGroupDtos)
                .ThenInclude(g => g.UserGroup2LanguageDtos)
            .Include(u => u.UserGroupDtos)
                .ThenInclude(g => g.UserGroup2PermissionDtos)
            .Include(u => u.UserGroupDtos)
                .ThenInclude(g => g.UserGroup2GranularPermissionDtos);
    }

    private IUser Map(UserDto dto)
    {
        return UserFactory.BuildEntity(_globalSettings, dto, _permissionMappers);
    }

    private IQueryable<UserDto> ApplyQuery(IQueryable<UserDto> dbQuery, IQuery<IUser>? query)
    {
        if (query == null) return dbQuery;

        foreach (var clause in query.GetWhereClauses())
        {
            var sql = clause.Item1;
            var args = clause.Item2;

            if (string.IsNullOrWhiteSpace(sql)) continue;

            var normalized = sql.ToLowerInvariant().Replace("(", "").Replace(")", "").Trim();

            if (normalized.Contains("userlogin"))
            {
                var val = args.FirstOrDefault()?.ToString();
                if (val != null)
                {
                    if (normalized.Contains("like"))
                    {
                        var cleanVal = val.Replace("%", "");
                        if (val.StartsWith("%") && val.EndsWith("%"))
                            dbQuery = dbQuery.Where(u => u.Login != null && u.Login.Contains(cleanVal));
                        else if (val.StartsWith("%"))
                            dbQuery = dbQuery.Where(u => u.Login != null && u.Login.EndsWith(cleanVal));
                        else
                            dbQuery = dbQuery.Where(u => u.Login != null && u.Login.StartsWith(cleanVal));
                    }
                    else
                    {
                        dbQuery = dbQuery.Where(u => u.Login == val);
                    }
                }
            }
            else if (normalized.Contains("useremail"))
            {
                var val = args.FirstOrDefault()?.ToString();
                if (val != null)
                {
                    if (normalized.Contains("like"))
                    {
                        var cleanVal = val.Replace("%", "");
                        if (val.StartsWith("%") && val.EndsWith("%"))
                            dbQuery = dbQuery.Where(u => u.Email != null && u.Email.Contains(cleanVal));
                        else if (val.StartsWith("%"))
                            dbQuery = dbQuery.Where(u => u.Email != null && u.Email.EndsWith(cleanVal));
                        else
                            dbQuery = dbQuery.Where(u => u.Email != null && u.Email.StartsWith(cleanVal));
                    }
                    else
                    {
                        dbQuery = dbQuery.Where(u => u.Email == val);
                    }
                }
            }
            else if (normalized.Contains("userdisabled"))
            {
                var val = args.FirstOrDefault();
                if (val is bool b)
                {
                    dbQuery = dbQuery.Where(u => u.Disabled == b);
                }
                else if (val is int i)
                {
                    dbQuery = dbQuery.Where(u => u.Disabled == (i != 0));
                }
            }
            else if (normalized.Contains("usernoconsole"))
            {
                var val = args.FirstOrDefault();
                if (val is bool b)
                {
                    dbQuery = dbQuery.Where(u => u.NoConsole == b);
                }
                else if (val is int i)
                {
                    dbQuery = dbQuery.Where(u => u.NoConsole == (i != 0));
                }
            }
            else if (normalized.Contains("userkey"))
            {
                var val = args.FirstOrDefault();
                if (val is Guid g)
                {
                    dbQuery = dbQuery.Where(u => u.Key == g);
                }
            }
            else if (normalized.Contains("id =") || normalized.Contains("id="))
            {
                var val = args.FirstOrDefault();
                if (val is int idVal)
                {
                    dbQuery = dbQuery.Where(u => u.Id == idVal);
                }
            }
        }

        return dbQuery;
    }

    private IQueryable<UserDto> ApplySort(IQueryable<UserDto> dbQuery, Expression<Func<IUser, object?>> orderBy, Direction direction)
    {
        var body = orderBy.Body;
        if (body is UnaryExpression unary) body = unary.Operand;
        var memberName = (body as MemberExpression)?.Member.Name;

        Expression<Func<UserDto, object?>> sortExpr = u => u.Id;

        if (!string.IsNullOrEmpty(memberName))
        {
            sortExpr = memberName.ToLowerInvariant() switch
            {
                "email" => u => u.Email,
                "username" => u => u.Login,
                "name" => u => u.UserName,
                "language" => u => u.UserLanguage,
                "lastlogindate" => u => u.LastLoginDate,
                "createdate" => u => u.CreateDate,
                "updatedate" => u => u.UpdateDate,
                "kind" => u => u.Kind,
                _ => u => u.Id
            };
        }

        return direction == Direction.Ascending ? dbQuery.OrderBy(sortExpr) : dbQuery.OrderByDescending(sortExpr);
    }

    #region IUserRepository Implementation

    public void Save(IUser entity)
    {
        entity.AddingEntity();

        if (entity.SecurityStamp.IsNullOrWhiteSpace())
        {
            entity.SecurityStamp = Guid.NewGuid().ToString();
        }

        var isNew = entity.HasIdentity is false;
        UserDto? userDto;
        var context = GetContext();

        if (isNew)
        {
            userDto = UserFactory.BuildDto(entity);
            if (string.IsNullOrEmpty(userDto.PasswordConfig))
            {
                userDto.PasswordConfig = DefaultPasswordConfigJson;
            }
            context.Set<UserDto>().Add(userDto);
        }
        else
        {
            userDto = context.Set<UserDto>()
                .Include(u => u.UserStartNodeDtos)
                .Include(u => u.UserGroupDtos)
                .FirstOrDefault(u => u.Id == entity.Id);

            if (userDto == null)
            {
                throw new InvalidOperationException($"User with ID {entity.Id} was not found.");
            }

            var tempDto = UserFactory.BuildDto(entity);
            userDto.Disabled = tempDto.Disabled;
            userDto.Email = tempDto.Email;
            userDto.Login = tempDto.Login;
            userDto.NoConsole = tempDto.NoConsole;

            if (entity.IsPropertyDirty("RawPasswordValue") && !string.IsNullOrWhiteSpace(entity.RawPasswordValue))
            {
                userDto.Password = tempDto.Password;
                userDto.PasswordConfig = tempDto.PasswordConfig ?? DefaultPasswordConfigJson;
            }

            userDto.UserLanguage = tempDto.UserLanguage;
            userDto.UserName = tempDto.UserName;
            userDto.SecurityStampToken = tempDto.SecurityStampToken;
            userDto.FailedLoginAttempts = tempDto.FailedLoginAttempts;
            userDto.LastLockoutDate = tempDto.LastLockoutDate;
            userDto.LastPasswordChangeDate = tempDto.LastPasswordChangeDate;
            userDto.LastLoginDate = tempDto.LastLoginDate;
            userDto.Avatar = tempDto.Avatar;
            userDto.EmailConfirmedDate = tempDto.EmailConfirmedDate;
            userDto.InvitedDate = tempDto.InvitedDate;
            userDto.UpdateDate = DateTime.UtcNow;
        }

        // Manage UserGroups
        userDto.UserGroupDtos.Clear();
        var assignedAliases = entity.Groups.Select(x => x.Alias).ToArray();
        if (assignedAliases.Length > 0)
        {
            var groups = context.Set<UserGroupDto>()
                .Where(g => assignedAliases.Contains(g.Alias))
                .ToList();
            userDto.UserGroupDtos.AddRange(groups);
        }

        // Manage StartNodes
        context.Set<UserStartNodeDto>().RemoveRange(userDto.UserStartNodeDtos);
        userDto.UserStartNodeDtos.Clear();

        foreach (var nodeType in new[] { UserStartNodeDto.StartNodeTypeValue.Content, UserStartNodeDto.StartNodeTypeValue.Media, UserStartNodeDto.StartNodeTypeValue.Element })
        {
            int[] ids = nodeType switch
            {
                UserStartNodeDto.StartNodeTypeValue.Content => entity.StartContentIds ?? Array.Empty<int>(),
                UserStartNodeDto.StartNodeTypeValue.Media => entity.StartMediaIds ?? Array.Empty<int>(),
                UserStartNodeDto.StartNodeTypeValue.Element => entity.StartElementIds ?? Array.Empty<int>(),
                _ => Array.Empty<int>()
            };

            foreach (var id in ids)
            {
                userDto.UserStartNodeDtos.Add(new UserStartNodeDto
                {
                    UserId = userDto.Id,
                    StartNode = id,
                    StartNodeType = (int)nodeType
                });
            }
        }

        context.SaveChanges();

        if (isNew)
        {
            entity.Id = userDto.Id;
        }

        entity.ResetDirtyProperties();
    }

    public void Delete(IUser entity)
    {
        var context = GetContext();
        var userDto = context.Set<UserDto>().FirstOrDefault(u => u.Id == entity.Id);
        if (userDto != null)
        {
            var startNodes = context.Set<UserStartNodeDto>().Where(n => n.UserId == entity.Id);
            context.Set<UserStartNodeDto>().RemoveRange(startNodes);

            var userLogins = context.Set<UserLoginDto>().Where(l => l.UserId == entity.Id);
            context.Set<UserLoginDto>().RemoveRange(userLogins);

            var userClientIds = context.Set<User2ClientIdDto>().Where(c => c.UserId == entity.Id);
            context.Set<User2ClientIdDto>().RemoveRange(userClientIds);

            context.Set<UserDto>().Remove(userDto);
            context.SaveChanges();
        }
    }

    public IUser? Get(Guid key)
    {
        var context = GetContext();
        var dto = GetUserQuery(context).FirstOrDefault(u => u.Key == key);
        return dto == null ? null : Map(dto);
    }

    public IEnumerable<IUser> GetMany(params Guid[]? ids)
    {
        var context = GetContext();
        var query = GetUserQuery(context);
        if (ids != null && ids.Length > 0)
        {
            query = query.Where(u => ids.Contains(u.Key));
        }
        return query.ToList().Select(Map);
    }

    public bool Exists(Guid id)
    {
        var context = GetContext();
        return context.Set<UserDto>().Any(u => u.Key == id);
    }

    public int GetCountByQuery(IQuery<IUser>? query) => Count(query);

    public bool ExistsByUserName(string username)
    {
        var context = GetContext();
        return context.Set<UserDto>().Any(u => u.Login == username);
    }

    public IUser? Get(int id)
    {
        var context = GetContext();
        var dto = GetUserQuery(context).FirstOrDefault(u => u.Id == id);
        return dto == null ? null : Map(dto);
    }

    public bool ExistsByLogin(string login)
    {
        var context = GetContext();
        return context.Set<UserDto>().Any(u => u.Login == login);
    }

    public IEnumerable<IUser> GetAllInGroup(int groupId)
    {
        var context = GetContext();
        return GetUserQuery(context)
            .Where(u => u.UserGroupDtos.Any(g => g.Id == groupId))
            .ToList()
            .Select(Map);
    }

    public IEnumerable<IUser> GetAllNotInGroup(int groupId)
    {
        var context = GetContext();
        return GetUserQuery(context)
            .Where(u => !u.UserGroupDtos.Any(g => g.Id == groupId))
            .ToList()
            .Select(Map);
    }

    public IEnumerable<IUser> GetPagedResultsByQuery(
        IQuery<IUser>? query,
        long pageIndex,
        int pageSize,
        out long totalRecords,
        Expression<Func<IUser, object?>> orderBy,
        Direction orderDirection = Direction.Ascending,
        string[]? includeUserGroups = null,
        string[]? excludeUserGroups = null,
        UserState[]? userState = null,
        IQuery<IUser>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(orderBy);

        var context = GetContext();
        var dbQuery = GetUserQuery(context);

        dbQuery = ApplyQuery(dbQuery, query);
        dbQuery = ApplyQuery(dbQuery, filter);

        if (includeUserGroups != null && includeUserGroups.Length > 0)
        {
            dbQuery = dbQuery.Where(u => u.UserGroupDtos.Any(g => includeUserGroups.Contains(g.Alias)));
        }

        if (excludeUserGroups != null && excludeUserGroups.Length > 0)
        {
            dbQuery = dbQuery.Where(u => !u.UserGroupDtos.Any(g => excludeUserGroups.Contains(g.Alias)));
        }

        if (userState != null && userState.Length > 0 && !userState.Contains(UserState.All))
        {
            dbQuery = dbQuery.Where(u =>
                (userState.Contains(UserState.Active) && !u.Disabled && !u.NoConsole && u.LastLoginDate != null) ||
                (userState.Contains(UserState.Inactive) && !u.Disabled && !u.NoConsole && u.LastLoginDate == null) ||
                (userState.Contains(UserState.Disabled) && u.Disabled) ||
                (userState.Contains(UserState.LockedOut) && u.NoConsole) ||
                (userState.Contains(UserState.Invited) && u.LastLoginDate == null && u.Disabled && u.InvitedDate != null)
            );
        }

        totalRecords = dbQuery.Count();

        dbQuery = ApplySort(dbQuery, orderBy, orderDirection);

        var items = dbQuery.Skip((int)pageIndex * pageSize).Take(pageSize).ToList();

        return items.Select(Map);
    }

    public IUser? GetByUsername(string username, bool includeSecurityData)
    {
        var context = GetContext();
        var query = includeSecurityData ? GetUserQuery(context) : context.Set<UserDto>();
        var dto = query.FirstOrDefault(u => u.Login == username);
        return dto == null ? null : Map(dto);
    }

    public IUser? Get(int? id, bool includeSecurityData)
    {
        if (id == null) return null;
        var context = GetContext();
        var query = includeSecurityData ? GetUserQuery(context) : context.Set<UserDto>();
        var dto = query.FirstOrDefault(u => u.Id == id);
        return dto == null ? null : Map(dto);
    }

    public IProfile? GetProfile(string username)
    {
        var context = GetContext();
        var dto = context.Set<UserDto>().FirstOrDefault(u => u.Login == username);
        return dto == null ? null : new UserProfile(dto.Id, dto.UserName);
    }

    public IProfile? GetProfile(int id)
    {
        var context = GetContext();
        var dto = context.Set<UserDto>().FirstOrDefault(u => u.Id == id);
        return dto == null ? null : new UserProfile(dto.Id, dto.UserName);
    }

    public IDictionary<UserState, int> GetUserStates()
    {
        var context = GetContext();
        var set = context.Set<UserDto>();
        var total = set.Count();
        var active = set.Count(u => !u.Disabled && !u.NoConsole && u.LastLoginDate != null);
        var disabled = set.Count(u => u.Disabled);
        var lockedOut = set.Count(u => u.NoConsole);
        var invited = set.Count(u => u.LastLoginDate == null && u.Disabled && u.InvitedDate != null);
        var inactive = set.Count(u => !u.Disabled && !u.NoConsole && u.LastLoginDate == null);

        return new Dictionary<UserState, int>
        {
            { UserState.All, total },
            { UserState.Active, active },
            { UserState.Disabled, disabled },
            { UserState.LockedOut, lockedOut },
            { UserState.Invited, invited },
            { UserState.Inactive, inactive }
        };
    }

    public Guid CreateLoginSession(int? userId, string requestingIpAddress, bool cleanStaleSessions = true)
    {
        var context = GetContext();
        DateTime now = DateTime.UtcNow;
        var dto = new UserLoginDto
        {
            UserId = userId,
            IpAddress = requestingIpAddress,
            LoggedIn = now,
            LastValidated = now,
            LoggedOut = null,
            SessionId = Guid.NewGuid()
        };
        context.Set<UserLoginDto>().Add(dto);
        context.SaveChanges();

        if (cleanStaleSessions)
        {
            ClearLoginSessions(TimeSpan.FromDays(15));
        }

        return dto.SessionId;
    }

    public bool ValidateLoginSession(int userId, Guid sessionId)
    {
        var context = GetContext();
        var found = context.Set<UserLoginDto>().FirstOrDefault(x => x.SessionId == sessionId);
        if (found == null || found.UserId != userId || found.LoggedOut.HasValue)
        {
            return false;
        }

        if (DateTime.UtcNow - found.LastValidated > _globalSettings.TimeOut)
        {
            ClearLoginSession(sessionId);
            return false;
        }

        found.LastValidated = DateTime.UtcNow;
        context.SaveChanges();
        return true;
    }

    public int ClearLoginSessions(int userId)
    {
        var context = GetContext();
        var sessions = context.Set<UserLoginDto>().Where(x => x.UserId == userId).ToList();
        context.Set<UserLoginDto>().RemoveRange(sessions);
        return context.SaveChanges();
    }

    public int ClearLoginSessions(TimeSpan timespan)
    {
        var context = GetContext();
        DateTime fromDate = DateTime.UtcNow - timespan;
        var sessions = context.Set<UserLoginDto>().Where(x => x.LastValidated < fromDate).ToList();
        context.Set<UserLoginDto>().RemoveRange(sessions);
        return context.SaveChanges();
    }

    public void ClearLoginSession(Guid sessionId)
    {
        var context = GetContext();
        var found = context.Set<UserLoginDto>().FirstOrDefault(x => x.SessionId == sessionId);
        if (found != null)
        {
            found.LoggedOut = DateTime.UtcNow;
            context.SaveChanges();
        }
    }

    public IEnumerable<string> GetAllClientIds()
    {
        var context = GetContext();
        return context.Set<User2ClientIdDto>().Select(c => c.ClientId!).ToList();
    }

    public IEnumerable<string> GetClientIds(int id)
    {
        var context = GetContext();
        return context.Set<User2ClientIdDto>()
            .Where(c => c.UserId == id)
            .Select(c => c.ClientId!)
            .ToList();
    }

    public void AddClientId(int id, string clientId)
    {
        var context = GetContext();
        var exists = context.Set<User2ClientIdDto>().Any(c => c.UserId == id && c.ClientId == clientId);
        if (!exists)
        {
            context.Set<User2ClientIdDto>().Add(new User2ClientIdDto { UserId = id, ClientId = clientId });
            context.SaveChanges();
        }
    }

    public bool RemoveClientId(int id, string clientId)
    {
        var context = GetContext();
        var found = context.Set<User2ClientIdDto>().FirstOrDefault(c => c.UserId == id && c.ClientId == clientId);
        if (found != null)
        {
            context.Set<User2ClientIdDto>().Remove(found);
            return context.SaveChanges() > 0;
        }
        return false;
    }

    public IUser? GetByClientId(string clientId)
    {
        var context = GetContext();
        var userId = context.Set<User2ClientIdDto>()
            .Where(c => c.ClientId == clientId)
            .Select(c => c.UserId)
            .FirstOrDefault();

        return userId == 0 ? null : Get(userId);
    }

    public IEnumerable<IUser> Get(IQuery<IUser> query)
    {
        var context = GetContext();
        var dbQuery = GetUserQuery(context);
        dbQuery = ApplyQuery(dbQuery, query);
        return dbQuery.ToList().Select(Map);
    }

    public int Count(IQuery<IUser>? query)
    {
        var context = GetContext();
        if (query == null) return context.Set<UserDto>().Count();
        var dbQuery = context.Set<UserDto>().AsQueryable();
        dbQuery = ApplyQuery(dbQuery, query);
        return dbQuery.Count();
    }

    #endregion
}
