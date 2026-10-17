using TaskManagement.Domain.Projects;

namespace TaskManagement.Domain.Tests.Projects;

public sealed class ProjectTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static Project NewProject() => Project.Create("TMA", "  Task app ", null, null, null, false, false, Actor, Now);

    [Theory]
    [InlineData("TMA", true)]
    [InlineData("WEB2", true)]
    [InlineData("AB", true)]
    [InlineData("ABCDEFGHIJ", true)]
    [InlineData("A", false)]
    [InlineData("ABCDEFGHIJK", false)]
    [InlineData("tma", false)]
    [InlineData("2WEB", false)]
    [InlineData("TM-A", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Key_must_be_2_to_10_upper_case_letters_or_digits_starting_with_a_letter(string? key, bool valid) =>
        Assert.Equal(valid, Project.IsValidKey(key));

    [Fact]
    public void Create_starts_active_and_trims_the_name()
    {
        var project = NewProject();

        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal("Task app", project.Name);
        Assert.True(project.AcceptsNewTasks);
        Assert.False(project.IsReadOnly);
    }

    [Fact]
    public void Create_rejects_an_invalid_key() =>
        Assert.Throws<ArgumentException>(() => Project.Create("bad key", "Name", null, null, null, false, false, Actor, Now));

    [Theory]
    [InlineData(ProjectStatus.Active, ProjectStatus.OnHold, true)]
    [InlineData(ProjectStatus.Active, ProjectStatus.Completed, true)]
    [InlineData(ProjectStatus.Active, ProjectStatus.Archived, true)]
    [InlineData(ProjectStatus.OnHold, ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.OnHold, ProjectStatus.Completed, true)]
    [InlineData(ProjectStatus.Completed, ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Completed, ProjectStatus.Archived, true)]
    [InlineData(ProjectStatus.Archived, ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Active, ProjectStatus.Active, false)]
    [InlineData(ProjectStatus.Completed, ProjectStatus.OnHold, false)]
    [InlineData(ProjectStatus.Archived, ProjectStatus.Completed, false)]
    [InlineData(ProjectStatus.Archived, ProjectStatus.OnHold, false)]
    public void Status_changes_follow_the_allowed_transitions(ProjectStatus from, ProjectStatus to, bool allowed)
    {
        var project = NewProject();
        if (from != ProjectStatus.Active)
        {
            Assert.True(project.ChangeStatus(from, Actor, Now).IsSuccess);
        }

        var result = project.ChangeStatus(to, Actor, Now);

        Assert.Equal(allowed, result.IsSuccess);
        Assert.Equal(allowed ? to : from, project.Status);
        if (!allowed)
        {
            Assert.Equal("project.invalid_status_change", result.Error!.Code);
        }
    }

    [Theory]
    [InlineData(ProjectStatus.Completed)]
    [InlineData(ProjectStatus.Archived)]
    public void Completed_and_archived_projects_are_read_only(ProjectStatus status)
    {
        var project = NewProject();
        project.ChangeStatus(status, Actor, Now);

        var result = project.UpdateDetails("New name", null, null, null, true, true, Actor, Now);

        Assert.True(project.IsReadOnly);
        Assert.False(project.AcceptsNewTasks);
        Assert.Equal("project.read_only", result.Error!.Code);
        Assert.Equal("Task app", project.Name);
    }

    [Fact]
    public void On_hold_projects_can_be_edited_but_take_no_new_tasks()
    {
        var project = NewProject();
        project.ChangeStatus(ProjectStatus.OnHold, Actor, Now);

        Assert.True(project.UpdateDetails("Renamed", "Desc", null, null, true, false, Actor, Now).IsSuccess);
        Assert.False(project.AcceptsNewTasks);
        Assert.Equal("Renamed", project.Name);
    }

    [Fact]
    public void Removing_a_member_keeps_the_row_as_history()
    {
        var member = ProjectMember.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Actor, Now);

        member.Remove(Actor, Now.AddDays(1));
        member.Remove(Guid.CreateVersion7(), Now.AddDays(2));

        Assert.False(member.IsCurrent);
        Assert.Equal(Now.AddDays(1), member.RemovedAt);
        Assert.Equal(Actor, member.RemovedBy);
    }
}
