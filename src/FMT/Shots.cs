#if DEBUG
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Fmt.App;

// Dev only: renders every page off-screen with sample data into PNGs.
internal static class Shots
{
    public static void Run(string dataPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var store = new Store(dataPath);
        store.Load();
        Seed(store.Data);
        var state = new AppState(store);
        ThemeManager.Apply(state.S.Theme);

        var panel = new PanelWindow(state, null)
        {
            Left = -6000, Top = -6000, Width = 600, Height = 1000, ShowActivated = false, FreezePlacement = true,
        };
        panel.Show();
        var trading = store.Data.Categories[0].Id;
        var farming = store.Data.Categories[1].Id;

        Capture(panel, Path.Combine(outDir, "1-dashboard.png"), () => panel.ShowPage("dashboard", false));
        Capture(panel, Path.Combine(outDir, "2-dailies.png"), () => panel.ShowPage("dailies", false));
        panel.Height = 1500;
        Capture(panel, Path.Combine(outDir, "2b-landoflife.png"), () => panel.ShowPage("landoflife", false));
        panel.Height = 1000;
        Capture(panel, Path.Combine(outDir, "3-trading.png"), () => panel.ShowPage("cat:" + trading, false));
        Capture(panel, Path.Combine(outDir, "4-editor.png"), () =>
        {
            panel.ShowPage("cat:" + farming, false);
            ((CategoryView)panel.CurrentPage!).OpenEditor(null);
        });
        Capture(panel, Path.Combine(outDir, "4b-nostaler.png"), () => panel.ShowPage("cat:" + store.Data.Categories.First(c => c.Name == "NosTaler").Id, false));
        Capture(panel, Path.Combine(outDir, "4c-notes.png"), () => panel.ShowPage("cat:" + store.Data.Categories.First(c => c.Name == "Notizen").Id, false));
        Capture(panel, Path.Combine(outDir, "5-layout.png"), () => panel.ShowPage("layout", false));
        Capture(panel, Path.Combine(outDir, "6-settings.png"), () => panel.ShowPage("settings", false));
        Capture(panel, Path.Combine(outDir, "7-light.png"), () =>
        {
            ThemeManager.ApplyPreset(state.S.Theme, "Hell");
            ThemeManager.Apply(state.S.Theme);
            panel.ShowPage("dashboard", false);
        });
        RenderButton(Path.Combine(outDir, "button.png"));
        Application.Current.Shutdown();
    }

    private static void Capture(PanelWindow panel, string file, Action show)
    {
        show();
        Pump();
        var root = (FrameworkElement)panel.Content;
        var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var backing = new DrawingVisual();
        using (var dc = backing.RenderOpen())
        {
            // Stands in for the game behind the translucent panel.
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(40, 50, 40)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bmp.Render(backing);
        bmp.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        png.Save(fs);
    }

    private static void RenderButton(string file)
    {
        var button = PillWindow.BuildButton();
        button.Width = PillWindow.ClockWidthPixels;
        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        button.Arrange(new Rect(button.DesiredSize));
        button.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)button.ActualWidth, (int)button.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(button);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        png.Save(fs);
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        }
    }

    private static void Seed(AppData d)
    {
        var today = Stats.GameDay(DateTime.Now, 0);
        d.Characters = ["Main", "Twink"];
        var trading = d.Categories[0];
        var farming = d.Categories[1];
        var raids = d.Categories[2];
        var rng = new Random(7);

        void Add(Category c, int daysAgo, params (string Field, string Value)[] values)
        {
            var e = new Entry { CategoryId = c.Id, Date = today.AddDays(-daysAgo), Character = "Main" };
            foreach (var (field, value) in values)
            {
                var f = c.Fields.First(x => x.Name == field);
                e.Values[f.Id] = f.Type == FieldType.Gold ? Gold.Parse(value)!.Value.ToString() : f.Type == FieldType.Minutes ? Duration.Parse(value)!.Value.ToString() : value;
            }
            d.Entries.Add(e);
        }

        Add(trading, 0, ("Item", "Engelsflügel"), ("Menge", "1"), ("Einkauf", "50kk"), ("Verkauf", "72kk"));
        Add(trading, 0, ("Item", "Perfektionsstein"), ("Menge", "20"), ("Einkauf", "18kk"));
        Add(trading, 1, ("Item", "Fee Lvl 90"), ("Menge", "1"), ("Einkauf", "120kk"), ("Verkauf", "105kk"));
        Add(trading, 3, ("Item", "Rune A"), ("Menge", "5"), ("Einkauf", "8,5kk"));
        Add(farming, 0, ("Spot", "Land des Todes"), ("Dauer", "1h 30m"), ("Gold", "14kk"), ("Drops", "3x Seele"));
        Add(raids, 0, ("Raid", "Draco"), ("Runs", "3"), ("Dauer", "45m"), ("Gold", "9kk"));
        var nosTaler = d.Categories.First(c => c.Name == "NosTaler");
        var notes = d.Categories.First(c => c.Name == "Notizen");
        if (!d.Entries.Any(e => e.CategoryId == nosTaler.Id))
        {
            Add(nosTaler, 0, ("Betrag", "100"));
            Add(nosTaler, 1, ("Betrag", "19.99"));
        }
        Add(notes, 0, ("Titel", "Markt"), ("Notiz", "Perfektionssteine abends checken\nPreis steigt am Wochenende"));
        for (var i = 1; i < 21; i++)
        {
            if (i % 5 == 0) continue;
            Add(farming, i, ("Spot", i % 2 == 0 ? "Land des Todes" : "Krem"), ("Dauer", $"{40 + rng.Next(100)}"), ("Gold", $"{4 + rng.Next(30)}kk"));
            if (i % 4 == 0) Add(trading, i, ("Item", "Sammelware"), ("Einkauf", "40kk"), ("Verkauf", $"{26 + rng.Next(20)}kk"));
            if (i % 3 == 0) Add(raids, i, ("Raid", "Glacerus"), ("Runs", "2"), ("Gold", $"{6 + rng.Next(10)}kk"));
        }

        foreach (var t in d.Tasks) t.CreatedDay = today.AddDays(-30);
        for (var i = 0; i < 21; i++)
        {
            var limit = i == 0 ? 3 : i < 6 ? 5 : rng.Next(6);
            foreach (var t in d.Tasks.Where(t => t.Recurrence == Recurrence.Daily).Take(limit))
                Stats.SetDone(d, t, "Main", today.AddDays(-i), true);
        }
    }
}
#endif
