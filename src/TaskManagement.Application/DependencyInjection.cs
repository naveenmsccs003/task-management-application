using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Identity;
using TaskManagement.Application.Members;
using TaskManagement.Application.Projects;
using TaskManagement.Application.Settings;
using TaskManagement.Application.Tasks;

namespace TaskManagement.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case handlers and application services. Modules add their registrations here.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMemoryCache();

        // Shared services
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<SettingsService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<SessionValidator>();
        services.AddScoped<PasswordTokenIssuer>();
        services.AddScoped<AccountEmails>();

        // Identity
        services.AddScoped<SignInHandler>();
        services.AddScoped<RequestPasswordResetHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<ChangePasswordHandler>();
        services.AddScoped<MeHandler>();

        // Members, roles, audit
        services.AddScoped<MemberQueries>();
        services.AddScoped<MemberCommands>();
        services.AddScoped<RoleManagement>();
        services.AddScoped<SearchAuditLogsHandler>();

        // Projects and tasks
        services.AddScoped<ProjectAccess>();
        services.AddScoped<ProjectQueries>();
        services.AddScoped<ProjectCommands>();
        services.AddScoped<ProjectMemberCommands>();
        services.AddScoped<TaskAccess>();
        services.AddScoped<TaskQueries>();
        services.AddScoped<TaskCommands>();
        services.AddScoped<TaskComments>();

        return services;
    }
}
