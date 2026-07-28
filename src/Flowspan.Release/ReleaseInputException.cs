namespace Flowspan.Release;

public sealed class ReleaseInputException : Exception
{
    public ReleaseInputException(string message)
        : base(message)
    {
    }

    public ReleaseInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
