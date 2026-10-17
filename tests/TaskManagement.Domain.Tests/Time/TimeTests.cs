using TaskManagement.Domain.Checklists;
using TaskManagement.Domain.Time;

namespace TaskManagement.Domain.Tests.Time;

public sealed class TimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static WorkSession NewSession() => WorkSession.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, Now);

    [Fact]
    public void Ending_a_session_stores_its_duration_once()
    {
        var session = NewSession();

        var normal = session.End(Now.AddMinutes(90), SessionEndReason.Paused);

        Assert.True(normal);
        Assert.False(session.IsActive);
        Assert.Equal(5400, session.DurationSeconds);
        Assert.Equal(SessionEndReason.Paused, session.EndReason);
        Assert.Throws<InvalidOperationException>(() => session.End(Now.AddHours(3), SessionEndReason.Stopped));
    }

    [Fact]
    public void Edge_case_18_a_clock_moving_backwards_never_stores_a_negative_duration()
    {
        var session = NewSession();

        var normal = session.End(Now.AddMinutes(-5), SessionEndReason.Stopped);

        Assert.False(normal);
        Assert.Equal(Now, session.EndedAt);
        Assert.Equal(0, session.DurationSeconds);
    }

    [Fact]
    public void Elapsed_time_of_an_active_session_counts_up_to_now()
    {
        var session = NewSession();

        Assert.Equal(600, session.ElapsedSeconds(Now.AddMinutes(10)));
        Assert.Equal(0, session.ElapsedSeconds(Now.AddMinutes(-1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TimeAdjustment.MaxMinutes + 1)]
    [InlineData(-TimeAdjustment.MaxMinutes - 1)]
    public void Adjustments_must_be_non_zero_and_bounded(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimeAdjustment.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, minutes, "Forgot to stop", Guid.CreateVersion7(), Now));

    [Fact]
    public void Updating_a_template_replaces_its_items_in_order()
    {
        var template = ChecklistTemplate.Create(null, "Definition of done", [new("Tests pass", true), new("Docs", false)], Guid.CreateVersion7(), Now);

        template.Update(" DoD ", false, [new("Reviewed", true)], Guid.CreateVersion7(), Now);

        Assert.Equal("DoD", template.Name);
        Assert.False(template.IsActive);
        var item = Assert.Single(template.Items);
        Assert.Equal((1, "Reviewed", true), (item.SortOrder, item.Text, item.IsMandatory));
    }

    [Fact]
    public void Checklist_items_copied_from_a_template_keep_text_and_flag_and_track_who_ticked_them()
    {
        var template = ChecklistTemplate.Create(null, "DoD", [new("Tests pass", true)], Guid.CreateVersion7(), Now);
        var item = TaskChecklistItem.FromTemplate(Guid.CreateVersion7(), template.Items[0], 1, Now);
        var developer = Guid.CreateVersion7();

        Assert.True(item.Complete(developer, " all green ", Now).IsSuccess);
        Assert.Equal("checklist.already_completed", item.Complete(developer, null, Now).Error!.Code);
        Assert.Equal((true, developer, "all green"), (item.IsCompleted, item.CompletedBy!.Value, item.Note));
        Assert.True(item.Uncomplete().IsSuccess);
        Assert.Equal((false, (Guid?)null, (string?)null), (item.IsCompleted, item.CompletedBy, item.Note));
        Assert.Equal(("Tests pass", true), (item.Text, item.IsMandatory));
    }
}
