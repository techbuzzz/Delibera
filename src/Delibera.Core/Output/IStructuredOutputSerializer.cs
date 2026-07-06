using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Delibera.Core.Models;

namespace Delibera.Core.Output;

/// <summary>
///    Serializes and deserializes typed verdicts for the F-05 Structured Output feature.
///    The default implementation uses <see cref="JsonSchemaOutputSerializer"/> which
///    generates a JSON schema from a C# record/type via <see cref="JsonSerializerOptions"/>,
///    appends it to the Chairman's synthesis prompt, and deserialises the LLM response
///    into the target type. One automatic retry with a correction prompt is performed
///    on deserialisation failure.
/// </summary>
public interface IStructuredOutputSerializer
{
    /// <summary>
    ///    Generates a JSON schema string for the target type <typeparamref name="T"/>,
    ///    suitable for appending to the Chairman's synthesis prompt.
    /// </summary>
    /// <typeparam name="T">The target verdict type.</typeparam>
    /// <returns>A JSON schema string (draft 2020-12 or similar).</returns>
    string GenerateSchema<T>();

    /// <summary>
    ///    Deserialises the LLM's response into a <typeparamref name="T"/>.
    ///    Throws <see cref="JsonException"/> on failure (the caller retries once).
    /// </summary>
    /// <typeparam name="T">The target verdict type.</typeparam>
    /// <param name="llmResponse">The raw LLM response text (expected to contain JSON).</param>
    /// <returns>The deserialised verdict.</returns>
    T Deserialize<T>(string llmResponse);
}

/// <summary>
///    Default <see cref="IStructuredOutputSerializer"/> backed by
///    <see cref="System.Text.Json"/>. Generates a JSON schema from the target type's
///    public properties, extracts JSON from the LLM response (handling markdown code
///    fences), and deserialises it into the target type.
/// </summary>
public sealed class JsonSchemaOutputSerializer : IStructuredOutputSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    ///    Creates a serializer with the specified <see cref="JsonSerializerOptions"/>.
    ///    Defaults to a permissive configuration that allows trailing commas and comments.
    /// </summary>
    /// <param name="options">JSON serializer options. <c>null</c> uses defaults.</param>
    public JsonSchemaOutputSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };
        // .NET 10 requires a TypeInfoResolver for schema generation and deserialisation.
        if (_options.TypeInfoResolver is null)
            _options.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
    }

    /// <inheritdoc />
    public string GenerateSchema<T>()
    {
        // Use the built-in JsonSchemaExporter (available in .NET 9+) to produce a schema.
        var schema = _options.GetJsonSchemaAsNode(typeof(T));
        return schema.ToJsonString();
    }

    /// <inheritdoc />
    public T Deserialize<T>(string llmResponse)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(llmResponse);

        // Extract JSON from the response. The LLM may wrap the JSON in markdown
        // code fences (```json ... ```) or prefix it with prose. We find the first
        // { or [ and the last } or ] and parse the substring.
        var json = ExtractJson(llmResponse);
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("No JSON object or array found in the LLM response.");

        return JsonSerializer.Deserialize<T>(json, _options)
            ?? throw new JsonException($"Deserialisation returned null for type {typeof(T).Name}.");
    }

    /// <summary>
    ///    Extracts the first JSON object or array from a text that may contain
    ///    markdown code fences or surrounding prose.
    /// </summary>
    internal static string ExtractJson(string text)
    {
        // Strip markdown code fences if present.
        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            // Skip the "```json" or "```" line.
            var lineEnd = text.IndexOf('\n', fenceStart);
            if (lineEnd > fenceStart)
            {
                var fenceEnd = text.IndexOf("```", lineEnd, StringComparison.Ordinal);
                if (fenceEnd > lineEnd)
                    text = text[(lineEnd + 1)..fenceEnd].Trim();
            }
        }

        // Find the first { or [ and the matching last } or ].
        var objStart = text.IndexOf('{');
        var arrStart = text.IndexOf('[');
        int start;
        char closeChar;
        if (objStart < 0 && arrStart < 0) return string.Empty;
        if (objStart < 0) { start = arrStart; closeChar = ']'; }
        else if (arrStart < 0) { start = objStart; closeChar = '}'; }
        else if (objStart < arrStart) { start = objStart; closeChar = '}'; }
        else { start = arrStart; closeChar = ']'; }

        var end = text.LastIndexOf(closeChar);
        if (end <= start) return string.Empty;

        return text[start..(end + 1)];
    }

    /// <summary>
    ///    Builds the augmented synthesis prompt that appends the JSON schema and
    ///    instructs the model to return ONLY valid JSON conforming to the schema.
    /// </summary>
    public static string BuildStructuredPrompt(string basePrompt, string schema, string typeName)
    {
        return $"""
                {basePrompt}

                ── STRUCTURED OUTPUT REQUIRED ──
                You MUST return your verdict as a single valid JSON object that conforms to the
                following JSON schema. Do NOT wrap it in markdown code fences. Do NOT include
                any prose before or after the JSON.

                Target type: {typeName}

                JSON Schema:
                {schema}

                Return ONLY the JSON object.
                """;
    }

    /// <summary>
    ///    Builds a correction prompt for the retry attempt when the first deserialisation fails.
    /// </summary>
    public static string BuildCorrectionPrompt(string originalResponse, string errorMessage, string schema, string typeName)
    {
        return $"""
                Your previous response could not be parsed as valid JSON conforming to the schema.

                Error: {errorMessage}

                Your previous response was:
                {originalResponse}

                Please return ONLY a valid JSON object conforming to this schema for type {typeName}:

                {schema}

                Return ONLY the JSON object. No prose, no markdown fences.
                """;
    }
}