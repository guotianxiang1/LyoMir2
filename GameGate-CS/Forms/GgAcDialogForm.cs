namespace GameGate.Forms;

internal sealed class GgAcDialogForm : Form
{
    private readonly Panel _titleBar;
    private Point _dragOrigin;

    public GgAcDialogForm(string title, Size contentSize)
    {
        Text = title;
        ClientSize = new Size(contentSize.Width, contentSize.Height + 34);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.None;
        ShowIcon = false;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(94, 196, 226);
        Padding = new Padding(3);
        Font = new Font("宋体", 9f, FontStyle.Regular);

        _titleBar = new Panel
        {
            Location = new Point(3, 3),
            Size = new Size(contentSize.Width - 6, 31),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(94, 196, 226)
        };
        var caption = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("宋体", 12f, FontStyle.Regular),
            ForeColor = Color.Black
        };
        var close = new Button
        {
            Text = "X",
            Dock = DockStyle.Right,
            Width = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(194, 76, 68),
            ForeColor = Color.Black,
            Font = new Font("Arial", 9f, FontStyle.Bold),
            TabStop = false
        };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Close();
        caption.MouseDown += BeginDrag;
        caption.MouseMove += ContinueDrag;
        _titleBar.MouseDown += BeginDrag;
        _titleBar.MouseMove += ContinueDrag;
        _titleBar.Controls.Add(caption);
        _titleBar.Controls.Add(close);
        Controls.Add(_titleBar);

        Content = new Panel
        {
            Location = new Point(3, 34),
            Size = new Size(contentSize.Width - 6, contentSize.Height - 3),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(245, 245, 245),
            Padding = new Padding(3)
        };
        Controls.Add(Content);
        _titleBar.BringToFront();
    }

    public Panel Content { get; }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) _dragOrigin = e.Location;
    }

    private void ContinueDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var screen = ((Control)sender!).PointToScreen(e.Location);
        Location = new Point(screen.X - _dragOrigin.X, screen.Y - _dragOrigin.Y);
    }
}
