using System;
using System.Collections.Concurrent;
using System.Reflection;
using SkyHook;
using UnityEngine;

namespace Argon.Compat;

/// <summary>
/// SkyHook's key mapper was published as <c>SkyHookKeyMapper</c> or <c>AsyncKeyMapper</c> depending on the
/// game release; bind whichever exists instead of hard-linking one. Pattern from Quartz (GPL-3.0) GameApi.
/// </summary>
internal static class HookKeyMapper
{
    private static readonly Type? MapperType = ResolveMapperType();
    private static readonly Func<KeyLabel, KeyCode>? ToUnityFast =
        Bind<Func<KeyLabel, KeyCode>>("SkyHookKeyToUnityKey", "AsyncKeyToUnityKey");
    private static readonly Func<KeyCode, KeyLabel>? FromUnityFast =
        Bind<Func<KeyCode, KeyLabel>>("UnityKeyToSkyHookKey", "UnityKeyToAsyncKey");
    private static readonly Func<ushort, KeyLabel>? NativeFast =
        Bind<Func<ushort, KeyLabel>>("NativeKeyCodeToKeyLabel");
    private static readonly ConcurrentDictionary<KeyCode, KeyLabel> FromUnityCache = new ConcurrentDictionary<KeyCode, KeyLabel>();

    internal static KeyCode ToUnity(KeyLabel label)
    {
        try
        {
            return ToUnityFast != null ? ToUnityFast(label) : KeyCode.None;
        }
        catch
        {
            return KeyCode.None;
        }
    }

    /// <summary>Falls back to a reverse scan of <see cref="ToUnity"/> when the mapper has no reverse method.</summary>
    internal static KeyLabel FromUnity(KeyCode keyCode)
    {
        if (FromUnityFast != null)
        {
            try
            {
                return FromUnityFast(keyCode);
            }
            catch
            {
                return KeyLabel.Unknown;
            }
        }

        return FromUnityCache.GetOrAdd(keyCode, code =>
        {
            foreach (KeyLabel label in Enum.GetValues(typeof(KeyLabel)))
            {
                if (ToUnity(label) == code) return label;
            }

            return KeyLabel.Unknown;
        });
    }

    internal static KeyLabel FromNative(ushort nativeKey)
    {
        try
        {
            return NativeFast != null ? NativeFast(nativeKey) : KeyLabel.Unknown;
        }
        catch
        {
            return KeyLabel.Unknown;
        }
    }

    private static Type? ResolveMapperType()
    {
        try
        {
            var assembly = typeof(SkyHookManager).Assembly;
            return assembly.GetType("SkyHook.SkyHookKeyMapper") ?? assembly.GetType("SkyHook.AsyncKeyMapper");
        }
        catch
        {
            return null;
        }
    }

    private static T? Bind<T>(params string[] names) where T : Delegate
    {
        foreach (var name in names)
        {
            var method = Refl.Method(MapperType, name, 1);
            if (method == null || !method.IsStatic) continue;
            try
            {
                return (T)Delegate.CreateDelegate(typeof(T), method);
            }
            catch
            {
                // Signature differs on this release; try the next name.
            }
        }

        return null;
    }
}
