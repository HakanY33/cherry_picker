using System.Security.Cryptography;

namespace MipRental.Web.Security;

public static class PasswordGenerator
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz"; // l çıkarıldı (1 ile karışmasın)
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // I, O çıkarıldı
    private const string Digits = "23456789"; // 0, 1 çıkarıldı
    private const string Symbols = "!@#$%";
    private const string All = Lower + Upper + Digits + Symbols;

    public static string Generate(int length = 12)
    {
        var chars = new char[length];

        // Her karakter sınıfından en az bir tane garanti edilir.
        chars[0] = PickRandom(Lower);
        chars[1] = PickRandom(Upper);
        chars[2] = PickRandom(Digits);
        chars[3] = PickRandom(Symbols);

        for (var i = 4; i < length; i++)
        {
            chars[i] = PickRandom(All);
        }

        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char PickRandom(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
