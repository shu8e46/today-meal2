using System;
using System.Windows;
using System.Windows.Media;

namespace TodayMeal
{
    /// <summary>XAML 한 줄로 쓰기 번거로운 아이콘 도형. 24x24 좌표계.</summary>
    public static class Icons
    {
        static Geometry gear;

        /// <summary>톱니바퀴: 톱니 8개 + 가운데 구멍.</summary>
        public static Geometry Gear
        {
            get
            {
                if (gear == null) gear = BuildGear();
                return gear;
            }
        }

        static Geometry BuildGear()
        {
            const int teeth = 8;
            const double cx = 12, cy = 12;
            const double tip = 10.3;     // 톱니 끝 반지름
            const double root = 7.7;     // 톱니 뿌리 반지름
            const double hole = 3.2;
            double step = 2 * Math.PI / teeth;
            double halfTip = step * 0.17;
            double halfRoot = step * 0.29;

            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                for (int i = 0; i < teeth; i++)
                {
                    double a = i * step - Math.PI / 2;
                    Point p1 = Polar(cx, cy, root, a - halfRoot);
                    Point p2 = Polar(cx, cy, tip, a - halfTip);
                    Point p3 = Polar(cx, cy, tip, a + halfTip);
                    Point p4 = Polar(cx, cy, root, a + halfRoot);

                    if (i == 0) c.BeginFigure(p1, false, true);
                    else c.ArcTo(p1, new Size(root, root), 0, false, SweepDirection.Clockwise, true, true);
                    c.LineTo(p2, true, true);
                    c.ArcTo(p3, new Size(tip, tip), 0, false, SweepDirection.Clockwise, true, true);
                    c.LineTo(p4, true, true);
                }
                c.ArcTo(Polar(cx, cy, root, -Math.PI / 2 - halfRoot), new Size(root, root), 0, false, SweepDirection.Clockwise, true, true);

                c.BeginFigure(new Point(cx + hole, cy), false, true);
                c.ArcTo(new Point(cx - hole, cy), new Size(hole, hole), 0, false, SweepDirection.Clockwise, true, true);
                c.ArcTo(new Point(cx + hole, cy), new Size(hole, hole), 0, false, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            return g;
        }

        static Point Polar(double cx, double cy, double r, double angle)
        {
            return new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        }
    }
}
