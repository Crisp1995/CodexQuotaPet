using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

#pragma warning disable 0618

namespace CodexQuotaPet
{
    internal sealed class QuotaRing : FrameworkElement
    {
        private int _remaining;
        private string _label = "额度";
        private string _caption = "等待读取";
        private bool _isAvailable = true;

        public int Remaining
        {
            get { return _remaining; }
            set { _remaining = Math.Max(0, Math.Min(100, value)); InvalidateVisual(); }
        }

        public string Label
        {
            get { return _label; }
            set { _label = value ?? "额度"; InvalidateVisual(); }
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value ?? string.Empty; InvalidateVisual(); }
        }

        public bool IsAvailable
        {
            get { return _isAvailable; }
            set { _isAvailable = value; InvalidateVisual(); }
        }

        public int WarningThreshold { get; set; }
        public int CriticalThreshold { get; set; }
        public bool CompactScaling { get; set; }
        public bool HideLabel { get; set; }

        public QuotaRing()
        {
            Width = 164;
            Height = 164;
            WarningThreshold = 30;
            CriticalThreshold = 15;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double size = Math.Min(ActualWidth, ActualHeight);
            Point center = new Point(ActualWidth / 2, ActualHeight / 2);
            double scale = CompactScaling ? Math.Max(0.38, Math.Min(1.0, size / 164.0)) : 1.0;
            double stroke = Math.Max(4, 10 * scale);
            double radius = Math.Max(10, size / 2 - Math.Max(6, 15 * scale));
            Pen track = new Pen(new SolidColorBrush(Color.FromRgb(42, 55, 83)), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawEllipse(null, track, center, radius, radius);

            Brush accent = IsAvailable ? GetAccentBrush(Remaining, WarningThreshold, CriticalThreshold) : new SolidColorBrush(Color.FromRgb(74, 88, 115));
            if (IsAvailable)
            {
                double angle = Math.Max(0.001, Remaining / 100.0 * 359.999);
                StreamGeometry arc = new StreamGeometry();
                using (StreamGeometryContext context = arc.Open())
                {
                    Point start = PointOnCircle(center, radius, -90);
                    Point end = PointOnCircle(center, radius, -90 + angle);
                    context.BeginFigure(start, false, false);
                    context.ArcTo(end, new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise, true, false);
                }
                arc.Freeze();
                dc.DrawGeometry(null, new Pen(accent, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
            }

            if (HideLabel)
            {
                // The floating ball is intentionally tiny: percentage and reset time are
                // the only two pieces of information that remain legible at tray-icon size.
                double percentSize = Math.Max(19, Math.Min(30, 34 * scale));
                double captionSize = Math.Max(10.5, Math.Min(14, 18 * scale));
                DrawCentered(dc, IsAvailable ? Remaining.ToString(CultureInfo.InvariantCulture) + "%" : "—",
                    percentSize, Brushes.White, center.Y - percentSize * 0.98, FontWeights.SemiBold);
                DrawCentered(dc, Caption, captionSize, new SolidColorBrush(Color.FromRgb(190, 211, 250)),
                    center.Y + 6, FontWeights.SemiBold);
            }
            else
            {
                DrawCentered(dc, Label, Math.Max(7, 12 * scale), new SolidColorBrush(Color.FromRgb(158, 172, 202)), center.Y - 42 * scale);
                DrawCentered(dc, IsAvailable ? Remaining.ToString(CultureInfo.InvariantCulture) + "%" : "—", Math.Max(16, 34 * scale), Brushes.White, center.Y - 17 * scale, FontWeights.SemiBold);
                DrawCentered(dc, Caption, Math.Max(6, 11 * scale), new SolidColorBrush(Color.FromRgb(139, 161, 204)), center.Y + 27 * scale);
            }
        }

        internal static Brush GetAccentBrush(int remaining, int warning, int critical)
        {
            if (remaining <= critical) return new SolidColorBrush(Color.FromRgb(255, 82, 112));
            if (remaining <= warning) return new SolidColorBrush(Color.FromRgb(255, 177, 73));
            return new LinearGradientBrush(Color.FromRgb(62, 139, 255), Color.FromRgb(116, 82, 255), 45);
        }

        private static Point PointOnCircle(Point center, double radius, double degrees)
        {
            double radians = degrees * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        private void DrawCentered(DrawingContext dc, string text, double size, Brush brush, double y, FontWeight? weight = null)
        {
            FormattedText formatted = new FormattedText(text ?? string.Empty, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                weight ?? FontWeights.Normal, FontStretches.Normal), size, brush);
            dc.DrawText(formatted, new Point((ActualWidth - formatted.Width) / 2, y));
        }
    }

    internal sealed class UsageChart : FrameworkElement
    {
        private IList<DailyUsage> _items = new List<DailyUsage>();

        public IList<DailyUsage> Items
        {
            get { return _items; }
            set { _items = value ?? new List<DailyUsage>(); InvalidateVisual(); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth <= 20 || ActualHeight <= 30) return;
            IList<DailyUsage> items = UsageSeries.LastCalendarDays(Items, DateTime.Today, 7);
            if (items.Count == 0) return;
            long max = Math.Max(1, items.Max(item => item.Tokens));
            double chartHeight = ActualHeight - 28;
            double slot = ActualWidth / items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                double barHeight = items[i].Tokens / (double)max * (chartHeight - 18);
                double x = i * slot + slot * 0.24;
                double y = chartHeight - barHeight;
                Rect rect = new Rect(x, y, slot * 0.52, Math.Max(2, barHeight));
                Brush brush = i == items.Count - 1
                    ? new LinearGradientBrush(Color.FromRgb(70, 129, 255), Color.FromRgb(121, 79, 255), 90)
                    : new LinearGradientBrush(Color.FromRgb(42, 94, 190), Color.FromRgb(50, 111, 224), 90);
                dc.DrawRoundedRectangle(brush, null, rect, 3, 3);
                DrawCentered(dc, FormatCompact(items[i].Tokens), 10, new SolidColorBrush(Color.FromRgb(167, 183, 216)), x + rect.Width / 2, Math.Max(0, y - 16));
                DrawCentered(dc, items[i].Date.ToString("M/d", CultureInfo.CurrentCulture), 10,
                    new SolidColorBrush(Color.FromRgb(111, 130, 167)), x + rect.Width / 2, chartHeight + 5);
            }
        }

        private static void DrawCentered(DrawingContext dc, string text, double size, Brush brush, double centerX, double y)
        {
            FormattedText formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), size, brush);
            dc.DrawText(formatted, new Point(centerX - formatted.Width / 2, y));
        }

        internal static string FormatCompact(long value)
        {
            if (value >= 1000000) return (value / 1000000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (value >= 1000) return (value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
#pragma warning restore 0618
