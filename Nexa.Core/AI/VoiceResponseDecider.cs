using Nexa.Core.Models;

namespace Nexa.Core.AI;

/// <summary>
/// Decides whether a command result should be spoken aloud. Nexa reads short
/// action confirmations but never reads long AI text answers aloud.
/// </summary>
public static class VoiceResponseDecider
{
    private const int MaxSpeechChars = 150;
    private const int MaxSpeechWords = 25;

    /// <summary>
    /// Returns the text to speak, or null when nothing should be spoken.
    /// </summary>
    public static string? GetConfirmationToSpeak(ActionResult result, string? aiAnswerText)
    {
        if (!result.Success)
            return null;

        // AI answers: only speak if genuinely short (a brief reply), never long text.
        if (!string.IsNullOrWhiteSpace(aiAnswerText))
        {
            var answer = aiAnswerText.Trim();
            if (answer.Length <= MaxSpeechChars && CountWords(answer) <= MaxSpeechWords)
                return answer;
            return null;
        }

        var message = result.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return null;

        return message.Length <= MaxSpeechChars && CountWords(message) <= MaxSpeechWords
            ? message
            : null;
    }

    private static int CountWords(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}