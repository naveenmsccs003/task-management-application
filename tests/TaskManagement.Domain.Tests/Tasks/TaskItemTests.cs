using TaskManagement.Domain.Tasks;

namespace TaskManagement.Domain.Tests.Tasks;

public sealed class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Lead = Guid.CreateVersion7();
    private static readonly Guid Developer = Guid.CreateVersion7();
    private static readonly DateOnly Due = new(2026, 11, 1);

    private static TaskDetails Details(bool algorithmRequired = false, DateOnly? dueDate = null, IReadOnlyList<string>? labels = null) =>
        new(" Build login ", "Body", TaskType.Feature, TaskPriority.High, null, dueDate ?? Due, 480, algorithmRequired, labels ?? []);

    private static TaskItem NewTask(bool algorithmRequired = false, DateOnly? dueDate = null) =>
        TaskItem.Create(Guid.CreateVersion7(), "TMA", 42, Details(algorithmRequired, dueDate), Lead, Now);

    [Fact]
    public void Create_makes_a_draft_with_a_key_revision_zero_and_a_creation_history_entry()
    {
        var task = NewTask();

        Assert.Equal("TMA-42", task.Key);
        Assert.Equal("Build login", task.Title);
        Assert.Equal(TaskItemStatus.Draft, task.Status);
        Assert.Equal(0, task.CurrentRevision);
        Assert.Null(task.AssigneeId);
        var entry = Assert.Single(task.StatusHistory);
        Assert.Null(entry.FromStatus);
        Assert.Equal(TaskItemStatus.Draft, entry.ToStatus);
        Assert.Equal(TaskAction.Create, entry.Action);
    }

    [Fact]
    public void Labels_are_trimmed_and_deduplicated_case_insensitively()
    {
        var labels = TaskItem.NormalizeLabels([" backend ", "Backend", "", null, "ui"]);

        Assert.Equal(["backend", "ui"], labels);
    }

    [Theory]
    [InlineData(false, TaskItemStatus.ReadyToStart)]
    [InlineData(true, TaskItemStatus.AwaitingAlgorithm)]
    public void Assign_moves_a_draft_to_the_next_state_and_records_history(bool algorithmRequired, TaskItemStatus expected)
    {
        var task = NewTask(algorithmRequired);

        var result = task.Assign(Developer, null, Lead, Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, task.Status);
        Assert.Equal(Developer, task.AssigneeId);
        Assert.Equal(Due, task.OriginalDueDate);
        var assignment = Assert.Single(task.Assignments);
        Assert.Equal(AssignmentKind.Initial, assignment.Kind);
        Assert.Null(assignment.FromUserId);
        Assert.Equal(Developer, assignment.ToUserId);
        Assert.Equal(TaskAction.Assign, task.StatusHistory.Last().Action);
    }

    [Fact]
    public void Assign_requires_a_due_date_which_can_be_given_with_the_assignment()
    {
        var task = TaskItem.Create(Guid.CreateVersion7(), "TMA", 1, Details() with { DueDate = null }, Lead, Now);

        var withoutDate = task.Assign(Developer, null, Lead, Now);
        var withDate = task.Assign(Developer, Due, Lead, Now);

        Assert.Equal("task.due_date_required", withoutDate.Error!.Code);
        Assert.Contains("dueDate", withoutDate.Error.FieldErrors!.Keys);
        Assert.True(withDate.IsSuccess);
        Assert.Equal(Due, task.DueDate);
    }

    [Fact]
    public void A_task_can_only_be_assigned_once_later_changes_are_reassignments()
    {
        var task = NewTask();
        task.Assign(Developer, null, Lead, Now);

        var again = task.Assign(Guid.CreateVersion7(), null, Lead, Now);

        Assert.Equal("task.invalid_transition", again.Error!.Code);
        Assert.Contains("Ready to start", again.Error.Message, StringComparison.Ordinal);
        Assert.Equal(Developer, task.AssigneeId);
    }

    [Fact]
    public void Due_date_is_editable_only_before_assignment()
    {
        var task = NewTask();
        Assert.True(task.UpdateDetails(Details(dueDate: Due.AddDays(3)), Lead, Now).IsSuccess);
        task.Assign(Developer, null, Lead, Now);

        var result = task.UpdateDetails(Details(dueDate: Due.AddDays(10)), Lead, Now);

        Assert.Equal("task.due_date_locked", result.Error!.Code);
        Assert.Equal(Due.AddDays(3), task.DueDate);
    }

    [Fact]
    public void Turning_the_algorithm_flag_on_or_off_after_assignment_moves_between_awaiting_and_ready()
    {
        var task = NewTask();
        task.Assign(Developer, null, Lead, Now);

        Assert.True(task.UpdateDetails(Details(algorithmRequired: true), Lead, Now).IsSuccess);
        Assert.Equal(TaskItemStatus.AwaitingAlgorithm, task.Status);
        Assert.True(task.UpdateDetails(Details(algorithmRequired: false), Lead, Now).IsSuccess);
        Assert.Equal(TaskItemStatus.ReadyToStart, task.Status);
        Assert.Equal(2, task.StatusHistory.Count(h => h.Action == TaskAction.Update));
    }

    [Fact]
    public void Cancel_requires_a_non_final_task_and_keeps_the_reason()
    {
        var task = NewTask();

        var first = task.Cancel("  Duplicate of TMA-7 ", Lead, Now);
        var second = task.Cancel("Again", Lead, Now);

        Assert.True(first.IsSuccess);
        Assert.Equal(TaskItemStatus.Cancelled, task.Status);
        Assert.Equal("Duplicate of TMA-7", task.CancelReason);
        Assert.Equal("Duplicate of TMA-7", task.StatusHistory.Last().Comment);
        Assert.Equal("task.invalid_transition", second.Error!.Code);
    }

    [Fact]
    public void Cancelled_tasks_cannot_be_edited()
    {
        var task = NewTask();
        task.Cancel("No longer needed", Lead, Now);

        var result = task.UpdateDetails(Details() with { Title = "Changed" }, Lead, Now);

        Assert.Equal("task.read_only", result.Error!.Code);
        Assert.Equal("Build login", task.Title);
    }
}
