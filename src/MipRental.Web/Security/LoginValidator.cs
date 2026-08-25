using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;

namespace MipRental.Web.Security;

public class LoginValidator
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;

    public LoginValidator(AppDbContext db, IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task<User?> ValidateAsync(string userName, string password)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.UserName == userName);
        if (user is null || !user.IsActive || user.PasswordHash is null)
        {
            return null;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : user;
    }
}
