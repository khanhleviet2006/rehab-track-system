using System;
using System.Collections.Generic;
using System.Linq;

namespace AngleMonitorWPF
{
    public static class DataOptimizer
    {
        private static double PerpendicularDistance(SessionDataPoint point, SessionDataPoint lineStart, SessionDataPoint lineEnd)
        {
            double area = Math.Abs(0.5 * (lineStart.Time * lineEnd.Angle + lineEnd.Time * point.Angle + point.Time * lineStart.Angle - lineEnd.Time * lineStart.Angle - point.Time * lineEnd.Angle - lineStart.Time * point.Angle));
            double bottom = Math.Sqrt(Math.Pow(lineStart.Time - lineEnd.Time, 2) + Math.Pow(lineStart.Angle - lineEnd.Angle, 2));
            return (area / bottom) * 2.0;
        }
        public static List<SessionDataPoint> RDP(List<SessionDataPoint> points, double epsilon)
        {
            if (points == null || points.Count < 3)
                return points;

            int dmaxIndex = 0;
            double dmax = 0;

            for (int i = 1; i < points.Count - 1; i++)
            {
                double d = PerpendicularDistance(points[i], points[0], points[points.Count - 1]);
                if (d > dmax)
                {
                    dmaxIndex = i;
                    dmax = d;
                }
            }

            List<SessionDataPoint> resultList = new List<SessionDataPoint>();

            if (dmax > epsilon)
            {
                var recResults1 = RDP(points.Take(dmaxIndex + 1).ToList(), epsilon);
                var recResults2 = RDP(points.Skip(dmaxIndex).ToList(), epsilon);

                resultList.AddRange(recResults1.Take(recResults1.Count - 1));
                resultList.AddRange(recResults2);
            }
            else
            {
                resultList.Add(points[0]);
                resultList.Add(points[points.Count - 1]);
            }

            return resultList;
        }
    }
}