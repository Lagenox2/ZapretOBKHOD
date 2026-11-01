using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZapretOBKHOD
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(0xF3, 0xF6, 0xFB);
        public static readonly Color Card = Color.White;
        public static readonly Color Text = Color.FromArgb(0x10, 0x18, 0x28);
        public static readonly Color Muted = Color.FromArgb(0x66, 0x70, 0x85);
        public static readonly Color Soft = Color.FromArgb(0x98, 0xA2, 0xB3);
        public static readonly Color Border = Color.FromArgb(0xE4, 0xE7, 0xEC);
        public static readonly Color Accent = Color.FromArgb(0x0B, 0x6C, 0xFB);
        public static readonly Color AccentHover = Color.FromArgb(0x09, 0x58, 0xD9);
        public static readonly Color AccentPressed = Color.FromArgb(0x07, 0x46, 0xAD);
        public static readonly Color Green = Color.FromArgb(0x12, 0xB7, 0x6A);
        public static readonly Color Red = Color.FromArgb(0xE5, 0x48, 0x4D);
        public static readonly Color Amber = Color.FromArgb(0xF7, 0x90, 0x09);
        public static readonly Color Dark = Color.FromArgb(0x0F, 0x17, 0x2A);

        public static Font Font(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal class RoundedPanel : Panel
    {
        public int Radius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }

        public RoundedPanel()
        {
            Radius = 14;
            FillColor = Theme.Card;
            BorderColor = Theme.Border;
            BackColor = Theme.Card;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Radius > 0 && Width > 0 && Height > 0)
            {
                using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Radius))
                {
                    Region = new Region(path);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundedRect(rect, Radius))
            {
                using (var brush = new SolidBrush(FillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (var pen = new Pen(BorderColor))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
            base.OnPaint(e);
        }
    }

    internal class RoundedButton : Button
    {
        public int Radius { get; set; }
        public Color FillColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color BorderColor { get; set; }
        public Color TextColor { get; set; }

        private bool _hover;
        private bool _pressed;

        public RoundedButton()
        {
            Radius = 10;
            FillColor = Theme.Accent;
            HoverColor = Theme.AccentHover;
            PressedColor = Theme.AccentPressed;
            BorderColor = Color.Transparent;
            TextColor = Color.White;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = Theme.Font(10f, FontStyle.Bold);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill;
            if (!Enabled) fill = Color.FromArgb(0xD0, 0xD5, 0xDD);
            else if (_pressed) fill = PressedColor;
            else if (_hover) fill = HoverColor;
            else fill = FillColor;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundedRect(rect, Radius))
            {
                using (var brush = new SolidBrush(fill))
                {
                    pevent.Graphics.FillPath(brush, path);
                }
                if (BorderColor.A > 0)
                {
                    using (var pen = new Pen(BorderColor))
                    {
                        pevent.Graphics.DrawPath(pen, path);
                    }
                }
            }

            Color color = Enabled ? TextColor : Color.FromArgb(0x66, 0x70, 0x85);
            TextRenderer.DrawText(pevent.Graphics, Text, Font, ClientRectangle, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    internal class StatusDot : Control
    {
        private Color _dotColor = Theme.Soft;

        public Color DotColor
        {
            get { return _dotColor; }
            set { _dotColor = value; Invalidate(); }
        }

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(20, 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Math.Min(Width, Height);
            var glow = new Rectangle(0, 0, d - 1, d - 1);
            using (var brush = new SolidBrush(Color.FromArgb(45, _dotColor)))
            {
                e.Graphics.FillEllipse(brush, glow);
            }
            var dot = new Rectangle(4, 4, d - 8, d - 8);
            using (var brush = new SolidBrush(_dotColor))
            {
                e.Graphics.FillEllipse(brush, dot);
            }
        }
    }

    internal class CheckChip : Control
    {
        private string _title = "";
        private string _detail = "не проверено";
        private Color _dotColor = Theme.Soft;
        private Color _fill = Color.FromArgb(0xF9, 0xFA, 0xFB);
        private Color _border = Theme.Border;

        public CheckChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Size = new Size(230, 54);
        }

        public void SetState(string title, string detail, Color dotColor)
        {
            _title = title;
            _detail = detail;
            _dotColor = dotColor;
            Invalidate();
        }

        public void SetTesting()
        {
            _detail = "проверка…";
            _dotColor = Theme.Amber;
            Invalidate();
        }

        public void SetResult(CheckResult result)
        {
            _detail = result.Detail;
            if (!result.Ok) _dotColor = Theme.Red;
            else if (result.Warn) _dotColor = Theme.Amber;
            else _dotColor = Theme.Green;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundedRect(rect, 11))
            {
                using (var brush = new SolidBrush(_fill))
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (var pen = new Pen(_border))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            using (var brush = new SolidBrush(_dotColor))
            {
                e.Graphics.FillEllipse(brush, 14, 20, 13, 13);
            }

            var titleRect = new Rectangle(38, 7, Width - 48, 22);
            var detailRect = new Rectangle(38, 27, Width - 48, 20);

            TextRenderer.DrawText(e.Graphics, _title, Theme.Font(10f, FontStyle.Bold), titleRect,
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(e.Graphics, _detail, Theme.Font(8.25f, FontStyle.Regular), detailRect,
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);
        }
    }

    internal class LinkLabelFlat : LinkLabel
    {
        public LinkLabelFlat()
        {
            LinkColor = Theme.Accent;
            ActiveLinkColor = Theme.AccentHover;
            VisitedLinkColor = Theme.Accent;
            Font = Theme.Font(9f, FontStyle.Regular);
            AutoSize = true;
            LinkBehavior = LinkBehavior.HoverUnderline;
        }
    }
}
