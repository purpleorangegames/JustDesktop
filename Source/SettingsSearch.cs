using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the search box over the pages in the mod options: every page is drawn once out of sight while its labels, tooltips
// and where they sit are written down, then the ones matching what was typed are listed, a click goes to that page
public static class SettingsSearch
{
    public struct Entry
    {
        public int page;
        public string label;
        public string tip;
        public float y;
    }

    public static readonly List<Entry> Index = new();
    // the page being written down, -1 when not
    public static int Recording = -1;

    public static bool Matches(Entry e, string query)
    {
        return e.label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || e.tip != null && e.tip.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

[HarmonyPatch]
public static class SettingsSearch_Listing
{
    private static readonly HashSet<string> names = new() { "Label", "CheckboxLabeled", "ButtonText", "ButtonTextLabeled", "ButtonTextLabeledPct", "SliderLabeled" };

    public static IEnumerable<MethodBase> TargetMethods()
    {
        return typeof(Listing_Standard).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => names.Contains(m.Name) && m.GetParameters().Length > 0 && (m.GetParameters()[0].ParameterType == typeof(string) || m.GetParameters()[0].ParameterType == typeof(TaggedString)));
    }

    public static void Prefix(Listing_Standard __instance, object[] __args, MethodBase __originalMethod)
    {
        if (SettingsSearch.Recording < 0 || __args[0] == null)
        {
            return;
        }
        string label = __args[0].ToString();
        if (label.NullOrEmpty())
        {
            return;
        }
        string tip = null;
        ParameterInfo[] ps = __originalMethod.GetParameters();
        for (int i = 1; i < ps.Length; i++)
        {
            if (__args[i] is string s && ps[i].Name == "tooltip")
            {
                tip = s;
            }
            else if (__args[i] is TipSignal signal)
            {
                tip = signal.text;
            }
        }
        SettingsSearch.Index.Add(new SettingsSearch.Entry { page = SettingsSearch.Recording, label = label.StripTags(), tip = tip, y = __instance.CurHeight });
    }
}
