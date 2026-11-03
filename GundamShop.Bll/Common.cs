using System.ComponentModel.DataAnnotations;
using GundamShop.Dal;
using Microsoft.EntityFrameworkCore;

namespace GundamShop.Bll;

public sealed class ShopException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount);

public static class PageHelper
{
    public static async Task<Page<T>> GetAsync<T>(IQueryable<T> query, int page, int size, CancellationToken ct = default)
    {
        if (page < 1 || size < 1 || size > 100) throw new ShopException(400, "page phải >= 1 và pageSize phải trong khoảng 1..100.");
        if ((long)(page - 1) * size > int.MaxValue) throw new ShopException(400, "page quá lớn.");
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items, page, size, total);
    }
}

public static class ShopGuard
{
    public static T Found<T>(T? value, string name) where T : class => value ?? throw new ShopException(404, $"Không tìm thấy {name}.");
    public static void Require(bool condition, string message, int status = 400)
    {
        if (!condition) throw new ShopException(status, message);
    }
    public static string Normalize(string text) => text.Trim();
}

public record RegisterRequest([property: Required, EmailAddress] string Email, [property: Required, MinLength(8)] string Password, [property: Required, MaxLength(100)] string FullName);
public record LoginRequest([property: Required, EmailAddress] string Email, [property: Required] string Password);
public record UserDto(Guid Id, string Email, string FullName, string Role);
public record AddressRequest([property: Required] string Recipient, [property: Required] string Phone, [property: Required] string Line1, [property: Required] string Ward, [property: Required] string District, [property: Required] string Province, bool IsDefault);
public record AddressDto(Guid Id, string Recipient, string Phone, string Line1, string Ward, string District, string Province, bool IsDefault);

public record CategoryRequest([property: Required] string Name, [property: Required, MaxLength(120)] string Slug, string? Description);
public record CategoryDto(Guid Id, string Name, string Slug, string? Description, bool IsActive);
public record ProductRequest(Guid CategoryId, [property: Required] string Name, [property: Required, MaxLength(160)] string Slug, [property: Required] string Grade, [property: Required] string Scale, string? Description, string? ImageUrl);
public record ProductDto(Guid Id, Guid CategoryId, string Name, string Slug, string Grade, string Scale, string? Description, string? ImageUrl, bool IsActive);
public class ProductODataDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Scale { get; set; } = "";
    public string Category { get; set; } = "";
    public long? FromPriceVnd { get; set; }
}
public record VariantRequest(Guid ProductId, [property: Required, MaxLength(64)] string Sku, [property: Required] string Label, [property: Range(1, long.MaxValue)] long PriceVnd, [property: Range(0, int.MaxValue)] int InitialStock);
public record VariantUpdateRequest([property: Required] string Label, [property: Range(1, long.MaxValue)] long PriceVnd, bool IsActive);
public record VariantDto(Guid Id, Guid ProductId, string Sku, string Label, long PriceVnd, int Stock, bool IsActive);
public record StockAdjustRequest(int Delta, [property: Required] string Reason);

public record CartItemRequest(Guid VariantId, [property: Range(1, 99)] int Quantity);
public record CartQuantityRequest([property: Range(1, 99)] int Quantity);
public record CartItemDto(Guid Id, Guid VariantId, string ProductName, string Sku, long UnitPriceVnd, int Quantity);
public record CheckoutRequest(Guid AddressId, string? VoucherCode);
public record OrderItemDto(Guid VariantId, string ProductName, string Sku, long UnitPriceVnd, int Quantity);
public record OrderDto(Guid Id, string Status, string PaymentStatus, long SubtotalVnd, long DiscountVnd, long TotalVnd, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, IReadOnlyList<OrderItemDto> Items);
public record StatusRequest([property: Required] string Status);

// DiscountPercent để trống: giảm cố định DiscountVnd. Có DiscountPercent: giảm theo %, DiscountVnd là mức giảm tối đa.
// IsActive để trống: tạo mới thì bật, sửa thì giữ nguyên trạng thái.
public record VoucherRequest(
    [property: Required(ErrorMessage = "Mã voucher là bắt buộc."), MaxLength(64, ErrorMessage = "Mã voucher tối đa 64 ký tự."),
        RegularExpression(VoucherRules.CodePattern, ErrorMessage = "Mã voucher chỉ gồm chữ, số, '-' và '_'.")] string Code,
    [property: Range(1, VoucherRules.MaxAmountVnd, ErrorMessage = "Số tiền giảm phải từ 1 đến 1.000.000.000 VND.")] long DiscountVnd,
    [property: Range(0, VoucherRules.MaxAmountVnd, ErrorMessage = "Giá trị đơn tối thiểu phải từ 0 đến 1.000.000.000 VND.")] long MinSubtotalVnd,
    [property: Range(1, VoucherRules.MaxUses, ErrorMessage = "Số lượt dùng phải từ 1 đến 1.000.000.")] int MaxUses,
    [property: Required(ErrorMessage = "Thời điểm bắt đầu là bắt buộc.")] DateTime? StartsAtUtc,
    [property: Required(ErrorMessage = "Thời điểm kết thúc là bắt buộc.")] DateTime? EndsAtUtc,
    [property: Range(1, 100, ErrorMessage = "Phần trăm giảm phải từ 1 đến 100.")] int? DiscountPercent = null,
    bool? IsActive = null);
// Status: Active (dùng được), Scheduled (chưa tới ngày), Expired (hết hạn), UsedUp (hết lượt), Disabled (đã ngừng).
public record VoucherDto(Guid Id, string Code, long DiscountVnd, long MinSubtotalVnd, int MaxUses, int UsedCount, DateTime StartsAtUtc, DateTime EndsAtUtc, bool IsActive,
    int? DiscountPercent, string Status);
public record ReviewRequest(Guid ProductId,
    [property: Range(1, 5, ErrorMessage = "Số sao phải từ 1 đến 5.")] int Rating,
    [property: Required(ErrorMessage = "Nội dung đánh giá là bắt buộc."), MaxLength(1000, ErrorMessage = "Nội dung đánh giá tối đa 1000 ký tự.")] string Comment);
public record ReviewUpdateRequest(
    [property: Range(1, 5, ErrorMessage = "Số sao phải từ 1 đến 5.")] int Rating,
    [property: Required(ErrorMessage = "Nội dung đánh giá là bắt buộc."), MaxLength(1000, ErrorMessage = "Nội dung đánh giá tối đa 1000 ký tự.")] string Comment);
public record ReviewDto(Guid Id, Guid ProductId, Guid UserId, int Rating, string Comment, DateTime CreatedAtUtc, string ReviewerName);
// Danh sách đánh giá kèm tổng hợp số sao của cả sản phẩm (không phụ thuộc bộ lọc rating).
public record ReviewPage(IReadOnlyList<ReviewDto> Items, int PageNumber, int PageSize, int TotalCount, double? AverageRating, IReadOnlyDictionary<int, int> RatingCounts);
