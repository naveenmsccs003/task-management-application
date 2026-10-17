using TaskManagement.Domain.Tasks;

namespace TaskManagement.Domain.Tests.Tasks;

public sealed class TaskWorkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Lead = Guid.CreateVersion7();
    private static readonly Guid Developer = Guid.CreateVersion7();

    private static TaskItem AssignedTask(bool algorithmRequired = false)
    {
        var details = new TaskDetails("Build login", null, TaskType.Feature, TaskPriority.High, null, new DateOnly(2026, 11, 1), 120, algorithmRequired, []);
        var task = TaskItem.Create(Guid.CreateVersion7(), "TMA", 1, details, Lead, Now);
        task.Assign(Developer, null, Lead, Now);
        return task;
    }

    [Fact]
    public void AC_ALG_1_start_without_an_approved_algorithm_gives_a_specific_reason()
    {
        var task = AssignedTask(algorithmRequired: true);

        var result = task.Start(Developer, Now);

        Assert.Equal("task.algorithm_approval_required", result.Error!.Code);
        Assert.Equal(TaskItemStatus.AwaitingAlgorithm, task.Status);
    }

    [Fact]
    public void Algorithm_submit_reject_resubmit_approve_moves_through_the_states()
    {
        var task = AssignedTask(algorithmRequired: true);
        var approvedSubmission = Guid.CreateVersion7();

        Assert.True(task.SubmitAlgorithm(Developer, Now).IsSuccess);
        Assert.Equal(TaskItemStatus.AlgorithmSubmitted, task.Status);
        Assert.True(task.RejectAlgorithm(Lead, "Handle lockout", Now).IsSuccess);
        Assert.Equal(TaskItemStatus.AwaitingAlgorithm, task.Status);
        Assert.True(task.SubmitAlgorithm(Developer, Now).IsSuccess);
        Assert.True(task.ApproveAlgorithm(approvedSubmission, Lead, null, Now).IsSuccess);

        Assert.Equal(TaskItemStatus.ReadyToStart, task.Status);
        Assert.Equal(approvedSubmission, task.AlgorithmApprovedSubmissionId);
        Assert.Equal("Handle lockout", task.StatusHistory.Single(h => h.Action == TaskAction.RejectAlgorithm).Comment);
    }

    [Fact]
    public void Start_pause_resume_stop_follow_the_workflow_and_record_the_first_start()
    {
        var task = AssignedTask();

        Assert.True(task.Start(Developer, Now).IsSuccess);
        Assert.True(task.Pause(Developer, "  Lunch ", Now.AddHours(1)).IsSuccess);
        Assert.True(task.Resume(Developer, Now.AddHours(2)).IsSuccess);
        Assert.True(task.Stop(Developer, "End of day", Now.AddHours(5)).IsSuccess);
        Assert.True(task.Start(Developer, Now.AddDays(1)).IsSuccess);

        Assert.Equal(TaskItemStatus.InProgress, task.Status);
        Assert.Equal(Now, task.FirstStartedAt);
        Assert.Equal("Lunch", task.StatusHistory.Single(h => h.Action == TaskAction.Pause).Comment);
        Assert.Equal("End of day", task.StatusHistory.Single(h => h.Action == TaskAction.Stop).Comment);
    }

    [Fact]
    public void Resume_needs_a_paused_task_and_pause_needs_a_running_one()
    {
        var task = AssignedTask();

        Assert.Equal("task.invalid_transition", task.Resume(Developer, Now).Error!.Code);
        Assert.Equal("task.invalid_transition", task.Pause(Developer, null, Now).Error!.Code);
    }

    [Fact]
    public void Algorithm_flag_is_locked_once_work_has_started()
    {
        var task = AssignedTask();
        task.Start(Developer, Now);
        task.Stop(Developer, "Blocked", Now);

        var result = task.UpdateDetails(
            new TaskDetails("Build login", null, TaskType.Feature, TaskPriority.High, null, task.DueDate, 120, true, []), Lead, Now);

        Assert.Equal("task.algorithm_flag_locked", result.Error!.Code);
    }

    [Fact]
    public void The_system_stops_only_running_tasks_and_records_no_actor()
    {
        var running = AssignedTask();
        running.Start(Developer, Now);
        var paused = AssignedTask();
        paused.Start(Developer, Now);
        paused.Pause(Developer, null, Now);

        running.StopBySystem("Work session auto-closed after 10 hours.", Now.AddHours(10));
        paused.StopBySystem("Work session auto-closed after 10 hours.", Now.AddHours(10));

        Assert.Equal(TaskItemStatus.Stopped, running.Status);
        Assert.Null(running.StatusHistory.Last().ActorId);
        Assert.Equal(TaskItemStatus.Paused, paused.Status);
    }
}
