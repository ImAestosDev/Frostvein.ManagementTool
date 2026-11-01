namespace Fmt.Core;

public static class Defaults
{
    private static FieldDef F(string name, FieldType type, bool show = true, bool required = false, string def = "") =>
        new() { Name = name, Type = type, ShowInList = show, Required = required, DefaultValue = def };

    private static FieldDef Calculated(string name, string formula, bool gold = true) =>
        new() { Name = name, Type = FieldType.Formula, Formula = formula, FormulaIsGold = gold };

    public static AppData Create(DateOnly today)
    {
        var data = new AppData();
        data.Categories.AddRange([Trading(), Farming(), Raids(), Crafting(), Events(), GoldBook(), NosTaler(), Notes()]);
        foreach (var (name, group, recurrence) in new[]
        {
            ("Tagesquests", "Quests", Recurrence.Daily),
            ("Instanzen laufen", "Quests", Recurrence.Daily),
            ("Raid-Runs", "Raids", Recurrence.Daily),
            ("Arena", "PvP", Recurrence.Daily),
            ("Markt checken", "Handel", Recurrence.Daily),
            ("Wochenboss", "Raids", Recurrence.Weekly),
            ("Family-Aufgaben", "Family", Recurrence.Weekly),
        })
        {
            data.Tasks.Add(new DailyTask { Name = name, Group = group, Recurrence = recurrence, CreatedDay = today });
        }
        return data;
    }

    private static Category Trading()
    {
        var profit = Calculated("Gewinn", "{Verkauf} - {Einkauf}");
        return new Category
        {
            Name = "Trading", Icon = "", Color = "#E8B65A", GoldFieldId = profit.Id,
            Fields = [F("Item", FieldType.Text, required: true), F("Menge", FieldType.Number, def: "1"),
                F("Einkauf", FieldType.Gold), F("Verkauf", FieldType.Gold), profit, F("Notiz", FieldType.LongText, show: false)],
        };
    }

    private static Category Farming()
    {
        var gold = F("Gold", FieldType.Gold);
        return new Category
        {
            Name = "Farming", Icon = "", Color = "#5CC28F", GoldFieldId = gold.Id,
            Fields = [F("Spot", FieldType.Text, required: true), F("Dauer", FieldType.Minutes), gold,
                Calculated("Gold/h", "{Gold} / {Dauer} * 60"), F("Drops", FieldType.Text), F("Notiz", FieldType.LongText, show: false)],
        };
    }

    private static Category Raids()
    {
        var gold = F("Gold", FieldType.Gold);
        var raid = F("Raid", FieldType.Select, required: true);
        raid.Options = ["Cuby", "Ginseng", "Castra", "Spider", "Slade", "Ibrahim", "Kertos", "Valakus", "Grenigas",
            "Draco", "Glacerus", "Laurena", "Zenas", "Erenia", "Fernon", "Belial", "Paimon", "Morcos", "Hatus", "Calvina", "Berios"];
        return new Category
        {
            Name = "Raids", Icon = "", Color = "#E5787C", GoldFieldId = gold.Id,
            Fields = [raid, F("Runs", FieldType.Number, def: "1"), F("Dauer", FieldType.Minutes), gold,
                F("Drops", FieldType.Text), F("Notiz", FieldType.LongText, show: false)],
        };
    }

    private static Category Crafting()
    {
        var profit = Calculated("Gewinn", "{Wert} - {Kosten}");
        return new Category
        {
            Name = "Crafting", Icon = "", Color = "#B79CED", GoldFieldId = profit.Id,
            Fields = [F("Item", FieldType.Text, required: true), F("Menge", FieldType.Number, def: "1"),
                F("Kosten", FieldType.Gold), F("Wert", FieldType.Gold), profit, F("Notiz", FieldType.LongText, show: false)],
        };
    }

    private static Category Events()
    {
        var gold = F("Gold", FieldType.Gold);
        return new Category
        {
            Name = "Events", Icon = "", Color = "#6FB7E9", GoldFieldId = gold.Id,
            Fields = [F("Event", FieldType.Text, required: true), F("Belohnung", FieldType.Text), gold, F("Notiz", FieldType.LongText, show: false)],
        };
    }

    public static Category NosTaler()
    {
        var amount = F("NosTaler", FieldType.Number, required: true);
        return new Category
        {
            Name = "NosTaler", Icon = "", Color = "#54C7A0", SumFieldId = amount.Id, CountsInTotal = false,
            Fields = [F("Wofür", FieldType.Text, required: true), amount, F("Notiz", FieldType.LongText, show: false)],
        };
    }

    public static Category Notes() => new()
    {
        Name = "Notizen", Icon = "", Color = "#8EA3B5", CountsInTotal = false,
        Fields = [F("Titel", FieldType.Text, required: true), F("Notiz", FieldType.LongText)],
    };

    private static Category GoldBook()
    {
        var start = F("Startgold", FieldType.Gold);
        var end = F("Endgold", FieldType.Gold);
        start.PrefillFrom = end.Id;
        var diff = Calculated("Differenz", "{Endgold} - {Startgold}");
        return new Category
        {
            Name = "Gold pro Tag", Icon = "", Color = "#F0D264", GoldFieldId = diff.Id, CountsInTotal = false,
            Fields = [start, end, diff, F("Notiz", FieldType.LongText, show: false)],
        };
    }
}
