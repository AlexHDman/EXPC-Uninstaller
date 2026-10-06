using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace ExpcUninstaller;

internal sealed class ProgramGrid : DataGridView
{
    public ProgramGrid() { DoubleBuffered = true; }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (RowCount == 0) return;
        var page = Math.Max(1, DisplayedRowCount(false));
        var lines = SystemInformation.MouseWheelScrollLines < 0 ? page : SystemInformation.MouseWheelScrollLines;
        FirstDisplayedScrollingRowIndex = Math.Clamp(Math.Max(0, FirstDisplayedScrollingRowIndex)
            - Math.Sign(e.Delta) * lines, 0, Math.Max(0, RowCount - page));
    }
}

internal sealed class MainWindow : Form
{
    private readonly ProgramGrid _grid = new();
    private readonly GlassScrollBar _scroll = new();
    private readonly MenuStrip _menu = new();
    private readonly TextBox _search = new();
    private readonly Panel _searchBox = new();
    private readonly ToolTip _tips = new() { ShowAlways = true, InitialDelay = 350, ReshowDelay = 100 };
    private readonly List<GlassButton> _actions = [];
    private readonly List<GlassButton> _caption = [];
    private readonly Dictionary<string, Bitmap?> _icons = new();
    private readonly HashSet<string> _iconRequests = [];
    private readonly SemaphoreSlim _iconSlots = new(2);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Icon _applicationIcon;
    private readonly CatalogResult?[] _catalogs = new CatalogResult?[2];
    private readonly Task<CatalogResult>?[] _pending = new Task<CatalogResult>?[2];
    private List<AppEntry> _visible = [];
    private int _tab;
    private int _sortColumn;
    private bool _descending;
    private bool _rowsPainted;
    private bool _shown;
    private string _status = UiText.Loading;
    private readonly GlassButton _refresh;
    private readonly GlassButton _programs;
    private readonly GlassButton _store;
    private int S(float value) => (int)Math.Round(value * DeviceDpi / 96f);

    public MainWindow()
    {
        using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Expc.Icon.ico")!;
        _applicationIcon = new Icon(iconStream, 32, 32);
        Icon = _applicationIcon;
        Text = $"{UiText.Title} — {UiText.Design}";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        FormBorderStyle = FormBorderStyle.None;
        BackColor = GlassTeal.Background;
        ForeColor = GlassTeal.Text;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 940);
        MinimumSize = new Size(640, 440);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        BuildMenu();
        AddAction(Glyph.Delete, UiText.Delete + " — " + UiText.Disabled, false);
        _refresh = AddAction(Glyph.Refresh, UiText.RefreshHint, true);
        _refresh.Click += async (_, _) => await LoadCatalog(true);
        AddAction(Glyph.Update, UiText.CheckVersions + " — " + UiText.Disabled, false);
        AddAction(Glyph.Theme, UiText.ThemeHint, false);
        AddAction(Glyph.Language, UiText.LanguageHint, false);
        _programs = AddAction(Glyph.Programs, UiText.Programs, true);
        _store = AddAction(Glyph.Store, UiText.StoreHint, true);
        _programs.Selected = true;
        _programs.Click += async (_, _) => await SwitchTab(0);
        _store.Click += async (_, _) => await SwitchTab(1);
        AddCaption(Glyph.Minimize, UiText.Minimize, () => WindowState = FormWindowState.Minimized);
        AddCaption(Glyph.Maximize, UiText.Maximize, ToggleMaximize);
        AddCaption(Glyph.Close, UiText.Close, Close);
        BuildGrid();
        BuildSearch();
        KeyPreview = true;
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F5) { e.Handled = true; await LoadCatalog(true); }
            if (e.Control && e.KeyCode == Keys.F) { _search.Focus(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.Tab) { e.Handled = true; await SwitchTab(1 - _tab); }
            if (e.KeyCode == Keys.Escape && _search.Focused) { _search.Clear(); e.Handled = true; }
        };
        Shown += async (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(S(720), area.Width), Math.Min(S(940), area.Height));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            _shown = true;
            LayoutShell();
            Update();
            Measurements.Record("window-painted", dpi: DeviceDpi);
            await LoadCatalog(false);
        };
        LayoutShell();
    }

    private GlassButton AddAction(Glyph glyph, string hint, bool enabled)
    {
        var button = new GlassButton
        {
            Glyph = glyph,
            ActionEnabled = enabled,
            TabStop = enabled,
            AccessibleName = hint,
            AccessibleDescription = hint,
            Cursor = enabled ? Cursors.Hand : Cursors.Default
        };
        _tips.SetToolTip(button, hint);
        _actions.Add(button);
        Controls.Add(button);
        return button;
    }
    private void AddCaption(Glyph glyph, string name, Action action)
    {
        var button = new GlassButton { Glyph = glyph, CaptionButton = true, AccessibleName = name, TabStop = false };
        button.Click += (_, _) => action();
        _tips.SetToolTip(button, name);
        _caption.Add(button);
        Controls.Add(button);
    }
    private void BuildMenu()
    {
        _menu.AutoSize = false;
        _menu.Dock = DockStyle.None;
        _menu.Font = Font;
        _menu.Renderer = new GlassRenderer();
        _menu.ShowItemToolTips = true;
        ToolStripMenuItem Top(string name)
        {
            var item = new ToolStripMenuItem(name) { ForeColor = GlassTeal.Text };
            item.DropDown.BackColor = GlassTeal.Surface;
            item.DropDown.ForeColor = GlassTeal.Text;
            item.DropDown.ShowItemToolTips = true;
            _menu.Items.Add(item);
            return item;
        }
        var file = Top(UiText.Menus[0]);
        file.DropDownItems.Add(UiText.Exit, null, (_, _) => Close());
        var actions = Top(UiText.Menus[1]);
        foreach (var label in UiText.FutureActions)
            actions.DropDownItems.Add(DisabledItem(label));
        actions.DropDownItems.Add(UiText.Refresh, null, async (_, _) => await LoadCatalog(true));
        var view = Top(UiText.Menus[2]);
        view.DropDownItems.Add(UiText.Programs, null, async (_, _) => await SwitchTab(0));
        view.DropDownItems.Add(UiText.Store, null, async (_, _) => await SwitchTab(1));
        view.DropDownItems.Add(DisabledItem(UiText.Theme));
        view.DropDownItems.Add(DisabledItem(UiText.Language));
        var help = Top(UiText.Menus[3]);
        help.DropDownItems.Add(DisabledItem(UiText.Attribution));
        help.DropDownItems.Add(DisabledItem(UiText.License));
        MainMenuStrip = _menu;
        Controls.Add(_menu);
    }
    private static ToolStripMenuItem DisabledItem(string label) => new(label)
    {
        Enabled = false,
        ToolTipText = UiText.Disabled
    };
    private void BuildGrid()
    {
        _grid.ReadOnly = true;
        _grid.VirtualMode = true;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToOrderColumns = false;
        _grid.AllowUserToResizeColumns = false;
        _grid.MultiSelect = false;
        _grid.ScrollBars = ScrollBars.None;
        _grid.Scroll += (_, _) => SyncScroll();
        _scroll.ValueChanged += (_, _) =>
        {
            if (_grid.RowCount > 0) _grid.FirstDisplayedScrollingRowIndex = Math.Min(_scroll.Value, _grid.RowCount - 1);
        };
        Controls.Add(_scroll);
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BorderStyle = BorderStyle.None;
        _grid.BackgroundColor = GlassTeal.Background;
        _grid.GridColor = GlassTeal.Border;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GlassTeal.Background,
            ForeColor = GlassTeal.Text,
            SelectionBackColor = GlassTeal.Selection,
            SelectionForeColor = GlassTeal.Text,
            Font = Font,
            Padding = new Padding(S(8), 0, S(3), 0),
            WrapMode = DataGridViewTriState.False
        };
        _grid.AlternatingRowsDefaultCellStyle.BackColor = GlassTeal.Stripe;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GlassTeal.Surface,
            ForeColor = GlassTeal.Text,
            Font = Font,
            Padding = new Padding(S(8), 0, 0, 0),
            SelectionBackColor = GlassTeal.Surface
        };
        foreach (var header in UiText.Columns) _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            SortMode = DataGridViewColumnSortMode.Programmatic
        });
        _grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visible.Count) return;
            var row = _visible[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => row.Name,
                1 => UiText.Size(row.SizeBytes),
                2 => row.Installed?.ToString("dd.MM.yyyy") ?? UiText.Empty,
                3 => string.IsNullOrWhiteSpace(row.Version) ? UiText.Empty : row.Version,
                4 => "?",
                _ => UiText.Empty
            };
        };
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visible.Count) return;
            var row = _visible[e.RowIndex];
            e.ToolTipText = e.ColumnIndex is 4 or 5 ? UiText.Unknown : row.Name + "\n" + row.Publisher + "\n" + row.Version;
        };
        _grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (_sortColumn == e.ColumnIndex) _descending = !_descending;
            else { _sortColumn = e.ColumnIndex; _descending = false; }
            ApplyView(false);
        };
        _grid.CellPainting += PaintCell;
        var context = new ContextMenuStrip
        {
            Renderer = new GlassRenderer(),
            BackColor = GlassTeal.Surface,
            ForeColor = GlassTeal.Text,
            ShowItemToolTips = true
        };
        foreach (var label in UiText.ContextActions)
            context.Items.Add(DisabledItem(label));
        _grid.ContextMenuStrip = context;
        Controls.Add(_grid);
    }

    private void BuildSearch()
    {
        _searchBox.BackColor = GlassTeal.Background;
        _searchBox.Resize += (_, _) => _searchBox.Invalidate();
        _searchBox.Paint += (_, e) =>
        {
            using var pen = new Pen(GlassTeal.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, _searchBox.Width - 1, _searchBox.Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var icon = new Pen(GlassTeal.Muted, S(1.5f));
            e.Graphics.DrawEllipse(icon, S(9), S(7), S(11), S(11));
            e.Graphics.DrawLine(icon, S(19), S(18), S(24), S(23));
        };
        _search.BorderStyle = BorderStyle.None;
        _search.BackColor = GlassTeal.Background;
        _search.ForeColor = GlassTeal.Text;
        _search.Font = Font;
        _search.PlaceholderText = UiText.Search;
        _search.AccessibleName = UiText.Search;
        _search.TextChanged += (_, _) => ApplyView(false);
        _searchBox.Controls.Add(_search);
        Controls.Add(_searchBox);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); if (_grid != null) LayoutShell(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); LayoutShell(); }
    private void LayoutShell()
    {
        if (_actions.Count != 7 || _caption.Count != 3) return;
        SuspendLayout();
        MinimumSize = new Size(S(640), S(440));
        _menu.SetBounds(S(3), S(35), ClientSize.Width - S(6), S(30));
        _menu.Padding = new Padding(S(6), 0, 0, 0);
        for (var i = 0; i < 3; i++) _caption[i].SetBounds(ClientSize.Width - S(138 - i * 46), S(1), S(44), S(32));
        for (var i = 0; i < 3; i++) _actions[i].SetBounds(S(11 + i * 44), S(71), S(34), S(30));
        for (var i = 3; i < 7; i++) _actions[i].SetBounds(ClientSize.Width - S(167 - (i - 3) * 41), S(71), S(33), S(30));
        _grid.SetBounds(S(2), S(108), ClientSize.Width - S(17), Math.Max(S(80), ClientSize.Height - S(175)));
        _scroll.SetBounds(ClientSize.Width - S(14), _grid.Top + S(28), S(12), _grid.Height - S(28));
        _grid.ColumnHeadersHeight = S(28);
        _grid.RowTemplate.Height = S(29);
        foreach (DataGridViewRow row in _grid.Rows) row.Height = S(29);
        _grid.DefaultCellStyle.Padding = new Padding(S(9), 0, S(3), 0);
        var available = _grid.ClientSize.Width - 2;
        int[] widths = [0, S(91), S(126), S(100), S(73), S(80)];
        widths[0] = Math.Max(S(120), available - widths.Sum());
        for (var i = 0; i < 6; i++) _grid.Columns[i].Width = widths[i];
        _searchBox.SetBounds(S(11), ClientSize.Height - S(60), ClientSize.Width - S(22), S(29));
        _search.SetBounds(S(32), Math.Max(2, (_searchBox.Height - _search.PreferredHeight) / 2), _searchBox.Width - S(40), _search.PreferredHeight);
        ResumeLayout();
        SyncScroll();
        Invalidate();
    }

    private void SyncScroll()
    {
        _scroll.SetRange(_grid.RowCount, Math.Max(1, _grid.DisplayedRowCount(false)),
            Math.Max(0, _grid.FirstDisplayedScrollingRowIndex));
    }

    private async Task SwitchTab(int tab)
    {
        if (_tab == tab) return;
        _tab = tab;
        _refresh.ActionEnabled = _pending[tab] == null;
        _refresh.Invalidate();
        _programs.Selected = tab == 0;
        _store.Selected = tab == 1;
        _programs.Invalidate(); _store.Invalidate();
        ApplyView();
        await LoadCatalog(false);
    }
    private async Task LoadCatalog(bool force)
    {
        var tab = _tab;
        if (!force && _catalogs[tab] != null) { _status = StatusFor(_catalogs[tab]!); ApplyView(); return; }
        if (_pending[tab] != null) { _status = UiText.Loading; Invalidate(); return; }
        var timer = Stopwatch.StartNew();
        _status = UiText.Loading;
        _refresh.ActionEnabled = false; _refresh.Invalidate();
        Invalidate();
        var task = Task.Run(() => tab == 0 ? FastCatalog.ReadRegistry() : FastCatalog.ReadStore());
        _pending[tab] = task;
        try
        {
            var result = await task;
            if (_lifetime.IsCancellationRequested) return;
            _catalogs[tab] = result;
            if (_tab == tab)
            {
                _status = StatusFor(result);
                ApplyView();
                _grid.Update();
            }
            Measurements.Record((tab == 0 ? "programs" : "store") + (force ? "-refresh" : "-filled"),
                result.Entries.Count, timer.Elapsed.TotalMilliseconds, result.ReadErrors, DeviceDpi);
            _rowsPainted = true;
            _grid.Invalidate(); // Start visible icon requests only after the first table paint.
        }
        catch (Exception ex)
        {
            if (_lifetime.IsCancellationRequested) return;
            if (_tab == tab) _status = _catalogs[tab] == null ? UiText.ReadFailed : UiText.RefreshFailed;
            _tips.SetToolTip(_grid, UiText.ReadError + ex.GetType().Name);
            Measurements.Record(tab == 0 ? "programs-error" : "store-error", errors: 1, operationMs: timer.Elapsed.TotalMilliseconds);
        }
        finally
        {
            _pending[tab] = null;
            if (!_lifetime.IsCancellationRequested)
            {
                _refresh.ActionEnabled = _pending[_tab] == null;
                _refresh.Invalidate(); Invalidate();
            }
        }
    }
    private string StatusFor(CatalogResult result) => result.ReadErrors > 0
        ? UiText.ReadErrors(result.ReadErrors) : UiText.Ready;

    private void ApplyView(bool preserveSelection = true)
    {
        var selected = preserveSelection && _grid.SelectedRows.Count > 0 && _grid.CurrentRow?.Index is int n && n >= 0 && n < _visible.Count
            ? _visible[n].Id : null;
        _visible = CatalogView.Select(_catalogs[_tab]?.Entries ?? [], _search.Text, _sortColumn, _descending);
        _grid.RowCount = _visible.Count;
        for (var i = 0; i < 6; i++) _grid.Columns[i].HeaderCell.SortGlyphDirection = i == _sortColumn
            ? _descending ? SortOrder.Descending : SortOrder.Ascending : SortOrder.None;
        var index = selected == null ? -1 : _visible.FindIndex(x => x.Id == selected);
        if (index >= 0) _grid.CurrentCell = _grid[0, index];
        else
        {
            _grid.CurrentCell = null;
            _grid.ClearSelection();
            if (_grid.RowCount > 0) _grid.FirstDisplayedScrollingRowIndex = 0;
        }
        _grid.Invalidate();
        SyncScroll();
        Invalidate();
    }

    private void PaintCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _visible.Count || e.ColumnIndex is not (0 or 4)) return;
        e.PaintBackground(e.ClipBounds, true);
        e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
        var g = e.Graphics!;
        var bounds = e.CellBounds;
        var graphicsState = g.Save();
        g.SetClip(Rectangle.Intersect(bounds, e.ClipBounds));
        if (e.ColumnIndex == 4)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(bounds.X + (bounds.Width - S(18)) / 2, bounds.Y + (bounds.Height - S(18)) / 2, S(18), S(18));
            using var brush = new LinearGradientBrush(r, GlassTeal.StatusTop, GlassTeal.StatusBottom, 90f);
            g.FillEllipse(brush, r);
            using var pen = new Pen(GlassTeal.Muted);
            g.DrawEllipse(pen, r);
            using var textBrush = new SolidBrush(GlassTeal.Text);
            using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("?", Font, textBrush, r, centered);
        }
        else
        {
            var row = _visible[e.RowIndex];
            var iconBounds = new Rectangle(bounds.X + S(12), bounds.Y + (bounds.Height - S(21)) / 2, S(21), S(21));
            if (_icons.TryGetValue(row.Id, out var bitmap) && bitmap != null) g.DrawImage(bitmap, iconBounds);
            else
            {
                using var pen = new Pen(GlassTeal.Muted);
                g.DrawRectangle(pen, iconBounds.X + S(2), iconBounds.Y + S(3), S(16), S(15));
                g.DrawLine(pen, iconBounds.X + S(2), iconBounds.Y + S(7), iconBounds.Right - S(3), iconBounds.Y + S(7));
                if (_rowsPainted && _iconRequests.Add(row.Id)) _ = LoadIcon(row);
            }
            var text = new Rectangle(bounds.X + S(44), bounds.Y, bounds.Width - S(49), bounds.Height);
            TextRenderer.DrawText(g, row.Name, Font, text, GlassTeal.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
        }
        g.Restore(graphicsState);
        e.Handled = true;
    }

    private async Task LoadIcon(AppEntry row)
    {
        try
        {
            await _iconSlots.WaitAsync(_lifetime.Token);
            Bitmap? bitmap;
            try { bitmap = await Task.Run(() => LazyIcons.Load(row.IconPath), _lifetime.Token); }
            finally { _iconSlots.Release(); }
            if (_lifetime.IsCancellationRequested) { bitmap?.Dispose(); return; }
            _icons[row.Id] = bitmap;
            var index = _visible.FindIndex(x => x.Id == row.Id);
            if (index >= 0) _grid.InvalidateCell(0, index);
        }
        catch (OperationCanceledException) { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        GlassTeal.Fill(e.Graphics, new Rectangle(0, 0, Width, S(35)), GlassTeal.Caption, GlassTeal.Background);
        GlassTeal.Fill(e.Graphics, new Rectangle(0, S(65), Width, S(43)), GlassTeal.Surface, GlassTeal.Background);
        GlassTeal.Fill(e.Graphics, new Rectangle(0, Height - S(27), Width, S(27)), GlassTeal.Surface, GlassTeal.Background);
        using var pen = new Pen(GlassTeal.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        e.Graphics.DrawLine(pen, 0, S(35), Width, S(35));
        e.Graphics.DrawLine(pen, 0, S(65), Width, S(65));
        e.Graphics.DrawLine(pen, 0, S(107), Width, S(107));
        e.Graphics.DrawLine(pen, 0, Height - S(27), Width, Height - S(27));
        e.Graphics.DrawIcon(_applicationIcon, new Rectangle(S(12), S(6), S(24), S(24)));
        using var titleFont = new Font(Font.FontFamily, 11f);
        TextRenderer.DrawText(e.Graphics, UiText.Title, titleFont, new Rectangle(S(43), 0, S(170), S(35)), GlassTeal.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, UiText.Design, Font, new Rectangle(S(219), 0, S(85), S(35)), GlassTeal.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var count = UiText.Count(_visible.Count, _catalogs[_tab]?.Entries.Count ?? 0);
        TextRenderer.DrawText(e.Graphics, count, Font, new Rectangle(S(13), Height - S(25), Width / 2, S(22)), GlassTeal.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, _status, Font, new Rectangle(Width / 2, Height - S(25), Width / 2 - S(19), S(22)), GlassTeal.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        for (var i = 0; i < 3; i++) e.Graphics.DrawLine(pen, Width - S(5 + i * 4), Height - S(3), Width - S(3), Height - S(5 + i * 4));
    }

    private void ToggleMaximize()
    {
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }
    protected override void WndProc(ref Message m)
    {
        const int hitTest = 0x84;
        base.WndProc(ref m);
        if (m.Msg != hitTest || !_shown) return;
        var point = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xffff), (short)((m.LParam.ToInt64() >> 16) & 0xffff)));
        if (WindowState != FormWindowState.Maximized)
        {
            var left = point.X < S(5); var right = point.X >= Width - S(5);
            var top = point.Y < S(5); var bottom = point.Y >= Height - S(5);
            var code = (left, right, top, bottom) switch
            {
                (true, _, true, _) => 13,
                (_, true, true, _) => 14,
                (true, _, _, true) => 16,
                (_, true, _, true) => 17,
                (true, _, _, _) => 10,
                (_, true, _, _) => 11,
                (_, _, true, _) => 12,
                (_, _, _, true) => 15,
                _ => 0
            };
            if (code != 0) { m.Result = (IntPtr)code; return; }
        }
        if (point.Y < S(35) && point.X < Width - S(140)) m.Result = (IntPtr)2;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            foreach (var icon in _icons.Values) icon?.Dispose();
            _applicationIcon.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
