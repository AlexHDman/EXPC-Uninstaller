namespace ExpcUninstaller;

internal sealed partial class MainWindow
{
    private readonly SecretVault _vault;
    private NotifyIcon? _tray;
    private GlassButton _lock = null!;
    private void InitializeVault()
    {
        _vault.StateChanged += VaultStateChanged;
        _tray = new NotifyIcon { Icon = _applicationIcon, Text = UiText.Title, Visible = true };
        _tray.DoubleClick += (_, _) => RestoreWindow();
        VaultStateChanged();
    }
    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void ExitApplication() => Close();
    internal void ActivateExisting() { if (!_lifetime.IsCancellationRequested) RestoreWindow(); }
    private void RefreshTray()
    {
        if (_tray == null) return;
        var old = _tray.ContextMenuStrip;
        var menu = new ContextMenuStrip { Renderer = new GlassRenderer() };
        menu.Items.Add(UiText.Title, null, (_, _) => RestoreWindow());
        menu.Items.Add(L.T(_vault.Unlocked ? "LockVault" : "UnlockVault"), null, (_, _) => { RestoreWindow(); ToggleVault(); });
        menu.Items.Add(UiText.Exit, null, (_, _) => ExitApplication()); _tray.ContextMenuStrip = menu; old?.Dispose();
    }
    private void VaultStateChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(VaultStateChanged); return; }
        _lock.Selected = _vault.Unlocked; _lock.Glyph = _vault.Unlocked ? Glyph.Unlocked : Glyph.Lock;
        _tips.SetToolTip(_lock, L.T(_vault.Unlocked ? "LockVault" : "UnlockVault")); _lock.Invalidate(); RefreshTray();
    }
    private void ToggleVault()
    {
        if (_vault.Unlocked) { _vault.Lock(); return; }
        using var dialog = new VaultPasswordWindow(_vault, false); dialog.ShowDialog(this);
    }
    private void ManageAccounts()
    {
        if (!_vault.Unlocked) ToggleVault();
        if (!_vault.Unlocked) return;
        using var dialog = new AccountWindow(_vault); dialog.ShowDialog(this);
    }
    private void ChangeMasterPassword()
    {
        if (!_vault.Unlocked) ToggleVault();
        if (!_vault.Unlocked) return;
        using var dialog = new VaultPasswordWindow(_vault, true); dialog.ShowDialog(this);
    }
    private void AddVaultMenus(ToolStripItemCollection items)
    {
        items.Add(new ToolStripSeparator());
        items.Add(L.T("UnlockVault"), MenuGlyph(Glyph.Unlocked), (_, _) => { if (!_vault.Unlocked) ToggleVault(); });
        items.Add(L.T("LockVault"), MenuGlyph(Glyph.Lock), (_, _) => _vault.Lock());
        items.Add(L.T("Accounts"), MenuGlyph(Glyph.Lock), (_, _) => ManageAccounts());
        items.Add(L.T("ChangeMasterPassword"), MenuGlyph(Glyph.Lock), (_, _) => ChangeMasterPassword());
    }
    private static Bitmap MenuGlyph(Glyph glyph)
    {
        using var button = new GlassButton { Glyph = glyph, IconRatio = .94f, Size = new Size(48, 48), BackColor = GlassTeal.Background };
        var bitmap = new Bitmap(48, 48); button.DrawToBitmap(bitmap, new Rectangle(0, 0, 48, 48)); bitmap.MakeTransparent(GlassTeal.Background); return bitmap;
    }
}

internal sealed class VaultPasswordWindow : Form
{
    public VaultPasswordWindow(SecretVault vault, bool change)
    {
        var create = !vault.Exists;
        Text = L.T(change ? "ChangeMasterPassword" : create ? "CreateVault" : "UnlockVault");
        ClientSize = new Size(460, change || create ? 290 : 200); MinimumSize = new Size(420, 240); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, AutoScroll = true };
        var fields = new List<TextBox>();
        TextBox Password(string caption)
        {
            panel.Controls.Add(new Label { Text = L.T(caption), AutoSize = true });
            var field = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Top, MaxLength = 1024 }; panel.Controls.Add(field); fields.Add(field); return field;
        }
        var old = change ? Password("OldPassword") : null;
        var password = Password(change || create ? "NewPassword" : "MasterPassword");
        var confirm = change || create ? Password("RepeatPassword") : null;
        var note = new Label { Text = L.T(create || change ? "PasswordLength" : "VaultSession"), AutoSize = true, MaximumSize = new Size(425, 0) }; panel.Controls.Add(note);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var cancel = new ThemedButton { Text = L.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var apply = new ThemedButton { Text = L.T("Confirm"), AutoSize = true }; buttons.Controls.AddRange([cancel, apply]);
        Controls.Add(panel); Controls.Add(buttons); AcceptButton = apply; CancelButton = cancel;
        apply.Click += async (_, _) =>
        {
            apply.Enabled = cancel.Enabled = false;
            try
            {
                if (confirm != null && confirm.Text != password.Text) throw new ArgumentException(L.T("PasswordMismatch"));
                // KDF work does not freeze the UI. Capture only for the duration of the operation.
                var text = password.Text; var previous = old?.Text ?? "";
                await Task.Run(() => { if (change) vault.ChangePassword(previous, text); else if (create) vault.Create(text); else vault.Unlock(text); });
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { note.Text = ex is System.Security.Cryptography.CryptographicException ? L.T("VaultInvalid") : ex.Message; }
            finally { foreach (var field in fields) field.Clear(); apply.Enabled = cancel.Enabled = true; }
        };
        FormClosing += (_, e) => { if (!apply.Enabled) e.Cancel = true; };
        FormClosed += (_, _) => { foreach (var field in fields) field.Clear(); };
        Theme.Apply(this);
    }
}

internal sealed class AccountWindow : Form
{
    public AccountWindow(SecretVault vault)
    {
        Text = L.T("Accounts"); ClientSize = new Size(550, 410); MinimumSize = new Size(480, 350); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterParent;
        var list = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Label" };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        TextBox Field(string label, bool secret = false)
        {
            var row = panel.RowCount++; panel.Controls.Add(new Label { Text = L.T(label), AutoSize = true }, 0, row);
            var box = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = secret, MaxLength = secret ? 4096 : 500 }; panel.Controls.Add(box, 1, row); return box;
        }
        var label = Field("AccountLabel"); var host = Field("AccountHost"); var user = Field("AccountUser"); var password = Field("AccountPassword", true);
        var note = new Label { Text = L.T("AccountHelp"), Dock = DockStyle.Fill, AutoSize = true }; panel.Controls.Add(note, 0, panel.RowCount++); panel.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var close = new ThemedButton { Text = UiText.Close, AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new ThemedButton { Text = L.T("Save"), AutoSize = true }; var add = new ThemedButton { Text = L.T("NewAccount"), AutoSize = true }; var remove = new ThemedButton { Text = L.T("RemoveAccount"), AutoSize = true };
        buttons.Controls.AddRange([close, save, add, remove]); Controls.Add(panel); Controls.Add(buttons); Controls.Add(list); CancelButton = close;
        void Reload() { list.Items.Clear(); foreach (var account in vault.Accounts) list.Items.Add(account); }
        list.SelectedIndexChanged += (_, _) => { if (list.SelectedItem is VaultAccount account) { label.Text = account.Label; host.Text = account.Host; user.Text = account.Username; password.Clear(); } };
        add.Click += (_, _) => { list.SelectedIndex = -1; label.Clear(); host.Text = "login.1c.ru"; user.Clear(); password.Clear(); label.Focus(); };
        save.Click += (_, _) =>
        {
            try
            {
                var previous = list.SelectedItem as VaultAccount;
                var account = new VaultAccount(previous?.Id ?? Guid.NewGuid().ToString("N"), label.Text.Trim(), host.Text.Trim().ToLowerInvariant(), user.Text, password.Text.Length == 0 && previous != null ? previous.Password : password.Text);
                vault.SaveAccounts(vault.Accounts.Where(a => a.Id != account.Id).Append(account)); password.Clear(); Reload();
            }
            catch (Exception ex) { note.Text = ex.Message; }
        };
        remove.Click += (_, _) => { if (list.SelectedItem is not VaultAccount account) return; try { vault.SaveAccounts(vault.Accounts.Where(a => a.Id != account.Id)); Reload(); password.Clear(); } catch (Exception ex) { note.Text = ex.Message; } };
        void Locked() { if (!vault.Unlocked && !IsDisposed) { if (InvokeRequired) BeginInvoke(Close); else Close(); } }
        vault.StateChanged += Locked; FormClosed += (_, _) => { password.Clear(); user.Clear(); list.Items.Clear(); vault.StateChanged -= Locked; };
        Reload(); Theme.Apply(this);
    }
}
