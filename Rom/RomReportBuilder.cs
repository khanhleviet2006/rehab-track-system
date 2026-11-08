using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace AngleMonitorWPF.Rom
{
    public static class RomReportBuilder
    {
        public static string Encode(string text) { return WebUtility.HtmlEncode(text ?? ""); }
        public static string Build(int patientId, IEnumerable<RomMilestone> milestones)
        {
            var html = new StringBuilder("<section style='page-break-before:always;padding:12mm 15mm;box-sizing:border-box;width:210mm'>");
            html.Append("<h2>Mục tiêu ROM và xác nhận bác sĩ</h2><p>Mã nội bộ bệnh nhân: ").Append(patientId);
            html.Append(". Thời điểm xác nhận: UTC. Kết quả đo và quyết định xác nhận được ghi riêng.</p>");
            if (milestones.Any(m => m.IsDemoConfirmation))
                html.Append("<p style='background:#fff2cf;color:#694b10;padding:10px;font-weight:bold'>BÁO CÁO DEMO — Có xác nhận từ Bác sĩ demo; danh tính bác sĩ chưa được xác minh.</p>");
            html.Append("<table style='width:100%;border-collapse:collapse;font-size:11px' border='1' cellpadding='7'><thead><tr><th>Mốc / vận động / quy ước</th><th>Mục tiêu / đo (°)</th><th>Đánh giá / nguồn</th><th>Xác nhận</th><th>Ghi chú</th></tr></thead><tbody>");
            int count = 0;
            foreach (var m in milestones)
            {
                count++;
                html.Append("<tr style='break-inside:avoid'><td>#").Append(m.MilestoneId).Append(" • v").Append(m.Revision).Append("<br>")
                    .Append(Encode(m.MovementLabel)).Append("<br>").Append(Encode(m.ConventionLabel)).Append("</td><td>")
                    .Append(m.TargetAngle.ToString("0.##")).Append(" / ").Append(m.MeasuredAngle.HasValue ? m.MeasuredAngle.Value.ToString("0.##") : "—")
                    .Append("<br>±").Append(m.Tolerance).Append("°; ").Append(m.RequiredRepetitions).Append(" lần; giữ ").Append(m.RequiredHoldSeconds).Append("s</td><td>")
                    .Append(Encode(m.EvidenceLabel)).Append("<br>").Append(Encode(m.MeasurementSource)).Append("<br>")
                    .Append(m.MeasurementTime?.ToString("dd/MM/yyyy HH:mm")).Append(" • Buổi: ").Append(m.SessionId?.ToString() ?? "khám lâm sàng")
                    .Append("</td><td>").Append(Encode(m.StatusLabel)).Append("<br>").Append(Encode(m.DoctorName)).Append("<br>")
                    .Append(m.ConfirmedAt?.ToString("dd/MM/yyyy HH:mm:ss")).Append("</td><td>").Append(Encode(m.Notes)).Append("</td></tr>");
            }
            if (count == 0) html.Append("<tr><td colspan='5'>Chưa thiết lập mục tiêu ROM cho bệnh nhân này.</td></tr>");
            return html.Append("</tbody></table><p>ROM là một phần của đánh giá chức năng; không kết luận hồi phục hoàn toàn chỉ từ ROM.</p></section>").ToString();
        }
    }
}
