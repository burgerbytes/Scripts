using System.Text;
using UnityEngine;

public enum InfoPanelSubjectKind
{
    Generic,
    Hero,
    Monster,
    Item
}

public enum InfoPanelDefaultTab
{
    Info,
    Reel,
    Status
}

public interface IInfoPanelContentProvider
{
    InfoPanelPresentation BuildInfoPanelPresentation();
}

[System.Serializable]
public class InfoPanelPresentation
{
    public InfoPanelSubjectKind SubjectKind = InfoPanelSubjectKind.Generic;
    public InfoPanelDefaultTab DefaultTab = InfoPanelDefaultTab.Info;
    public InfoPanelData Info;
    public bool ShowInfoTab = true;
    public bool ShowReelTab = false;
    public bool ShowStatusTab = false;
}

public static class InfoPanelContentFactory
{
    public static InfoPanelPresentation BuildGenericPresentation(string title, string body, Sprite image = null)
    {
        return new InfoPanelPresentation
        {
            SubjectKind = InfoPanelSubjectKind.Generic,
            DefaultTab = InfoPanelDefaultTab.Info,
            ShowInfoTab = true,
            ShowReelTab = false,
            ShowStatusTab = false,
            Info = new InfoPanelData
            {
                title = string.IsNullOrWhiteSpace(title) ? "Info" : title,
                body = string.IsNullOrWhiteSpace(body)
                    ? "Shared info panel placeholder. Wire a subject-specific provider later to replace this text."
                    : body,
                image = image
            }
        };
    }

    public static InfoPanelPresentation BuildMonsterPresentation(Monster monster)
    {
        string title = monster != null && !string.IsNullOrWhiteSpace(monster.DisplayName)
            ? monster.DisplayName
            : "Monster";

        string body = BuildMonsterPlaceholderBody(monster, null);

        return new InfoPanelPresentation
        {
            SubjectKind = InfoPanelSubjectKind.Monster,
            DefaultTab = InfoPanelDefaultTab.Info,
            ShowInfoTab = true,
            ShowReelTab = monster != null,
            ShowStatusTab = monster != null,
            Info = new InfoPanelData
            {
                title = title,
                body = body,
                image = null
            }
        };
    }

    public static InfoPanelPresentation BuildMonsterPresentation(Monster monster, InfoPanelData overrideData)
    {
        InfoPanelPresentation presentation = BuildMonsterPresentation(monster);

        if (!string.IsNullOrWhiteSpace(overrideData.title))
            presentation.Info.title = overrideData.title;

        if (!string.IsNullOrWhiteSpace(overrideData.body))
            presentation.Info.body = BuildMonsterPlaceholderBody(monster, overrideData.body);

        if (overrideData.image != null)
            presentation.Info.image = overrideData.image;

        return presentation;
    }

    public static InfoPanelPresentation BuildHeroPresentation(HeroStats hero)
    {
        string title = hero != null ? hero.name : "Hero";

        return new InfoPanelPresentation
        {
            SubjectKind = InfoPanelSubjectKind.Hero,
            DefaultTab = InfoPanelDefaultTab.Info,
            ShowInfoTab = true,
            ShowReelTab = false,
            ShowStatusTab = false,
            Info = new InfoPanelData
            {
                title = title,
                body = BuildHeroPlaceholderBody(hero, null),
                image = hero != null ? hero.Portrait : null
            }
        };
    }

    public static InfoPanelPresentation BuildHeroPresentation(HeroStats hero, InfoPanelData overrideData)
    {
        InfoPanelPresentation presentation = BuildHeroPresentation(hero);

        if (!string.IsNullOrWhiteSpace(overrideData.title))
            presentation.Info.title = overrideData.title;

        if (!string.IsNullOrWhiteSpace(overrideData.body))
            presentation.Info.body = BuildHeroPlaceholderBody(hero, overrideData.body);

        if (overrideData.image != null)
            presentation.Info.image = overrideData.image;

        return presentation;
    }

    public static InfoPanelPresentation BuildItemPresentation(ItemSO item)
    {
        string title = item != null && !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName : "Item";

        return new InfoPanelPresentation
        {
            SubjectKind = InfoPanelSubjectKind.Item,
            DefaultTab = InfoPanelDefaultTab.Info,
            ShowInfoTab = true,
            ShowReelTab = false,
            ShowStatusTab = false,
            Info = new InfoPanelData
            {
                title = title,
                body = BuildItemPlaceholderBody(item, null),
                image = item != null ? item.icon : null
            }
        };
    }

    public static InfoPanelPresentation BuildItemPresentation(ItemSO item, InfoPanelData overrideData)
    {
        InfoPanelPresentation presentation = BuildItemPresentation(item);

        if (!string.IsNullOrWhiteSpace(overrideData.title))
            presentation.Info.title = overrideData.title;

        if (!string.IsNullOrWhiteSpace(overrideData.body))
            presentation.Info.body = BuildItemPlaceholderBody(item, overrideData.body);

        if (overrideData.image != null)
            presentation.Info.image = overrideData.image;

        return presentation;
    }

    private static string BuildMonsterPlaceholderBody(Monster monster, string suppliedBody)
    {
        StringBuilder sb = new StringBuilder(256);

        if (monster != null)
        {
            sb.AppendLine($"HP: {monster.CurrentHp}/{monster.MaxHp}");
            sb.AppendLine($"Damage: {monster.GetDamage()}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(suppliedBody))
        {
            sb.AppendLine(suppliedBody.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("Shared monster info placeholder.");
        sb.AppendLine("Use this tab for a reusable summary that any subject type can populate later.");
        sb.AppendLine("Future data source: monster info provider or encounter-specific presenter.");

        return sb.ToString().Trim();
    }

    private static string BuildHeroPlaceholderBody(HeroStats hero, string suppliedBody)
    {
        StringBuilder sb = new StringBuilder(256);

        if (hero != null)
        {
            sb.AppendLine($"Level: {hero.Level}");
            sb.AppendLine($"HP: {hero.CurrentHp}/{hero.MaxHp}");
            sb.AppendLine($"Stamina: {hero.CurrentStamina}/{hero.MaxStamina}");
            sb.AppendLine($"ATK: {hero.Attack}   DEF: {hero.Defense}   SPD: {hero.Speed}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(suppliedBody))
        {
            sb.AppendLine(suppliedBody.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("Shared hero info placeholder.");
        sb.AppendLine("Use this same Info tab shell for heroes, with provider-driven content later.");
        sb.AppendLine("Future data source: hero info provider, equipment summary, and status summary.");

        return sb.ToString().Trim();
    }

    private static string BuildItemPlaceholderBody(ItemSO item, string suppliedBody)
    {
        StringBuilder sb = new StringBuilder(256);

        if (item != null && !string.IsNullOrWhiteSpace(item.description))
        {
            sb.AppendLine(item.description.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(suppliedBody))
        {
            sb.AppendLine(suppliedBody.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("Shared item info placeholder.");
        sb.AppendLine("Use this same Info tab shell for items, relics, consumables, and equipment later.");
        sb.AppendLine("Future data source: inventory item provider or item tooltip presenter.");

        return sb.ToString().Trim();
    }
}
