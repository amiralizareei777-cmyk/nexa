namespace Nexa.Core.Models;

/// <summary>
/// Result of an action execution.
/// </summary>
public class ActionResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Data { get; set; }
    public Exception? Exception { get; set; }

    public static ActionResult Successful(string? message = null, Dictionary<string, object>? data = null)
    {
        return new ActionResult
        {
            Success = true,
            Message = message,
            Data = data
        };
    }

    public static ActionResult Failed(string message, Exception? exception = null)
    {
        return new ActionResult
        {
            Success = false,
            Message = message,
            Exception = exception
        };
    }
}
