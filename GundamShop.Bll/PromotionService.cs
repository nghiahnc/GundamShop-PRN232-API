using System.Globalization;
using GundamShop.Dal;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GundamShop.Bll;

public static class VoucherRules
{
    public const long MaxAmountVnd = 1_000_000_000;
    public const int MaxUses = 1_000_000;
    // Cho phép khoảng trắng đầu/cuối vì mã được cắt bỏ trước khi lưu.
    public const string CodePattern = @"^\s*[A-Za-z0-9][A-Za-z0-9_-]*\s*$";
    public static readonly string[] Statuses = ["Active", "Scheduled", "Expired", "UsedUp", "Disabled"];

    // Kiểm tra voucher lúc đặt hàng (Checkout gọi hàm này) và trả số tiền được giảm.
    // usedByCustomer: kết quả của UsedBy, mỗi khách chỉ dùng mỗi voucher một lần.
    public static long Apply(Voucher v, long subtotal, DateTime nowUtc, bool usedByCustomer)
    {
        ShopGuard.Require(v.IsActive, "Voucher đã ngừng sử dụng.", 409);
        ShopGuard.Require(v.StartsAtUtc <= nowUtc, "Voucher chưa đến thời gian sử dụng.", 409);
        ShopGuard.Require(v.EndsAtUtc > nowUtc, "Voucher đã hết hạn.", 409);
        ShopGuard.Require(v.UsedCount < v.MaxUses, "Voucher đã hết lượt sử dụng.", 409);
        ShopGuard.Require(!usedByCustomer, "Bạn đã dùng voucher này.", 409);
        ShopGuard.Require(subtotal >= v.MinSubtotalVnd, $"Đơn hàng cần tối thiểu {Vnd(v.MinSubtotalVnd)} để dùng voucher này.", 409);
        return Discount(v, subtotal);
    }

    // Khách đã có đơn dùng voucher này và lượt chưa được trả lại.
    // Đơn hủy, hết hạn hoặc thanh toán thất bại đã trả lượt (UsedCount--), nên không tính.
    public static Task<bool> UsedBy(ShopDbContext db, Guid userId, Guid voucherId) =>
        db.Orders.AnyAsync(o => o.UserId == userId && o.VoucherId == voucherId
            && o.Status != "Cancelled" && o.Status != "Expired" && o.Status != "PaymentFailed");

    // Voucher cố định giảm DiscountVnd. Voucher % giảm DiscountPercent% tạm tính (làm tròn xuống), tối đa DiscountVnd.
    // Không bao giờ giảm quá tạm tính.
    public static long Discount(Voucher v, long subtotal)
    {
        var amount = v.DiscountPercent is int percent ? (long)Math.Floor(subtotal * (decimal)percent / 100) : v.DiscountVnd;
        return Math.Min(subtotal, Math.Min(amount, v.DiscountVnd));
    }

    public static string Vnd(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.') + " VND";
}

public class PromotionService(ShopDbContext db)
{
    private const string DuplicateCodeMessage = "Mã voucher đã tồn tại.";
    private const string DuplicateReviewMessage = "Bạn đã đánh giá sản phẩm.";

    public Task<Page<VoucherDto>> Vouchers(string? search, string? status, int page, int size)
    {
        var now = DateTime.UtcNow;
        var query = db.Vouchers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(x => x.Code.Contains(term));
        }
        query = FilterStatus(query, status, now);
        return PageHelper.GetAsync(Project(query.OrderBy(x => x.Code), now), page, size);
    }

    public async Task<VoucherDto> Voucher(Guid id) =>
        ShopGuard.Found(await Project(db.Vouchers.AsNoTracking().Where(x => x.Id == id), DateTime.UtcNow).FirstOrDefaultAsync(), "voucher");

    public async Task<VoucherDto> AddVoucher(VoucherRequest i)
    {
        var (starts, ends) = Period(i);
        ShopGuard.Require(ends > DateTime.UtcNow, "Thời điểm kết thúc phải ở tương lai.");
        var code = i.Code.Trim().ToUpperInvariant();
        ShopGuard.Require(!await db.Vouchers.AnyAsync(x => x.Code == code), DuplicateCodeMessage, 409);
        var x = new Voucher { IsActive = i.IsActive ?? true, DiscountPercent = Percent(i.DiscountPercent) };
        Copy(i, x, code, starts, ends);
        db.Vouchers.Add(x);
        await SaveVoucher();
        return await Voucher(x.Id);
    }

    public async Task<VoucherDto> UpdateVoucher(Guid id, VoucherRequest i)
    {
        var (starts, ends) = Period(i);
        var x = ShopGuard.Found(await db.Vouchers.FindAsync(id), "voucher");
        var code = i.Code.Trim().ToUpperInvariant();
        ShopGuard.Require(!await db.Vouchers.AnyAsync(v => v.Id != id && v.Code == code), DuplicateCodeMessage, 409);
        ShopGuard.Require(i.MaxUses >= x.UsedCount, $"Voucher đã được dùng {x.UsedCount} lượt, MaxUses không thể nhỏ hơn số này.", 409);
        Copy(i, x, code, starts, ends);
        // Không gửi thì giữ nguyên: isActive (true để bật lại voucher đã ngừng), discountPercent (0 để chuyển sang giảm cố định).
        // Nhờ vậy client chỉ gia hạn ngày hay sửa số lượt không vô tình đổi voucher % thành giảm cố định.
        if (i.IsActive is bool active) x.IsActive = active;
        if (i.DiscountPercent is int percent) x.DiscountPercent = Percent(percent);
        await SaveVoucher();
        return await Voucher(id);
    }

    public async Task DeleteVoucher(Guid id)
    {
        var x = ShopGuard.Found(await db.Vouchers.FindAsync(id), "voucher");
        x.IsActive = false; await db.SaveChangesAsync();
    }

    public async Task<ReviewPage> Reviews(Guid productId, int? rating, int page, int size, bool admin = false)
    {
        ShopGuard.Require(rating is null or (>= 1 and <= 5), "rating phải từ 1 đến 5.");
        ShopGuard.Require(await ProductVisible(productId, admin), "Không tìm thấy sản phẩm.", 404);
        var all = db.Reviews.AsNoTracking().Where(x => x.ProductId == productId);
        // Tổng hợp số sao trên mọi đánh giá của sản phẩm, không phụ thuộc bộ lọc rating.
        var counts = await all.GroupBy(x => x.Rating).Select(g => new { Rating = g.Key, Count = g.Count() }).ToListAsync();
        var total = counts.Sum(c => c.Count);
        double? average = total == 0 ? null : Math.Round(counts.Sum(c => (double)c.Rating * c.Count) / total, 1, MidpointRounding.AwayFromZero);
        var ratingCounts = Enumerable.Range(1, 5).ToDictionary(r => r, r => counts.FirstOrDefault(c => c.Rating == r)?.Count ?? 0);
        var filtered = rating is int star ? all.Where(x => x.Rating == star) : all;
        var result = await PageHelper.GetAsync(Project(filtered.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)), page, size);
        return new ReviewPage(result.Items, result.PageNumber, result.PageSize, result.TotalCount, average, ratingCounts);
    }

    public async Task<ReviewDto> Review(Guid id, bool admin = false) =>
        ShopGuard.Found(await Project(db.Reviews.AsNoTracking()
            .Where(x => x.Id == id && (admin || (x.Product.IsActive && x.Product.Category.IsActive)))).FirstOrDefaultAsync(), "đánh giá");

    public async Task<ReviewDto> AddReview(Guid userId, ReviewRequest i)
    {
        ShopGuard.Require(await ProductVisible(i.ProductId, false), "Sản phẩm không tồn tại hoặc đã ngừng bán.");
        ShopGuard.Require(!await db.Reviews.AnyAsync(x => x.ProductId == i.ProductId && x.UserId == userId), DuplicateReviewMessage, 409);
        var bought = await db.Orders.Where(o => o.UserId == userId && o.Status == "Delivered")
            .Join(db.OrderItems, o => o.Id, oi => oi.OrderId, (o, oi) => oi.VariantId)
            .Join(db.Variants, id => id, v => v.Id, (id, v) => v.ProductId)
            .AnyAsync(id => id == i.ProductId);
        ShopGuard.Require(bought, "Chỉ được đánh giá sản phẩm đã nhận.", 403);
        var x = new Review { UserId = userId, ProductId = i.ProductId, Rating = i.Rating, Comment = i.Comment.Trim() };
        db.Reviews.Add(x);
        // Hai request cùng lúc có thể cùng qua bước kiểm tra trên; unique index (UserId, ProductId) chặn request đến sau.
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { throw new ShopException(409, DuplicateReviewMessage); }
        return await Review(x.Id, true);
    }

    public async Task<ReviewDto> UpdateReview(Guid userId, Guid id, ReviewUpdateRequest i)
    {
        var x = ShopGuard.Found(await db.Reviews.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId), "đánh giá");
        // Đánh giá của sản phẩm đã ngừng bán bị ẩn, nên không sửa được (vẫn xóa được).
        ShopGuard.Require(await ProductVisible(x.ProductId, false), "Sản phẩm đã ngừng bán, không sửa được đánh giá.", 409);
        x.Rating = i.Rating; x.Comment = i.Comment.Trim(); await db.SaveChangesAsync();
        return await Review(id, true);
    }

    public async Task DeleteReview(Guid userId, Guid id, bool admin)
    {
        var x = ShopGuard.Found(await db.Reviews.SingleOrDefaultAsync(x => x.Id == id && (admin || x.UserId == userId)), "đánh giá");
        db.Reviews.Remove(x); await db.SaveChangesAsync();
    }

    private static IQueryable<Voucher> FilterStatus(IQueryable<Voucher> query, string? status, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(status)) return query;
        var name = VoucherRules.Statuses.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ShopException(400, "status phải là Active, Scheduled, Expired, UsedUp hoặc Disabled.");
        return name switch
        {
            "Disabled" => query.Where(x => !x.IsActive),
            "Expired" => query.Where(x => x.IsActive && x.EndsAtUtc <= now),
            "Scheduled" => query.Where(x => x.IsActive && x.EndsAtUtc > now && x.StartsAtUtc > now),
            "UsedUp" => query.Where(x => x.IsActive && x.EndsAtUtc > now && x.StartsAtUtc <= now && x.UsedCount >= x.MaxUses),
            _ => query.Where(x => x.IsActive && x.EndsAtUtc > now && x.StartsAtUtc <= now && x.UsedCount < x.MaxUses)
        };
    }

    // Status tính tại thời điểm đọc, cùng thứ tự ưu tiên với FilterStatus.
    private static IQueryable<VoucherDto> Project(IQueryable<Voucher> query, DateTime now) => query.Select(x => new VoucherDto(
        x.Id, x.Code, x.DiscountVnd, x.MinSubtotalVnd, x.MaxUses, x.UsedCount, x.StartsAtUtc, x.EndsAtUtc, x.IsActive, x.DiscountPercent,
        !x.IsActive ? "Disabled" : x.EndsAtUtc <= now ? "Expired" : x.StartsAtUtc > now ? "Scheduled" : x.UsedCount >= x.MaxUses ? "UsedUp" : "Active"));

    private IQueryable<ReviewDto> Project(IQueryable<Review> query) => query.Select(x => new ReviewDto(
        x.Id, x.ProductId, x.UserId, x.Rating, x.Comment, x.CreatedAtUtc,
        db.Users.Where(u => u.Id == x.UserId).Select(u => u.FullName).FirstOrDefault() ?? ""));

    private Task<bool> ProductVisible(Guid productId, bool admin) =>
        db.Products.AnyAsync(p => p.Id == productId && (admin || (p.IsActive && p.Category.IsActive)));

    private async Task SaveVoucher()
    {
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { throw new ShopException(409, DuplicateCodeMessage); }
        // CHECK UsedCount <= MaxUses: có đơn vừa dùng voucher sau khi kiểm tra MaxUses ở trên.
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            throw new ShopException(409, "Voucher vừa được dùng thêm, MaxUses không còn hợp lệ. Hãy tải lại rồi thử lại.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static (DateTime Starts, DateTime Ends) Period(VoucherRequest i)
    {
        var starts = ToUtc(i.StartsAtUtc!.Value);
        var ends = ToUtc(i.EndsAtUtc!.Value);
        ShopGuard.Require(starts < ends, "Thời điểm kết thúc phải sau thời điểm bắt đầu.");
        return (starts, ends);
    }

    // Trường tên ...Utc: giờ không kèm múi giờ được hiểu là UTC, không phụ thuộc múi giờ của máy chạy API.
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static void Copy(VoucherRequest i, Voucher x, string code, DateTime starts, DateTime ends)
    {
        x.Code = code; x.DiscountVnd = i.DiscountVnd;
        x.MinSubtotalVnd = i.MinSubtotalVnd; x.MaxUses = i.MaxUses;
        x.StartsAtUtc = starts; x.EndsAtUtc = ends;
    }

    // 0 hoặc để trống: giảm cố định (lưu null).
    private static int? Percent(int? value) => value is > 0 ? value : null;
}
