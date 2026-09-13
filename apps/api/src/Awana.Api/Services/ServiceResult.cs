using Awana.Api.Contracts;

namespace Awana.Api.Services;

/// <summary>
/// A failure a caller is expected to handle, carrying the HTTP status it maps
/// to. Returned rather than thrown, because "this round is invalid" and "the
/// session is finished" are ordinary outcomes of a scorekeeper's night, not
/// exceptional conditions.
/// </summary>
public sealed record ServiceError(
    int Status,
    string Code,
    string Message,
    IReadOnlyList<ValidationErrorDto>? Errors = null)
{
    public static ServiceError NotFound(string what) =>
        new(404, "not_found", $"{what} was not found.");

    public static ServiceError Conflict(string code, string message) =>
        new(409, code, message);

    public static ServiceError Invalid(string message, IReadOnlyList<ValidationErrorDto>? errors = null) =>
        new(400, "invalid_request", message, errors);
}

public sealed record ServiceResult<T>(T? Value, ServiceError? Error)
{
    public bool Ok => Error is null;

    public static ServiceResult<T> Success(T value) => new(value, null);
    public static ServiceResult<T> Fail(ServiceError error) => new(default, error);
}
