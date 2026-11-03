using GundamShop.Bll;

namespace GundamShop.Api;

public static class ShopRoutes
{
    public static void MapShopRoutes(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>();
        var auth = api.MapGroup("/auth").WithTags("1 - Tài khoản");
        auth.MapPost("/register", async (RegisterRequest i, AuthService s, IConfiguration c) => Results.Ok(ApiAuth.Token(await s.Register(i), c)));
        auth.MapPost("/login", async (LoginRequest i, AuthService s, IConfiguration c) => Results.Ok(ApiAuth.Token(await s.Login(i), c)));
        auth.MapGet("/me", async (HttpContext h, AuthService s) => Results.Ok(await s.Me(h.UserId()))).RequireAuthorization();

        var addresses = api.MapGroup("/addresses").WithTags("1 - Địa chỉ").RequireAuthorization();
        addresses.MapGet("/", async (HttpContext h, AuthService s) => Results.Ok(await s.Addresses(h.UserId())));
        addresses.MapGet("/{id:guid}", async (HttpContext h, Guid id, AuthService s) => Results.Ok(await s.Address(h.UserId(), id)));
        addresses.MapPost("/", async (HttpContext h, AddressRequest i, AuthService s) => Results.Created("/api/v1/addresses", await s.AddAddress(h.UserId(), i)));
        addresses.MapPut("/{id:guid}", async (HttpContext h, Guid id, AddressRequest i, AuthService s) => Results.Ok(await s.UpdateAddress(h.UserId(), id, i)));
        addresses.MapDelete("/{id:guid}", async (HttpContext h, Guid id, AuthService s) => { await s.DeleteAddress(h.UserId(), id); return Results.NoContent(); });

        var categories = api.MapGroup("/categories").WithTags("2 - Danh mục");
        categories.MapGet("/", async (HttpContext h, int? page, int? pageSize, CatalogService s) => Results.Ok(await s.Categories(page ?? 1, pageSize ?? 20, h.User.IsInRole("Admin"))));
        categories.MapGet("/{id:guid}", async (HttpContext h, Guid id, CatalogService s) => Results.Ok(await s.Category(id, h.User.IsInRole("Admin"))));
        categories.MapPost("/", async (CategoryRequest i, CatalogService s) => Results.Created("/api/v1/categories", await s.AddCategory(i))).RequireAuthorization("Admin");
        categories.MapPut("/{id:guid}", async (Guid id, CategoryRequest i, CatalogService s) => Results.Ok(await s.UpdateCategory(id, i))).RequireAuthorization("Admin");
        categories.MapDelete("/{id:guid}", async (Guid id, CatalogService s) => { await s.DeleteCategory(id); return Results.NoContent(); }).RequireAuthorization("Admin");

        var products = api.MapGroup("/products").WithTags("2 - Sản phẩm");
        products.MapGet("/", async (HttpContext h, int? page, int? pageSize, CatalogService s) => Results.Ok(await s.Products(page ?? 1, pageSize ?? 20, h.User.IsInRole("Admin"))));
        products.MapGet("/{id:guid}", async (HttpContext h, Guid id, CatalogService s) => Results.Ok(await s.Product(id, h.User.IsInRole("Admin"))));
        products.MapPost("/", async (ProductRequest i, CatalogService s) => Results.Created("/api/v1/products", await s.AddProduct(i))).RequireAuthorization("Admin");
        products.MapPut("/{id:guid}", async (Guid id, ProductRequest i, CatalogService s) => Results.Ok(await s.UpdateProduct(id, i))).RequireAuthorization("Admin");
        products.MapDelete("/{id:guid}", async (Guid id, CatalogService s) => { await s.DeleteProduct(id); return Results.NoContent(); }).RequireAuthorization("Admin");
        products.MapGet("/{id:guid}/variants", async (Guid id, int? page, int? pageSize, HttpContext h, CatalogService s) => Results.Ok(await s.Variants(id, page ?? 1, pageSize ?? 20, h.User.IsInRole("Admin"))));
        products.MapGet("/{id:guid}/reviews", async (HttpContext h, Guid id, int? rating, int? page, int? pageSize, PromotionService s) =>
            Results.Ok(await s.Reviews(id, rating, page ?? 1, pageSize ?? 20, h.User.IsInRole("Admin"))));

        var variants = api.MapGroup("/variants").WithTags("3 - SKU và tồn kho");
        variants.MapGet("/{id:guid}", async (HttpContext h, Guid id, CatalogService s) => Results.Ok(await s.Variant(id, h.User.IsInRole("Admin"))));
        variants.MapPost("/", async (VariantRequest i, CatalogService s) => Results.Created("/api/v1/variants", await s.AddVariant(i))).RequireAuthorization("Admin");
        variants.MapPut("/{id:guid}", async (Guid id, VariantUpdateRequest i, CatalogService s) => Results.Ok(await s.UpdateVariant(id, i))).RequireAuthorization("Admin");
        variants.MapDelete("/{id:guid}", async (Guid id, CatalogService s) => { await s.DeleteVariant(id); return Results.NoContent(); }).RequireAuthorization("Admin");
        variants.MapPost("/{id:guid}/stock-adjustments", async (Guid id, StockAdjustRequest i, CatalogService s) => Results.Ok(await s.AdjustStock(id, i))).RequireAuthorization("Admin");

        var cart = api.MapGroup("/cart/items").WithTags("4 - Giỏ hàng").RequireAuthorization();
        cart.MapGet("/", async (HttpContext h, ShoppingService s) => Results.Ok(await s.Cart(h.UserId())));
        cart.MapGet("/{id:guid}", async (HttpContext h, Guid id, ShoppingService s) => Results.Ok(await s.CartItem(h.UserId(), id)));
        cart.MapPost("/", async (HttpContext h, CartItemRequest i, ShoppingService s) => Results.Created("/api/v1/cart/items", await s.AddCartItem(h.UserId(), i)));
        cart.MapPut("/{id:guid}", async (HttpContext h, Guid id, CartQuantityRequest i, ShoppingService s) => Results.Ok(await s.UpdateCartItem(h.UserId(), id, i)));
        cart.MapDelete("/{id:guid}", async (HttpContext h, Guid id, ShoppingService s) => { await s.DeleteCartItem(h.UserId(), id); return Results.NoContent(); });

        var orders = api.MapGroup("/orders").WithTags("4 - Đơn hàng").RequireAuthorization();
        orders.MapGet("/", async (HttpContext h, int? page, int? pageSize, ShoppingService s) => Results.Ok(await s.Orders(h.UserId(), page ?? 1, pageSize ?? 20, h.User.IsInRole("Admin"))));
        orders.MapGet("/{id:guid}", async (HttpContext h, Guid id, ShoppingService s) => Results.Ok(await s.Order(h.UserId(), id, h.User.IsInRole("Admin"))));
        orders.MapPost("/", async (HttpContext h, CheckoutRequest i, ShoppingService s, PaymentService p) => Results.Created("/api/v1/orders", await s.Checkout(h.UserId(), i, p.Provider)));
        orders.MapPost("/{id:guid}/cancel", async (HttpContext h, Guid id, ShoppingService s) => Results.Ok(await s.Cancel(h.UserId(), id, h.User.IsInRole("Admin"))));
        orders.MapPut("/{id:guid}/status", async (Guid id, StatusRequest i, ShoppingService s) => Results.Ok(await s.SetStatus(id, i.Status))).RequireAuthorization("Admin");

        var vouchers = api.MapGroup("/vouchers").WithTags("5 - Voucher").RequireAuthorization("Admin");
        vouchers.MapGet("/", async (string? search, string? status, int? page, int? pageSize, PromotionService s) => Results.Ok(await s.Vouchers(search, status, page ?? 1, pageSize ?? 20)));
        vouchers.MapGet("/{id:guid}", async (Guid id, PromotionService s) => Results.Ok(await s.Voucher(id)));
        vouchers.MapPost("/", async (VoucherRequest i, PromotionService s) => { var v = await s.AddVoucher(i); return Results.Created($"/api/v1/vouchers/{v.Id}", v); });
        vouchers.MapPut("/{id:guid}", async (Guid id, VoucherRequest i, PromotionService s) => Results.Ok(await s.UpdateVoucher(id, i)));
        vouchers.MapDelete("/{id:guid}", async (Guid id, PromotionService s) => { await s.DeleteVoucher(id); return Results.NoContent(); });

        var reviews = api.MapGroup("/reviews").WithTags("5 - Đánh giá");
        reviews.MapGet("/{id:guid}", async (HttpContext h, Guid id, PromotionService s) => Results.Ok(await s.Review(id, h.User.IsInRole("Admin"))));
        reviews.MapPost("/", async (HttpContext h, ReviewRequest i, PromotionService s) => { var r = await s.AddReview(h.UserId(), i); return Results.Created($"/api/v1/reviews/{r.Id}", r); }).RequireAuthorization();
        reviews.MapPut("/{id:guid}", async (HttpContext h, Guid id, ReviewUpdateRequest i, PromotionService s) => Results.Ok(await s.UpdateReview(h.UserId(), id, i))).RequireAuthorization();
        reviews.MapDelete("/{id:guid}", async (HttpContext h, Guid id, PromotionService s) => { await s.DeleteReview(h.UserId(), id, h.User.IsInRole("Admin")); return Results.NoContent(); }).RequireAuthorization();

        var payments = api.MapGroup("/payments").WithTags("5 - Thanh toán");
        payments.MapPost("/orders/{id:guid}/session", async (HttpContext h, Guid id, PaymentService s) => Results.Ok(await s.Start(h.UserId(), id))).RequireAuthorization();
        if (app.Environment.IsDevelopment())
            payments.MapPost("/mock/orders/{id:guid}/complete", async (HttpContext h, Guid id, MockResult i, PaymentService s) => { await s.CompleteMock(h.UserId(), id, i.Succeeded); return Results.Ok(new { orderId = id, succeeded = i.Succeeded }); }).RequireAuthorization();
        payments.MapPost("/stripe/webhook", async (HttpContext h, PaymentService s) =>
        {
            using var reader = new StreamReader(h.Request.Body);
            var payload = await reader.ReadToEndAsync();
            var signature = h.Request.Headers["Stripe-Signature"].ToString();
            return Results.Ok(new { processed = await s.HandleStripeWebhook(payload, signature) });
        });
    }
}

public record MockResult(bool Succeeded);
