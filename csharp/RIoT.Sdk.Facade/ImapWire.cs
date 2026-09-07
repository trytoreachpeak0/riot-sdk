using System.Text.Json;
using Microsoft.Kiota.Abstractions;
using RIoT.Sdk.Core;

namespace RIoT.Sdk.Facade;

/// <summary>
/// Custom deserialization for the imap route-graph endpoints (BC-MAP-003).
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
/// </remarks>
internal static class ImapWire
{
    /// <summary>
    /// Sends a prepared GET, validates the ResponseMsg envelope (ADR-sdk-0005), and hands the
    /// <c>result</c> element to <paramref name="mapResult"/> while the document is still alive.
    /// </summary>
    internal static async Task<T> ReadAsync<T>(
        IRequestAdapter adapter,
        RequestInformation requestInfo,
        string operation,
        Func<JsonElement, T> mapResult,
        CancellationToken cancellationToken)
    {
        var stream = await adapter
            .SendPrimitiveAsync<Stream>(requestInfo, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (stream is null)
        {
            throw new RiotApiException($"{operation} returned empty response.");
        }

        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

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
