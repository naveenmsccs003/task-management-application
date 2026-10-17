using TaskManagement.Domain.Tasks;

namespace TaskManagement.Domain.Tests.Tasks;

public sealed class ReviewAndRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Lead = Guid.CreateVersion7();
    private static readonly Guid Manager = Guid.CreateVersion7();
    private static readonly Guid Developer = Guid.CreateVersion7();
    private static readonly DateOnly Due = new(2026, 11, 1);

    private static TaskItem InReview(bool algorithmRequired = false)
    {
        var details = new TaskDetails("Build login", null, TaskType.Feature, TaskPriority.High, null, Due, 120, algorithmRequired, []);
        var task = TaskItem.Create(Guid.CreateVersion7(), "TMA", 1, details, Lead, Now);
        task.Assign(Developer, null, Lead, Now);
        if (algorithmRequired)
        {
            task.SubmitAlgorithm(Developer, Now);
            task.ApproveAlgorithm(Guid.CreateVersion7(), Lead, null, Now);
        }

        task.Start(Developer, Now);
        Assert.True(task.SubmitReview(Developer, Now).IsSuccess);
        return task;
    }

    [Fact]
    public void Approval_records_the_revision_and_closing_needs_it()
    {
        var task = InReview();

        Assert.True(task.ApproveReview(Lead, "Good", closeOnApproval: false, Now).IsSuccess);
        Assert.Equal(0, task.ApprovedRevision);
        Assert.True(task.Close(Manager, null, Now).IsSuccess);
        Assert.Equal(TaskItemStatus.Closed, task.Status);
        Assert.Equal(Manager, task.ClosedBy);
    }

    [Fact]
    public void Close_on_approval_closes_straight_away()
    {
        var task = InReview();

        task.ApproveReview(Lead, null, closeOnApproval: true, Now);

        Assert.Equal(TaskItemStatus.Closed, task.Status);
        Assert.Equal([TaskAction.ApproveReview, TaskAction.Close], task.StatusHistory.TakeLast(2).Select(h => h.Action));
    }

    [Fact]
    public void A_revision_request_starts_a_new_revision_and_resets_allocation_notifications()
    {
        var task = InReview();
        task.CheckAllocation(120 * 60, 80);

        Assert.True(task.RequestRevision(Lead, "Add tests", algorithmReapprovalRequired: false, Now).IsSuccess);

        Assert.Equal(TaskItemStatus.RevisionRequested, task.Status);
        Assert.Equal(1, task.CurrentRevision);
        Assert.Equal(AllocationAlert.Reached, task.CheckAllocation(120 * 60, 80)); // WQ2: notifies again in the new revision
        Assert.Equal(1, task.StatusHistory.Last().Revision);
    }

    [Fact]
    public void Re_approval_on_revision_sends_the_task_back_to_awaiting_algorithm()
    {
        var task = InReview(algorithmRequired: true);

        task.RequestRevision(Lead, "New approach", algorithmReapprovalRequired: true, Now);

        Assert.Equal(TaskItemStatus.AwaitingAlgorithm, task.Status);
        Assert.Null(task.AlgorithmApprovedSubmissionId);
    }

    [Fact]
    public void Reopening_a_closed_task_starts_a_new_revision()
    {
        var task = InReview();
        task.ApproveReview(Lead, null, closeOnApproval: true, Now);

        Assert.True(task.Reopen(Manager, "Bug found", Now).IsSuccess);

        Assert.Equal(TaskItemStatus.RevisionRequested, task.Status);
        Assert.Equal(1, task.CurrentRevision);
        Assert.Null(task.ClosedAt);
    }

    [Theory]
    [InlineData(false, false, TaskItemStatus.ReadyToStart)]
    [InlineData(false, true, TaskItemStatus.Stopped)]
    [InlineData(true, false, TaskItemStatus.AwaitingAlgorithm)]
    public void Reassignment_picks_the_next_state_from_what_has_happened(bool reapproval, bool started, TaskItemStatus expected)
    {
        var details = new TaskDetails("Build login", null, TaskType.Feature, TaskPriority.High, null, Due, null, true, []);
        var task = TaskItem.Create(Guid.CreateVersion7(), "TMA", 1, details, Lead, Now);
        task.Assign(Developer, null, Lead, Now);
        task.SubmitAlgorithm(Developer, Now);
        task.ApproveAlgorithm(Guid.CreateVersion7(), Lead, null, Now);
        if (started)
        {
            task.Start(Developer, Now);
        }

        var newDeveloper = Guid.CreateVersion7();
        Assert.True(task.Reassign(newDeveloper, "Moving to another dev", reapproval, Manager, Now).IsSuccess);

        Assert.Equal(expected, task.Status);
        Assert.Equal(newDeveloper, task.AssigneeId);
        var assignment = task.Assignments.Last();
        Assert.Equal((AssignmentKind.Reassignment, (Guid?)Developer), (assignment.Kind, assignment.FromUserId));
    }

    [Fact]
    public void Tasks_in_review_cannot_be_reassigned()
    {
        var task = InReview();

        Assert.Equal("task.cannot_reassign", task.Reassign(Guid.CreateVersion7(), "Too late now", false, Manager, Now).Error!.Code);
    }

    [Fact]
    public void Requests_are_decided_once_and_never_by_the_requester()
    {
        var request = ApprovalRequest.DeadlineExtension(Guid.CreateVersion7(), Due, Due.AddDays(7), "Waiting for the API team", Developer, Now);

        Assert.Equal("request.own_request", request.Approve(Developer, null, null, Now).Error!.Code);
        Assert.True(request.Approve(Lead, "OK", Due.AddDays(3), Now).IsSuccess);
        Assert.Equal(Due.AddDays(3), request.ApprovedDueDate);
        Assert.Equal("request.not_pending", request.Reject(Manager, "No", Now).Error!.Code);
        Assert.Equal("request.not_pending", request.Withdraw(Developer, Now).Error!.Code);
    }

    [Fact]
    public void Auto_cancel_only_affects_pending_requests()
    {
        var pending = ApprovalRequest.Reassignment(Guid.CreateVersion7(), Guid.CreateVersion7(), false, "Rebalancing work", Lead, Now);
        var rejected = ApprovalRequest.Reassignment(Guid.CreateVersion7(), Guid.CreateVersion7(), false, "Rebalancing work", Lead, Now);
        rejected.Reject(Manager, "No", Now);

        pending.AutoCancel("Task cancelled", Now);
        rejected.AutoCancel("Task cancelled", Now);

        Assert.Equal(ApprovalRequestStatus.AutoCancelled, pending.Status);
        Assert.Equal(ApprovalRequestStatus.Rejected, rejected.Status);
    }

    [Fact]
    public void Overdue_and_due_soon_notices_follow_the_local_date_and_are_sent_once()
    {
        var details = new TaskDetails("Build login", null, TaskType.Feature, TaskPriority.High, null, Due, null, false, []);
        var task = TaskItem.Create(Guid.CreateVersion7(), "TMA", 1, details, Lead, Now);
        task.Assign(Developer, null, Lead, Now);

        Assert.False(task.TakeDueSoonNotification(Due.AddDays(-2)));
        Assert.True(task.TakeDueSoonNotification(Due.AddDays(-1)));
        Assert.False(task.TakeDueSoonNotification(Due));
        Assert.False(task.RefreshOverdue(Due));
        Assert.True(task.RefreshOverdue(Due.AddDays(1)));
        Assert.True(task.TakeOverdueNotification(Due.AddDays(1)));
        Assert.False(task.TakeOverdueNotification(Due.AddDays(1)));
        Assert.True(task.TakeOverdueNotification(Due.AddDays(2)));

        task.ExtendDueDate(Due.AddDays(10), Lead, Now);
        Assert.True(task.RefreshOverdue(Due.AddDays(2)));
        Assert.False(task.IsOverdue);
    }
}
