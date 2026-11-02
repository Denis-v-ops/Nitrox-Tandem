#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Nitrox.Model.DevBridge;

/// <summary>
///     Minimal JSON writer for dev bridge replies. Works the same in the game (net472) and the server, so values written
///     on both sides can be compared as text (see <c>bridge.ps1 diff</c>).
/// </summary>
/// <remarks>
///     Objects without special handling are written as their public instance fields and properties, sorted by name.
///     <c>adapter</c> can replace a value before it is written (the game uses it for Unity types); return the value
///     itself to keep the default handling.
/// </remarks>
public static class BridgeJson
{
    private const int MAX_ITEMS = 500;

    public static string Serialize(object? value, Func<object, object?>? adapter = null, int maxDepth = 4, int? floatDecimals = null)
    {
        StringBuilder builder = new();
        Write(builder, value, adapter, maxDepth, floatDecimals, true);
        return builder.ToString();
    }

    private static void Write(StringBuilder sb, object? value, Func<object, object?>? adapter, int depth, int? decimals, bool useAdapter)
    {
        if (value != null && useAdapter && adapter != null)
        {
            object? adapted = adapter(value);
            if (!ReferenceEquals(adapted, value))
            {
                Write(sb, adapted, adapter, depth, decimals, false);
                return;
            }
        }

        switch (value)
        {
            case null:
                sb.Append("null");
                return;
            case string text:
                WriteString(sb, text);
                return;
            case bool flag:
                sb.Append(flag ? "true" : "false");
                return;
            case char c:
                WriteString(sb, c.ToString());
                return;
            case Enum:
                WriteString(sb, value.ToString());
                return;
            case float f:
                WriteNumber(sb, f, decimals);
                return;
            case double d:
                WriteNumber(sb, d, decimals);
                return;
            case decimal or byte or sbyte or short or ushort or int or uint or long or ulong:
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            case Guid or DateTime or DateTimeOffset or TimeSpan or Type:
                WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                return;
        }

        if (depth <= 0)
        {
            WriteString(sb, value.ToString() ?? "");
            return;
        }

        if (value is IDictionary dictionary)
        {
            sb.Append('{');
            bool first = true;
            int count = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (count++ >= MAX_ITEMS)
                {
                    break;
                }
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "");
                sb.Append(':');
                Write(sb, entry.Value, adapter, depth - 1, decimals, true);
            }
            sb.Append('}');
            return;
        }

        if (value is IEnumerable enumerable)
        {
            sb.Append('[');
            int count = 0;
            foreach (object item in enumerable)
            {
                if (count >= MAX_ITEMS)
                {
                    sb.Append(",\"...\"");
                    break;
                }
                if (count > 0)
                {
                    sb.Append(',');
                }
                count++;
                Write(sb, item, adapter, depth - 1, decimals, true);
            }
            sb.Append(']');
            return;
        }

        // Plain object: public instance fields and properties, sorted so both runtimes write the same text.
        Type type = value.GetType();
        IEnumerable<MemberInfo> members = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                              .Cast<MemberInfo>()
                                              .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
                                              .OrderBy(m => m.Name, StringComparer.Ordinal);
        sb.Append("{\"$type\":");
        WriteString(sb, type.Name);
        foreach (MemberInfo member in members)
        {
            object? memberValue;
            try
            {
                memberValue = member is FieldInfo field ? field.GetValue(value) : ((PropertyInfo)member).GetValue(value, null);
            }
            catch (Exception ex)
            {
                memberValue = $"<{ex.GetType().Name}>";
            }
            sb.Append(',');
            WriteString(sb, member.Name);
            sb.Append(':');
            Write(sb, memberValue, adapter, depth - 1, decimals, true);
        }
        sb.Append('}');
    }

    private static void WriteNumber(StringBuilder sb, double number, int? decimals)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            WriteString(sb, number.ToString(CultureInfo.InvariantCulture));
            return;
        }
        if (decimals.HasValue)
        {
            number = Math.Round(number, decimals.Value);
            // Avoid "-0" so that 0 and -0.001 compare equal after rounding.
            if (number == 0)
            {
                number = 0;
            }
        }
        sb.Append(number.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void WriteString(StringBuilder sb, string text)
    {
        sb.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }
}
#endif
