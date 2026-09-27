using System;
using System.Collections.Generic;
using Argon.Compat;
using UnityEngine;

namespace Argon.Localization;

/// <summary>Resolves the player's language the way the game does. Adapted from Quartz (GPL-3.0) GameLanguage.</summary>
internal static class GameLanguage
{
    private static readonly Refl.Member PersistenceLanguage = new Refl.Member(typeof(Persistence), "language");
    private static readonly Refl.Member RdStringLanguage = new Refl.Member(Refl.Type("RDString"), "language");

    // Persistence.language is the in-game setting. RDString.language is that value clamped to the game's
    // shipped languages and stays default(Afrikaans) until RDString.Setup() runs; ADOFAI has no Afrikaans,
    // so that only means "not ready yet".
    internal static SystemLanguage Detect()
    {
        if (PersistenceLanguage.Get(null) is SystemLanguage saved && saved != SystemLanguage.Unknown) return saved;
        if (RdStringLanguage.Get(null) is SystemLanguage active && active != SystemLanguage.Afrikaans) return active;
        try
        {
            return Application.systemLanguage;
        }
        catch
        {
            return SystemLanguage.Unknown;
        }
    }

    private static readonly Dictionary<SystemLanguage, string[]> Candidates = new Dictionary<SystemLanguage, string[]>
    {
        [SystemLanguage.Arabic] = new[] { "ar-SA", "ar" },
        [SystemLanguage.Chinese] = new[] { "zh-CN", "zh-Hans", "zh-TW", "zh-Hant", "zh" },
        [SystemLanguage.ChineseSimplified] = new[] { "zh-CN", "zh-Hans", "zh-SG", "zh" },
        [SystemLanguage.ChineseTraditional] = new[] { "zh-TW", "zh-Hant", "zh-HK", "zh" },
        [SystemLanguage.Czech] = new[] { "cs-CZ", "cs" },
        [SystemLanguage.Dutch] = new[] { "nl-NL", "nl" },
        [SystemLanguage.English] = new[] { "en-US", "en-GB", "en" },
        [SystemLanguage.French] = new[] { "fr-FR", "fr-CA", "fr" },
        [SystemLanguage.German] = new[] { "de-DE", "de" },
        [SystemLanguage.Indonesian] = new[] { "id-ID", "id", "in" },
        [SystemLanguage.Italian] = new[] { "it-IT", "it" },
        [SystemLanguage.Japanese] = new[] { "ja-JP", "ja" },
        [SystemLanguage.Korean] = new[] { "ko-KR", "ko" },
        [SystemLanguage.Polish] = new[] { "pl-PL", "pl" },
        [SystemLanguage.Portuguese] = new[] { "pt-BR", "pt-PT", "pt" },
        [SystemLanguage.Russian] = new[] { "ru-RU", "ru" },
        [SystemLanguage.Spanish] = new[] { "es-ES", "es-419", "es-MX", "es" },
        [SystemLanguage.Thai] = new[] { "th-TH", "th" },
        [SystemLanguage.Turkish] = new[] { "tr-TR", "tr" },
        [SystemLanguage.Ukrainian] = new[] { "uk-UA", "uk" },
        [SystemLanguage.Vietnamese] = new[] { "vi-VN", "vi" },
    };

    // Exact tag first across every candidate, then the primary subtag, so a pt-BR player still
    // lands on a pt-PT translation rather than falling back to English.
    internal static string? Match(SystemLanguage language, IEnumerable<string> available)
    {
        if (!Candidates.TryGetValue(language, out var codes)) return null;
        var pool = new List<string>(available);
        foreach (var candidate in codes)
        foreach (var code in pool)
            if (string.Equals(code, candidate, StringComparison.OrdinalIgnoreCase)) return code;
        foreach (var candidate in codes)
        foreach (var code in pool)
            if (string.Equals(Primary(code), Primary(candidate), StringComparison.OrdinalIgnoreCase)) return code;
        return null;
    }

    private static string Primary(string code)
    {
        var dash = code.IndexOf('-');
        return dash < 0 ? code : code.Substring(0, dash);
    }
}
