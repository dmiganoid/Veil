using System.Text.Json;

namespace Veil.Models;

/// <summary>
/// Case-insensitive JSON field readers shared by the persisted models.
/// Lenient readers fall back to defaults on unexpected types (used for user imports),
/// strict readers reject them (used for Veil's own state files).
/// </summary>
internal static class JsonFields
{
    public static bool TryGet(JsonElement json, string name, out JsonElement value)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static string LenientString(JsonElement json, string name, string fallback) =>
        TryGet(json, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    public static int LenientInt(JsonElement json, string name, int fallback)
    {
        if (!TryGet(json, name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => fallback
        };
    }

    public static bool LenientBool(JsonElement json, string name, bool fallback)
    {
        if (!TryGet(json, name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    public static List<string> LenientStringList(JsonElement json, string name)
    {
        if (!TryGet(json, name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString())
            .ToList();
    }

    public static string StrictString(JsonElement json, string name, string fallback)
    {
        if (!TryGet(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : throw new InvalidDataException($"{name} must be a string.");
    }

    public static string? StrictNullableString(JsonElement json, string name, string? fallback)
    {
        if (!TryGet(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new InvalidDataException($"{name} must be a string.");
    }

    public static string RequiredString(JsonElement json, string name) =>
        TryGet(json, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : throw new InvalidDataException($"{name} must be a string.");

    public static int StrictInt(JsonElement json, string name, int fallback)
    {
        if (!TryGet(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidDataException($"{name} must be a number.");
    }

    public static bool StrictBool(JsonElement json, string name, bool fallback)
    {
        if (!TryGet(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"{name} must be a boolean.")
        };
    }

    public static List<string> StrictStringList(JsonElement json, string name)
    {
        if (!TryGet(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{name} must be an array.");
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"{name} entries must be strings.");
            }

            result.Add(item.GetString() ?? "");
        }

        return result;
    }
}
