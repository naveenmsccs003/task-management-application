namespace TaskManagement.Domain.GitLab;

public enum GitLabConnectionStatus
{
    Unknown,
    Healthy,
    Unhealthy,
}

/// <summary>
/// A GitLab instance and the access token used to read it (FR-IMP-01/02). The token is stored only encrypted (Data
/// Protection, keys outside the database) and is never returned; <see cref="TokenHint"/> shows its last four characters.
/// </summary>
public sealed class GitLabConnection
{
    public const int NameMaxLength = 100;
    public const int BaseUrlMaxLength = 300;

    private GitLabConnection()
    {
        Name = string.Empty;
        BaseUrl = string.Empty;
        TokenCiphertext = [];
        TokenHint = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Normalized: scheme and host (and path for GitLab under a sub-path), no trailing slash.</summary>
    public string BaseUrl { get; private set; }

    public byte[] TokenCiphertext { get; private set; }

    public string TokenHint { get; private set; }

    public GitLabConnectionStatus Status { get; private set; }

    public DateTimeOffset? LastCheckedAt { get; private set; }

    /// <summary>A short, secret-free description of the last failure.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static GitLabConnection Create(string name, string baseUrl, byte[] tokenCiphertext, string tokenHint, Guid by, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(tokenCiphertext);
        return new GitLabConnection
        {
            Id = Guid.CreateVersion7(now),
            Name = name.Trim(),
            BaseUrl = baseUrl,
            TokenCiphertext = tokenCiphertext,
            TokenHint = tokenHint,
            Status = GitLabConnectionStatus.Unknown,
            CreatedAt = now,
            CreatedBy = by,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string baseUrl, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        if (baseUrl != BaseUrl)
        {
            Status = GitLabConnectionStatus.Unknown;
        }

        Name = name.Trim();
        BaseUrl = baseUrl;
        UpdatedAt = now;
    }

    public void ReplaceToken(byte[] tokenCiphertext, string tokenHint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tokenCiphertext);
        TokenCiphertext = tokenCiphertext;
        TokenHint = tokenHint;
        Status = GitLabConnectionStatus.Unknown;
        UpdatedAt = now;
    }

    public void RecordCheck(bool healthy, string? error, DateTimeOffset now)
    {
        Status = healthy ? GitLabConnectionStatus.Healthy : GitLabConnectionStatus.Unhealthy;
        LastError = healthy ? null : error;
        LastCheckedAt = now;
    }

    /// <summary>"…abcd" from the token's last four characters (nothing for very short tokens).</summary>
    public static string HintFor(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token.Length >= 12 ? "…" + token[^4..] : "…";
    }
}

/// <summary>
/// An internal project linked to one GitLab project (FR-PROJ-06, FR-IMP-03). The optional webhook secret is stored as a
/// SHA-256 hash only (security.md S18).
/// </summary>
public sealed class GitLabProjectLink
{
    private GitLabProjectLink()
    {
        PathWithNamespace = string.Empty;
        WebUrl = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid ConnectionId { get; private set; }

    public long GitLabProjectId { get; private set; }

    public string PathWithNamespace { get; private set; }

    public string WebUrl { get; private set; }

    public bool SyncEnabled { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    public byte[]? WebhookSecretHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public static GitLabProjectLink Create(
        Guid projectId, Guid connectionId, long gitLabProjectId, string pathWithNamespace, string webUrl, Guid by, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathWithNamespace);
        return new GitLabProjectLink
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            ConnectionId = connectionId,
            GitLabProjectId = gitLabProjectId,
            PathWithNamespace = pathWithNamespace,
            WebUrl = webUrl,
            SyncEnabled = true,
            CreatedAt = now,
            CreatedBy = by,
        };
    }

    public void SetSyncEnabled(bool enabled) => SyncEnabled = enabled;

    public void MarkSynced(DateTimeOffset now) => LastSyncedAt = now;

    public void SetWebhookSecretHash(byte[]? hash) => WebhookSecretHash = hash;
}

public enum GitLabIssueState
{
    Opened,
    Closed,

    /// <summary>The issue can no longer be found in the linked GitLab project (moved or deleted, edge case 29).</summary>
    Missing,
}

/// <summary>Changes made in GitLab after the task left Draft, waiting for a TL to accept them (task-workflow.md §8).</summary>
public sealed record GitLabPendingChanges(string? Title, string? Description)
{
    public bool IsEmpty => Title is null && Description is null;
}

/// <summary>
/// A task imported from a GitLab issue. (connection, GitLab project, issue IID) is unique, which makes imports idempotent
/// (FR-IMP-04). GitLab never changes the task's state (FR-IMP-07); it only sets flags and pending changes.
/// </summary>
public sealed class GitLabIssueLink
{
    private GitLabIssueLink()
    {
        WebUrl = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid ConnectionId { get; private set; }

    public long GitLabProjectId { get; private set; }

    public long IssueIid { get; private set; }

    public long IssueId { get; private set; }

    public string WebUrl { get; private set; }

    public GitLabIssueState State { get; private set; }

    public DateTimeOffset? GitLabUpdatedAt { get; private set; }

    public GitLabPendingChanges? PendingChanges { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public static GitLabIssueLink Create(
        Guid taskId, Guid connectionId, long gitLabProjectId, long issueIid, long issueId, string webUrl, GitLabIssueState state,
        DateTimeOffset? gitLabUpdatedAt, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            TaskId = taskId,
            ConnectionId = connectionId,
            GitLabProjectId = gitLabProjectId,
            IssueIid = issueIid,
            IssueId = issueId,
            WebUrl = webUrl,
            State = state,
            GitLabUpdatedAt = gitLabUpdatedAt,
            LastSeenAt = now,
        };

    /// <summary>Records what GitLab says now; returns the previous state so the caller can react to a change.</summary>
    public GitLabIssueState Observe(GitLabIssueState state, string webUrl, DateTimeOffset? gitLabUpdatedAt, DateTimeOffset now)
    {
        var previous = State;
        State = state;
        if (state != GitLabIssueState.Missing)
        {
            WebUrl = webUrl;
            GitLabUpdatedAt = gitLabUpdatedAt;
            LastSeenAt = now;
        }

        return previous;
    }

    public void SetPendingChanges(GitLabPendingChanges? changes) => PendingChanges = changes is { IsEmpty: false } ? changes : null;
}

public enum ImportRunKind
{
    Import,
    Sync,
    Webhook,
}

/// <summary>One import or sync and its outcome (FR-IMP-08). Error messages never contain tokens.</summary>
public sealed class ImportRun
{
    public const int MaxErrors = 50;

    private ImportRun()
    {
        Errors = [];
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public ImportRunKind Kind { get; private set; }

    /// <summary>Null for scheduled syncs and webhooks.</summary>
    public Guid? StartedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public int CreatedCount { get; private set; }

    public int UpdatedCount { get; private set; }

    public int SkippedCount { get; private set; }

    public int FailedCount { get; private set; }

    public List<string> Errors { get; private set; }

    public static ImportRun Start(Guid projectId, ImportRunKind kind, Guid? startedBy, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            Kind = kind,
            StartedBy = startedBy,
            StartedAt = now,
        };

    public void Created() => CreatedCount++;

    public void Updated() => UpdatedCount++;

    public void Skipped() => SkippedCount++;

    public void Failed(string error)
    {
        FailedCount++;
        if (Errors.Count < MaxErrors)
        {
            Errors.Add(error.Length > 500 ? error[..500] : error);
        }
    }

    public void Finish(DateTimeOffset now) => FinishedAt = now;
}
