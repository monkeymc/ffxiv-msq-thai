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

    private readonly IAddonLifecycle _addonLifecycle;
    private readonly DialogueDictionary _dictionary;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;

    public string[] CurrentTokens { get; private set; } = Array.Empty<string>();

    public TalkHook(
        IAddonLifecycle addonLifecycle,
        DialogueDictionary dictionary,
        IClientState clientState,
        IObjectTable objectTable)
    {
        _addonLifecycle = addonLifecycle;
        _dictionary     = dictionary;
        _clientState    = clientState;
        _objectTable    = objectTable;

        _addonLifecycle.RegisterListener(AddonEvent.PreRefresh, AddonName, OnPreRefresh);
        _addonLifecycle.RegisterListener(AddonEvent.PreHide,    AddonName, OnHide);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnHide);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PreRefresh, AddonName, OnPreRefresh);
        _addonLifecycle.UnregisterListener(AddonEvent.PreHide,    AddonName, OnHide);
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

        // Normalize and strip the player's character name so our generic JSON
        // placeholder ("Forename Surname" / "Forename") aligns with whatever the player chose.
        var displayEn  = DialogueDictionary.NormalizeEnglishKey(textEn);
        var fullName   = _objectTable.LocalPlayer?.Name.ToString() ?? string.Empty;
        var firstName  = !string.IsNullOrEmpty(fullName) ? fullName.Split(' ')[0] : string.Empty;

        if (!string.IsNullOrEmpty(fullName) && displayEn.Contains(fullName))
        {
            displayEn = displayEn.Replace(fullName, "Forename Surname");
        }
        else if (!string.IsNullOrEmpty(firstName) && displayEn.Contains(firstName))
        {
            displayEn = displayEn.Replace(firstName, "Forename");
        }

        if (displayEn.Length == 0)
        {
            CurrentTokens = Array.Empty<string>();
            return;
        }

        var lookupKey = DialogueDictionary.ToPureAlphanumericKey(displayEn);

        string? rawThai = null;

        // Step A: Strict Exact Probing (100%)
        if (_dictionary.TryGetThai(lookupKey, out var exactTh))
        {
            rawThai = exactTh;
        }
        // Step B: Conditional Name-Gated Fuzzy Probing (85%)
        else
        {
            bool textContainsPlayerName = (!string.IsNullOrEmpty(fullName) && textEn.Contains(fullName, StringComparison.OrdinalIgnoreCase)) ||
                                          (!string.IsNullOrEmpty(firstName) && textEn.Contains(firstName, StringComparison.OrdinalIgnoreCase));

            if (textContainsPlayerName)
            {
                var fuzzyResult = _dictionary.FindFuzzyMatch(lookupKey, threshold: 0.85f);
                if (fuzzyResult != null)
                {
                    rawThai = fuzzyResult.ThaiText;
                }
            }
        }

        // Step C: Absolute Miss Gate (The Suppress Rule)
        if (rawThai == null)
        {
            CurrentTokens = Array.Empty<string>();
            return;
        }

        var clean = SanitizeThai(rawThai);
        if (string.IsNullOrWhiteSpace(clean))
        {
            CurrentTokens = Array.Empty<string>();
            return;
        }

        CurrentTokens = ThaiWordSegmenter.Segment(clean);
    }

    private void OnHide(AddonEvent type, AddonArgs args)
        => CurrentTokens = Array.Empty<string>();

    /// <summary>
    /// Strips game artefacts and AI "no-translation" markers from Thai output.
    /// </summary>
    internal static string SanitizeThai(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        if (DashOnlyLine.IsMatch(text)) return string.Empty;

        text = SeControl.Replace(text, string.Empty);
        text = InlineDashRun.Replace(text, string.Empty);
        return text.Trim();
    }
}
