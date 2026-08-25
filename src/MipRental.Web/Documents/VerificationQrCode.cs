using QRCoder;

namespace MipRental.Web.Documents;

/// <summary>
/// Belgenin sağ altına basılan doğrulama karekodu.
///
/// Karekodun içinde /Dogrula/{kod} adresinin TAMAMI vardır: kâğıdı eline alan
/// kişi telefonuyla okutunca doğrudan doğrulama sayfasına gider, kodu elle
/// yazmak zorunda kalmaz. Kod ayrıca okunabilir metin olarak da basılır —
/// karekod okunamazsa (faks, kötü fotokopi) elle girilebilsin diye.
/// </summary>
public static class VerificationQrCode
{
    /// <summary>
    /// PNG baytları. ECC seviyesi Q (%25 hata düzeltme): fotokopi ve mühür
    /// lekesine dayanıklı olsun diye M yerine Q seçildi.
    /// </summary>
    public static byte[] CreatePng(string verificationUrl, int pixelsPerModule = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationUrl);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(verificationUrl, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(data);
        return qrCode.GetGraphic(pixelsPerModule);
    }
}
