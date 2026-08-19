using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(x => x.RoleId);

        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Scope)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasData(
            new Role { RoleId = 1, Code = "REQUESTER", Name = "Talep Eden", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 2, Code = "SUPERVISOR", Name = "Amir", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 3, Code = "DEPT_HEAD", Name = "Departman Müdürü", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 4, Code = "BUDGET", Name = "Bütçe", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 5, Code = "ACCOUNTING", Name = "Muhasebe", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 6, Code = "FIRM_USER", Name = "Firma Kullanıcısı", Scope = RoleScope.EXTERNAL },
            new Role { RoleId = 7, Code = "ADMIN", Name = "Sistem Yöneticisi", Scope = RoleScope.INTERNAL }
        );
    }
}
