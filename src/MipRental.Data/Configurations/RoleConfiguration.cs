using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Security;

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

        // Adım 10: RoleId 2 ve 3'ün KODU değişti, satırın kendisi değişmedi.
        // UserRoles ve ApprovalFlowSteps RoleId ile bağlı olduğundan mevcut
        // kullanıcı yetkileri ve onay zinciri taşınmaya gerek kalmadan korunur.
        builder.HasData(
            new Role { RoleId = 1, Code = RoleCodes.Requester, Name = "Talep Eden", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 2, Code = RoleCodes.EquipmentManager, Name = "Ekipman Müdürlüğü Yöneticisi", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 3, Code = RoleCodes.BudgetManager, Name = "Bütçe Yöneticisi", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 4, Code = RoleCodes.Budget, Name = "Bütçe", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 5, Code = RoleCodes.Accounting, Name = "Muhasebe", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 6, Code = RoleCodes.FirmUser, Name = "Firma Kullanıcısı", Scope = RoleScope.EXTERNAL },
            new Role { RoleId = 7, Code = RoleCodes.Admin, Name = "Sistem Yöneticisi", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 8, Code = RoleCodes.EquipmentViewer, Name = "Ekipman Müdürlüğü Kullanıcısı", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 9, Code = RoleCodes.FirmManager, Name = "Firma Yetkilisi", Scope = RoleScope.EXTERNAL },
            new Role { RoleId = 10, Code = RoleCodes.FirmOperator, Name = "Firma Operatörü", Scope = RoleScope.EXTERNAL }
        );
    }
}
