using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FfxivMsqThai.Services;

public sealed class TalkHook : IDisposable
{
    private const string AddonName = "Talk";
    private const string NamePlaceholder = "Forename Surname";

    // ── Text sanitization ────────────────────────────────────────────────────
    // Strip lines that are purely dashes/hyphens (AI "no-translation" markers)
    private static readonly Regex DashOnlyLine =
        new(@"^[\-–—\s]+$", RegexOptions.Compiled);

    // Remove inline sequences of 3+ dashes (layout artefacts from game payloads)
    private static readonly Regex InlineDashRun =
        new(@"[\-–—]{3,}", RegexOptions.Compiled);

    // Strip raw SeString control-code bytes that survive the string decoder
    private static readonly Regex SeControl =
        new(@"[\x02][\s\S]{1,4}[\x03]|[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]",
            RegexOptions.Compiled);

    // ────────────────────────────────────────────────────────────────────────

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly DialogueDictionary _dictionary;
    private readonly IPluginLog _log;

    public string[] CurrentTokens { get; private set; } = Array.Empty<string>();

    public TalkHook(IAddonLifecycle addonLifecycle, DialogueDictionary dictionary, IPluginLog log)
    {
        _addonLifecycle = addonLifecycle;
        _dictionary = dictionary;
        _log = log;

        _addonLifecycle.RegisterListener(AddonEvent.PreRefresh, AddonName, OnPreRefresh);
        _addonLifecycle.RegisterListener(AddonEvent.PreHide, AddonName, OnHide);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnHide);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PreRefresh, AddonName, OnPreRefresh);
        _addonLifecycle.UnregisterListener(AddonEvent.PreHide, AddonName, OnHide);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnHide);
    }

    private unsafe void OnPreRefresh(AddonEvent type, AddonArgs args)
    {
        if (args is not AddonRefreshArgs refreshArgs) return;

        var atkValues = (AtkValue*)refreshArgs.AtkValues;
        if (atkValues == null || refreshArgs.AtkValueCount < 1) return;

        var textPtr = (nint)atkValues[0].String.Value;
        if (textPtr == 0) { CurrentTokens = Array.Empty<string>(); return; }

        var textEn = MemoryHelper.ReadSeStringAsString(out _, textPtr);
        if (string.IsNullOrWhiteSpace(textEn)) { CurrentTokens = Array.Empty<string>(); return; }

        // displayEn: human-readable form used for the English fallback token
        var displayEn = DialogueDictionary.NormalizeEnglishKey(textEn);
        if (displayEn.Length == 0) { CurrentTokens = Array.Empty<string>(); return; }

        // lookupKey: letters + digits only, lowercase — immune to punctuation/space/quote variance
        var lookupKey = DialogueDictionary.ToPureAlphanumericKey(displayEn);

        // TODO: replace NamePlaceholder with real player name once ClientStructs API confirmed
        if (!_dictionary.TryGetThai(lookupKey, out var raw) &&
            !_dictionary.TryGetThaiFuzzy(lookupKey, out raw))
        {
            CurrentTokens = new[] { displayEn };
            return;
        }

        var clean = SanitizeThai(raw);
        if (string.IsNullOrWhiteSpace(clean))
        {
            CurrentTokens = new[] { displayEn };
            return;
        }

        CurrentTokens = ThaiWordSegmenter.Segment(clean);
        _log.Debug($"[ffxiv-msq-thai] {displayEn[..Math.Min(40, displayEn.Length)]}…");
    }

    private void OnHide(AddonEvent type, AddonArgs args)
        => CurrentTokens = Array.Empty<string>();

    /// <summary>
    /// Strips game artefacts and AI "no-translation" markers from Thai output.
    /// </summary>
    internal static string SanitizeThai(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Drop the whole string if it is nothing but dashes (AI placeholder)
        if (DashOnlyLine.IsMatch(text)) return string.Empty;

        text = SeControl.Replace(text, string.Empty);     // control codes
        text = InlineDashRun.Replace(text, string.Empty); // "------" runs
        return text.Trim();
    }
}
