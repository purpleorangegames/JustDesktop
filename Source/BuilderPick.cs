using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JustDesktop;

// who builds it: a button on the plan of anything that gets a quality (and on its frame) to pick the colonist that
// builds it, from the ones with construction on in the work tab. Then only they build it, anyone can still bring the
// materials. Kept with the save
public class BuilderPick : GameComponent
{
    private Dictionary<Thing, Pawn> builders = new();
    private List<Thing> keys;
    private List<Pawn> values;

    public BuilderPick(Game game)
    {
    }

    public static BuilderPick Get => Current.Game?.GetComponent<BuilderPick>();

    public static bool On => JustDesktopMod.Settings.builderPick;

    // a plan or frame of something that gets a quality
    public static bool Fits(Thing t) => t is Blueprint_Build or Frame && t.Faction == Faction.OfPlayer && t.def.entityDefToBuild is ThingDef def && def.HasComp(typeof(CompQuality));

    // the one picked, while they can still do it; null means anyone
    public Pawn BuilderOf(Thing t)
    {
        if (t == null || !builders.TryGetValue(t, out Pawn p))
        {
            return null;
        }
        if (p == null || p.Dead || p.Destroyed || p.Faction != Faction.OfPlayer)
        {
            builders.Remove(t);
            return null;
        }
        return p;
    }

    public void Set(Thing t, Pawn p)
    {
        if (p == null)
        {
            builders.Remove(t);
        }
        else
        {
            builders[t] = p;
        }
    }

    // the plan became a frame: the frame keeps the builder
    public void Moved(Thing from, Thing to)
    {
        if (from != null && to != null && builders.TryGetValue(from, out Pawn p))
        {
            builders.Remove(from);
            builders[to] = p;
        }
    }

    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            builders.RemoveAll(kv => kv.Key == null || kv.Key.Destroyed || kv.Value == null || kv.Value.Dead);
        }
        Scribe_Collections.Look(ref builders, "builders", LookMode.Reference, LookMode.Reference, ref keys, ref values);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            builders ??= new Dictionary<Thing, Pawn>();
            builders.RemoveAll(kv => kv.Key == null || kv.Value == null);
        }
    }

    // what makes them build better than their skill says: an inspiration of creativity, or a role with a quality bonus
    public static string Bonus(Pawn p)
    {
        if (p.InspirationDef == InspirationDefOf.Inspired_Creativity)
        {
            return p.InspirationDef.LabelCap;
        }
        if (ModsConfig.IdeologyActive && p.Ideo?.GetRole(p) is { } role && role.def.roleEffects != null && role.def.roleEffects.Any(e => e is RoleEffect_ProductionQualityOffset))
        {
            return role.LabelCap;
        }
        return null;
    }

    public static int Skill(Pawn p) => p.RaceProps.IsMechanoid ? p.RaceProps.mechFixedSkillLevel : p.skills?.GetSkill(SkillDefOf.Construction).Level ?? 0;

    // everyone on the map with construction on in the work tab: the ones with a bonus first, then by skill
    public static List<Pawn> Candidates(Map map) => map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
        .Where(p => p.workSettings != null && p.workSettings.EverWork && p.workSettings.WorkIsActive(WorkTypeDefOf.Construction) && !p.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
        .OrderByDescending(p => Bonus(p) != null).ThenByDescending(Skill).ThenBy(p => p.LabelShort).ToList();

    private static Texture2D icon;
    private static Texture2D Icon => icon ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/Builder");

    public static Gizmo Button(Thing t)
    {
        Pawn p = Get.BuilderOf(t);
        return new Command_Action
        {
            defaultLabel = p != null ? "Builder: " + p.LabelShort : "Builder: anyone",
            defaultDesc = "Pick who builds it. Only they build it, anyone can still bring the materials.\n\nThe list has everyone with construction on in the work tab, the ones with something that makes them build better (an inspiration of creativity, a role) at the top, then by skill. With several selected it goes for all of them",
            icon = p != null ? PortraitsCache.Get(p, new Vector2(75f, 75f), Rot4.South, default, 1.4f) : Icon,
            // one button for all the selected plans, whoever they're set to
            groupKeyIgnoreContent = 784412,
            action = () => Menu(t.Map)
        };
    }

    public static void Menu(Map map)
    {
        List<Thing> targets = Find.Selector.SelectedObjects.OfType<Thing>().Where(Fits).ToList();
        var options = new List<FloatMenuOption>();
        List<Pawn> pawns = Candidates(map);
        int order = pawns.Count + 1;
        options.Add(new FloatMenuOption("Anyone", () => Pick(targets, null), MenuOptionPriority.Default, orderInPriority: order--));
        foreach (Pawn p in pawns)
        {
            Pawn pawn = p;
            string label = p.LabelShort + "  ·  " + SkillDefOf.Construction.LabelCap + " " + Skill(p);
            if (p.skills?.GetSkill(SkillDefOf.Construction) is { passion: > Passion.None } skill)
            {
                label += skill.passion == Passion.Major ? " ++" : " +";
            }
            if (Bonus(p) is { } bonus)
            {
                label += "  ·  " + bonus.Colorize(Style.Accent);
            }
            options.Add(new FloatMenuOption(label, () => Pick(targets, pawn), p, Color.white, orderInPriority: order--));
        }
        if (pawns.Count == 0)
        {
            options.Add(new FloatMenuOption("Nobody here has construction on in the work tab", null));
        }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private static void Pick(List<Thing> targets, Pawn p)
    {
        foreach (Thing t in targets)
        {
            Get.Set(t, p);
        }
    }
}

[HarmonyPatch(typeof(Blueprint_Build), nameof(Blueprint_Build.GetGizmos))]
public static class BuilderPick_BlueprintGizmos
{
    public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Blueprint_Build __instance)
    {
        foreach (Gizmo g in gizmos)
        {
            yield return g;
        }
        if (BuilderPick.On && BuilderPick.Get != null && BuilderPick.Fits(__instance))
        {
            yield return BuilderPick.Button(__instance);
        }
    }
}

[HarmonyPatch(typeof(Frame), nameof(Frame.GetGizmos))]
public static class BuilderPick_FrameGizmos
{
    public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Frame __instance)
    {
        foreach (Gizmo g in gizmos)
        {
            yield return g;
        }
        if (BuilderPick.On && BuilderPick.Get != null && BuilderPick.Fits(__instance))
        {
            yield return BuilderPick.Button(__instance);
        }
    }
}

// the builder in the text about the plan and the frame
[HarmonyPatch(typeof(Blueprint_Build), nameof(Blueprint_Build.GetInspectString))]
public static class BuilderPick_BlueprintText
{
    public static void Postfix(Blueprint_Build __instance, ref string __result) => BuilderPick_FrameText.Add(__instance, ref __result);
}

[HarmonyPatch(typeof(Frame), nameof(Frame.GetInspectString))]
public static class BuilderPick_FrameText
{
    public static void Postfix(Frame __instance, ref string __result) => Add(__instance, ref __result);

    public static void Add(Thing t, ref string text)
    {
        if (BuilderPick.On && BuilderPick.Get?.BuilderOf(t) is { } p)
        {
            text = text.NullOrEmpty() ? "Builder: " + p.LabelShort : text.TrimEndNewlines() + "\nBuilder: " + p.LabelShort;
        }
    }
}

[HarmonyPatch(typeof(Blueprint), nameof(Blueprint.TryReplaceWithSolidThing))]
public static class BuilderPick_BlueprintToFrame
{
    public static void Postfix(Blueprint __instance, bool __result, Thing createdThing)
    {
        if (__result)
        {
            BuilderPick.Get?.Moved(__instance, createdThing);
        }
    }
}

// only the one picked builds it, unless you order someone else to with a right click. Clearing what's in the way
// stays open to anyone
[HarmonyPatch(typeof(WorkGiver_ConstructFinishFrames), nameof(WorkGiver_ConstructFinishFrames.JobOnThing))]
public static class BuilderPick_OnlyThem
{
    public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
    {
        if (__result != null && __result.def == JobDefOf.FinishFrame && !forced && BuilderPick.On && BuilderPick.Get?.BuilderOf(t) is { } builder && builder != pawn)
        {
            __result = null;
        }
    }
}
