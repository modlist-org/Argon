using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Argon.Compat;

/// <summary>
/// Never-throwing reflection helpers for game members that differ between ADOFAI releases.
/// Adapted from Quartz (GPL-3.0) Compat/Game/Refl.cs.
/// </summary>
internal static class Refl
{
    internal const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic |
                                          BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<(Type Owner, string Name, int ArgCount), MethodInfo?> MethodCache =
        new ConcurrentDictionary<(Type, string, int), MethodInfo?>();

    /// <summary>A property or field resolved from the first of several candidate names that exists.</summary>
    internal sealed class Member
    {
        private readonly PropertyInfo? _property;
        private readonly FieldInfo? _field;

        internal Member(Type? owner, params string[] names)
        {
            if (owner == null) return;
            foreach (var name in names)
            {
                PropertyInfo? property;
                try
                {
                    property = owner.GetProperty(name, Any);
                }
                catch (AmbiguousMatchException)
                {
                    property = Walk(owner, type => type.GetProperty(name, Declared));
                }

                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    _property = property;
                    return;
                }

                FieldInfo? field;
                try
                {
                    field = owner.GetField(name, Any);
                }
                catch (AmbiguousMatchException)
                {
                    field = Walk(owner, type => type.GetField(name, Declared));
                }

                if (field != null)
                {
                    _field = field;
                    return;
                }
            }
        }

        internal bool Exists => _property != null || _field != null;

        internal object? Get(object? instance)
        {
            try
            {
                if (_property != null) return _property.CanRead ? _property.GetValue(instance, null) : null;
                return _field?.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        internal T Get<T>(object? instance, T fallback) => Get(instance) is T value ? value : fallback;

        /// <summary>A compiled getter (property delegate or field ref), or null when the member is absent or mistyped.</summary>
        internal Func<TOwner, TValue>? BindGetter<TOwner, TValue>() where TOwner : class
        {
            try
            {
                var getter = _property != null && _property.CanRead ? _property.GetGetMethod(true) : null;
                if (getter != null && !getter.IsStatic && getter.ReturnType == typeof(TValue))
                {
                    return (Func<TOwner, TValue>)Delegate.CreateDelegate(typeof(Func<TOwner, TValue>), getter);
                }

                if (_field != null && !_field.IsStatic && _field.FieldType == typeof(TValue))
                {
                    var reference = AccessTools.FieldRefAccess<TOwner, TValue>(_field);
                    return owner => owner == null ? default! : reference(owner);
                }
            }
            catch
            {
                // Fall back to the slow boxed path.
            }

            return null;
        }

        private static T? Walk<T>(Type owner, Func<Type, T?> pick) where T : class
        {
            for (var type = owner; type != null; type = type.BaseType)
            {
                try
                {
                    var found = pick(type);
                    if (found != null) return found;
                }
                catch
                {
                    // Keep walking.
                }
            }

            return null;
        }
    }

    /// <summary>A typed reader that uses a compiled getter when possible and never throws.</summary>
    internal sealed class Reader<TOwner, TValue> where TOwner : class
    {
        private readonly Member _member;
        private readonly Func<TOwner, TValue>? _fast;

        internal Reader(params string[] names) : this(typeof(TOwner), names)
        {
        }

        internal Reader(Type? owner, params string[] names)
        {
            _member = new Member(owner, names);
            _fast = _member.BindGetter<TOwner, TValue>();
        }

        internal bool Exists => _member.Exists;

        internal TValue Get(TOwner? instance, TValue fallback)
        {
            if (instance == null || !_member.Exists) return fallback;
            if (_fast != null)
            {
                try
                {
                    return _fast(instance);
                }
                catch
                {
                    return fallback;
                }
            }

            return _member.Get(instance) is TValue value ? value : fallback;
        }
    }

    internal static Type? Type(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        try
        {
            var game = typeof(ADOBase).Assembly;
            return game.GetType(name) ?? game.GetType("ADOFAI." + name);
        }
        catch
        {
            return null;
        }
    }

    internal static MethodInfo? Method(Type? owner, string name, int argCount = -1)
    {
        if (owner == null || string.IsNullOrEmpty(name)) return null;
        return MethodCache.GetOrAdd((owner, name, argCount), key => ResolveMethod(key.Owner, key.Name, key.ArgCount));
    }

    private static MethodInfo? ResolveMethod(Type owner, string name, int argCount)
    {
        try
        {
            var all = owner.GetMethods(Any).Where(method => method.Name == name).ToArray();
            if (all.Length == 0) return null;
            if (argCount < 0) return all[0];
            return all.FirstOrDefault(method => method.GetParameters().Length == argCount)
                   ?? all.FirstOrDefault(method => method.GetParameters().Length >= argCount &&
                                                   method.GetParameters().Skip(argCount).All(parameter => parameter.IsOptional));
        }
        catch
        {
            return null;
        }
    }

    internal static object? Invoke(MethodInfo? method, object? instance, params object?[] args)
    {
        if (method == null) return null;
        try
        {
            var parameters = method.GetParameters();
            if (args.Length < parameters.Length)
            {
                var padded = new object?[parameters.Length];
                Array.Copy(args, padded, args.Length);
                for (var i = args.Length; i < parameters.Length; i++)
                {
                    padded[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
                }

                args = padded;
            }

            return method.Invoke(instance, args);
        }
        catch
        {
            return null;
        }
    }
}
