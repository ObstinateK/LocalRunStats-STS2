using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalRunStats;

// Mechanical-synergy detection via plain keyword matching against card/relic
// description text. There's no structured "this card is about Vulnerable"
// tagging in the game's data (CardTag is just Strike/Defend/Minion/etc.), so
// this is a text heuristic, not semantic understanding — it can't tell
// "applies Vulnerable" from "benefits from Vulnerable", only that both
// mention it. Good enough to surface "Dominate applies Vulnerable, and your
// deck already has 2 other Vulnerable cards" style signals.
public static class SynergyKeywords
{
    // STS2's actual synergy mechanics (per sts2companion.com/synergies, checked
    // 2026-08-28 — STS2 has different characters/mechanics than STS1, e.g. no
    // Watcher/Stance, but does have Necrobinder/Osty summons and Defect orbs).
    // Earlier version of this list carried over STS1-only terms (Stance,
    // Mantra, Divinity, Metallicize, etc.) that don't appear in STS2 card text
    // at all — replaced with the confirmed STS2 mechanic list below.
    private static readonly string[] Keywords =
    {
        "Vulnerable", "Weak", "Poison",                    // Debuffs
        "Strength", "Dexterity", "Block",                  // Buffs
        "Exhaust", "Discard", "Retain", "Draw",             // Card manipulation
        "Energy",                                           // Resources
        "Orb", "Lightning", "Frost", "Dark", "Plasma",       // Defect orbs
        "Osty", "Summon",                                    // Necrobinder
        "Ethereal", "Wound", "Burn", "Dazed", "Void",        // Other / status cards
    };

    public static HashSet<string> ExtractKeywords(LocString description)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (description == null || !description.Exists()) return found;

        var text = description.GetRawText();
        if (string.IsNullOrEmpty(text)) return found;

        foreach (var keyword in Keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(keyword);
            }
        }

        return found;
    }

    public static HashSet<string> ExtractKeywords(CardModel card) => ExtractKeywords(card.Description);
    public static HashSet<string> ExtractKeywords(RelicModel relic) => ExtractKeywords(relic.DynamicDescription);

    public static HashSet<string> ExtractDeckKeywords(IEnumerable<CardModel> deck)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in deck)
        {
            all.UnionWith(ExtractKeywords(card));
        }
        return all;
    }

    public static HashSet<string> ExtractRelicKeywords(IEnumerable<RelicModel> relics)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relic in relics)
        {
            all.UnionWith(ExtractKeywords(relic));
        }
        return all;
    }

    public static List<string> Overlap(HashSet<string> offered, HashSet<string> deckKeywords)
    {
        return offered.Where(deckKeywords.Contains).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
