using Microsoft.EntityFrameworkCore;

namespace GundamShop.Dal;

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> Variants => Set<ProductVariant>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<ShopOrder> Orders => Set<ShopOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        b.Entity<AppUser>().Property(x => x.Email).HasMaxLength(256);
        b.Entity<AppUser>().Property(x => x.Role).HasMaxLength(20);
        b.Entity<Address>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Category>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Category>().Property(x => x.Slug).HasMaxLength(120);
        b.Entity<Product>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Product>().Property(x => x.Slug).HasMaxLength(160);
        b.Entity<Product>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductVariant>().HasIndex(x => x.Sku).IsUnique();
        b.Entity<ProductVariant>().Property(x => x.Sku).HasMaxLength(64);
        b.Entity<ProductVariant>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<ProductVariant>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CartItem>().HasIndex(x => new { x.UserId, x.VariantId }).IsUnique();
        b.Entity<CartItem>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CartItem>().HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Voucher>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Voucher>().Property(x => x.Code).HasMaxLength(64);
        b.Entity<Voucher>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Vouchers_DiscountVnd", "[DiscountVnd] > 0");
            t.HasCheckConstraint("CK_Vouchers_DiscountPercent", "[DiscountPercent] IS NULL OR [DiscountPercent] BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_Vouchers_MinSubtotalVnd", "[MinSubtotalVnd] >= 0");
            t.HasCheckConstraint("CK_Vouchers_UsedCount", "[UsedCount] >= 0 AND [UsedCount] <= [MaxUses]");
            t.HasCheckConstraint("CK_Vouchers_Period", "[StartsAtUtc] < [EndsAtUtc]");
        });
        // SQL Server không lưu DateTimeKind: đánh dấu lại là UTC để JSON trả "...Z" đúng với tên trường.
        b.Entity<Voucher>().Property(x => x.StartsAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Entity<Voucher>().Property(x => x.EndsAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Entity<ShopOrder>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ShopOrder>().HasOne<Voucher>().WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ShopOrder>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrderItem>().HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Payment>().HasIndex(x => x.OrderId).IsUnique();
        b.Entity<Payment>().HasOne<ShopOrder>().WithOne().HasForeignKey<Payment>(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Payment>().HasIndex(x => x.ExternalSessionId).IsUnique().HasFilter("[ExternalSessionId] IS NOT NULL");
        b.Entity<Payment>().Property(x => x.ExternalSessionId).HasMaxLength(255);
        b.Entity<PaymentWebhookEvent>().HasKey(x => x.EventId);
        b.Entity<PaymentWebhookEvent>().Property(x => x.EventId).HasMaxLength(255);
        b.Entity<Review>().HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
        b.Entity<Review>().ToTable(t => t.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5"));
        b.Entity<Review>().Property(x => x.CreatedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Entity<Review>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Review>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(string)))
                if (property.GetMaxLength() == null) property.SetMaxLength(1000);
    }
}
