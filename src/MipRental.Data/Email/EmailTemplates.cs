using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MipRental.Domain.Entities;

namespace MipRental.Data.Email;

/// <summary>
/// ADIM 15 — mail gövdesi. Basit HTML, gömülü CSS, ŞABLON MOTORU YOK.
///
/// Kuyruğa yazılan satır (konu + düz metin gövde) burada bir HTML kabuğuna
/// sarılır. İçerik NotificationQueue'da üretilir, biçim burada: bir bildirimin
/// metni değişince şablonu, şablon değişince metni bozmayalım diye ayrı.
///
/// GÜVENLİK:
/// - Gövdeye giren her şey HTML-KAÇIŞINDAN geçer. Metinlerde firma adı, iş
///   tanımı, red gerekçesi gibi KULLANICI GİRDİSİ var; kaçış olmadan mail
///   istemcisinde HTML olarak yorumlanırdı.
/// - Gövdeye TUTAR YAZILMAZ (fiyat gizliliği, ADR-016). Tek istisna hakediş
///   onay maili: alıcısı zaten fiyat görme yetkisi olan Bütçe Yöneticisi'dir.
/// - Hakediş onay mailinde HAM TOKEN vardır (magic link). O mail hiçbir yerde
///   loglanmaz; işaretini <see cref="ContainsSecret"/> taşır.
/// </summary>
public static partial class EmailTemplates
{
    private const string SystemName = "MIP Hizmet & Kiralama Yönetim Sistemi";

    /// <summary>Magic link taşıyan şablon: gövdesi loglanmaz.</summary>
    public const string ProgressPaymentApprovalTemplate = "PP_APPROVAL_LINK";

    /// <summary>Tutar taşımasına İZİN VERİLEN tek şablon (alıcısı fiyat yetkilisi).</summary>
    public static bool MayContainAmount(string templateCode) =>
        templateCode == ProgressPaymentApprovalTemplate;

    public static bool ContainsSecret(string templateCode) =>
        templateCode == ProgressPaymentApprovalTemplate;

    private static readonly IReadOnlyDictionary<string, string> Headings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WR_APPROVAL_PENDING"] = "Onayınızı bekleyen çalışma kaydı var",
        ["WR_APPROVAL_REMINDER"] = "Hatırlatma: onayınız bekleniyor",
        ["WR_APPROVAL_ESCALATION"] = "Eskalasyon: onay süresi aşıldı",
        ["WR_APPROVED"] = "Çalışma kaydı onaylandı",
        ["WR_REJECTED"] = "Çalışma kaydı reddedildi",
        ["WR_REVISION_REQUESTED"] = "Çalışma kaydı için revizyon istendi",
        ["WR_LINE_OBJECTED"] = "Çalışma kaydının bir satırına itiraz edildi",
        ["WR_DERIVED_PENDING_SUBMIT"] = "Çalışma kaydı oluştu, gönderim bekliyor",
        ["REQ_SUBMITTED"] = "Yeni talep gönderildi",
        ["REQ_EQUIPMENT_APPROVED"] = "Talep Ekipman Müdürlüğü tarafından onaylandı",
        ["REQ_EQUIPMENT_REJECTED"] = "Talep Ekipman Müdürlüğü tarafından reddedildi",
        ["REQ_EQUIPMENT_EDITED"] = "Talepte düzenleme yapıldı",
        ["REQ_FIRM_ACCEPTED"] = "Talep firma tarafından kabul edildi",
        ["REQ_FIRM_REJECTED"] = "Talep firma tarafından reddedildi",
        ["REQ_CANCELLED"] = "Talep iptal edildi",
        ["REQ_ASSIGNMENT_CHANGED"] = "Talepte operatör/plaka değişti",
        ["REQ_DERIVE_FAILED"] = "Talepten çalışma kaydı oluşturulamadı",
        [ProgressPaymentApprovalTemplate] = "Hakediş onayınızı bekliyor"
    };

    public static string Heading(string templateCode) =>
        Headings.TryGetValue(templateCode, out var heading) ? heading : "MIP Hizmet Kiralama bildirimi";

    public static string Render(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return Render(notification.TemplateCode, notification.Subject, notification.Body);
    }

    public static string Render(string templateCode, string? subject, string? body)
    {
        var heading = WebUtility.HtmlEncode(Heading(templateCode));
        var subjectLine = string.IsNullOrWhiteSpace(subject) ? string.Empty : WebUtility.HtmlEncode(subject);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"tr\"><head><meta charset=\"utf-8\" /></head>");
        sb.Append("<body style=\"margin:0;padding:24px;background:#f4f5f7;font-family:Segoe UI,Arial,sans-serif;color:#212529;\">");
        sb.Append("<div style=\"max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #dee2e6;border-radius:6px;overflow:hidden;\">");
        sb.Append("<div style=\"padding:16px 24px;border-bottom:1px solid #dee2e6;\">");
        sb.Append("<div style=\"font-weight:700;font-size:14px;\">MERSİN ULUSLARARASI LİMAN İŞLETMECİLİĞİ</div>");
        sb.Append($"<div style=\"font-size:12px;color:#6c757d;\">{WebUtility.HtmlEncode(SystemName)}</div>");
        sb.Append("</div>");
        sb.Append("<div style=\"padding:24px;\">");
        sb.Append($"<h1 style=\"margin:0 0 12px;font-size:18px;\">{heading}</h1>");

        if (subjectLine.Length > 0)
        {
            sb.Append($"<p style=\"margin:0 0 16px;font-weight:600;\">{subjectLine}</p>");
        }

        sb.Append($"<div style=\"font-size:14px;line-height:1.6;\">{FormatBody(body)}</div>");
        sb.Append("</div>");
        sb.Append("<div style=\"padding:16px 24px;border-top:1px solid #dee2e6;font-size:12px;color:#6c757d;\">");
        sb.Append("Bu e-posta otomatik gönderilmiştir; bu adres yanıtlanmaz. ");
        sb.Append($"Gönderen: {WebUtility.HtmlEncode(SystemName)}.");
        sb.Append("</div></div></body></html>");

        return sb.ToString();
    }

    /// <summary>
    /// Düz metin gövdeyi güvenli HTML'e çevirir.
    ///
    /// SIRA ÖNEMLİ: önce TAMAMI kaçışlanır, sonra bağlantı aranır. Tersi olsaydı
    /// gövdeye yazılmış bir metin &lt;a&gt; etiketinin içine kaçışsız girerdi.
    /// </summary>
    private static string FormatBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var encoded = WebUtility.HtmlEncode(body);

        // Gövdedeki adres tıklanabilir olur (magic link bunun için var). Kalıp
        // dar tutuldu: yalnızca http/https ve boşluğa kadar.
        encoded = LinkPattern().Replace(encoded, match =>
            $"<a href=\"{match.Value}\" style=\"color:#0d6efd;\">{match.Value}</a>");

        return encoded.Replace("\r\n", "\n").Replace("\n", "<br />");
    }

    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
