#if DEBUG
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nitrox.Model.DevBridge;

/// <summary>
///     Request parsing and the entity dump format shared by the game and the server bridge.
/// </summary>
public static class BridgeText
{
    /// <summary>
    ///     Columns of an <c>entities</c> dump, one entity per line, tab separated. Lines starting with <c>#</c> are headers.
    /// </summary>
    public const string ENTITY_DUMP_HEADER = "#id\ttechType\tkind\tparentId\tparentKind\tposition\tmetadata";

    /// <summary>
    ///     Splits a request into words. Double quotes group words (<c>chat "hello there"</c>); <c>\"</c> is a literal quote.
    /// </summary>
    public static List<string> SplitArgs(string line)
    {
        List<string> args = [];
        StringBuilder current = new();
        bool inQuotes = false;
        bool hasToken = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                hasToken = true;
                i++;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }
        if (hasToken)
        {
            args.Add(current.ToString());
        }
        return args;
    }

    /// <summary>
    ///     Text after the first <paramref name="wordCount" /> words, unparsed (for <c>console</c>, <c>chat</c>, server commands).
    /// </summary>
    public static string Rest(string line, int wordCount)
    {
        int index = 0;
        for (int word = 0; word < wordCount; word++)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }
            while (index < line.Length && !char.IsWhiteSpace(line[index]))
            {
                index++;
            }
        }
        return line.Substring(index).Trim();
    }

    public static string EntityDumpLine(string id, string techType, string kind, string? parentId, string parentKind, double[]? position, string metadata)
    {
        string pos = position == null ? "-" : string.Join(",", FormatCoordinate(position[0]), FormatCoordinate(position[1]), FormatCoordinate(position[2]));
        return string.Join("\t", id, techType, kind, parentId ?? "-", parentKind, pos, Clean(metadata));
    }

    /// <summary>
    ///     Metadata as comparable text: same JSON on both sides, floats rounded to 2 decimals.
    /// </summary>
    public static string DescribeMetadata(object? metadata) => metadata == null ? "-" : BridgeJson.Serialize(metadata, null, 3, 2);

    private static string FormatCoordinate(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
#endif
