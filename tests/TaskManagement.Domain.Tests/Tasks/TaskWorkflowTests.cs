using TaskManagement.Domain.Tasks;

namespace TaskManagement.Domain.Tests.Tasks;

public sealed class TaskWorkflowTests
{
    [Theory]
    [InlineData(TaskItemStatus.Draft, TaskAction.Assign)]
    [InlineData(TaskItemStatus.AwaitingAlgorithm, TaskAction.SubmitAlgorithm)]
    [InlineData(TaskItemStatus.AlgorithmSubmitted, TaskAction.ApproveAlgorithm)]
    [InlineData(TaskItemStatus.AlgorithmSubmitted, TaskAction.RejectAlgorithm)]
    [InlineData(TaskItemStatus.ReadyToStart, TaskAction.Start)]
    [InlineData(TaskItemStatus.InProgress, TaskAction.Pause)]
    [InlineData(TaskItemStatus.InProgress, TaskAction.Stop)]
    [InlineData(TaskItemStatus.InProgress, TaskAction.SubmitReview)]
    [InlineData(TaskItemStatus.Paused, TaskAction.Resume)]
    [InlineData(TaskItemStatus.Paused, TaskAction.Stop)]
    [InlineData(TaskItemStatus.Stopped, TaskAction.Start)]
    [InlineData(TaskItemStatus.Stopped, TaskAction.SubmitReview)]
    [InlineData(TaskItemStatus.InReview, TaskAction.ApproveReview)]
    [InlineData(TaskItemStatus.InReview, TaskAction.RequestRevision)]
    [InlineData(TaskItemStatus.RevisionRequested, TaskAction.Start)]
    [InlineData(TaskItemStatus.Approved, TaskAction.Close)]
    [InlineData(TaskItemStatus.Closed, TaskAction.Reopen)]
    public void Transitions_from_the_workflow_document_are_allowed(TaskItemStatus status, TaskAction action) =>
        Assert.True(TaskWorkflow.IsAllowed(status, action));

    [Theory]
    [InlineData(TaskItemStatus.Draft, TaskAction.Start)]
    [InlineData(TaskItemStatus.ReadyToStart, TaskAction.Assign)]
    [InlineData(TaskItemStatus.InReview, TaskAction.Start)]
    [InlineData(TaskItemStatus.Approved, TaskAction.Reopen)]
    [InlineData(TaskItemStatus.Closed, TaskAction.Cancel)]
    [InlineData(TaskItemStatus.Cancelled, TaskAction.Reopen)]
    public void Other_transitions_are_rejected(TaskItemStatus status, TaskAction action) =>
        Assert.False(TaskWorkflow.IsAllowed(status, action));

    [Fact]
    public void Every_non_final_state_can_be_cancelled_and_final_states_cannot()
    {
        foreach (var status in Enum.GetValues<TaskItemStatus>())
        {
            Assert.Equal(!TaskWorkflow.IsFinal(status), TaskWorkflow.IsAllowed(status, TaskAction.Cancel));
        }
    }

    [Fact]
    public void Open_statuses_exclude_approved_closed_and_cancelled() =>
        Assert.Equal(
            [TaskItemStatus.Approved, TaskItemStatus.Closed, TaskItemStatus.Cancelled],
            Enum.GetValues<TaskItemStatus>().Except(TaskWorkflow.OpenStatuses));

    [Fact]
    public void Invalid_transition_error_names_the_state_and_the_allowed_actions()
    {
        var error = TaskWorkflow.InvalidTransition("TMA-42", TaskItemStatus.InReview, TaskAction.Start);

        Assert.Equal("task.invalid_transition", error.Code);
        Assert.Equal("Task TMA-42 is 'In review', so 'start' is not allowed. Allowed actions: approve, request-revision, cancel.", error.Message);
    }
}
