using TaskManagement.Domain.Tasks;

namespace TaskManagement.Domain.Tests.Tasks;

public sealed class TaskCommentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Author = Guid.CreateVersion7();

    private static TaskComment NewComment() => TaskComment.Create(Guid.CreateVersion7(), Author, " First version ", Now);

    [Fact]
    public void Author_can_edit_within_15_minutes_and_the_previous_text_is_kept()
    {
        var comment = NewComment();

        var result = comment.Edit("Second version", Author, Now.AddMinutes(15));

        Assert.True(result.IsSuccess);
        Assert.Equal("Second version", comment.Body);
        Assert.Equal(Now.AddMinutes(15), comment.EditedAt);
        Assert.Equal("First version", Assert.Single(comment.Edits).PreviousBody);
    }

    [Fact]
    public void Editing_after_15_minutes_is_rejected()
    {
        var comment = NewComment();

        var result = comment.Edit("Too late", Author, Now.AddMinutes(15).AddSeconds(1));

        Assert.Equal("comment.edit_window_closed", result.Error!.Code);
        Assert.Equal("First version", comment.Body);
        Assert.False(comment.CanBeEditedBy(Author, Now.AddMinutes(16)));
    }

    [Fact]
    public void Only_the_author_can_edit()
    {
        var comment = NewComment();

        var result = comment.Edit("Hijacked", Guid.CreateVersion7(), Now.AddMinutes(1));

        Assert.Equal("comment.not_author", result.Error!.Code);
        Assert.Empty(comment.Edits);
    }

    [Fact]
    public void Saving_the_same_text_records_no_edit()
    {
        var comment = NewComment();

        comment.Edit("First version", Author, Now.AddMinutes(1));

        Assert.Empty(comment.Edits);
        Assert.Null(comment.EditedAt);
    }

    [Fact]
    public void Deleted_comments_cannot_be_edited()
    {
        var comment = NewComment();
        comment.Delete(Author, Now.AddMinutes(1));

        var result = comment.Edit("Back again", Author, Now.AddMinutes(2));

        Assert.True(comment.IsDeleted);
        Assert.Equal("comment.deleted", result.Error!.Code);
    }
}
