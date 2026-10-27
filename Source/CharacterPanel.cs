using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the Character tab laid out to fit what's in it, as wide as the card: name and the game's buttons (rename, banish...),
// who they are, the faction, ideoligion, xenotype and titles as chips, backstory, traits, what they can't do,
// abilities, and the skills as bars in two columns with the level in a passion badge. One function lays it out, measuring or drawing, so the panel
// is exactly as tall as its contents
public static class CharacterPanel
{
    private static float Scale => Pane.Scale;
    private static float Width => Mathf.Round(400f * Scale);
    private static float ChipHeight => Mathf.Round(20f * Scale);
    private static float Gap => Mathf.Round(4f * Scale);
    private static float Heading => Mathf.Round(18f * Scale);
    private static float Section => Mathf.Round(8f * Scale);

    private static readonly Func<Pawn, WorkTags, string> disabledCausedBy = AccessTools.MethodDelegate<Func<Pawn, WorkTags, string>>(AccessTools.Method(typeof(CharacterCardUtility), "GetWorkTypeDisabledCausedBy"));
    private static readonly Func<WorkTags, string> disabledByTag = AccessTools.MethodDelegate<Func<WorkTags, string>>(AccessTools.Method(typeof(CharacterCardUtility), "GetWorkTypesDisabledByWorkTag"));
    private static readonly Func<Pawn, WorkTags, Color> disabledColor = AccessTools.MethodDelegate<Func<Pawn, WorkTags, Color>>(AccessTools.Method(typeof(CharacterCardUtility), "GetDisabledWorkTagLabelColor"));
    private static readonly Func<Pawn, Faction, RoyalTitle, int, string> titleTip = AccessTools.MethodDelegate<Func<Pawn, Faction, RoyalTitle, int, string>>(AccessTools.Method(typeof(CharacterCardUtility), "GetTitleTipString"));
    private static readonly Func<SkillRecord, string> skillTip = AccessTools.MethodDelegate<Func<SkillRecord, string>>(AccessTools.Method(typeof(SkillUI), "GetSkillDescription"));
    private static Texture2D _unrecruitableIcon;
    private static Texture2D UnrecruitableIcon => _unrecruitableIcon ??= ContentFinder<Texture2D>.Get("UI/Icons/UnwaveringlyLoyal");
    // measured with the fonts it's drawn with, put back after
    public static Vector2 Size(Pawn p)
    {
        GameFont font = Text.Font;
        float h = Layout(new Rect(0f, 0f, Width - Pane.Pad * 2f, 9999f), p, false);
        Text.Font = font;
        return new Vector2(Width, Mathf.Round(h + Pane.Pad * 2f));
    }

    public static void Draw(Rect all, Pawn p)
    {
        Rect inner = all.ContractedBy(Pane.Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Layout(inner.AtZero(), p, true);
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

    // ---- chips: rounded labels that wrap onto the next line ----

    private struct Chip
    {
        public float Width;
        public Action<Rect> Draw;
    }

    private static readonly List<Chip> chips = new();

    private static float ChipWidth(string label, bool icon) => Mathf.Ceil(Text.CalcSize(label).x) + 12f * Scale + (icon ? ChipHeight : 0f);

    // lays out the chips from y, returns how tall they came out
    private static float Flow(float y, float width, bool draw)
    {
        float x = 0f, h = chips.Count > 0 ? ChipHeight : 0f;
        foreach (Chip chip in chips)
        {
            float w = Mathf.Min(chip.Width, width);
            if (x > 0f && x + w > width)
            {
                x = 0f;
                h += ChipHeight + Gap;
            }
            if (draw)
            {
                chip.Draw(new Rect(x, y + h - ChipHeight, w, ChipHeight));
            }
            x += w + Gap;
        }
        chips.Clear();
        return h;
    }

    private static void ChipBack(Rect r)
    {
        GUI.color = Mouse.IsOver(r) ? new Color(1f, 1f, 1f, 0.14f) : new Color(1f, 1f, 1f, 0.07f);
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
        GUI.color = Color.white;
    }

    // background, an optional icon in its own color, the label
    private static void ChipLabel(Rect r, string label, Texture icon = null, Color? iconColor = null, Color? labelColor = null)
    {
        ChipBack(r);
        float x = r.x + 6f * Scale;
        if (icon != null)
        {
            float s = Mathf.Round(r.height - 4f * Scale);
            GUI.color = iconColor ?? Color.white;
            GUI.DrawTexture(new Rect(x - 2f * Scale, Mathf.Round(r.y + (r.height - s) / 2f), s, s), icon);
            x += r.height - 2f * Scale;
        }
        GUI.color = labelColor ?? Style.Text;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        Widgets.Label(new Rect(x, r.y, r.xMax - x - 4f * Scale, r.height), label);
        Text.WordWrap = true;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
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

    // ---- the layout ----

    private static float Layout(Rect area, Pawn p, bool draw)
    {
        float y = 0f;
        float width = area.width;

        // name, and the buttons at the right
        Text.Font = Pane.NameFont;
        float buttons = draw ? Buttons(new Rect(0f, 0f, width, 24f * Scale), p) : 0f;
        if (draw)
        {
            string name = p.Name?.ToStringFull.CapitalizeFirst() ?? p.LabelCap;
            Rect nameRect = new Rect(0f, 0f, width - buttons - 30f, 24f * Scale);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(nameRect, name.Truncate(nameRect.width));
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;
        }
        y += 24f * Scale;
        Text.Font = Pane.SmallFont;

        // who they are: sex, age, kind
        if (draw)
        {
            GUI.color = Style.Dim;
            Text.WordWrap = false;
            Widgets.Label(new Rect(0f, y, width, 16f * Scale), p.MainDesc(false, true).CapitalizeFirst().Truncate(width));
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
        y += 16f * Scale + Section;

        // where they belong
        TopChips(p);
        float h = Flow(y, width, draw);
        if (h > 0f)
        {
            y += h + Section;
        }

        // backstory
        if (p.story != null)
        {
            float label = Mathf.Round(70f * Scale);
            foreach (BackstorySlot slot in Enum.GetValues(typeof(BackstorySlot)))
            {
                BackstoryDef story = p.story.GetBackstory(slot);
                if (story == null)
                {
                    continue;
                }
                if (draw)
                {
                    GUI.color = Style.Dim;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(0f, y, label, ChipHeight), slot == BackstorySlot.Adulthood ? "Adulthood".Translate() : "Childhood".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                    string title = story.TitleCapFor(p.gender);
                    Rect chip = new Rect(label, y, Mathf.Min(ChipWidth(title, false), width - label), ChipHeight);
                    ChipLabel(chip, title.Truncate(chip.width - 10f * Scale));
                    if (Mouse.IsOver(chip))
                    {
                        TooltipHandler.TipRegion(chip, story.FullDescriptionFor(p).Resolve());
                    }
                }
                y += ChipHeight + Gap;
            }
            if (p.story.title != null)
            {
                if (draw)
                {
                    GUI.color = Style.Dim;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(0f, y, label, ChipHeight), "BackstoryTitle".Translate());
                    GUI.color = Style.Text;
                    Widgets.Label(new Rect(label, y, width - label, ChipHeight), p.story.title);
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
                y += ChipHeight + Gap;
            }
            y += Section - Gap;

            // traits
            y += DrawHeading(y, width, "Traits".Translate(), draw);
            List<Trait> traits = p.story.traits?.TraitsSorted;
            if (traits.NullOrEmpty())
            {
                y += None(y, width, p.DevelopmentalStage.Baby() ? "TraitsDevelopLaterBaby".Translate() : "None".Translate(), draw);
            }
            else
            {
                foreach (Trait trait in traits)
                {
                    Trait local = trait;
                    Color color = trait.Suppressed ? ColoredText.SubtleGrayColor : trait.sourceGene != null ? ColoredText.GeneColor : Style.Text;
                    chips.Add(new Chip
                    {
                        Width = ChipWidth(trait.LabelCap, false),
                        Draw = r =>
                        {
                            ChipLabel(r, local.LabelCap, labelColor: color);
                            if (Mouse.IsOver(r))
                            {
                                TooltipHandler.TipRegion(r, new TipSignal(() => local.TipString(p), local.def.index * 37 + local.Degree));
                            }
                        }
                    });
                }
                y += Flow(y, width, draw);
            }
            y += Section;
        }

        // what they can't do
        WorkTags disabled = p.CombinedDisabledWorkTags;
        y += DrawHeading(y, width, "IncapableOf".Translate(p), draw);
        if (disabled == WorkTags.None)
        {
            y += None(y, width, "None".Translate(), draw);
        }
        else
        {
            foreach (WorkTags tag in disabled.GetAllSelectedItems<WorkTags>())
            {
                if (tag == WorkTags.None)
                {
                    continue;
                }
                WorkTags local = tag;
                string label = tag.LabelTranslated().CapitalizeFirst();
                chips.Add(new Chip
                {
                    Width = ChipWidth(label, false),
                    Draw = r =>
                    {
                        ChipLabel(r, label, labelColor: disabledColor(p, local));
                        if (Mouse.IsOver(r))
                        {
                            TooltipHandler.TipRegion(r, new TipSignal(() => disabledCausedBy(p, local) + "\n" + disabledByTag(local), (int)local * 31));
                        }
                    }
                });
            }
            y += Flow(y, width, draw);
        }
        y += Section;

        // abilities, as their icons
        List<Ability> abilities = p.abilities?.AllAbilitiesForReading.Where(a => a.def.showOnCharacterCard).OrderBy(a => a.def.level).ThenBy(a => a.def.EntropyGain).ToList();
        if (!abilities.NullOrEmpty())
        {
            y += DrawHeading(y, width, "Abilities".Translate(p), draw);
            float size = Mathf.Round(28f * Scale), x = 0f;
            foreach (Ability ability in abilities)
            {
                if (x > 0f && x + size > width)
                {
                    x = 0f;
                    y += size + Gap;
                }
                if (draw)
                {
                    Rect r = new Rect(x, y, size, size);
                    ChipBack(r);
                    Ability local = ability;
                    if (Widgets.ButtonImage(r.ContractedBy(2f * Scale), ability.def.uiIcon, false))
                    {
                        Find.WindowStack.Add(new Dialog_InfoCard(local.def));
                    }
                    if (Mouse.IsOver(r))
                    {
                        TooltipHandler.TipRegion(r, new TipSignal(() => local.Tooltip + "\n\n" + "ClickToLearnMore".Translate().Colorize(ColoredText.SubtleGrayColor), local.def.index * 41));
                    }
                }
                x += size + Gap;
            }
            y += size + Section;
        }

        // skills, two columns of bars
        if (p.skills != null)
        {
            y += DrawHeading(y, width, "Skills", draw);
            List<SkillRecord> skills = p.skills.skills.OrderBy(s => s.def.listOrder).ToList();
            int rows = Mathf.CeilToInt(skills.Count / 2f);
            float gap = Mathf.Round(10f * Scale);
            float col = Mathf.Floor((width - gap) / 2f);
            float row = Pane.RowHeight + 2f;
            if (draw)
            {
                for (int i = 0; i < skills.Count; i++)
                {
                    Skill(new Rect(i / rows * (col + gap), y + i % rows * row, col, Pane.RowHeight), skills[i]);
                }
            }
            y += rows * row - 2f;
        }

        // quests they're part of
        QuestUtility.AppendInspectStringsFromQuestParts((text, quest) =>
        {
            y += Gap;
            if (draw)
            {
                Rect r = new Rect(0f, y, width, 16f * Scale);
                GUI.color = Mouse.IsOver(r) ? Style.Text : Style.Dim;
                Text.WordWrap = false;
                Widgets.Label(r, text.Truncate(width));
                Text.WordWrap = true;
                GUI.color = Color.white;
                TooltipHandler.TipRegion(r, text + "\n\n" + "ClickToViewInQuestsTab".Translate().Colorize(ColoredText.SubtleGrayColor));
                if (Widgets.ButtonInvisible(r))
                {
                    Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Quests);
                    ((MainTabWindow_Quests)MainButtonDefOf.Quests.TabWindow).Select(quest);
                }
            }
            y += 16f * Scale;
        }, p, out _);
        return y;
    }

    private static float None(float y, float width, string text, bool draw)
    {
        if (draw)
        {
            GUI.color = ColoredText.SubtleGrayColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, y, width, ChipHeight), text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        return ChipHeight;
    }

    // xenotype, faction, ideoligion and role, royal titles, favorite color, loyalty, like the game's top row
    private static void TopChips(Pawn p)
    {
        if (ModsConfig.BiotechActive && p.genes != null && p.genes.Xenotype != null)
        {
            string label = p.genes.XenotypeLabelCap;
            chips.Add(new Chip
            {
                Width = ChipWidth(label, true),
                Draw = r =>
                {
                    ChipLabel(r, label, p.genes.XenotypeIcon, XenotypeDef.IconColor);
                    if (Mouse.IsOver(r))
                    {
                        TooltipHandler.TipRegion(r, () => ("Xenotype".Translate() + ": " + p.genes.XenotypeLabelCap).Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + p.genes.XenotypeDescShort + "\n\n" + "ViewGenesDesc".Translate(p.Named("PAWN")).ToString().StripTags().Colorize(ColoredText.SubtleGrayColor), 883938493);
                    }
                    if (Widgets.ButtonInvisible(r))
                    {
                        InspectPaneUtility.OpenTab(typeof(ITab_Genes));
                    }
                }
            });
        }
        if (p.Faction != null && !p.Faction.Hidden)
        {
            Faction faction = p.Faction;
            chips.Add(new Chip
            {
                Width = ChipWidth(faction.Name, true),
                Draw = r =>
                {
                    ChipLabel(r, faction.Name, faction.def.FactionIcon, faction.Color);
                    if (Widgets.ButtonInvisible(r))
                    {
                        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Factions);
                    }
                    if (Mouse.IsOver(r))
                    {
                        TooltipHandler.TipRegion(r, new TipSignal("Faction".Translate().Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "FactionDesc".Translate(p.Named("PAWN")).Resolve() + "\n\n" + "ClickToViewFactions".Translate().Colorize(ColoredText.SubtleGrayColor), faction.loadID * 37));
                    }
                }
            });
        }
        if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && p.Ideo != null)
        {
            Ideo ideo = p.Ideo;
            chips.Add(new Chip
            {
                Width = ChipWidth(ideo.name, true),
                Draw = r =>
                {
                    ChipBack(r);
                    // the game's: icon, name, tooltip and click to the ideoligion
                    IdeoUIUtility.DrawIdeoPlate(new Rect(r.x + 2f * Scale, r.y, r.width - 2f * Scale, r.height), ideo, p);
                }
            });
            if (ideo.GetRole(p) is { } role)
            {
                string label = role.LabelForPawn(p);
                chips.Add(new Chip
                {
                    Width = ChipWidth(label, true),
                    Draw = r =>
                    {
                        ChipLabel(r, label, role.Icon, ideo.Color);
                        if (Widgets.ButtonInvisible(r))
                        {
                            InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Social));
                        }
                        if (Mouse.IsOver(r))
                        {
                            TooltipHandler.TipRegion(r, new TipSignal(() => role.GetTip(), 9137 ^ role.Id));
                        }
                    }
                });
            }
        }
        if (p.royalty != null)
        {
            foreach (RoyalTitle title in p.royalty.AllTitlesInEffectForReading)
            {
                RoyalTitle local = title;
                string label = title.def.GetLabelCapFor(p) + " (" + p.royalty.GetFavor(title.faction) + ")";
                chips.Add(new Chip
                {
                    Width = ChipWidth(label, true),
                    Draw = r =>
                    {
                        ChipLabel(r, label, local.faction.def.FactionIcon, local.faction.Color);
                        if (Widgets.ButtonInvisible(r))
                        {
                            Find.WindowStack.Add(new Dialog_InfoCard(local.def, local.faction, p));
                        }
                        if (Mouse.IsOver(r))
                        {
                            int favor = p.royalty.GetFavor(local.faction);
                            TooltipHandler.TipRegion(r, new TipSignal(() => titleTip(p, local.faction, local, favor), local.def.index * 37));
                        }
                    }
                });
            }
        }
        if (ModsConfig.IdeologyActive && !p.DevelopmentalStage.Baby() && p.story?.favoriteColor != null)
        {
            chips.Add(new Chip
            {
                Width = ChipHeight,
                Draw = r =>
                {
                    Rect box = r.ContractedBy(3f * Scale);
                    GUI.color = p.story.favoriteColor.color;
                    Widgets.DrawAtlas(box, ModernWidgets.Fill);
                    GUI.color = Style.Border;
                    Widgets.DrawAtlas(box, ModernWidgets.Line);
                    GUI.color = Color.white;
                    string orIdeo = p.Ideo != null && !p.Ideo.classicMode ? "OrIdeoColor".Translate(p.Named("PAWN")).ToString() : "";
                    TooltipHandler.TipRegion(r, () => "FavoriteColorTooltip".Translate(p.Named("PAWN"), p.story.favoriteColor.label.Named("COLOR"), 0.6f.ToStringPercent().Named("PERCENTAGE"), orIdeo.Named("ORIDEO")).Resolve(), 837472764);
                }
            });
        }
        if (p.guest != null && !p.guest.Recruitable)
        {
            chips.Add(new Chip
            {
                Width = ChipHeight,
                Draw = r =>
                {
                    ChipBack(r);
                    GUI.DrawTexture(r.ContractedBy(2f * Scale), UnrecruitableIcon);
                    TooltipHandler.TipRegion(r, () => "Unrecruitable".Translate().AsTipTitle().CapitalizeFirst() + "\n\n" + "UnrecruitableDesc".Translate(p.Named("PAWN")).Resolve(), 15877733);
                }
            });
        }
    }

    private static void Skill(Rect r, SkillRecord skill)
    {
        bool disabled = skill.TotallyDisabled;
        int level = skill.GetLevelForUI();
        float value = disabled ? 0f : Mathf.Clamp01((level + (level < SkillRecord.MaxLevel ? skill.XpProgressPercent : 0f)) / SkillRecord.MaxLevel);
        // one color for all, the badge says the passion
        Color color = new Color(0.55f, 0.68f, 0.8f);
        // the icon, the bar for how far along, the level in the passion badge after it
        float badge = Pane.BadgeWidth;
        float icon = Pane.SkillIconRoom(skill.def, r.height);
        Pane.DrawSkillIcon(r, skill.def, disabled);
        Rect bar = new Rect(r.x + icon, r.y, r.width - icon - badge - 4f * Scale, r.height);
        Pane.DrawBar(bar, new Pane.Bar { Label = skill.def.skillLabel.CapitalizeFirst(), Value = value, ValueText = "", Color = color, Tip = Mouse.IsOver(r) ? skillTip(skill) : null }, null);
        Pane.SkillBadge(new Rect(r.xMax - badge, r.y, badge, r.height), skill);
        GUI.color = Color.white;
    }

    // the game's buttons at the top right, right to left: banish, rename, renounce a title, execute
    private static float Buttons(Rect row, Pawn p)
    {
        if (p.health.Dead)
        {
            return 0f;
        }
        float size = Mathf.Round(22f * Scale);
        // room for our close button in the corner
        float x = row.width - 24f;
        Rect Next()
        {
            x -= size + 4f * Scale;
            return new Rect(x, Mathf.Round(row.center.y - size / 2f), size, size);
        }
        if (p.IsFreeColonist && p.Spawned && !p.IsQuestLodger())
        {
            Rect r = Next();
            TooltipHandler.TipRegion(r, PawnBanishUtility.GetBanishButtonTip(p));
            if (Widgets.ButtonImage(r, TexButton.Banish))
            {
                if (p.Downed)
                {
                    Messages.Message("MessageCantBanishDownedPawn".Translate(p.LabelShort, p).AdjustedFor(p), p, MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    PawnBanishUtility.ShowBanishPawnConfirmationDialog(p);
                }
            }
        }
        if (p.IsColonist || p.IsColonySubhuman || DebugSettings.ShowDevGizmos)
        {
            Rect r = Next();
            TooltipHandler.TipRegionByKey(r, "RenameColonist");
            if (Widgets.ButtonImage(r, TexButton.Rename))
            {
                Find.WindowStack.Add(p.NamePawnDialog());
            }
        }
        if (p.IsFreeColonist && !p.IsQuestLodger() && p.royalty != null && p.royalty.AllTitlesForReading.Count > 0)
        {
            Rect r = Next();
            TooltipHandler.TipRegionByKey(r, "RenounceTitle");
            if (Widgets.ButtonImage(r, TexButton.RenounceTitle))
            {
                RenounceMenu(p);
            }
        }
        if (p.guilt != null && p.guilt.IsGuilty && p.IsFreeColonist && !p.IsQuestLodger())
        {
            Rect r = Next();
            TooltipHandler.TipRegionByKey(r, "ExecuteColonist");
            if (Widgets.ButtonImage(r, TexButton.ExecuteColonist))
            {
                p.guilt.awaitingExecution = !p.guilt.awaitingExecution;
                if (p.guilt.awaitingExecution)
                {
                    Messages.Message("MessageColonistMarkedForExecution".Translate(p), p, MessageTypeDefOf.SilentInput, false);
                }
            }
            if (p.guilt.awaitingExecution)
            {
                ModernWidgets.CheckBox(new Rect(r.xMax - 10f * Scale, r.y - 2f * Scale, 12f * Scale, 12f * Scale), true, true);
            }
        }
        return row.width - x;
    }

    // the game's renounce menu and confirmation
    private static void RenounceMenu(Pawn pawn)
    {
        FloatMenuUtility.MakeMenu(pawn.royalty.AllTitlesForReading, title => "RenounceTitle".Translate() + ": " + "TitleOfFaction".Translate(title.def.GetLabelCapFor(pawn), title.faction.GetCallLabel()), title => () =>
        {
            List<FactionPermit> permits = pawn.royalty.PermitsFromFaction(title.faction);
            RoyalTitleUtility.FindLostAndGainedPermits(title.def, null, out _, out List<RoyalTitlePermitDef> lost);
            StringBuilder sb = new StringBuilder();
            if (lost.Count > 0 || permits.Count > 0)
            {
                sb.AppendLine("RenounceTitleWillLoosePermits".Translate(pawn.Named("PAWN")) + ":");
                foreach (RoyalTitlePermitDef permit in lost)
                {
                    RoyalTitleDef first = title.faction.def.RoyalTitlesAwardableInSeniorityOrderForReading.First(t => t.permits != null && t.permits.Contains(permit));
                    sb.AppendLine("- " + permit.LabelCap + " (" + first.GetLabelFor(pawn) + ")");
                }
                foreach (FactionPermit permit in permits)
                {
                    sb.AppendLine("- " + permit.Permit.LabelCap + " (" + permit.Title.GetLabelFor(pawn) + ")");
                }
                sb.AppendLine();
            }
            int points = pawn.royalty.GetPermitPoints(title.faction);
            if (points > 0)
            {
                sb.AppendLineTagged("RenounceTitleWillLosePermitPoints".Translate(pawn.Named("PAWN"), points.Named("POINTS"), title.faction.Named("FACTION")));
            }
            if (pawn.abilities.abilities.Any())
            {
                sb.AppendLine();
                sb.AppendLineTagged("RenounceTitleWillKeepPsylinkLevels".Translate(pawn.Named("PAWN")));
            }
            if (!title.faction.def.renounceTitleMessage.NullOrEmpty())
            {
                sb.AppendLine();
                sb.AppendLine(title.faction.def.renounceTitleMessage);
            }
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RenounceTitleDescription".Translate(pawn.Named("PAWN"), "TitleOfFaction".Translate(title.def.GetLabelCapFor(pawn), title.faction.GetCallLabel()).Named("TITLE"), sb.ToString().TrimEndNewlines().Named("EFFECTS")), () =>
            {
                pawn.royalty.SetTitle(title.faction, null, false);
                pawn.royalty.ResetPermitsAndPoints(title.faction, title.def);
            }, true));
        });
    }
}
