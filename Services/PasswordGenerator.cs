using System.Security.Cryptography;
using System.Text;

namespace Veil.Services;

public static class PasswordGenerator
{
    public const int DefaultLength = 16;
    public const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#%^&*";

    public static string Generate(int length = DefaultLength)
    {
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            // GetInt32 is unbiased, unlike taking a random byte modulo the alphabet size.
            builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return builder.ToString();
    }
}
