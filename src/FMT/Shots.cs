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
        panel.Height = 1700;
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
        Capture(panel, Path.Combine(outDir, "4d-damage-off.png"), () => panel.ShowPage("damage", false));
        var capture = new PacketCapture(new Dictionary<long, string>(), state.S.PacketLogHidden, new NameBook());
        capture.GetType().GetProperty(nameof(PacketCapture.IsRunning))!.SetValue(capture, true);
        panel.Capture = capture;
        capture.Feed(1, "c_info Aestos - -1 -1 - 77 0 0 0 0 1 0 0 0 0 0 0 0");
        capture.Feed(1, "in 1 Mitspieler - 99 20 30 2 0 0 0");
        foreach (var damage in new[] { 1200, 4800, 45123, 3900 })
            capture.Feed(1, $"su 1 77 3 500 240 10 11 12 20 30 1 80 {damage} 0 0");
        capture.Feed(1, "su 1 77 3 500 240 10 11 12 20 30 1 80 0 2 0");
        capture.Feed(1, "su 1 99 3 500 240 10 11 12 20 30 1 80 99999 0 0");
        capture.Feed(1, "su 1 123 3 500 240 10 11 12 20 30 1 80 7777 0 0");
        capture.Feed(1, "mv 3 3071 131 42 5");
        capture.Feed(1, "drop 1046 900 20 30 5000 0 -1");
        capture.Feed(1, "get 1 77 900 0");
        for (var i = 0; i < 22; i++)
            capture.Feed(1, $"su 1 {1000 + i} 3 500 240 10 11 12 20 30 1 80 {90000 - i * 3000} 0 0");
        foreach (var mode in Enum.GetValues<DamageMode>())
        {
            state.S.DamageMode = mode;
            Capture(panel, Path.Combine(outDir, $"4e-damage-{mode}.png"), () => panel.ShowPage("damage", false));
            RenderCounter(Path.Combine(outDir, $"counter-{mode}.png"), capture, mode, false);
            RenderCounter(Path.Combine(outDir, $"counter-{mode}-small.png"), capture, mode, true);
        }
        capture.Feed(1, "lev 85 2500000 40 500000 10000000 1000000 100000 50 700000 12 2800000 0");
        capture.StartExpRecording(1);
        capture.Feed(1, "lev 85 3742000 40 512000 10000000 1000000 100000 50 700000 12 2800000 0");
        Capture(panel, Path.Combine(outDir, "4g-exp.png"), () => panel.ShowPage("exp", false));
        RenderExp(Path.Combine(outDir, "counter-exp.png"), capture);
        foreach (var (vnum, amount, id) in new[] { (1012, 42, 901), (2282, 7, 902), (1, 1, 903), (5060, 1, 904), (1012, 8, 905), (500, 2, 906), (2, 3, 907) })
        {
            capture.Feed(1, $"drop {vnum} {id} 20 30 {amount} 0 77");
            capture.Feed(1, $"get 1 77 {id} 0");
        }
        Capture(panel, Path.Combine(outDir, "4h-drops.png"), () => panel.ShowPage("drops", false));
        RenderDrops(Path.Combine(outDir, "counter-drops.png"), capture);
        capture.Feed(1, "inv 1 3.1242.50.0 4.1011.20.0");
        capture.Feed(1, "inv 2 7.2329.2.0");
        capture.Feed(1, "in 3 333 600 10 10 2 100 100 0 0 0 -1 1 0");
        capture.Feed(1, "in 3 1 601 10 10 2 100 100 0 0 0 -1 1 0");
        capture.Feed(1, "in 3 2 602 10 10 2 100 100 0 0 0 -1 1 0");
        capture.StartSession(1);
        foreach (var id in new[] { 600, 601, 602 }) capture.Feed(1, $"su 1 77 3 {id} 240 10 11 12 20 30 0 0 500 0 0");
        capture.Feed(1, "ivn 1 3.1242.38.0");
        capture.Feed(1, "ivn 2 7.2329.1.0");
        capture.Feed(1, "bf 1 77 0.121.21000 90");
        capture.Feed(1, "su 1 77 3 500 240 10 11 12 20 30 1 80 45123 0 0");
        capture.Feed(1, "lev 85 3992000 40 522000 10000000 1000000 100000 50 700000 12 2800000 0");
        capture.Feed(1, "drop 1012 950 20 30 12 0 77");
        capture.Feed(1, "get 1 77 950 0");
        capture.Feed(1, "drop 1046 951 20 30 25000 0 77");
        capture.Feed(1, "get 1 77 951 0");
        System.Threading.Thread.Sleep(1600);
        capture.Feed(1, "mv 3 1 1 1 1");
        panel.Height = 1700;
        Capture(panel, Path.Combine(outDir, "4i-session-running.png"), () => panel.ShowPage("session", false));
        Capture(panel, Path.Combine(outDir, "4k-items.png"), () => panel.ShowPage("items", false));
        Capture(panel, Path.Combine(outDir, "4l-monsters.png"), () => panel.ShowPage("monsters", false));
        RenderSession(Path.Combine(outDir, "counter-session-idle.png"), null);
        RenderSession(Path.Combine(outDir, "counter-session-running.png"), capture.Session(1));
        RenderSession(Path.Combine(outDir, "counter-session-warning.png"), capture.Session(1)! with { Running = false, Duration = Session.Limit, TimeIsUp = true });
        capture.EndSession(1);
        Capture(panel, Path.Combine(outDir, "4j-session-ended.png"), () => panel.ShowPage("session", false));
        Capture(panel, Path.Combine(outDir, "4m-history.png"), () => panel.ShowPage("sessions", false));
        Capture(panel, Path.Combine(outDir, "4n-history-detail.png"), () => ((SessionHistoryView)panel.CurrentPage!).Open(capture.SavedSessions()[0]));
        panel.Height = 1000;
        foreach (var line in new[] { "in 3 809 700 1 1 2 100 100 0", "in 3 809 701 1 1 2 100 100 0", "in 3 1481 702 1 1 2 100 100 0", "in 3 1461 710 1 1 2 100 100 0", "in 3 1482 711 1 1 2 100 100 0", "msgi 0 2566 0 0 0 0 0", "su 1 77 3 701 240 10 11 12 20 30 0 0 500 0 0", "su 1 77 3 710 240 10 11 12 20 30 0 0 500 0 0" })
            capture.Feed(1, line);
        panel.Height = 1900;
        Capture(panel, Path.Combine(outDir, "2c-land-timers.png"), () => panel.ShowPage("landoflife", false));
        panel.Height = 1000;
        var landCard = new LandCard();
        landCard.Show(capture.Land(1, state.S.LandOfLife));
        landCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        landCard.Arrange(new Rect(landCard.DesiredSize));
        landCard.UpdateLayout();
        var landBitmap = new RenderTargetBitmap((int)Math.Ceiling(landCard.ActualWidth), (int)Math.Ceiling(landCard.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        landBitmap.Render(landCard);
        var landPng = new PngBitmapEncoder();
        landPng.Frames.Add(BitmapFrame.Create(landBitmap));
        using (var landFile = File.Create(Path.Combine(outDir, "counter-land.png"))) landPng.Save(landFile);
        Capture(panel, Path.Combine(outDir, "4f-packets.png"), () => panel.ShowPage("packets", false));
        Capture(panel, Path.Combine(outDir, "5-layout.png"), () => panel.ShowPage("layout", false));
        Capture(panel, Path.Combine(outDir, "6-settings.png"), () => panel.ShowPage("settings", false));
        Capture(panel, Path.Combine(outDir, "7-light.png"), () =>
        {
            ThemeManager.ApplyPreset(state.S.Theme, "Hell");
            ThemeManager.Apply(state.S.Theme);
            panel.ShowPage("dashboard", false);
        });
        foreach (var skin in new[] { "Dunkel", "NosTale" })
        {
            ThemeManager.ApplyPreset(state.S.Theme, skin);
            ThemeManager.Apply(state.S.Theme);
            Capture(panel, Path.Combine(outDir, $"8-{skin}-dashboard.png"), () => panel.ShowPage("dashboard", false));
            Capture(panel, Path.Combine(outDir, $"8-{skin}-settings.png"), () => panel.ShowPage("settings", false));
            Capture(panel, Path.Combine(outDir, $"8-{skin}-drops.png"), () => panel.ShowPage("drops", false));
        }
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

    private static void RenderCounter(string file, PacketCapture capture, DamageMode mode, bool small)
    {
        var card = new DamageCard(() => { }, _ => { }, () => { });
        card.Show(capture.Stats(1), mode, small);
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(card);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        png.Save(fs);
    }

    private static void RenderExp(string file, PacketCapture capture)
    {
        var card = new ExpCard(() => { });
        card.Show(capture.Exp(1));
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(card);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        png.Save(fs);
    }

    private static void RenderDrops(string file, PacketCapture capture)
    {
        var card = new DropCard(() => { });
        card.Show(capture.Drops(1));
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(card);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        png.Save(fs);
    }

    private static void RenderSession(string file, SessionStats? stats)
    {
        var card = new SessionCard(() => { }, () => { }, () => { });
        card.Show(stats);
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        card.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(card);
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
