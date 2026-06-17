namespace InsurTech.BuildingBlocks.Results;

/// <summary>
/// Lightweight functional result. Carries either success (with value) or a structured
/// <see cref="Error"/> that maps to an RFC 7807 problem-details response
/// (LLD Appendix A.1.6 error catalogue: CLM-*, FR-*, DOC-*).
/// </summary>
public readonly record struct Result<T>
{
    private Result(T value) { IsSuccess = true; Value = value; Error = default; }
    private Result(Error error) { IsSuccess = false; Value = default; Error = error; }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public Error Error { get; }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>
/// Structured domain error. <see cref="Code"/> is the catalogue code (e.g. "CLM-021"),
/// <see cref="HttpStatus"/> the HTTP mapping, <see cref="Detail"/> the human message.
/// </summary>
public readonly record struct Error(string Code, int HttpStatus, string Detail)
{
    public static Error Validation(string code, string detail) => new(code, 400, detail);
    public static Error NotFound(string code, string detail) => new(code, 404, detail);
    public static Error Conflict(string code, string detail) => new(code, 409, detail);
    public static Error Unprocessable(string code, string detail) => new(code, 422, detail);
    public static Error Upstream(string code, string detail) => new(code, 502, detail);
}
