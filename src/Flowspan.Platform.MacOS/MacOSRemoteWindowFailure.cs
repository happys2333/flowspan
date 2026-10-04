namespace Flowspan.Platform.MacOS;

internal static class MacOSRemoteWindowFailure
{
    internal static OutOfMemoryException? FindFatal(Exception exception)
    {
        if (exception is OutOfMemoryException fatal)
        {
            return fatal;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
            {
                OutOfMemoryException? nested = FindFatal(inner);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (exception.InnerException is not null)
        {
            return FindFatal(exception.InnerException);
        }

        return null;
    }
}
