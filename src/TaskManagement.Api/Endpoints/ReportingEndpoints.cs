using TaskManagement.Api.Auth;
using TaskManagement.Api.Common;
using TaskManagement.Application.Reporting;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;

namespace TaskManagement.Api.Endpoints;

/// <summary>
/// Dashboards and reports (docs/api-design.md §4.12). Any scope of dashboard.view / reports.view lets a member in; what
/// they see is limited per project by the services (FR-RPT-06).
/// </summary>
internal static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/dashboard", async (DashboardService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(ct)))
            .WithTags("Reports")
            .RequirePermission(PermissionCatalog.DashboardView, PermissionScope.Own)
            .WithSummary("dashboard.view — sections depend on what the member may do.");

        var reports = api.MapGroup("/reports").WithTags("Reports").RequirePermission(PermissionCatalog.ReportsView, PermissionScope.Own);
        reports.MapGet("/", async (ReportService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)))
            .WithSummary("reports.view — the reports this member may run and export.");
        reports.MapGet("/{name}", async (
                string name, DateOnly? from, DateOnly? to, Guid? projectId, Guid? userId, string? groupBy, string? format, ReportService service,
                CancellationToken ct) =>
            {
                var query = new ReportQuery(from, to, projectId, userId, groupBy);
                switch (format)
                {
                    case null or "json":
                        return (await service.RunAsync(name, query, ct)).ToHttpResult();
                    case "csv":
                        var file = await service.ExportAsync(name, query, ct);
                        return file.IsSuccess
                            ? TypedResults.File(file.Value!.Content, "text/csv; charset=utf-8", file.Value.FileName)
                            : file.Error!.ToProblem();
                    default:
                        return Error.Validation("report.format", "Use format=csv, or leave it out for JSON.").ToProblem();
                }
            })
            .WithSummary("reports.view (+ reports.export for ?format=csv) — time, allocated-vs-actual, overdue, extensions, revisions, delays.");
        return api;
    }
}
