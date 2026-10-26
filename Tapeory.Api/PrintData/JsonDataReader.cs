using System.Text.Json;

namespace Tapeory.Api.PrintData;

/// <summary>Reads JSON as a table: a list of objects becomes one row per object, with the
/// property names as the header row. The list may be the document itself or sit inside it
/// (<c>{"data": {"items": [...]}}</c>); nested objects become columns named like "address.city".</summary>
public static class JsonDataReader
{
    /// <summary>How deep inside the document the list is looked for.</summary>
    private const int MaxDepthToList = 4;

    public static bool LooksLikeJson(string text)
    {
        var start = text.AsSpan().TrimStart();
        return start.Length > 0 && start[0] is '[' or '{';
    }

    /// <param name="maxRows">Reading stops one row past this, so the caller can tell there were more.</param>
    /// <returns>The header row and the data rows; null with <paramref name="error"/> set when
    /// the text is JSON but holds no list of records; null without an error when it isn't JSON
    /// at all.</returns>
    public static List<string[]>? Read(string text, int maxRows, out string? error)
    {
        error = null;
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (FindList(document.RootElement, MaxDepthToList) is not { } list)
            {
                error = "This JSON has no list of records. It needs a list of objects, like [{\"name\": \"Box\"}, {\"name\": \"Bag\"}].";
                return null;
            }

            var columns = new List<string>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var records = new List<Dictionary<int, string>>();

            foreach (var item in list.EnumerateArray())
            {
                if (records.Count > maxRows)
                {
                    break;
                }

                var record = new Dictionary<int, string>();

                void Set(string name, string value)
                {
                    if (!index.TryGetValue(name, out var column))
                    {
                        column = index[name] = columns.Count;
                        columns.Add(name);
                    }
                    record[column] = value;
                }

                if (item.ValueKind == JsonValueKind.Object)
                {
                    Flatten(item, string.Empty, Set);
                }
                else
                {
                    // A plain list of values is one column.
                    Set("value", Text(item));
                }

                records.Add(record);
            }

            var rows = new List<string[]>(records.Count + 1) { columns.ToArray() };

            foreach (var record in records)
            {
                var row = new string[columns.Count];
                Array.Fill(row, string.Empty);
                foreach (var (column, value) in record)
                {
                    row[column] = value.Trim();
                }
                rows.Add(row);
            }

            return rows;
        }
    }

    /// <summary>The document itself if it is a list, else the first list found in its properties,
    /// nearest first.</summary>
    private static JsonElement? FindList(JsonElement root, int depth)
    {
        var level = new List<JsonElement> { root };

        for (var i = 0; i <= depth && level.Count > 0; i++)
        {
            foreach (var element in level)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    return element;
                }
            }

            level = level
                .Where(element => element.ValueKind == JsonValueKind.Object)
                .SelectMany(element => element.EnumerateObject().Select(property => property.Value))
                .ToList();
        }

        return null;
    }

    private static void Flatten(JsonElement element, string prefix, Action<string, string> set)
    {
        foreach (var property in element.EnumerateObject())
        {
            var name = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Flatten(property.Value, name, set);
            }
            else
            {
                set(name, Text(property.Value));
            }
        }
    }

    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        // Numbers and true/false as written; a list inside a record as its JSON text.
        _ => value.GetRawText()
    };
}
