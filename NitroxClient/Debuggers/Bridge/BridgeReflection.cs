#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DevBridge;
using NitroxClient.GameLogic.Helper;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Debuggers.Bridge;

/// <summary>
///     Resolves targets, member paths and arguments for the dev bridge's <c>get</c>/<c>set</c>/<c>call</c>/<c>use</c>.
///     Game assemblies are only publicized at compile time, so everything here uses non-public binding flags.
/// </summary>
internal static class BridgeReflection
{
    private const BindingFlags ALL_MEMBERS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static Dictionary<string, Type>? typesByName;

    /// <summary>
    ///     A target is <c>player</c>, a NitroxId, or <c>find:&lt;ComponentType&gt;</c> (the loaded object of that type
    ///     nearest to the player, for scene objects without a NitroxId).
    /// </summary>
    public static bool IsTarget(string token) =>
        token.Equals("player", StringComparison.OrdinalIgnoreCase) || token.StartsWith("find:", StringComparison.OrdinalIgnoreCase) || IsGuid(token);

    public static GameObject ResolveTarget(string token)
    {
        if (token.Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            if (!Player.main)
            {
                throw new InvalidOperationException("No local player (not in game yet)");
            }
            return Player.main.gameObject;
        }
        if (token.StartsWith("find:", StringComparison.OrdinalIgnoreCase))
        {
            // find:<Type> or find:<Type>#<GameObject name> (e.g. find:CinematicModeTrigger#EntranceHatch)
            string[] parts = token.Substring("find:".Length).Split(['#'], 2);
            Type type = FindType(parts[0]);
            Component? nearest = FindLoaded(type).FirstOrDefault(c => parts.Length == 1 || c.gameObject.name == parts[1]);
            if (!nearest)
            {
                throw new InvalidOperationException($"No loaded {type.Name}{(parts.Length > 1 ? $" named {parts[1]}" : "")}");
            }
            return nearest!.gameObject;
        }
        if (!IsGuid(token))
        {
            throw new ArgumentException($"'{token}' is not a target (player, a NitroxId or find:<Type>)");
        }
        if (!NitroxEntity.TryGetObjectFrom(new NitroxId(token), out GameObject? gameObject))
        {
            throw new InvalidOperationException($"Entity {token} isn't loaded on this client");
        }
        return gameObject;
    }

    /// <summary>
    ///     Active loaded components of a type (or implementing an interface), nearest to the player first.
    /// </summary>
    public static List<Component> FindLoaded(Type type)
    {
        Vector3 origin = Player.main ? Player.main.transform.position : Vector3.zero;
        IEnumerable<Component> found = typeof(Component).IsAssignableFrom(type)
            ? UnityEngine.Object.FindObjectsOfType(type).OfType<Component>()
            : UnityEngine.Object.FindObjectsOfType<MonoBehaviour>().Where(type.IsInstanceOfType).Cast<Component>();
        return found.OrderBy(c => (c.transform.position - origin).sqrMagnitude).ToList();
    }

    public static Component GetComponentOn(GameObject gameObject, Type type)
    {
        Component component = gameObject.GetComponent(type);
        if (!component)
        {
            component = gameObject.GetComponentInChildren(type, true);
        }
        if (!component)
        {
            throw new InvalidOperationException($"No {type.Name} on {gameObject.name}");
        }
        return component;
    }

    public static Type FindType(string name)
    {
        if (typesByName == null || !typesByName.ContainsKey(name))
        {
            typesByName = BuildTypeIndex();
        }
        if (!typesByName.TryGetValue(name, out Type type))
        {
            throw new ArgumentException($"Unknown type '{name}'");
        }
        return type;
    }

    public static bool TryFindType(string name, out Type? type)
    {
        try
        {
            type = FindType(name);
            return true;
        }
        catch (ArgumentException)
        {
            type = null;
            return false;
        }
    }

    /// <summary>
    ///     Resolves the object a path leads to.
    ///     Entity form: <c>&lt;target&gt; [Component[.member...]]</c>. Static form: <c>Type.staticMember[.member...]</c>
    ///     (types may be namespaced: <c>Story.StoryGoalManager.main</c>). <c>member[3]</c> indexes a list.
    /// </summary>
    /// <param name="args">Request words after the command name. On return, the words after the path.</param>
    /// <param name="keepLast">Stop before the last path segment (the member to set or the method to call), which is returned in <paramref name="last" />.</param>
    public static object? ResolvePath(List<string> args, bool keepLast, out Type type, out string? last)
    {
        if (args.Count == 0)
        {
            throw new ArgumentException("Missing target or path");
        }
        object? current;
        List<string> segments;
        if (IsTarget(args[0]))
        {
            GameObject gameObject = ResolveTarget(args[0]);
            segments = args.Count > 1 ? args[1].Split('.').ToList() : [];
            args.RemoveRange(0, Math.Min(2, args.Count));
            if (segments.Count == 0 || segments[0] == nameof(GameObject))
            {
                current = gameObject;
                if (segments.Count > 0)
                {
                    segments.RemoveAt(0);
                }
            }
            else
            {
                current = GetComponentOn(gameObject, FindType(segments[0]));
                segments.RemoveAt(0);
            }
            type = current.GetType();
        }
        else
        {
            segments = args[0].Split('.').ToList();
            args.RemoveAt(0);
            int typeLength = 0;
            Type? staticType = null;
            for (int i = 1; i <= segments.Count && staticType == null; i++)
            {
                if (TryFindType(string.Join(".", segments.Take(i)), out staticType))
                {
                    typeLength = i;
                }
            }
            if (staticType == null)
            {
                throw new ArgumentException($"'{string.Join(".", segments)}' doesn't start with a target or a known type");
            }
            segments.RemoveRange(0, typeLength);
            current = null;
            type = staticType;
        }

        last = null;
        if (keepLast)
        {
            if (segments.Count == 0)
            {
                throw new ArgumentException("Missing member name at the end of the path");
            }
            last = segments[segments.Count - 1];
            segments.RemoveAt(segments.Count - 1);
        }
        foreach (string segment in segments)
        {
            current = GetMemberValue(current, type, segment, out type);
        }
        return current;
    }

    public static object? GetMemberValue(object? instance, Type type, string segment, out Type valueType)
    {
        string name = segment;
        int? index = null;
        int bracket = segment.IndexOf('[');
        if (bracket > 0 && segment.EndsWith("]"))
        {
            name = segment.Substring(0, bracket);
            index = int.Parse(segment.Substring(bracket + 1, segment.Length - bracket - 2), CultureInfo.InvariantCulture);
        }

        MemberInfo member = FindMember(type, name) ?? throw new MissingMemberException($"{type.Name} has no field or property '{name}'");
        object? value = member switch
        {
            FieldInfo field => field.GetValue(field.IsStatic ? null : RequireInstance(instance, type, name)),
            PropertyInfo property => property.GetValue(property.GetGetMethod(true)!.IsStatic ? null : RequireInstance(instance, type, name), null),
            _ => throw new MissingMemberException(name)
        };
        valueType = value?.GetType() ?? MemberType(member);

        if (index.HasValue)
        {
            if (value is not IEnumerable enumerable)
            {
                throw new ArgumentException($"{name} can't be indexed");
            }
            value = value is IList list ? list[index.Value] : enumerable.Cast<object>().ElementAt(index.Value);
            valueType = value?.GetType() ?? typeof(object);
        }
        return value;
    }

    public static void SetMemberValue(object? instance, Type type, string name, string rawValue)
    {
        switch (FindMember(type, name))
        {
            case FieldInfo field:
                field.SetValue(field.IsStatic ? null : RequireInstance(instance, type, name), ConvertArg(rawValue, field.FieldType));
                break;
            case PropertyInfo property:
                MethodInfo setter = property.GetSetMethod(true) ?? throw new MissingMemberException($"{type.Name}.{name} has no setter");
                setter.Invoke(setter.IsStatic ? null : RequireInstance(instance, type, name), [ConvertArg(rawValue, property.PropertyType)]);
                break;
            default:
                throw new MissingMemberException($"{type.Name} has no field or property '{name}'");
        }
    }

    /// <summary>
    ///     Calls the first overload named <paramref name="name" /> whose parameters accept <paramref name="rawArgs" />.
    /// </summary>
    public static object? Call(object? instance, Type type, string name, List<string> rawArgs, out MethodInfo calledMethod)
    {
        List<string> failures = [];
        foreach (MethodInfo method in AllMethods(type).Where(m => m.Name == name))
        {
            ParameterInfo[] parameters = method.GetParameters();
            int required = parameters.Count(p => !p.IsOptional);
            if (rawArgs.Count < required || rawArgs.Count > parameters.Length || (!method.IsStatic && instance == null) || method.ContainsGenericParameters)
            {
                continue;
            }
            object?[] values = new object?[parameters.Length];
            try
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    values[i] = i < rawArgs.Count ? ConvertArg(rawArgs[i], parameters[i].ParameterType) : parameters[i].DefaultValue;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{Signature(method)}: {ex.Message}");
                continue;
            }
            calledMethod = method;
            try
            {
                return method.Invoke(method.IsStatic ? null : instance, values);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw new InvalidOperationException($"{Signature(method)} threw {ex.InnerException.GetType().Name}: {ex.InnerException.Message}", ex.InnerException);
            }
        }
        string candidates = string.Join("; ", AllMethods(type).Where(m => m.Name == name).Select(Signature).Distinct());
        throw new MissingMethodException($"No {type.Name}.{name} accepts ({string.Join(", ", rawArgs)}). Overloads: {(candidates == "" ? "none" : candidates)}. {string.Join(" ", failures)}");
    }

    public static object? ConvertArg(string raw, Type type)
    {
        Type? nullableOf = Nullable.GetUnderlyingType(type);
        if (raw == "null")
        {
            if (type.IsValueType && nullableOf == null)
            {
                throw new ArgumentException($"{type.Name} can't be null");
            }
            return null;
        }
        type = nullableOf ?? type;
        if (type == typeof(string) || type == typeof(object))
        {
            return raw;
        }
        if (type.IsEnum)
        {
            return Enum.Parse(type, raw, true);
        }
        if (type == typeof(bool))
        {
            return bool.Parse(raw);
        }
        // No thousands separators: a number formatted with a comma decimal separator must fail instead of changing value
        if (type == typeof(float))
        {
            return float.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (type == typeof(double))
        {
            return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (type == typeof(decimal))
        {
            return decimal.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (type.IsPrimitive)
        {
            return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
        }
        if (type == typeof(Vector3))
        {
            float[] parts = raw.Split(',').Select(p => float.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            return new Vector3(parts[0], parts[1], parts[2]);
        }
        if (type == typeof(Quaternion))
        {
            return Quaternion.Euler((Vector3)ConvertArg(raw, typeof(Vector3))!);
        }
        if (type == typeof(NitroxId))
        {
            return new NitroxId(raw);
        }
        if (type == typeof(Type))
        {
            return FindType(raw);
        }
        if (type == typeof(GameObject))
        {
            return ResolveTarget(raw);
        }
        // Containers and their items aren't components; resolve them from their owner/item object like the game UI does
        if (type == typeof(InventoryItem))
        {
            return ((Pickupable)GetComponentOn(ResolveTarget(raw), typeof(Pickupable))).inventoryItem ?? throw new ArgumentException($"'{raw}' is in no container");
        }
        if (type == typeof(Equipment))
        {
            Optional<Equipment> equipment = EquipmentHelper.FindEquipmentComponent(ResolveTarget(raw));
            return equipment.HasValue ? equipment.Value : throw new ArgumentException($"'{raw}' has no Equipment");
        }
        if (type == typeof(ItemsContainer) || type == typeof(IItemsContainer))
        {
            Optional<ItemsContainer> container = InventoryContainerHelper.TryGetContainerByOwner(ResolveTarget(raw));
            return container.HasValue ? container.Value : throw new ArgumentException($"'{raw}' has no ItemsContainer");
        }
        // A plain Unity object parameter (Object.Destroy): the target's GameObject
        if (type == typeof(UnityEngine.Object))
        {
            return ResolveTarget(raw);
        }
        // An out-style result of a coroutine (Constructable.DeconstructAsync): a fresh TaskResult, whatever was written
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOut<>))
        {
            return Activator.CreateInstance(typeof(TaskResult<>).MakeGenericType(type.GetGenericArguments()));
        }
        if (typeof(Component).IsAssignableFrom(type) || type.IsInterface)
        {
            return GetComponentOn(ResolveTarget(raw), type);
        }
        throw new ArgumentException($"Can't convert '{raw}' to {type.Name}");
    }

    /// <summary>
    ///     Replaces Unity values with JSON-friendly ones for <see cref="BridgeJson" />.
    /// </summary>
    public static object? Adapt(object value)
    {
        switch (value)
        {
            case Vector3 v:
                return Round(v);
            case Vector2 v:
                return new[] { Math.Round(v.x, 3), Math.Round(v.y, 3) };
            case Quaternion q:
                return Round(q.eulerAngles);
            case Color c:
                return new[] { Math.Round(c.r, 3), Math.Round(c.g, 3), Math.Round(c.b, 3), Math.Round(c.a, 3) };
            case NitroxId id:
                return id.ToString();
            case GameObject gameObject:
                return gameObject ? Summary(gameObject) : "<destroyed>";
            case Component component:
                if (!component)
                {
                    return "<destroyed>";
                }
                Dictionary<string, object?> summary = Summary(component.gameObject);
                summary["$component"] = component.GetType().Name;
                return summary;
            case UnityEngine.Object unityObject:
                return unityObject ? $"{unityObject.GetType().Name}:{unityObject.name}" : "<destroyed>";
            case Delegate or MemberInfo or Assembly:
                return value.ToString();
            default:
                return value;
        }
    }

    public static string ToJson(object? value, int maxDepth = 6) => BridgeJson.Serialize(value, Adapt, maxDepth);

    /// <summary>
    ///     A component's own fields (not Unity's), for <c>entity</c> and <c>get &lt;id&gt; Component</c>.
    /// </summary>
    public static Dictionary<string, object?> DescribeComponent(Component component, bool onlySimpleFields)
    {
        Dictionary<string, object?> fields = new() { ["$component"] = component.GetType().Name };
        for (Type? type = component.GetType(); type != null && type.Assembly != typeof(MonoBehaviour).Assembly && type.Assembly != typeof(Component).Assembly; type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                string name = field.Name.StartsWith("<") ? field.Name.Substring(1, field.Name.IndexOf('>') - 1) : field.Name;
                if (fields.ContainsKey(name) || (onlySimpleFields && !IsSimple(field.FieldType)))
                {
                    continue;
                }
                try
                {
                    fields[name] = field.GetValue(component);
                }
                catch (Exception ex)
                {
                    fields[name] = $"<{ex.GetType().Name}>";
                }
            }
        }
        return fields;
    }

    public static string? IdOf(GameObject? gameObject) => gameObject && gameObject!.TryGetNitroxId(out NitroxId? id) ? id.ToString() : null;

    /// <summary>
    ///     Id of the nearest ancestor (not the object itself) that has one.
    /// </summary>
    public static string? AncestorIdOf(Transform transform)
    {
        for (Transform parent = transform.parent; parent; parent = parent.parent)
        {
            if (parent.TryGetComponent(out NitroxEntity entity) && entity.Id != null)
            {
                return entity.Id.ToString();
            }
        }
        return null;
    }

    public static Dictionary<string, object?> Summary(GameObject gameObject)
    {
        string? id = IdOf(gameObject);
        if (id == null && AncestorIdOf(gameObject.transform) is { } ancestorId)
        {
            id = $"(in {ancestorId})";
        }
        return new Dictionary<string, object?>
        {
            ["name"] = gameObject.name,
            ["id"] = id,
            ["techType"] = CraftData.GetTechType(gameObject).ToString(),
            ["position"] = Round(gameObject.transform.position)
        };
    }

    public static double[] Round(Vector3 v) => [Math.Round(v.x, 3), Math.Round(v.y, 3), Math.Round(v.z, 3)];

    private static bool IsSimple(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Vector3) || type == typeof(Quaternion) || type == typeof(NitroxId) || typeof(UnityEngine.Object).IsAssignableFrom(type);

    private static object RequireInstance(object? instance, Type type, string member) =>
        instance ?? throw new InvalidOperationException($"{type.Name}.{member} is an instance member, but the path has no instance (use an entity target or Type.main...)");

    private static MemberInfo? FindMember(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name, ALL_MEMBERS | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                return field;
            }
            PropertyInfo? property = current.GetProperties(ALL_MEMBERS | BindingFlags.DeclaredOnly).FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0);
            if (property != null)
            {
                return property;
            }
        }
        return null;
    }

    private static IEnumerable<MethodInfo> AllMethods(Type type)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            foreach (MethodInfo method in current.GetMethods(ALL_MEMBERS | BindingFlags.DeclaredOnly))
            {
                yield return method;
            }
        }
    }

    private static Type MemberType(MemberInfo member) => member switch
    {
        FieldInfo field => field.FieldType,
        PropertyInfo property => property.PropertyType,
        _ => typeof(object)
    };

    private static string Signature(MethodInfo method) => $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})";

    private static bool IsGuid(string token)
    {
        try
        {
            _ = new Guid(token);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static Dictionary<string, Type> BuildTypeIndex()
    {
        Dictionary<string, Type> index = new(StringComparer.Ordinal);
        IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name switch
        {
            "Assembly-CSharp" => 0,
            "Assembly-CSharp-firstpass" => 1,
            "NitroxClient" => 2,
            _ when a.GetName().Name.StartsWith("UnityEngine") => 3,
            _ => 4
        });
        foreach (Assembly assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }
            foreach (Type type in types)
            {
                if (type.FullName != null && !index.ContainsKey(type.FullName))
                {
                    index[type.FullName] = type;
                }
                if (!index.ContainsKey(type.Name))
                {
                    index[type.Name] = type;
                }
            }
        }
        return index;
    }
}
#endif
