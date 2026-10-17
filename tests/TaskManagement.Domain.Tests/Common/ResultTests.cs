using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Tests.Common;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.Conflict("task.invalid_transition", "Not allowed now.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_keeps_the_error()
    {
        var result = Result.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Same(SampleError, result.Error);
    }

    [Fact]
    public void Successful_generic_result_exposes_value()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Reading_value_of_failed_result_throws_with_error_code()
    {
        Result<int> result = SampleError;

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("task.invalid_transition", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_requires_an_error()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));
    }
}
