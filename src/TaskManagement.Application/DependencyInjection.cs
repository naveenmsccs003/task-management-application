using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Application.Access;
using TaskManagement.Application.Algorithms;
using TaskManagement.Application.Attachments;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Checklists;
using TaskManagement.Application.Common;
using TaskManagement.Application.Deadlines;
using TaskManagement.Application.Delays;
using TaskManagement.Application.GitLab;
using TaskManagement.Application.Identity;
using TaskManagement.Application.Members;
using TaskManagement.Application.Notifications;
using TaskManagement.Application.Projects;
using TaskManagement.Application.Reporting;
using TaskManagement.Application.Requests;
using TaskManagement.Application.Review;
using TaskManagement.Application.Settings;
using TaskManagement.Application.Tasks;
using TaskManagement.Application.Time;
using TaskManagement.Application.Work;

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

        // Algorithms, checklists, work sessions and time
        services.AddScoped<IdempotencyStore>();
        services.AddScoped<AlgorithmService>();
        services.AddScoped<ChecklistTemplates>();
        services.AddScoped<TaskChecklist>();
        services.AddScoped<WorkService>();
        services.AddScoped<TimeService>();
        services.AddScoped<SessionMonitor>();

        // Notifications and operations
        services.AddScoped<Notifier>();
        services.AddScoped<NotificationService>();
        services.AddScoped<JobRecorder>();
        services.AddScoped<RetentionCleanup>();

        // Review, requests, delays and due dates
        services.AddScoped<ReviewService>();
        services.AddScoped<RequestService>();
        services.AddScoped<DelayService>();
        services.AddScoped<DueDateMonitor>();

        // Attachments
        services.AddScoped<AttachmentService>();
        services.AddScoped<AttachmentScanning>();

        // GitLab
        services.AddScoped<GitLabConnections>();
        services.AddScoped<GitLabImports>();

        // Dashboards and reports
        services.AddScoped<DashboardService>();
        services.AddScoped<ReportService>();

        return services;
    }
}
