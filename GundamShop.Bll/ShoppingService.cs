using System.Data;
using GundamShop.Dal;
using Microsoft.EntityFrameworkCore;

namespace GundamShop.Bll;

public class ShoppingService(ShopDbContext db)
{
    public async Task<IReadOnlyList<CartItemDto>> Cart(Guid userId) => await db.CartItems.AsNoTracking()
        .Where(x => x.UserId == userId).OrderBy(x => x.Variant.Sku)
        .Select(x => new CartItemDto(x.Id, x.VariantId, x.Variant.Product.Name, x.Variant.Sku, x.Variant.PriceVnd, x.Quantity)).ToListAsync();

    public async Task<CartItemDto> CartItem(Guid userId, Guid id)
    {
        var x = ShopGuard.Found(await db.CartItems.AsNoTracking().Where(x => x.Id == id && x.UserId == userId)
            .Select(x => new CartItemDto(x.Id, x.VariantId, x.Variant.Product.Name, x.Variant.Sku, x.Variant.PriceVnd, x.Quantity)).FirstOrDefaultAsync(), "mục giỏ hàng");
        return x;
    }

    public async Task<CartItemDto> AddCartItem(Guid userId, CartItemRequest i)
    {
        ShopGuard.Require(await db.Variants.AnyAsync(v => v.Id == i.VariantId && v.IsActive && v.Product.IsActive && v.Product.Category.IsActive), "SKU không bán.");
        var existing = await db.CartItems.SingleOrDefaultAsync(x => x.UserId == userId && x.VariantId == i.VariantId);
        ShopGuard.Require(existing == null, "SKU đã có trong giỏ, hãy cập nhật số lượng.", 409);
        var x = new CartItem { UserId = userId, VariantId = i.VariantId, Quantity = i.Quantity };
        db.CartItems.Add(x); await db.SaveChangesAsync(); return await CartItem(userId, x.Id);
    }

    public async Task<CartItemDto> UpdateCartItem(Guid userId, Guid id, CartQuantityRequest i)
    {
        var x = ShopGuard.Found(await db.CartItems.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId), "mục giỏ hàng");
        x.Quantity = i.Quantity; await db.SaveChangesAsync(); return await CartItem(userId, id);
    }

    public async Task DeleteCartItem(Guid userId, Guid id)
    {
        var x = ShopGuard.Found(await db.CartItems.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId), "mục giỏ hàng");
        db.CartItems.Remove(x); await db.SaveChangesAsync();
    }

    public Task<Page<OrderDto>> Orders(Guid userId, int page, int size, bool admin = false) => PageHelper.GetAsync(db.Orders.AsNoTracking()
        .Where(o => admin || o.UserId == userId).OrderByDescending(o => o.CreatedAtUtc)
        .Select(o => new OrderDto(o.Id, o.Status, o.PaymentStatus, o.SubtotalVnd, o.DiscountVnd, o.TotalVnd, o.CreatedAtUtc, o.ExpiresAtUtc,
            o.Items.Select(i => new OrderItemDto(i.VariantId, i.ProductName, i.Sku, i.UnitPriceVnd, i.Quantity)).ToList())), page, size);

    public async Task<OrderDto> Order(Guid userId, Guid id, bool admin = false)
    {
        var x = ShopGuard.Found(await db.Orders.AsNoTracking().Where(o => o.Id == id && (admin || o.UserId == userId))
            .Select(o => new OrderDto(o.Id, o.Status, o.PaymentStatus, o.SubtotalVnd, o.DiscountVnd, o.TotalVnd, o.CreatedAtUtc, o.ExpiresAtUtc,
                o.Items.Select(i => new OrderItemDto(i.VariantId, i.ProductName, i.Sku, i.UnitPriceVnd, i.Quantity)).ToList())).FirstOrDefaultAsync(), "đơn hàng");
        return x;
    }

    public async Task<OrderDto> Checkout(Guid userId, CheckoutRequest i, string provider)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var address = ShopGuard.Found(await db.Addresses.SingleOrDefaultAsync(a => a.Id == i.AddressId && a.UserId == userId), "địa chỉ");
        var cart = await db.CartItems.Include(c => c.Variant).ThenInclude(v => v.Product)
            .Where(c => c.UserId == userId).ToListAsync();
        ShopGuard.Require(cart.Count > 0, "Giỏ hàng trống.");
        foreach (var c in cart)
        {
            ShopGuard.Require(c.Variant.IsActive && c.Variant.Product.IsActive, $"SKU {c.Variant.Sku} không còn bán.", 409);
            ShopGuard.Require(c.Variant.Stock >= c.Quantity, $"SKU {c.Variant.Sku} không đủ tồn kho.", 409);
        }
        var subtotal = checked(cart.Sum(c => checked(c.Variant.PriceVnd * c.Quantity)));
        Voucher? voucher = null;
        long discount = 0;
        if (!string.IsNullOrWhiteSpace(i.VoucherCode))
        {
            var code = i.VoucherCode.Trim().ToUpperInvariant();
            voucher = ShopGuard.Found(await db.Vouchers.SingleOrDefaultAsync(v => v.Code == code), "voucher");
            // Quy tắc voucher (thời hạn, lượt dùng, đơn tối thiểu, giảm cố định hoặc theo %) nằm ở VoucherRules.
            discount = VoucherRules.Apply(voucher, subtotal, DateTime.UtcNow);
            voucher.UsedCount++;
        }
        var order = new ShopOrder
        {
            UserId = userId, VoucherId = voucher?.Id, Recipient = address.Recipient, Phone = address.Phone,
            ShippingAddress = $"{address.Line1}, {address.Ward}, {address.District}, {address.Province}",
            SubtotalVnd = subtotal, DiscountVnd = discount, TotalVnd = subtotal - discount,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24),
            Items = cart.Select(c => new OrderItem { VariantId = c.VariantId, ProductName = c.Variant.Product.Name,
                Sku = c.Variant.Sku, UnitPriceVnd = c.Variant.PriceVnd, Quantity = c.Quantity }).ToList()
        };
        foreach (var c in cart) c.Variant.Stock -= c.Quantity;
        db.CartItems.RemoveRange(cart);
        db.Orders.Add(order);
        db.Payments.Add(new Payment { OrderId = order.Id, Provider = provider });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return await Order(userId, order.Id);
    }

    public async Task<OrderDto> Cancel(Guid userId, Guid id, bool admin)
    {
        var order = ShopGuard.Found(await db.Orders.FirstOrDefaultAsync(o => o.Id == id && (admin || o.UserId == userId)), "đơn hàng");
        ShopGuard.Require(order.Status == "PendingPayment", "Chỉ có thể hủy đơn chưa thanh toán.", 409);
        await Release(order, "Cancelled", "Cancelled");
        return await Order(userId, id, admin);
    }

    public async Task<OrderDto> SetStatus(Guid id, string status)
    {
        var o = ShopGuard.Found(await db.Orders.FindAsync(id), "đơn hàng");
        var next = o.Status switch { "Paid" => "Shipped", "Shipped" => "Delivered", _ => "" };
        ShopGuard.Require(next == status, "Chuyển trạng thái không hợp lệ.", 409);
        o.Status = status; await db.SaveChangesAsync(); return await Order(o.UserId, id, true);
    }

    public async Task Release(ShopOrder order, string status, string paymentStatus)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await db.Entry(order).ReloadAsync();
        if (order.Status != "PendingPayment") { await tx.CommitAsync(); return; }
        var items = await db.OrderItems.Where(x => x.OrderId == order.Id).ToListAsync();
        foreach (var item in items)
        {
            var variant = ShopGuard.Found(await db.Variants.FindAsync(item.VariantId), "SKU");
            variant.Stock += item.Quantity;
        }
        if (order.VoucherId is Guid voucherId)
        {
            var voucher = ShopGuard.Found(await db.Vouchers.FindAsync(voucherId), "voucher");
            voucher.UsedCount--;
        }
        order.Status = status; order.PaymentStatus = paymentStatus;
        var payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
        payment.Status = paymentStatus; payment.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
