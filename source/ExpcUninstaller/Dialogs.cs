namespace ExpcUninstaller;

// App-owned button labels follow the selected UI language rather than the Windows language.
internal static class Dialogs
{
    public static DialogResult Show(IWin32Window owner, string text, string caption = "EXPC Uninstaller",
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        using var dialog = new Form { Text = caption, ClientSize = new Size(540, 235), MinimumSize = new Size(420, 220),
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi, Font = new Font("Segoe UI", 9) };
        var message = new TextBox { Text = text.ReplaceLineEndings(Environment.NewLine), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, TabStop = false };
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) }; panel.Controls.Add(message);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        Button Add(string label, DialogResult result)
        {
            var button = new Button { Text = L.T(label), DialogResult = result, AutoSize = true, MinimumSize = new Size(85, 28) };
            bar.Controls.Add(button); return button;
        }
        if (buttons == MessageBoxButtons.YesNo)
        {
            var no = Add("No", DialogResult.No); var yes = Add("Yes", DialogResult.Yes);
            dialog.CancelButton = no; dialog.AcceptButton = defaultButton == MessageBoxDefaultButton.Button2 ? no : yes;
            dialog.Shown += (_, _) => ((Control)dialog.AcceptButton).Focus();
        }
        else { var close = Add("Close", DialogResult.OK); dialog.AcceptButton = dialog.CancelButton = close; }
        dialog.Controls.Add(panel); dialog.Controls.Add(bar); Theme.Apply(dialog);
        return dialog.ShowDialog(owner);
    }
}
