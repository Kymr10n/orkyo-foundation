namespace Api.Helpers;

/// <summary>
/// A tenant transfer refused for a reason the caller can act on: a schema version that does
/// not match, an archive that breaks a format rule, an import already running. Carries its
/// own status and wire code because the refusals span 409, 413, 422 and 503 and the frontend
/// switches on the code.
/// </summary>
public sealed class TransferRejectedException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    /// <summary>One of the <c>transfer_*</c> values in <see cref="Api.Constants.ApiErrorCodes"/>.</summary>
    public string Code { get; } = code;
}
