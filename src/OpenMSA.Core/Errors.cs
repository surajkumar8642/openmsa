namespace OpenMSA.Core;

public enum OpenMsaError
{
    None,
    NotFound,
    Forbidden,
    InvalidInput,
    Unauthorized,
    Conflict,
    Internal,
}

public sealed class OpenMsaException : Exception
{
    public OpenMsaError Code { get; }

    public OpenMsaException(OpenMsaError code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }
}
