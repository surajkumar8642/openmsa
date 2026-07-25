using Isopoh.Cryptography.Argon2;

namespace OpenMSA.Identity;

public sealed class PasswordService
{
    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty.");
        return Argon2.Hash(password);
    }

    public bool VerifyPassword(string hash, string password)
    {
        return !string.IsNullOrWhiteSpace(hash) && Argon2.Verify(hash, password);
    }
}
