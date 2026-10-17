namespace TaskManagement.Domain.Common;

/// <summary>
/// The outcome of an operation that can fail in an expected way (validation, permission, workflow rule).
/// Unexpected failures are exceptions, not results.
/// </summary>
public class Result
{
    protected Result(Error? error)
    {
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error)
    {
        _value = value;
    }

    /// <summary>The value of a successful result. Reading it from a failed result is a programming error.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error!.Code}).");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
