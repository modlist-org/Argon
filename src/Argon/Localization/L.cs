using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Argon.Localization;

/// <summary>
/// UI string lookup. Built-in languages are embedded JSON (Lang/*.json); extra or overriding files can be
/// dropped into <c>persistentDataPath/Argon/Lang</c>. File format and the "0KTL" sentinel follow Quartz's
/// (GPL-3.0) Translator so its translation tooling can be reused:
/// <code>{ "ko-KR": { "0KTL": "DO_NOT_TRANSLATE_THIS_KEY!", "some.key": "텍스트" } }</code>
/// Lookup order: selected language → en-US → the call-site fallback → the key itself.
/// </summary>
internal static class L
{
    internal const string Auto = "auto";
    internal const string FallbackLanguage = "en-US";
    private const string SentinelKey = "0KTL";
    private const string SentinelValue = "DO_NOT_TRANSLATE_THIS_KEY!";

    private static readonly Dictionary<string, Dictionary<string, string>> Tables =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string>? _active;
    private static Dictionary<string, string>? _fallback;
    private static bool _loaded;

    internal static string Language { get; private set; } = FallbackLanguage;

    /// <summary>Raised on the main thread after the active language changes.</summary>
    internal static event Action? Changed;

    internal static IReadOnlyList<string> Available
    {
        get
        {
            EnsureLoaded();
            var codes = new List<string>(Tables.Keys);
            codes.Sort(StringComparer.OrdinalIgnoreCase);
            return codes;
        }
    }

    internal static string T(string key, string? fallback = null)
    {
        EnsureLoaded();
        if (_active != null && _active.TryGetValue(key, out var value)) return value;
        if (_fallback != null && _fallback.TryGetValue(key, out value)) return value;
        return fallback ?? key;
    }

    internal static string F(string key, params object?[] args)
    {
        var format = T(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    /// <summary>The language's own name (e.g. "한국어"), from the file's "language.name" entry.</summary>
    internal static string NativeName(string code)
    {
        EnsureLoaded();
        return Tables.TryGetValue(code, out var table) && table.TryGetValue("language.name", out var name) ? name : code;
    }

    /// <summary>Selects <paramref name="preference"/> ("auto" or a code such as "ko-KR").</summary>
    internal static void SetLanguage(string? preference)
    {
        EnsureLoaded();
        var code = string.IsNullOrWhiteSpace(preference) || string.Equals(preference, Auto, StringComparison.OrdinalIgnoreCase)
            ? GameLanguage.Match(GameLanguage.Detect(), Tables.Keys) ?? FallbackLanguage
            : Tables.ContainsKey(preference!) ? preference! : FallbackLanguage;
        if (string.Equals(code, Language, StringComparison.OrdinalIgnoreCase) && _active != null) return;

        Language = code;
        Tables.TryGetValue(code, out _active);
        Changed?.Invoke();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        var assembly = typeof(L).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith("Argon.Lang.", StringComparison.Ordinal) || !resource.EndsWith(".json", StringComparison.Ordinal)) continue;
            try
            {
                using (var stream = assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream!))
                {
                    Merge(reader.ReadToEnd(), resource);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Argon] Built-in language file '{resource}' is invalid: {exception.Message}");
            }
        }

        try
        {
            var userDirectory = Path.Combine(Application.persistentDataPath, "Argon", "Lang");
            if (Directory.Exists(userDirectory))
            {
                foreach (var file in Directory.GetFiles(userDirectory, "*.json"))
                {
                    try
                    {
                        Merge(File.ReadAllText(file), file);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[Argon] Language file '{file}' is invalid: {exception.Message}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Argon] Could not scan user language files: {exception.Message}");
        }

        Tables.TryGetValue(FallbackLanguage, out _fallback);
        Tables.TryGetValue(Language, out _active);
    }

    // Later sources win per key, so a user file can override single built-in strings.
    private static void Merge(string json, string source)
    {
        foreach (var property in JObject.Parse(json).Properties())
        {
            if (!(property.Value is JObject block)) continue;
            if (block.Value<string>(SentinelKey) != SentinelValue)
            {
                Debug.LogWarning($"[Argon] Skipping language block '{property.Name}' in '{source}': missing {SentinelKey} sentinel.");
                continue;
            }

            if (!Tables.TryGetValue(property.Name, out var table))
            {
                table = new Dictionary<string, string>(StringComparer.Ordinal);
                Tables[property.Name] = table;
            }

            foreach (var entry in block.Properties())
            {
                if (entry.Name == SentinelKey || entry.Value.Type != JTokenType.String) continue;
                table[entry.Name] = entry.Value.ToString();
            }
        }
    }
}
