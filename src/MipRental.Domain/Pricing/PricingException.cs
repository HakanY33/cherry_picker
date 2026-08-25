namespace MipRental.Domain.Pricing;

// Fiyatlandırma hesabı sırasında oluşan, kullanıcıya doğrudan gösterilebilecek
// Türkçe mesajlı hatalar için kullanılır (örn. sözleşme satırı bulunamadı,
// birden fazla satır eşleşti, geçersiz miktar/saat girişi).
public sealed class PricingException : Exception
{
    public PricingException(string message) : base(message)
    {
    }
}
