using System.Threading.Tasks;

namespace CleverBot.Helpers;

public static class TaskHelper
{
    public static async Task<T?> GulpException<T>(this Task<T> task, ILogger logger, string? message = null) {
        try {
            return await task;
        } catch (Exception ex) {
            logger.LogError(ex, message ?? "An error occurred while processing the task");
        }
        return default;
    }

    public static async Task GulpException(this Task task, ILogger logger, string? message = null) {
        try {
            await task;
        } catch (Exception ex) {
            logger.LogError(ex, message ?? "An error occurred while processing the task");
            throw;
        }
    }
}