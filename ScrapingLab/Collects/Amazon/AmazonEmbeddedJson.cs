using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScrapingLab.Collects.Amazon;

/// <summary>실행하지 않은 JavaScript에서 개별 JSON 속성만 읽습니다.</summary>
internal static class AmazonEmbeddedJson
{
    public static JsonElement? ReadProperty(string script, string property)
    {
        var pattern = "\"" + Regex.Escape(property) + "\"\\s*:\\s*";
        foreach (Match match in Regex.Matches(script, pattern))
        {
            var start = match.Index + match.Length;
            if (start >= script.Length || script[start] is not ('{' or '[')) continue;
            var closings = new Stack<char>();
            char? quote = null;
            var escaped = false;
            for (var index = start; index < script.Length; index++)
            {
                var character = script[index];
                if (quote is not null)
                {
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == quote) quote = null;
                    continue;
                }
                if (character is '"' or '\'') { quote = character; continue; }
                if (character == '{') closings.Push('}');
                else if (character == '[') closings.Push(']');
                else if (character is '}' or ']')
                {
                    if (closings.Count == 0 || closings.Pop() != character) break;
                    if (closings.Count != 0) continue;
                    try
                    {
                        using var value = JsonDocument.Parse(script[start..(index + 1)]);
                        return value.RootElement.Clone();
                    }
                    catch (JsonException) { break; }
                }
            }
        }
        return null;
    }

    public static string? ReadString(string script, string property)
    {
        var pattern = "\"" + Regex.Escape(property) + "\"\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\")";
        foreach (Match match in Regex.Matches(script, pattern))
        {
            try { return JsonSerializer.Deserialize<string>(match.Groups[1].Value); }
            catch (JsonException) { }
        }
        return null;
    }
}
