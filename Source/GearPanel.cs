using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// the Gear tab laid out to fit, as wide as the card: mass carried as a bar, the comfortable temperatures, overall
// armor as three bars in a row, then equipment, apparel and inventory as rows with icon, name, mass and the game's
// buttons (eat now, drop, info). Past a certain height the rows scroll. Weapons also get a swap button (one in the
// inventory takes the place of the one in hand) or a put away one (the one in hand goes to the inventory), and the same
// in a right click menu, like Combat Extended's gear tab. With CE it's CE's own switch, so its ammo and bulk stay right
public static class GearPanel
{
    private static float Scale => Pane.Scale;
    private static float Width => Mathf.Round(400f * Scale);
    private static float Row => Mathf.Round(24f * Scale);
    private static float Heading => Mathf.Round(18f * Scale);
    private static float Section => Mathf.Round(8f * Scale);
    private static float MaxHeight => Mathf.Round(520f * Scale);

    private static readonly Func<ITab_Pawn_Gear, bool> canControl = AccessTools.MethodDelegate<Func<ITab_Pawn_Gear, bool>>(AccessTools.PropertyGetter(typeof(ITab_Pawn_Gear), "CanControl"));
    private static readonly Func<ITab_Pawn_Gear, bool> canControlColonist = AccessTools.MethodDelegate<Func<ITab_Pawn_Gear, bool>>(AccessTools.PropertyGetter(typeof(ITab_Pawn_Gear), "CanControlColonist"));
    private static readonly Action<ITab_Pawn_Gear, Thing> interfaceDrop = AccessTools.MethodDelegate<Action<ITab_Pawn_Gear, Thing>>(AccessTools.Method(typeof(ITab_Pawn_Gear), "InterfaceDrop"));
    private static readonly Func<ITab_Pawn_Gear, Pawn, bool> showInventory = AccessTools.MethodDelegate<Func<ITab_Pawn_Gear, Pawn, bool>>(AccessTools.Method(typeof(ITab_Pawn_Gear), "ShouldShowInventory"));
    private static readonly Func<ITab_Pawn_Gear, Pawn, bool> showApparel = AccessTools.MethodDelegate<Func<ITab_Pawn_Gear, Pawn, bool>>(AccessTools.Method(typeof(ITab_Pawn_Gear), "ShouldShowApparel"));
    private static readonly Func<ITab_Pawn_Gear, Pawn, bool> showEquipment = AccessTools.MethodDelegate<Func<ITab_Pawn_Gear, Pawn, bool>>(AccessTools.Method(typeof(ITab_Pawn_Gear), "ShouldShowEquipment"));
    private static readonly Func<ITab_Pawn_Gear, Pawn, bool> showArmor = AccessTools.MethodDelegate<Func<ITab_Pawn_Gear, Pawn, bool>>(AccessTools.Method(typeof(ITab_Pawn_Gear), "ShouldShowOverallArmor"));

    private static Vector2 scroll;
    private static Texture2D swapIcon, putAwayIcon;
    private static Texture2D SwapIcon => swapIcon ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Pane/Swap");
    private static Texture2D PutAwayIcon => putAwayIcon ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Pane/PutAway");
    private static readonly Type ceInventory = AccessTools.TypeByName("CombatExtended.CompInventory");
    private static readonly System.Reflection.MethodInfo ceSwitch = ceInventory == null ? null : AccessTools.Method(ceInventory, "TrySwitchToWeapon");
    private static readonly List<Thing> things = new();

    public static Vector2 Size(ITab_Pawn_Gear tab, Pawn p)
    {
        GameFont font = Text.Font;
        Text.Font = Pane.SmallFont;
        float h = Layout(new Rect(0f, 0f, Width - Pane.Pad * 2f, 9999f), tab, p, false);
        Text.Font = font;
        return new Vector2(Width, Mathf.Min(MaxHeight, Mathf.Round(Pane.Pad * 2f + 24f * Scale + Section + h)));
    }

    public static void Draw(Rect all, ITab_Pawn_Gear tab, Pawn p)
    {
        Rect inner = all.ContractedBy(Pane.Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            Text.Font = Pane.NameFont;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, 0f, area.width / 2f, 24f * Scale), "TabGear".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = Pane.SmallFont;
            if (Prefs.DevMode && Widgets.ButtonText(new Rect(area.width - 24f - 100f, 0f, 100f, 22f * Scale), "Dev tool..."))
            {
                Find.WindowStack.Add(new FloatMenu(DebugToolsPawns.PawnGearDevOptions(p)));
            }
            float top = 24f * Scale + Section;
            Rect list = new Rect(0f, top, area.width, area.height - top);
            float h = Layout(new Rect(0f, 0f, list.width, 9999f), tab, p, false);
            bool scrolls = h > list.height + 0.5f;
            Rect view = new Rect(0f, 0f, list.width - (scrolls ? 16f : 0f), h);
            Widgets.BeginScrollView(list, ref scroll, view, scrolls);
            Layout(view, tab, p, true);
            Widgets.EndScrollView();
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
            Widgets.EndGroup();
        }
    }

    private static float DrawHeading(float y, float width, string label, bool draw)
    {
        if (draw)
        {
            GUI.color = Style.Dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, y, width, Heading), label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        return Heading;
    }

    // measures, or draws when draw is on; returns the height
    private static float Layout(Rect area, ITab_Pawn_Gear tab, Pawn p, bool draw)
    {
        float y = 0f, width = area.width;
        float row = Pane.RowHeight + 2f;
        if (!p.Dead && showInventory(tab, p))
        {
            if (draw)
            {
                float mass = MassUtility.GearAndInventoryMass(p), capacity = MassUtility.Capacity(p);
                Rect r = new Rect(0f, y, width, Pane.RowHeight);
                Pane.DrawBar(r, new Pane.Bar { Label = "Mass".Translate().CapitalizeFirst(), Value = capacity > 0f ? mass / capacity : 0f, ValueText = mass.ToString("0.##") + " / " + capacity.ToString("0.##") + " kg", Color = mass > capacity ? new Color(0.92f, 0.45f, 0.45f) : new Color(0.55f, 0.68f, 0.8f), Tip = "MassCarried".Translate(mass.ToString("0.##"), capacity.ToString("0.##")) }, null);
            }
            y += row;
        }
        if (!p.Dead)
        {
            if (draw)
            {
                Temperature(new Rect(0f, y, width, TemperatureHeight), p);
            }
            y += TemperatureHeight + 2f;
        }
        y += Section;

        if (showArmor(tab, p))
        {
            y += DrawHeading(y, width, "OverallArmor".Translate(), draw);
            if (draw)
            {
                float gap = Mathf.Round(8f * Scale), col = Mathf.Floor((width - gap * 2f) / 3f);
                Armor(new Rect(0f, y, col, Pane.RowHeight), p, StatDefOf.ArmorRating_Sharp, "ArmorSharp".Translate(), new Color(0.6f, 0.68f, 0.78f));
                Armor(new Rect(col + gap, y, col, Pane.RowHeight), p, StatDefOf.ArmorRating_Blunt, "ArmorBlunt".Translate(), new Color(0.7f, 0.64f, 0.56f));
                Armor(new Rect((col + gap) * 2f, y, col, Pane.RowHeight), p, StatDefOf.ArmorRating_Heat, "ArmorHeat".Translate(), new Color(0.92f, 0.6f, 0.35f));
            }
            y += row + Section;
        }

        if (showEquipment(tab, p))
        {
            things.Clear();
            things.AddRange(p.equipment.AllEquipmentListForReading);
            if (p.apparel != null)
            {
                things.AddRange(p.apparel.WornApparel.Where(a => a.def.apparel.layers.Contains(ApparelLayerDefOf.Belt)));
            }
            y = Things(y, width, tab, p, "Equipment".Translate(), false, draw);
        }
        if (showApparel(tab, p))
        {
            things.Clear();
            things.AddRange(p.apparel.WornApparel.Where(a => !a.def.apparel.layers.Contains(ApparelLayerDefOf.Belt)).OrderByDescending(a => a.def.apparel.bodyPartGroups[0].listOrder));
            y = Things(y, width, tab, p, "Apparel".Translate(), false, draw);
        }
        if (showInventory(tab, p))
        {
            things.Clear();
            things.AddRange(p.inventory.innerContainer);
            y = Things(y, width, tab, p, "Inventory".Translate(), true, draw);
        }
        things.Clear();
        return Mathf.Max(0f, y - Section);
    }

    private static readonly Color Cold = new(0.45f, 0.63f, 0.96f);
    private static readonly Color Comfy = new(0.45f, 0.78f, 0.5f);
    private static readonly Color Hot = new(0.95f, 0.6f, 0.3f);

    // a thin bar, and the text below it
    private static float TemperatureBar => Mathf.Round(8f * Scale);
    private static float TemperatureHeight => TemperatureBar + 2f * Scale + 16f * Scale;

    // the comfortable range on a scale of temperatures: too cold in blue, comfortable in green, too hot in orange,
    // and a mark where the temperature around them is now, so it shows at a glance whether they're comfortable;
    // below it the range on the left and the temperature now on the right
    private static void Temperature(Rect area, Pawn p)
    {
        Rect r = new Rect(area.x, area.y, area.width, TemperatureBar);
        float min = p.GetStatValue(StatDefOf.ComfyTemperatureMin), max = p.GetStatValue(StatDefOf.ComfyTemperatureMax);
        bool here = p.MapHeld != null;
        float now = here ? p.AmbientTemperature : (min + max) / 2f;
        // the scale: the range and now, with room around them, in steps of ten degrees
        float low = Mathf.Floor((Mathf.Min(min, now) - 10f) / 10f) * 10f, high = Mathf.Ceil((Mathf.Max(max, now) + 10f) / 10f) * 10f;
        float At(float t) => Mathf.Round(r.x + r.width * Mathf.InverseLerp(low, high, t));

        GUI.color = new Color(1f, 1f, 1f, 0.07f);
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
        float a = At(min), b = At(max);
        GUI.color = new Color(Cold.r, Cold.g, Cold.b, 0.3f);
        Widgets.DrawAtlas(new Rect(r.x, r.y, a - r.x, r.height), ModernWidgets.Fill);
        GUI.color = new Color(Hot.r, Hot.g, Hot.b, 0.3f);
        Widgets.DrawAtlas(new Rect(b, r.y, r.xMax - b, r.height), ModernWidgets.Fill);
        GUI.color = new Color(Comfy.r, Comfy.g, Comfy.b, 0.7f);
        Widgets.DrawAtlas(new Rect(a, r.y, b - a, r.height), ModernWidgets.Fill);
        if (here)
        {
            // a bit taller than the bar so it stands out
            float x = At(now);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(x - 2f, r.y - 2f, 4f, r.height + 4f), BaseContent.WhiteTex);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x - 1f, r.y - 2f, 2f, r.height + 4f), BaseContent.WhiteTex);
        }
        Rect text = new Rect(area.x + 2f * Scale, r.yMax + 2f * Scale, area.width - 4f * Scale, 16f * Scale);
        GUI.color = Style.Dim;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(text, "ComfyTemperatureRange".Translate() + ": " + min.ToStringTemperature("F0") + " ~ " + max.ToStringTemperature("F0"));
        if (here)
        {
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = now < min ? Cold : now > max ? Hot : Style.Text;
            Widgets.Label(text, now.ToStringTemperature("F0"));
        }
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        r = area;
        if (Mouse.IsOver(r))
        {
            string tip = "ComfyTemperatureRange".Translate() + ": " + min.ToStringTemperature("F0") + " ~ " + max.ToStringTemperature("F0");
            if (here)
            {
                tip += "\n\n" + "Temperature".Translate().CapitalizeFirst() + ": " + now.ToStringTemperature("F0") + "\n"
                    + (now < min ? "Too cold, " + (min - now).ToStringTemperatureOffset("F0") + " below" : now > max ? "Too hot, " + (now - max).ToStringTemperatureOffset("F0") + " above" : "Comfortable");
            }
            TooltipHandler.TipRegion(r, tip);
        }
    }

    // the game's sum: how much of the body the armor covers, and how well
    private static void Armor(Rect r, Pawn p, StatDef stat, string label, Color color)
    {
        float natural = Mathf.Clamp01(p.GetStatValue(stat) / 2f);
        float total = 0f;
        List<Apparel> worn = p.apparel?.WornApparel;
        foreach (BodyPartRecord part in p.RaceProps.body.AllParts)
        {
            float through = 1f - natural;
            if (worn != null)
            {
                foreach (Apparel a in worn)
                {
                    if (a.def.apparel.CoversBodyPart(part))
                    {
                        through *= 1f - Mathf.Clamp01(a.GetStatValue(stat) / 2f);
                    }
                }
            }
            total += part.coverageAbs * (1f - through);
        }
        total = Mathf.Clamp(total * 2f, 0f, 2f);
        Pane.DrawBar(r, new Pane.Bar { Label = label, Value = Mathf.Clamp01(total), ValueText = total.ToStringPercent(), Color = color, Tip = stat.LabelCap + ": " + total.ToStringPercent() + "\n\n" + stat.description }, null);
    }

    private static float Things(float y, float width, ITab_Pawn_Gear tab, Pawn p, string heading, bool inventory, bool draw)
    {
        y += DrawHeading(y, width, heading, draw);
        if (things.Count == 0)
        {
            if (draw)
            {
                GUI.color = ColoredText.SubtleGrayColor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(5f * Scale, y, width, Row), "None".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            return y + Row + Section;
        }
        foreach (Thing thing in things.ToList())
        {
            if (draw)
            {
                ThingRow(new Rect(0f, y, width, Row), tab, p, thing, inventory);
            }
            y += Row + 2f;
        }
        return y - 2f + Section;
    }

    // icon, name (forced, locked), mass, then eat now, drop and info at the right, like the game's rows
    private static void ThingRow(Rect row, ITab_Pawn_Gear tab, Pawn p, Thing thing, bool inventory)
    {
        bool over = Mouse.IsOver(row);
        GUI.color = over ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.035f);
        Widgets.DrawAtlas(row, ModernWidgets.Fill);
        GUI.color = Color.white;
        float icon = Mathf.Round(18f * Scale), right = row.xMax - 2f * Scale;
        Rect Next(float size = 0f)
        {
            size = size > 0f ? size : icon;
            right -= size + 4f * Scale;
            return new Rect(right + 4f * Scale, Mathf.Round(row.center.y - size / 2f), size, size);
        }
        // the game's info button is always 24
        Rect info = Next(24f);
        Widgets.InfoCardButton(info.x, info.y, thing);
        bool locked = false;
        if (canControl(tab) && (inventory || canControlColonist(tab) || (p.Spawned && !p.Map.IsPlayerHome)))
        {
            Rect drop = Next();
            bool lodger = p.IsQuestLodger() && (inventory || !EquipmentUtility.QuestLodgerCanUnequip(thing, p));
            bool destroys = !inventory && p.kindDef.destroyGearOnDrop;
            bool apparelLocked = thing is Apparel worn && p.apparel != null && p.apparel.IsLocked(worn);
            locked = lodger || apparelLocked || destroys;
            if (Mouse.IsOver(drop))
            {
                TooltipHandler.TipRegion(drop, apparelLocked ? "DropThingLocked".Translate() : lodger ? "DropThingLodger".Translate() : "DropThing".Translate());
            }
            if (Widgets.ButtonImage(drop, TexButton.Drop, locked ? Style.Disabled : Style.Dim, locked ? Style.Disabled : Color.white, !locked) && !locked)
            {
                Drop(tab, p, thing);
            }
        }
        bool swappable = Swappable(tab, p, thing, inventory);
        if (swappable)
        {
            Rect swap = Next();
            string cant = inventory ? CantEquip(p, (ThingWithComps)thing) : CantPutAway(p, (ThingWithComps)thing);
            TooltipHandler.TipRegion(swap, cant ?? (inventory ? EquipTip(p, thing) : PutAwayTip(thing)));
            if (Widgets.ButtonImage(swap, inventory ? SwapIcon : PutAwayIcon, cant != null ? Style.Disabled : Style.Dim, cant != null ? Style.Disabled : Color.white, cant == null) && cant == null)
            {
                SwapOrPutAway(p, (ThingWithComps)thing, inventory);
            }
        }
        if (canControlColonist(tab) && FoodUtility.WillIngestFromInventoryNow(p, thing))
        {
            Rect eat = Next();
            TooltipHandler.TipRegionByKey(eat, "ConsumeThing", thing.LabelNoCount, thing);
            if (Widgets.ButtonImage(eat, TexButton.Ingest, Style.Dim, Color.white))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                FoodUtility.IngestFromInventoryNow(p, thing);
            }
        }
        // mass, dim, before the buttons
        Text.Anchor = TextAnchor.MiddleRight;
        Text.WordWrap = false;
        float massWidth = Mathf.Round(56f * Scale);
        GUI.color = Style.Dim;
        Widgets.Label(new Rect(right - massWidth - 4f * Scale, row.y, massWidth, row.height), (thing.GetStatValue(StatDefOf.Mass) * thing.stackCount).ToStringMass());
        float x = row.x + 4f * Scale;
        if (thing.def.DrawMatSingle != null && thing.def.DrawMatSingle.mainTexture != null)
        {
            float size = row.height - 4f * Scale;
            GUI.color = Color.white;
            Widgets.ThingIcon(new Rect(x, row.y + 2f * Scale, size, size), thing);
            x += size + 6f * Scale;
        }
        string label = thing.LabelCap;
        if (thing is Apparel apparel && p.outfits != null && p.outfits.forcedHandler.IsForced(apparel))
        {
            label += ", " + "ApparelForcedLower".Translate();
        }
        if (locked)
        {
            label += " (" + "ApparelLockedLower".Translate() + ")";
        }
        Rect name = new Rect(x, row.y, right - massWidth - 8f * Scale - x, row.height);
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = Style.Text;
        Widgets.Label(name, label.Truncate(name.width));
        Text.WordWrap = true;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        if (Mouse.IsOver(name))
        {
            TooltipHandler.TipRegion(name, thing.GetTooltip());
        }
        if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && Mouse.IsOver(row))
        {
            Event.current.Use();
            Menu(tab, p, thing, inventory, swappable, locked);
        }
    }

    private static void Drop(ITab_Pawn_Gear tab, Pawn p, Thing thing)
    {
        void Act()
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            interfaceDrop(tab, thing);
        }
        if (!ModsConfig.BiotechActive || !MechanitorUtility.TryConfirmBandwidthLossFromDroppingThing(p, thing, Act))
        {
            Act();
        }
    }

    // a weapon in hand, or one in the inventory, of a colonist that can be told what to do
    private static bool Swappable(ITab_Pawn_Gear tab, Pawn p, Thing thing, bool inventory) =>
        canControlColonist(tab) && p.equipment != null && p.inventory != null && thing is ThingWithComps w && w.def.IsWeapon && w.TryGetComp<CompEquippable>() != null
        && (inventory || thing == p.equipment.Primary);

    private static string CantEquip(Pawn p, ThingWithComps weapon)
    {
        if (!EquipmentUtility.CanEquip(weapon, p, out string reason))
        {
            return "CannotEquip".Translate(weapon.LabelNoParenthesis) + ": " + reason;
        }
        if (p.WorkTagIsDisabled(WorkTags.Violent))
        {
            return "CannotEquip".Translate(weapon.LabelNoParenthesis) + ": " + "IsIncapableOfViolenceLower".Translate(p.LabelShort, p);
        }
        if (p.IsQuestLodger() && (!EquipmentUtility.QuestLodgerCanEquip(weapon, p) || (p.equipment.Primary != null && !EquipmentUtility.QuestLodgerCanUnequip(p.equipment.Primary, p))))
        {
            return "CannotEquip".Translate(weapon.LabelNoParenthesis) + ": " + "QuestRelated".Translate();
        }
        return null;
    }

    private static string CantPutAway(Pawn p, ThingWithComps weapon) =>
        p.IsQuestLodger() && !EquipmentUtility.QuestLodgerCanUnequip(weapon, p) ? "Can't put " + weapon.LabelNoParenthesis + " away: " + "QuestRelated".Translate() : null;

    private static string EquipTip(Pawn p, Thing thing) => p.equipment.Primary is { } held
        ? "Equip " + thing.LabelNoParenthesis + ", " + held.LabelNoParenthesis + " goes to the inventory"
        : "Equip " + thing.LabelNoParenthesis;

    private static string PutAwayTip(Thing thing) => "Put " + thing.LabelNoParenthesis + " away in the inventory";

    public static void SwapOrPutAway(Pawn p, ThingWithComps weapon, bool inventory)
    {
        SoundDefOf.Tick_High.PlayOneShotOnCamera();
        if (!inventory)
        {
            p.equipment.TryTransferEquipmentToContainer(weapon, p.inventory.innerContainer);
            return;
        }
        if (ceSwitch != null && p.AllComps.FirstOrDefault(c => ceInventory.IsInstanceOfType(c)) is { } ce)
        {
            ceSwitch.Invoke(ce, new object[] { weapon, true });
            return;
        }
        p.jobs?.StopAll(false, true);
        if (p.equipment.Primary != null && !p.equipment.TryTransferEquipmentToContainer(p.equipment.Primary, p.inventory.innerContainer))
        {
            return;
        }
        p.equipment.AddEquipment((ThingWithComps)p.inventory.innerContainer.Take(weapon, 1));
        weapon.def.soundInteract?.PlayOneShot(new TargetInfo(p.Position, p.Map));
    }

    // right click on a row: equip or put away, drop, and the info card
    public static void Menu(ITab_Pawn_Gear tab, Pawn p, Thing thing, bool inventory, bool swappable, bool locked)
    {
        var options = new List<FloatMenuOption>();
        if (swappable)
        {
            var weapon = (ThingWithComps)thing;
            string cant = inventory ? CantEquip(p, weapon) : CantPutAway(p, weapon);
            options.Add(cant != null
                ? new FloatMenuOption(cant, null)
                : new FloatMenuOption(inventory ? EquipTip(p, thing) : PutAwayTip(thing), () => SwapOrPutAway(p, weapon, inventory)));
        }
        if (canControl(tab) && (inventory || canControlColonist(tab) || (p.Spawned && !p.Map.IsPlayerHome)))
        {
            options.Add(locked ? new FloatMenuOption("DropThing".Translate() + " (" + "ApparelLockedLower".Translate() + ")", null) : new FloatMenuOption("DropThing".Translate(), () => Drop(tab, p, thing)));
        }
        options.Add(new FloatMenuOption("ThingInfo".Translate(), () => Find.WindowStack.Add(new Dialog_InfoCard(thing))));
        Find.WindowStack.Add(new FloatMenu(options));
    }
}
