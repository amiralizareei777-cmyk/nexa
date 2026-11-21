namespace Nexa.Core.Interfaces;

/// <summary>
/// Parses user input to determine intent.
/// </summary>
public interface IIntentParser
{
    /// <summary>
    /// Attempts to parse a command locally without using AI.
    /// </summary>
    /// <returns>An intent if successfully parsed, otherwise null.</returns>
    Intent? TryParseLocal(string command);
}
