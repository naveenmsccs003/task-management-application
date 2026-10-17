using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Tests.Common;

public sealed class ErrorTests
{
    [Fact]
    public void Factories_set_the_matching_type()
    {
        Assert.Equal(ErrorType.Validation, Error.Validation("c", "m").Type);
        Assert.Equal(ErrorType.Unauthorized, Error.Unauthorized("c", "m").Type);
        Assert.Equal(ErrorType.Forbidden, Error.Forbidden("c", "m").Type);
        Assert.Equal(ErrorType.NotFound, Error.NotFound("c", "m").Type);
        Assert.Equal(ErrorType.Conflict, Error.Conflict("c", "m").Type);
    }

    [Fact]
    public void Validation_error_keeps_field_errors()
    {
        var fields = new Dictionary<string, string[]> { ["title"] = ["Title is required."] };

        var error = Error.Validation("validation_failed", "Some fields are invalid.", fields);

        Assert.Equal(["Title is required."], error.FieldErrors!["title"]);
    }

    [Theory]
    [InlineData("", "message")]
    [InlineData(" ", "message")]
    [InlineData("code", "")]
    public void Code_and_message_are_required(string code, string message)
    {
        Assert.ThrowsAny<ArgumentException>(() => Error.Conflict(code, message));
    }
}
