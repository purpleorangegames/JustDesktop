using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// caravan, transport pod and trade rows: an animal's gender and age stage next to its other icons (bonded, pregnant...),
// optionally colonists' too and whether an animal follows its master when drafted. Animals that differ in being
// trained to be released or following when drafted aren't grouped into one row. Like Animals Gender On Caravan,
// which keeps doing it when it's active
public static class Transfer
{
    private const float Icon = 24f;

    private static Settings S => JustDesktopMod.Settings;

    private static bool otherMod;
    private static bool checkedOther;

    public static bool On
    {
        get
        {
            if (!checkedOther)
            {
                otherMod = ModsConfig.IsActive("mrcool92.agoc");
                checkedOther = true;
            }
            return !otherMod;
        }
    }

    private static Texture2D _follow;
    private static Texture2D Follow => _follow ??= ContentFinder<Texture2D>.Get("UI/Icons/Animal/FollowDrafted");

    private static Rect Next(Rect row, ref float curX)
    {
        curX -= Icon;
        return new Rect(curX, (row.height - Icon) / 2f, Icon, Icon);
    }

    public static void Icons(Transferable trad, Rect rect, ref float curX)
    {
        if (!On || trad.AnyThing is not Pawn p)
        {
            return;
        }
        bool animal = p.RaceProps.Animal;
        if (!animal && !(p.RaceProps.Humanlike && p.RaceProps.hasGenders))
        {
            return;
        }
        if (animal ? S.tradeAnimalAge : S.tradeColonistAge)
        {
            Texture2D icon = p.ageTracker.CurLifeStageRace.GetIcon(p);
            if (icon != null)
            {
                Rect r = Next(rect, ref curX);
                GUI.DrawTexture(r, icon);
                if (Mouse.IsOver(r))
                {
                    p.ageTracker.AgeBiologicalTicks.TicksToPeriod(out int years, out int quadrums, out int days, out _);
                    TooltipHandler.TipRegion(r, p.ageTracker.CurLifeStage.LabelCap.CapitalizeFirst() + "\n\n" + "AgeBiological".Translate(years, quadrums, days));
                }
            }
        }
        if ((animal ? S.tradeAnimalGender : S.tradeColonistGender) && p.gender != Gender.None)
        {
            Rect r = Next(rect, ref curX);
            GUI.DrawTexture(r, p.gender.GetIcon());
            TooltipHandler.TipRegion(r, p.GetGenderLabel().CapitalizeFirst());
        }
        if (animal && S.tradeFollow && p.playerSettings is { followDrafted: true })
        {
            Rect r = Next(rect, ref curX);
            GUI.DrawTexture(r, Follow);
            TooltipHandler.TipRegion(r, "CreatureFollowDrafted".Translate());
        }
    }
}

[HarmonyPatch(typeof(TransferableUIUtility), nameof(TransferableUIUtility.DoExtraIcons))]
public static class Transfer_Icons
{
    public static void Prefix(Transferable trad, Rect rect, ref float curX)
    {
        Transfer.Icons(trad, rect, ref curX);
    }
}

[HarmonyPatch(typeof(TransferableUtility), nameof(TransferableUtility.TransferAsOne))]
public static class Transfer_Group
{
    public static bool Prefix(Thing a, Thing b, ref bool __result)
    {
        if (!Transfer.On || a is not Pawn pa || b is not Pawn pb || a.def != b.def || !pa.RaceProps.Animal)
        {
            return true;
        }
        bool releaseA = pa.training?.HasLearned(TrainableDefOf.Release) ?? false;
        bool releaseB = pb.training?.HasLearned(TrainableDefOf.Release) ?? false;
        bool followA = pa.playerSettings?.followDrafted ?? false;
        bool followB = pb.playerSettings?.followDrafted ?? false;
        if (releaseA != releaseB || followA != followB)
        {
            __result = false;
            return false;
        }
        return true;
    }
}
