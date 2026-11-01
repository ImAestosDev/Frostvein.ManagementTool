using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Fmt.App;

// The main overlay: placed relative to the game and kept topmost while it is in front.
public sealed class PanelWindow : Window
{
    private readonly AppState _state;
    private readonly GameWindowTracker? _tracker;
    private readonly StackPanel _nav = new();
    private readonly ContentControl _host = new();
    private readonly Border _toast;
    private readonly TextBlock _toastText = Ui.T("", 13, wrap: true);
    private readonly Button _toastUndo;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Grid _body = new();
    private readonly StackPanel _characterHost = new();
    private PageView? _page;
    private string _pageKey = "dashboard";
    private nint _hwnd;
    private bool _dragging;
    private bool _wantOpen;

    public event Action? HotkeysChanged;

    public string PageKey => _pageKey;
    public PageView? CurrentPage => _page;
    public bool FreezePlacement { get; init; }
    public bool IsOpen => _wantOpen;
    public Updater? Updater { get; set; }
    public Action? Quit { get; set; }

    public PanelWindow(AppState state, GameWindowTracker? tracker)
    {
        _state = state;
        _tracker = tracker;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.NoResize;
        Title = "FMT";
        Topmost = tracker != null;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        WindowStartupLocation = WindowStartupLocation.Manual;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _toastUndo = Ui.Btn("Rückgängig", () => _state.Undo(), "Ghost", Ui.G.Undo);
        _toast = Ui.Card(Ui.Cols("*,Auto", _toastText, _toastUndo), 10);
        _toast.Margin = new Thickness(12);
        _toast.VerticalAlignment = VerticalAlignment.Bottom;
        _toast.Visibility = Visibility.Collapsed;
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast.Visibility = Visibility.Collapsed; };

        Content = BuildShell();
        BuildCharacterPicker();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW);
            Place();
        };
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => _state.Flush();

        _state.DataChanged += () => _page?.Refresh();
        _state.StructureChanged += () => { BuildNav(); BuildCharacterPicker(); };
        _state.Toast += ShowToast;
        BuildNav();
        ShowPage(_state.S.LastPage, activate: false);
    }

    private UIElement BuildShell()
    {
        var header = new DockPanel { Height = 40, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, LastChildFill = true };
        header.MouseLeftButtonDown += (_, _) => BeginDrag();

        var close = Ui.IconBtn(Ui.G.Close, "Schließen (Esc)", Hide2);
        DockPanel.SetDock(close, Dock.Right);
        close.Margin = new Thickness(0, 0, 6, 0);
        header.Children.Add(close);
        var power = Ui.IconBtn(Ui.G.Power, "FMT komplett beenden", () => Quit?.Invoke());
        DockPanel.SetDock(power, Dock.Right);
        header.Children.Add(power);
        var restart = Ui.IconBtn(Ui.G.Restart, "FMT neu starten und Update installieren", RestartApp);
        DockPanel.SetDock(restart, Dock.Right);
        header.Children.Add(restart);
        DockPanel.SetDock(_characterHost, Dock.Right);
        _characterHost.VerticalAlignment = VerticalAlignment.Center;
        _characterHost.Margin = new Thickness(0, 0, 6, 0);
        header.Children.Add(_characterHost);
        var logo = new Image { Source = Ui.Logo, Height = 18, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        header.Children.Add(Ui.Row(0, logo, Ui.T("FMT", 14, bold: true), Ui.T("  Frostvein Management", 12, "Muted", trim: true)));

        var side = new Border { Child = _nav, Padding = new Thickness(8, 4, 8, 8), BorderThickness = new Thickness(0, 0, 1, 0) };
        side.SetResourceReference(Border.BorderBrushProperty, "Line");

        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(side, 0);
        _body.Children.Add(side);
        Grid.SetColumn(_host, 1);
        _body.Children.Add(_host);
        Grid.SetColumn(_toast, 1);
        _body.Children.Add(_toast);

        var line = new Border { Height = 1 };
        line.SetResourceReference(Border.BackgroundProperty, "Line");

        var layout = new DockPanel { LayoutTransform = _scale };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(line, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(line);
        layout.Children.Add(_body);

        var grip = new Thumb { Width = 16, Height = 16, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0, ToolTip = "Größe ändern" };
        grip.DragDelta += OnResize;
        grip.DragCompleted += (_, _) => _state.Touch();

        var stack = new Grid();
        stack.Children.Add(layout);
        stack.Children.Add(grip);

        var root = new Border { Child = stack, BorderThickness = new Thickness(1), ClipToBounds = true };
        root.SetResourceReference(Border.BackgroundProperty, "Bg");
        root.SetResourceReference(Border.BorderBrushProperty, "Line");
        root.SetResourceReference(Border.CornerRadiusProperty, "Corner");
        return root;
    }

    private async void RestartApp()
    {
        if (Updater == null) return;
        ShowToast("Suche Update und starte neu …", false);
        await Updater.RestartAsync();
        // Only reached with a visible result when the restart did not happen.
        ShowToast(Updater.Status, false);
    }

    private void BuildCharacterPicker()
    {
        _characterHost.Children.Clear();
        var chars = _state.Data.Characters;
        if (chars.Count <= 1) return;
        var combo = Ui.Combo(chars.Select(c => (c, c)), _state.Character, c =>
        {
            _state.S.ActiveCharacter = c;
            _state.Touch();
            _page?.Refresh();
        });
        combo.MinWidth = 110;
        combo.ToolTip = "Aktiver Charakter: neue Einträge und Dailies zählen für ihn";
        _characterHost.Children.Add(combo);
    }

    private void BuildNav()
    {
        _nav.Children.Clear();
        var iconsOnly = _state.S.SidebarIconsOnly;
        _nav.Width = iconsOnly ? 40 : 158;

        void Add(string key, string glyph, string label, string? color = null)
        {
            var icon = color == null ? Ui.Glyph(glyph, 15) : Ui.GlyphColored(glyph, color, 15);
            icon.Width = 18;
            UIElement content = iconsOnly ? icon : Ui.Cols("27,*", icon, Ui.T(label, 13, trim: true));
            var b = new RadioButton
            {
                Style = (Style)Application.Current.Resources["Nav"],
                GroupName = "nav",
                Content = content,
                IsChecked = _pageKey == key,
                ToolTip = iconsOnly ? label : null,
            };
            b.Click += (_, _) => ShowPage(key);
            _nav.Children.Add(b);
        }

        Add("dashboard", Ui.G.Home, "Übersicht");
        Add("dailies", Ui.G.Check, "Dailies");
        if (_state.S.LandOfLife.Visible) Add("landoflife", Ui.G.Clock, _state.S.LandOfLife.Name);
        var sep = new Border { Height = 1, Margin = new Thickness(4, 6, 4, 6) };
        sep.SetResourceReference(Border.BackgroundProperty, "Line");
        _nav.Children.Add(sep);
        foreach (var c in _state.Data.VisibleCategories) Add("cat:" + c.Id, c.Icon, c.Name, c.Color);
        var sep2 = new Border { Height = 1, Margin = new Thickness(4, 6, 4, 6) };
        sep2.SetResourceReference(Border.BackgroundProperty, "Line");
        _nav.Children.Add(sep2);
        Add("layout", Ui.G.Sliders, "Kategorien");
        Add("settings", Ui.G.Settings, "Anpassen");
    }

    public void ShowPage(string key, bool activate = true)
    {
        var page = CreatePage(key);
        if (page == null)
        {
            key = "dashboard";
            page = CreatePage(key)!;
        }
        _pageKey = key;
        _page = page;
        _host.Content = page;
        page.Refresh();
        _state.S.LastPage = key;
        if (key.StartsWith("cat:", StringComparison.Ordinal)) _state.S.LastCategoryId = key[4..];
        _state.Touch();
        foreach (var r in _nav.Children.OfType<RadioButton>()) r.IsChecked = false;
        BuildNav();
        if (activate && !IsVisible) Open();
    }

    private PageView? CreatePage(string key)
    {
        if (key == "dashboard") return new DashboardView(_state, this);
        if (key == "dailies") return new DailiesView(_state);
        if (key == "landoflife" && _state.S.LandOfLife.Visible) return new LandOfLifeView(_state);
        if (key == "settings") return new SettingsView(_state, () => HotkeysChanged?.Invoke(), Updater);
        if (key == "layout") return new LayoutView(_state, this);
        if (key.StartsWith("cat:", StringComparison.Ordinal) && _state.Data.FindCategory(key[4..]) != null)
            return new CategoryView(_state, key[4..], this);
        return null;
    }

    public void Toggle()
    {
        if (_wantOpen && IsVisible) Hide2();
        else Open();
    }

    // Follows the game: hides while another program is in front, comes back afterwards.
    public void SyncWithGame()
    {
        if (_tracker == null) return;
        // Without a game it is a normal window on the desktop.
        Topmost = _tracker.Target != 0;
        if (!_wantOpen) return;
        var show = _tracker.Target == 0 || _tracker.ShowOverlay;
        if (show && !IsVisible)
        {
            ShowActivated = false;
            Show();
            ShowActivated = true;
            Place();
        }
        else if (!show && IsVisible) Hide();
    }

    public void Open()
    {
        _wantOpen = true;
        Show();
        Place();
        Activate();
        if (_hwnd != 0) Native.SetForegroundWindow(_hwnd);
        _page?.Refresh();
    }

    // Hides the panel and gives the keyboard back to the game.
    public void Hide2()
    {
        _wantOpen = false;
        _state.Flush();
        Hide();
        if (_tracker is { Target: not 0 } t) Native.SetForegroundWindow(t.Target);
    }

    public void QuickEntry()
    {
        var category = _state.Data.FindCategory(_state.S.LastCategoryId) ?? _state.Data.VisibleCategories.FirstOrDefault();
        if (category == null) return;
        ShowPage("cat:" + category.Id);
        Open();
        (_page as CategoryView)?.OpenEditor(null);
    }

    public void OpenEntry(Entry entry)
    {
        ShowPage("cat:" + entry.CategoryId);
        (_page as CategoryView)?.OpenEditor(entry);
    }

    public void ShowAndNotify(string message)
    {
        Open();
        ShowToast(message, false);
    }


    public void ApplySettings()
    {
        var scale = Math.Clamp(_state.S.Theme.Scale, 0.7, 1.6);
        _scale.ScaleX = _scale.ScaleY = scale;
        Place();
    }

    public void Place()
    {
        if (_hwnd == 0 || _dragging || !IsVisible || FreezePlacement) return;
        var s = _state.S;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var attached = _tracker is { IsVisible: true };
        RECT area;
        if (attached) area = _tracker!.Rect;
        else
        {
            Native.GetWindowRect(_hwnd, out var own);
            area = Native.WorkArea(own);
        }
        var screen = Native.WorkArea(area);
        var scale = Math.Clamp(s.Theme.Scale, 0.7, 1.6);

        int w, h, x, y;
        if (s.Dock == DockMode.Free)
        {
            w = (int)(s.FreeWidth * dpi);
            h = (int)(s.FreeHeight * dpi);
            x = area.Left + (int)(s.FreeX * area.Width);
            y = area.Top + (int)(s.FreeY * area.Height);
        }
        else
        {
            const int margin = 10;
            w = (int)(s.PanelWidth * scale * dpi);
            h = (int)(area.Height * Math.Clamp(s.PanelHeightPercent, 30, 100) / 100);
            y = area.Top + (area.Height - h) / 2;
            x = s.Dock switch
            {
                DockMode.InsideLeft => area.Left + margin,
                DockMode.OutsideRight => area.Right + margin,
                DockMode.OutsideLeft => area.Left - margin - w,
                _ => area.Right - margin - w,
            };
            // A maximized game leaves no outside, so dock inside instead.
            if (s.Dock == DockMode.OutsideRight && x + w > screen.Right) x = area.Right - margin - w;
            if (s.Dock == DockMode.OutsideLeft && x < screen.Left) x = area.Left + margin;
        }
        w = Math.Min(w, screen.Width);
        h = Math.Min(h, screen.Height);
        x = Math.Clamp(x, screen.Left, screen.Right - w);
        y = Math.Clamp(y, screen.Top, screen.Bottom - h);
        Native.SetWindowPos(_hwnd, 0, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    private void BeginDrag()
    {
        _dragging = true;
        Native.GetWindowRect(_hwnd, out var before);
        DragMove();
        _dragging = false;
        Native.GetWindowRect(_hwnd, out var after);
        if (before.Equals(after)) return;
        StoreFreePosition(after);
        _state.Touch();
    }

    private void StoreFreePosition(RECT r)
    {
        var s = _state.S;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var area = _tracker is { IsVisible: true } t ? t.Rect : Native.WorkArea(r);
        s.Dock = DockMode.Free;
        s.FreeX = (double)(r.Left - area.Left) / Math.Max(1, area.Width);
        s.FreeY = (double)(r.Top - area.Top) / Math.Max(1, area.Height);
        s.FreeWidth = r.Width / dpi;
        s.FreeHeight = r.Height / dpi;
    }

    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Native.GetWindowRect(_hwnd, out var r);
        var w = Math.Max((int)(420 * dpi), r.Width + (int)(e.HorizontalChange * dpi));
        var h = Math.Max((int)(360 * dpi), r.Height + (int)(e.VerticalChange * dpi));
        Native.SetWindowPos(_hwnd, 0, r.Left, r.Top, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Native.GetWindowRect(_hwnd, out r);
        StoreFreePosition(r);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var typing = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.Escape)
        {
            if (Keyboard.FocusedElement is DependencyObject { } d && ItemsControl.ItemsControlFromItemContainer(d) is ComboBox { IsDropDownOpen: true }) return;
            e.Handled = true;
            if (_page?.HandleEscape() != true) Hide2();
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !typing)
        {
            e.Handled = true;
            _state.Undo();
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control && _page is CategoryView cv)
        {
            e.Handled = true;
            cv.OpenEditor(null);
        }
    }

    private void ShowToast(string message, bool undoable)
    {
        _toastText.Text = message;
        _toastUndo.Visibility = undoable && _state.Store.CanUndo ? Visibility.Visible : Visibility.Collapsed;
        _toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
