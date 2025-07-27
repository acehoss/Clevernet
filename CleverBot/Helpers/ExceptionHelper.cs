namespace CleverBot.Helpers;

public static class ExceptionHelper
{
    public static Exception Unwrap(this Exception e)
    {
        if (e is AggregateException aggregateException)
        {
            if (aggregateException.InnerExceptions.Count == 1)
            {
                return Unwrap(aggregateException.InnerExceptions[0]);
            }
            return e;
        }

        return e;
    }

    public static async Task<T> Handle<T>(this Task<T> task, Func<Exception, T> onException)
    {
        try
        {
            return await task;
        }
        catch (Exception ex)
        {
            return onException(ex);
        }
    }
}