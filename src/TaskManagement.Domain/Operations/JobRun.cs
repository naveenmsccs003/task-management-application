namespace TaskManagement.Domain.Operations;

/// <summary>
/// The latest run of one background job, for the Admin system status page (FR-ADM-01). One row per job, updated on
/// every run; failures keep the time of the last success so a stuck job is easy to spot.
/// </summary>
public sealed class JobRun
{
    public const int NameMaxLength = 100;
    public const int MessageMaxLength = 1_000;

    private JobRun()
    {
        JobName = string.Empty;
    }

    public string JobName { get; private set; }

    public DateTimeOffset LastStartedAt { get; private set; }

    public DateTimeOffset LastFinishedAt { get; private set; }

    public bool LastSucceeded { get; private set; }

    public DateTimeOffset? LastSucceededAt { get; private set; }

    /// <summary>A short summary (e.g. "2 sessions closed") or the error type; never secrets.</summary>
    public string? LastMessage { get; private set; }

    public static JobRun Create(string jobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        return new JobRun { JobName = jobName };
    }

    public void Record(DateTimeOffset startedAt, DateTimeOffset finishedAt, bool succeeded, string? message)
    {
        LastStartedAt = startedAt;
        LastFinishedAt = finishedAt;
        LastSucceeded = succeeded;
        if (succeeded)
        {
            LastSucceededAt = finishedAt;
        }

        LastMessage = message is { Length: > MessageMaxLength } ? message[..MessageMaxLength] : message;
    }
}
