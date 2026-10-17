using TaskManagement.Domain.Files;

namespace TaskManagement.Domain.Tests.Files;

public sealed class AttachmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static Attachment Create(AttachmentOwnerType ownerType = AttachmentOwnerType.Comment, bool scanned = false) =>
        Attachment.Create(
            Guid.NewGuid(), TaskId, ownerType, ownerType == AttachmentOwnerType.Task ? TaskId : null, "a.pdf", "application/pdf", "pdf", 10,
            new byte[32], "2026/10/0123456789abcdef0123456789abcdef", scanned, UserId, Now);

    [Fact]
    public void New_files_wait_for_the_scan_unless_scanning_is_off()
    {
        Assert.Equal(AttachmentScanStatus.Pending, Create().ScanStatus);
        Assert.Equal(AttachmentScanStatus.Clean, Create(scanned: true).ScanStatus);
    }

    [Fact]
    public void Task_files_are_owned_by_their_task_and_others_start_as_drafts()
    {
        Assert.False(Create(AttachmentOwnerType.Task).IsDraft);
        Assert.True(Create().IsDraft);
        Assert.Throws<ArgumentException>(() => Attachment.Create(
            Guid.NewGuid(), TaskId, AttachmentOwnerType.Task, Guid.NewGuid(), "a.pdf", "application/pdf", "pdf", 10, new byte[32], "k", false, UserId, Now));
    }

    [Fact]
    public void Only_the_uploader_links_a_draft_once_to_the_same_kind_of_record_on_the_same_task()
    {
        var draft = Create();
        Assert.True(draft.LinkTo(AttachmentOwnerType.Algorithm, Guid.NewGuid(), TaskId, UserId).IsFailure);
        Assert.True(draft.LinkTo(AttachmentOwnerType.Comment, Guid.NewGuid(), Guid.NewGuid(), UserId).IsFailure);
        Assert.True(draft.LinkTo(AttachmentOwnerType.Comment, Guid.NewGuid(), TaskId, Guid.NewGuid()).IsFailure);

        var commentId = Guid.NewGuid();
        Assert.True(draft.LinkTo(AttachmentOwnerType.Comment, commentId, TaskId, UserId).IsSuccess);
        Assert.Equal(commentId, draft.OwnerId);
        Assert.Equal("attachment.not_linkable", draft.LinkTo(AttachmentOwnerType.Comment, Guid.NewGuid(), TaskId, UserId).Error!.Code);
    }

    [Fact]
    public void Scan_errors_are_retried_then_the_file_is_marked_failed()
    {
        var attachment = Create();
        for (var attempt = 1; attempt < Attachment.MaxScanAttempts; attempt++)
        {
            attachment.RecordScanError(Now);
            Assert.Equal(AttachmentScanStatus.Pending, attachment.ScanStatus);
        }

        attachment.RecordScanError(Now);
        Assert.Equal(AttachmentScanStatus.Failed, attachment.ScanStatus);
    }

    [Fact]
    public void Deleting_is_soft_and_keeps_the_first_deletion()
    {
        var attachment = Create(AttachmentOwnerType.Task);
        var first = Guid.NewGuid();
        attachment.Delete(first, Now);
        attachment.Delete(Guid.NewGuid(), Now.AddHours(1));
        Assert.True(attachment.IsDeleted);
        Assert.Equal(first, attachment.DeletedBy);
        Assert.Equal(Now, attachment.DeletedAt);
    }
}
