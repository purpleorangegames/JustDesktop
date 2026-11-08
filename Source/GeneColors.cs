using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

// in a pawn's genes, a colored corner on each gene says if you already have it: a genepack with only that gene (green),
// a genepack with it and other genes (yellow), someone in the colony who has it, the pawn itself included (red), or
// none of those (beige, only for pawns that aren't ours).
// Archite genes can't be extracted, so other pawns don't count for them. Hovering a gene says where it is
[StaticConstructorOnStartup]
public static class GeneColors
{
    private static readonly Color Missing = new(0.835f, 0.741f, 0.686f);   // d5bdaf
    private static readonly Color Someone = new(0.878f, 0.478f, 0.373f);   // e07a5f
    private static readonly Color Grouped = new(0.949f, 0.8f, 0.561f);     // f2cc8f
    private static readonly Color Alone = new(0.506f, 0.698f, 0.604f);     // 81b29a

    // the pawn whose genes are being drawn, set around GeneUIUtility.DrawGenesInfo
    public static Pawn Viewing;

    private enum Have { Missing, Someone, Grouped, Alone }

    private static Texture2D corner;

    // the top-left half of a square, cut along the diagonal from the bottom-left to the top-right, smooth on the edge
    private static Texture2D Corner
    {
        get
        {
            if (corner == null)
            {
                const int n = 128;
                corner = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                Color32[] px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        // texture rows go bottom up: the top-left half is where x + (n - 1 - y) < n - 1, i.e. x < y
                        float d = (y - x) / Mathf.Sqrt(2f);
                        byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(d + 0.5f) * 255f);
                        px[y * n + x] = new Color32(255, 255, 255, a);
                    }
                }
                corner.SetPixels32(px);
                corner.Apply(false, true);
            }
            return corner;
        }
    }

    // what the colony has, gathered every couple of seconds
    private static readonly Dictionary<GeneDef, List<Genepack>> packs = new();
    private static readonly Dictionary<GeneDef, List<Pawn>> holders = new();
    private static float gathered = -999f;

    private static void Gather()
    {
        if (Time.realtimeSinceStartup - gathered < 2f)
        {
            return;
        }
        gathered = Time.realtimeSinceStartup;
        packs.Clear();
        holders.Clear();
        foreach (Map map in Find.Maps)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b.TryGetComp<CompGenepackContainer>() is { } bank)
                {
                    foreach (Genepack pack in bank.ContainedGenepacks)
                    {
                        AddPack(pack);
                    }
                }
            }
            if (map.IsPlayerHome)
            {
                foreach (Thing t in map.listerThings.ThingsOfDef(ThingDefOf.Genepack))
                {
                    AddPack(t as Genepack);
                }
            }
        }
        foreach (Caravan caravan in Find.WorldObjects.Caravans)
        {
            if (caravan.IsPlayerControlled)
            {
                foreach (Thing t in CaravanInventoryUtility.AllInventoryItems(caravan))
                {
                    AddPack(t as Genepack);
                }
            }
        }
        foreach (Pawn p in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
        {
            if (p.genes == null || !p.RaceProps.Humanlike || p.Faction != Faction.OfPlayer && !p.IsPrisonerOfColony)
            {
                continue;
            }
            foreach (Gene g in p.genes.GenesListForReading)
            {
                if (g.def.biostatArc > 0)
                {
                    continue;
                }
                if (!holders.TryGetValue(g.def, out List<Pawn> list))
                {
                    holders[g.def] = list = new List<Pawn>();
                }
                if (!list.Contains(p))
                {
                    list.Add(p);
                }
            }
        }
    }

    private static void AddPack(Genepack pack)
    {
        if (pack?.GeneSet == null)
        {
            return;
        }
        foreach (GeneDef def in pack.GeneSet.GenesListForReading)
        {
            if (!packs.TryGetValue(def, out List<Genepack> list))
            {
                packs[def] = list = new List<Genepack>();
            }
            list.Add(pack);
        }
    }

    private static Have Status(GeneDef def, Pawn viewing, out List<Genepack> inPacks, out List<Pawn> others)
    {
        packs.TryGetValue(def, out inPacks);
        // the pawn being looked at counts when it's ours: a colonist's genes are always at least in the colony
        holders.TryGetValue(def, out List<Pawn> all);
        bool inColony = all is { Count: > 0 };
        others = all?.Where(p => p != viewing).ToList();
        if (inPacks != null && inPacks.Any(p => p.GeneSet.GenesListForReading.Count == 1))
        {
            return Have.Alone;
        }
        if (inPacks is { Count: > 0 })
        {
            return Have.Grouped;
        }
        return inColony ? Have.Someone : Have.Missing;
    }

    private static Color ColorOf(Have have) => have switch { Have.Alone => Alone, Have.Grouped => Grouped, Have.Someone => Someone, _ => Missing };

    public static void Draw(Gene gene, Rect rect)
    {
        if (!JustDesktopMod.Settings.geneColors || Viewing == null || !ModsConfig.BiotechActive || Current.ProgramState != ProgramState.Playing)
        {
            return;
        }
        Gather();
        Have have = Status(gene.def, Viewing, out List<Genepack> inPacks, out List<Pawn> others);
        Color c = ColorOf(have);
        GUI.color = new Color(c.r, c.g, c.b, 0.6f);
        GUI.DrawTexture(rect, Corner);
        GUI.color = Color.white;
        if (Mouse.IsOver(rect))
        {
            TooltipHandler.TipRegion(rect, new TipSignal(() => Tip(gene.def, have, inPacks, others), gene.def.shortHash * 7 + 91283));
        }
    }

    private static string Tip(GeneDef def, Have have, List<Genepack> inPacks, List<Pawn> others)
    {
        Color c = ColorOf(have);
        string head = have switch
        {
            Have.Alone => "In a genepack on its own",
            Have.Grouped => "Only in genepacks with other genes",
            Have.Someone => "Not in a genepack, but someone in the colony has it",
            _ => def.biostatArc > 0 ? "Not in a genepack" : "Not in a genepack, and nobody in the colony has it"
        };
        string tip = head.Colorize(c);
        if (inPacks is { Count: > 0 })
        {
            foreach (Genepack pack in inPacks.Take(5))
            {
                List<GeneDef> rest = pack.GeneSet.GenesListForReading.Where(g => g != def).ToList();
                tip += "\n  - " + (rest.Count == 0 ? def.LabelCap.ToString() : def.LabelCap + " + " + rest.Select(g => g.label).ToCommaList());
            }
            if (inPacks.Count > 5)
            {
                tip += "\n  - " + (inPacks.Count - 5) + " more";
            }
        }
        if (def.biostatArc == 0 && others is { Count: > 0 })
        {
            tip += "\nAlso has it: " + others.Take(8).Select(p => p.LabelShort).ToCommaList() + (others.Count > 8 ? " and " + (others.Count - 8) + " more" : "");
        }
        return tip;
    }
}

[HarmonyPatch(typeof(GeneUIUtility), nameof(GeneUIUtility.DrawGenesInfo))]
public static class GeneColors_GenesInfo
{
    public static void Prefix(Thing target) => GeneColors.Viewing = target as Pawn;

    public static void Finalizer() => GeneColors.Viewing = null;
}

[HarmonyPatch(typeof(GeneUIUtility), nameof(GeneUIUtility.DrawGene))]
public static class GeneColors_Gene
{
    public static void Prefix(Gene gene, Rect geneRect) => GeneColors.Draw(gene, geneRect);
}
