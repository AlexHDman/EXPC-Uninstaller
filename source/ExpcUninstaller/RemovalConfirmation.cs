namespace ExpcUninstaller;

internal sealed class RemovalConfirmation : Form
{
    public RemovalConfirmation(string name)
    {
        Text = UiText.Title; Font = new Font("Segoe UI", 10.5f); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(430, 135); FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        var question = new Label { Text = L.F("RemovalQuestion", name), Dock = DockStyle.Fill, Padding = new Padding(14), AutoEllipsis = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        var remove = new Button { Text = L.T("OperationDelete"), DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = L.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        bar.Controls.AddRange([remove, cancel]); Controls.Add(question); Controls.Add(bar);
        AcceptButton = cancel; CancelButton = cancel; ActiveControl = cancel;
        Shown += (_, _) => cancel.Focus(); Theme.Apply(this);
    }
    public bool Ask(Form owner)
    {
        var work = Screen.FromControl(owner).WorkingArea; Location = owner.Location;
        var size = new Size(Math.Min(work.Width, 450 * owner.DeviceDpi / 96), Math.Min(work.Height, 180 * owner.DeviceDpi / 96));
        Bounds = new Rectangle(Math.Clamp(owner.Left + (owner.Width - size.Width) / 2, work.Left, work.Right - size.Width),
            Math.Clamp(owner.Top + (owner.Height - size.Height) / 2, work.Top, work.Bottom - size.Height), size.Width, size.Height);
        return ShowDialog(owner) == DialogResult.OK;
    }
}
