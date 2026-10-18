using TaskManagement.Domain.GitLab;

namespace TaskManagement.Domain.Tests.GitLab;

public sealed class GitLabTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("glpat-abcdefghijkl1234", "…1234")]
    [InlineData("short", "…")]
    public void Token_hints_show_at_most_the_last_four_characters(string token, string hint) =>
        Assert.Equal(hint, GitLabConnection.HintFor(token));

    [Fact]
    public void A_new_address_or_token_makes_the_connection_untested_again()
    {
        var connection = GitLabConnection.Create("Main", "https://gitlab.com", [1], "…1234", Guid.NewGuid(), Now);
        connection.RecordCheck(true, null, Now);
        connection.Update("Main", "https://gitlab.com", Now);
        Assert.Equal(GitLabConnectionStatus.Healthy, connection.Status);
        connection.Update("Main", "https://gitlab.example.com", Now);
        Assert.Equal(GitLabConnectionStatus.Unknown, connection.Status);
        connection.RecordCheck(false, "refused", Now);
        Assert.Equal("refused", connection.LastError);
        connection.ReplaceToken([2], "…9999", Now);
        Assert.Equal(GitLabConnectionStatus.Unknown, connection.Status);
    }

    [Fact]
    public void Observing_reports_the_previous_state_and_a_missing_issue_keeps_its_last_known_details()
    {
        var link = GitLabIssueLink.Create(Guid.NewGuid(), Guid.NewGuid(), 5, 1, 100, "https://gitlab.com/a/b/-/issues/1", GitLabIssueState.Opened, Now, Now);
        Assert.Equal(GitLabIssueState.Opened, link.Observe(GitLabIssueState.Closed, "https://gitlab.com/a/b/-/issues/1", Now.AddMinutes(1), Now));
        Assert.Equal(GitLabIssueState.Closed, link.Observe(GitLabIssueState.Missing, "ignored", null, Now.AddHours(1)));
        Assert.Equal("https://gitlab.com/a/b/-/issues/1", link.WebUrl);
        Assert.Equal(Now, link.LastSeenAt);
        Assert.Equal(GitLabIssueState.Missing, link.State);
    }

    [Fact]
    public void Empty_pending_changes_are_not_kept()
    {
        var link = GitLabIssueLink.Create(Guid.NewGuid(), Guid.NewGuid(), 5, 1, 100, "u", GitLabIssueState.Opened, Now, Now);
        link.SetPendingChanges(new GitLabPendingChanges(null, null));
        Assert.Null(link.PendingChanges);
        link.SetPendingChanges(new GitLabPendingChanges("New title", null));
        Assert.Equal("New title", link.PendingChanges!.Title);
    }

    [Fact]
    public void Import_runs_count_outcomes_and_cap_their_errors()
    {
        var run = ImportRun.Start(Guid.NewGuid(), ImportRunKind.Import, null, Now);
        run.Created();
        run.Updated();
        run.Skipped();
        for (var i = 0; i < ImportRun.MaxErrors + 5; i++)
        {
            run.Failed(new string('e', 600));
        }

        Assert.Equal((1, 1, 1, ImportRun.MaxErrors + 5), (run.CreatedCount, run.UpdatedCount, run.SkippedCount, run.FailedCount));
        Assert.Equal(ImportRun.MaxErrors, run.Errors.Count);
        Assert.All(run.Errors, e => Assert.Equal(500, e.Length));
    }
}
