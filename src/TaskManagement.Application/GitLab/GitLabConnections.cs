using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.GitLab;

namespace TaskManagement.Application.GitLab;

/// <summary>A connection as the API shows it: never the token, only its last characters (AC-IMP-3).</summary>
public sealed record GitLabConnectionDto(
    Guid Id,
    string Name,
    string BaseUrl,
    string TokenHint,
    GitLabConnectionStatus Status,
    DateTimeOffset? LastCheckedAt,
    string? LastError,
    int LinkedProjects,
    uint Version);

/// <summary>Name and status only, for members who import issues but do not manage connections.</summary>
public sealed record GitLabConnectionOptionDto(Guid Id, string Name, GitLabConnectionStatus Status);

/// <summary><paramref name="Token"/> is required on create; on update it replaces the stored one only when given.</summary>
public sealed record SaveGitLabConnectionCommand(string? Name, string? BaseUrl, string? Token, uint? Version);

/// <summary>
/// GitLab connections (FR-IMP-01/02), managed by members with <c>gitlab.connections.manage</c> (Admins). Tokens are
/// write-only: they are encrypted before they are stored and never returned, logged or audited.
/// </summary>
public sealed class GitLabConnections(
    IAppDbContext db, ICurrentUser currentUser, ProjectAccess projects, ITokenProtector protector, IGitLabClient gitLab,
    IOptions<GitLabOptions> options, IAuditLog auditLog, TimeProvider timeProvider)
{
    public const int TokenMaxLength = 500;

    public async Task<IReadOnlyList<GitLabConnectionDto>> ListAsync(CancellationToken cancellationToken)
    {
        var connections = await db.GitLabConnections.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
        var counts = await db.GitLabProjectLinks.AsNoTracking().GroupBy(l => l.ConnectionId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return connections.Select(c => ToDto(c, counts.GetValueOrDefault(c.Id))).ToList();
    }

    /// <summary>For the import screens: anyone with gitlab.import somewhere may pick a connection by name.</summary>
    public async Task<Result<IReadOnlyList<GitLabConnectionOptionDto>>> OptionsAsync(CancellationToken cancellationToken)
    {
        var scope = await projects.ProjectsWithAsync(PermissionCatalog.GitLabImport, cancellationToken);
        if (!scope.Everywhere && scope.ProjectWide.Count == 0)
        {
            return CommonErrors.Forbidden("import GitLab issues");
        }

        IReadOnlyList<GitLabConnectionOptionDto> result = await db.GitLabConnections.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new GitLabConnectionOptionDto(c.Id, c.Name, c.Status)).ToListAsync(cancellationToken);
        return Result.Success(result);
    }

    public async Task<Result<GitLabConnectionDto>> CreateAsync(SaveGitLabConnectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (errors, baseUrl) = Validate(command, tokenRequired: true);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var token = command.Token!.Trim();
        var connection = GitLabConnection.Create(
            command.Name!, baseUrl!, protector.Protect(token), GitLabConnection.HintFor(token), currentUser.RequiredUserId, timeProvider.GetUtcNow());
        db.GitLabConnections.Add(connection);
        auditLog.Record("gitlab.connection_created", "GitLabConnection", connection.Id.ToString(), new AuditChanges()
            .Set("name", connection.Name)
            .Set("baseUrl", connection.BaseUrl));
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(connection, 0);
    }

    public async Task<Result<GitLabConnectionDto>> UpdateAsync(Guid id, SaveGitLabConnectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var connection = await db.GitLabConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
        {
            return CommonErrors.NotFound("GitLab_connection");
        }

        if (command.Version is { } version && version != connection.Version)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var (errors, baseUrl) = Validate(command, tokenRequired: false);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var now = timeProvider.GetUtcNow();
        var changes = new AuditChanges()
            .Change("name", connection.Name, command.Name!.Trim())
            .Change("baseUrl", connection.BaseUrl, baseUrl);
        connection.Update(command.Name!, baseUrl!, now);
        if (!string.IsNullOrWhiteSpace(command.Token))
        {
            var token = command.Token.Trim();
            connection.ReplaceToken(protector.Protect(token), GitLabConnection.HintFor(token), now);
            changes.Set("tokenReplaced", true);
        }

        auditLog.Record("gitlab.connection_updated", "GitLabConnection", connection.Id.ToString(), changes);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        return ToDto(connection, await db.GitLabProjectLinks.CountAsync(l => l.ConnectionId == id, cancellationToken));
    }

    /// <summary>A connection still linked to a project cannot be deleted; unlink the projects first.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var connection = await db.GitLabConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
        {
            return CommonErrors.NotFound("GitLab_connection");
        }

        if (await db.GitLabProjectLinks.AnyAsync(l => l.ConnectionId == id, cancellationToken)
            || await db.GitLabIssueLinks.AnyAsync(l => l.ConnectionId == id, cancellationToken))
        {
            return Error.Conflict("gitlab.connection_in_use", "Projects or tasks are still linked through this connection. Unlink the projects first.");
        }

        db.GitLabConnections.Remove(connection);
        auditLog.Record("gitlab.connection_deleted", "GitLabConnection", connection.Id.ToString(), new AuditChanges().Set("name", connection.Name));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Calls GitLab with the stored token and records the outcome on the connection.</summary>
    public async Task<Result<GitLabConnectionDto>> TestAsync(Guid id, CancellationToken cancellationToken)
    {
        var connection = await db.GitLabConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
        {
            return CommonErrors.NotFound("GitLab_connection");
        }

        try
        {
            await gitLab.GetCurrentUserAsync(EndpointFor(connection), cancellationToken);
            connection.RecordCheck(true, null, timeProvider.GetUtcNow());
        }
        catch (GitLabException exception)
        {
            connection.RecordCheck(false, exception.Message, timeProvider.GetUtcNow());
        }

        auditLog.Record("gitlab.connection_tested", "GitLabConnection", connection.Id.ToString(), new AuditChanges().Set("status", connection.Status));
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(connection, await db.GitLabProjectLinks.CountAsync(l => l.ConnectionId == id, cancellationToken));
    }

    public GitLabEndpoint EndpointFor(GitLabConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new GitLabEndpoint(connection.BaseUrl, protector.Unprotect(connection.TokenCiphertext));
    }

    private (FieldErrors Errors, string? BaseUrl) Validate(SaveGitLabConnectionCommand command, bool tokenRequired)
    {
        var errors = new FieldErrors()
            .Required("name", command.Name, "A name")
            .MaxLength("name", command.Name?.Trim(), GitLabConnection.NameMaxLength, "The name")
            .MaxLength("token", command.Token?.Trim(), TokenMaxLength, "The token");
        if (tokenRequired)
        {
            errors.Required("token", command.Token, "An access token");
        }

        var baseUrl = GitLabUrls.Normalize(command.BaseUrl, options.Value, out var urlError);
        if (urlError is not null)
        {
            errors.Add("baseUrl", urlError);
        }
        else if (baseUrl!.Length > GitLabConnection.BaseUrlMaxLength)
        {
            errors.Add("baseUrl", "The address is too long.");
        }

        return (errors, baseUrl);
    }

    private static GitLabConnectionDto ToDto(GitLabConnection c, int linkedProjects) =>
        new(c.Id, c.Name, c.BaseUrl, c.TokenHint, c.Status, c.LastCheckedAt, c.LastError, linkedProjects, c.Version);
}
