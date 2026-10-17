using System.Text.Json;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using RIoT.Sdk.Core;

namespace RIoT.Sdk.Facade;

/// <summary>
/// Custom deserialization for the RIoT endpoints whose wire format no generated model can read.
/// </summary>
/// <remarks>
/// <para>
/// The generated Kiota models cannot read these responses. Round 43 measured why, on the
/// production RIoT: <c>edges</c> is snake_case and spells the compound keys differently from the
/// OpenAPI description (<c>s_node</c>, not <c>snode</c>); <c>stations</c> spells keys with a
/// literal dot (<c>pos.x</c>); <c>mapEdgeGroup/all</c> is camelCase — a second style on the same
/// service — nests its result under group-name keys, and stamps timestamps that are not ISO 8601.
/// </para>
/// <para>
/// So these five endpoints reuse the generated layer for URL construction and auth
/// (<see cref="RequestInformation"/> straight off the generated request builders) and read the
/// body here. Everything in this file is internal: the wire format must not reach product code.
/// </para>
/// <para>
/// Only the parse is replaced, never the request. The body comes through the same
/// <c>SendAsync</c> the generated <c>GetAsync</c> uses, with Kiota's own untyped model, so the
/// timeout, the release of the response, error mapping and Content-Type handling are the
/// generated layer's. riot-sdk#4: the earlier <c>SendPrimitiveAsync&lt;Stream&gt;</c> sent with
/// <c>ResponseHeadersRead</c>, so <c>HttpClient.Timeout</c> stopped at the headers and a stalled
/// body hung; and a failed response was never released, because Kiota does not drain for
/// <c>Stream</c>.
/// </para>
/// </remarks>
/// <remarks>
/// Named for RIoT rather than for imap because the task module has one endpoint with the same
/// problem: <c>GET /api/task/v1/route/</c> answers with a shape nobody has ever seen populated,
/// so the SDK reports what is there rather than deserializing into a type it made up.
/// </remarks>
internal static class RiotWire
{
    /// <summary>
    /// Sends a prepared GET, validates the ResponseMsg envelope (ADR-sdk-0005), and hands the
    /// <c>result</c> element to <paramref name="mapResult"/> while the document is still alive.
    /// </summary>
    /// <remarks>
    /// No error mapping, as the generated <c>GetAsync</c> of these endpoints has none: a 4xx or
    /// 5xx is Kiota's <see cref="ApiException"/>. A body Kiota reads as nothing — no Content-Type,
    /// or empty — is the no-body failure below; a Content-Type other than JSON is Kiota's
    /// <see cref="InvalidOperationException"/>, as for the generated read.
    /// </remarks>
    internal static async Task<T> ReadAsync<T>(
        IRequestAdapter adapter,
        RequestInformation requestInfo,
        string operation,
        Func<JsonElement, T> mapResult,
        CancellationToken cancellationToken)
    {
        var body = await adapter
            .SendAsync(requestInfo, UntypedNode.CreateFromDiscriminatorValue, default, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            throw new RiotApiException(
                $"{operation} returned no body that could be read: empty, or without a Content-Type.");
        }

        using var document = ToJsonDocument(body, operation);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new RiotApiException(
                $"{operation} returned a {root.ValueKind} body, expected a ResponseMsg object.");
        }

        RiotBusinessResponse.EnsureSuccess(
            OptionalString(root, "code"),
            OptionalString(root, "message"));

        return mapResult(
            root.TryGetProperty("result", out var result) ? result : default);
    }

    /// <summary>
    /// Writes the untyped tree back out as JSON for the readers below, which predate it and are
    /// written against <see cref="JsonElement"/>.
    /// </summary>
    /// <remarks>
    /// Numbers keep their value: Kiota reads a number as the first of int, long, decimal, float,
    /// double that holds it. A plain integer literal is an int or a long and is written back as
    /// one. Anything that reaches decimal was not an integer literal — <c>128.0</c>, or an
    /// exponent such as <c>1.28E2</c> — and is written back with a fraction, so it still fails an
    /// int read as it did when the reader parsed the raw body, and as Python's reader does
    /// (riot-sdk#4 review S1). Kiota parses <c>1.28E2</c> to a decimal of scale 0, which would
    /// otherwise be written as <c>128</c>. A number beyond decimal's range (about 7.9e28)
    /// is narrowed to float precision, and one beyond float's (about 3.4e38) becomes infinity,
    /// which has no JSON spelling: that read fails closed. Nothing on these endpoints comes near.
    /// </remarks>
    private static JsonDocument ToJsonDocument(UntypedNode node, string operation)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, node, operation);
        }

        return JsonDocument.Parse(buffer.WrittenMemory);
    }

    private static void Write(Utf8JsonWriter writer, UntypedNode node, string operation)
    {
        switch (node)
        {
            case UntypedObject obj:
                writer.WriteStartObject();
                foreach (var (key, value) in obj.GetValue())
                {
                    writer.WritePropertyName(key);
                    Write(writer, value, operation);
                }
                writer.WriteEndObject();
                break;
            case UntypedArray array:
                writer.WriteStartArray();
                foreach (var item in array.GetValue())
                {
                    Write(writer, item, operation);
                }
                writer.WriteEndArray();
                break;
            case UntypedString text when text.GetValue() is { } value:
                writer.WriteStringValue(value);
                break;
            case UntypedBoolean flag:
                writer.WriteBooleanValue(flag.GetValue());
                break;
            case UntypedInteger number:
                writer.WriteNumberValue(number.GetValue());
                break;
            case UntypedLong number:
                writer.WriteNumberValue(number.GetValue());
                break;
            case UntypedDecimal number:
                var fraction = number.GetValue();
                writer.WriteNumberValue(fraction.Scale == 0 ? fraction + 0.0m : fraction);
                break;
            case UntypedFloat number when float.IsFinite(number.GetValue()):
                writer.WriteNumberValue(number.GetValue());
                break;
            case UntypedDouble number when double.IsFinite(number.GetValue()):
                writer.WriteNumberValue(number.GetValue());
                break;
            case UntypedFloat or UntypedDouble:
                throw new RiotApiException($"{operation} returned a number beyond the range the SDK reads.");
            default:
                writer.WriteNullValue();
                break;
        }
    }

    /// <summary>
    /// Maps a <c>result</c> array. A missing or null result is an empty list — RIoT returns
    /// <c>"result":[]</c> for "nothing here", and both spellings mean the same thing. A result
    /// that is present but not an array is a protocol violation and fails closed.
    /// </summary>
    internal static IReadOnlyList<T> MapArray<T>(
        JsonElement result,
        string operation,
        Func<JsonElement, T> mapItem)
    {
        if (result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return [];
        }

        if (result.ValueKind != JsonValueKind.Array)
        {
            throw new RiotApiException(
                $"{operation} returned result of kind {result.ValueKind}, expected an array.");
        }

        var items = new List<T>(result.GetArrayLength());
        foreach (var item in result.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new RiotApiException(
                    $"{operation} returned a {item.ValueKind} array element, expected an object.");
            }

            items.Add(mapItem(item));
        }

        return items;
    }

    /// <summary>Reads an int, trying each spelling in turn. Fails closed when none is present.</summary>
    internal static int RequireInt(JsonElement element, string operation, params string[] keys)
        => TryGetInt(element, keys, out var value)
            ? value
            : throw Missing(operation, keys, "int");

    /// <summary>Reads an int, or <paramref name="fallback"/> when no spelling is present.</summary>
    internal static int OptionalInt(JsonElement element, int fallback, params string[] keys)
        => TryGetInt(element, keys, out var value) ? value : fallback;

    /// <summary>Reads a double. Fails closed when no spelling is present.</summary>
    internal static double RequireDouble(JsonElement element, string operation, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var property) &&
                property.ValueKind == JsonValueKind.Number &&
                property.TryGetDouble(out var value))
            {
                return value;
            }
        }

        throw Missing(operation, keys, "number");
    }

    /// <summary>Reads a double, or <paramref name="fallback"/> when no spelling is present.</summary>
    internal static double OptionalDouble(JsonElement element, double fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var property) &&
                property.ValueKind == JsonValueKind.Number &&
                property.TryGetDouble(out var value))
            {
                return value;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Reads a bool. RIoT spells these both ways — <c>is_back_edge</c> is a JSON bool,
    /// <c>isDelete</c> is 0 or 1 — so both are accepted.
    /// </summary>
    internal static bool OptionalBool(JsonElement element, bool fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!element.TryGetProperty(key, out var property))
            {
                continue;
            }

            switch (property.ValueKind)
            {
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Number when property.TryGetInt32(out var number):
                    return number != 0;
            }
        }

        return fallback;
    }

    /// <summary>Reads a string. Fails closed when no spelling holds a non-empty one.</summary>
    internal static string RequireString(JsonElement element, string operation, params string[] keys)
    {
        var value = OptionalString(element, keys);
        return string.IsNullOrWhiteSpace(value) ? throw Missing(operation, keys, "string") : value;
    }

    /// <summary>Reads a string, or null when no spelling is present.</summary>
    internal static string? OptionalString(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static bool TryGetInt(JsonElement element, string[] keys, out int value)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var property) &&
                property.ValueKind == JsonValueKind.Number &&
                property.TryGetInt32(out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static RiotApiException Missing(string operation, string[] keys, string kind)
        => new($"{operation} element is missing a {kind} under any of: {string.Join(", ", keys)}.");
}
