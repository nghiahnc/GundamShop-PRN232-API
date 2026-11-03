using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GundamShop.Bll;
using GundamShop.Dal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GundamShop.Api;

public class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try { await next(context); }
        catch (Exception ex)
        {
            var status = ex switch
            {
                ShopException e => e.Status,
                BadHttpRequestException e => e.StatusCode,
                DbUpdateConcurrencyException => 409,
                DbUpdateException => 409,
                OverflowException => 400,
                _ => 500
            };
            if (status == 500) logger.LogError(ex, "Unhandled API error");
            context.Response.StatusCode = status;
            var title = ex switch
            {
                _ when status == 500 => "Lỗi máy chủ",
                // JSON sai cú pháp, ngày giờ hoặc số không đọc được (ví dụ "startsAtUtc": "abc").
                BadHttpRequestException => "Dữ liệu gửi lên không đúng định dạng.",
                _ => ex.Message
            };
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title }, options: null, contentType: "application/problem+json; charset=utf-8");
        }
    }
}

public class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var arg in context.Arguments.Where(x => x != null && x.GetType().Namespace == typeof(RegisterRequest).Namespace))
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(arg!, new ValidationContext(arg!), results, true);
            foreach (var result in results)
                foreach (var key in result.MemberNames.DefaultIfEmpty("request"))
                    errors[key] = [result.ErrorMessage ?? "Không hợp lệ."];
        }
        return errors.Count > 0 ? Results.ValidationProblem(errors) : await next(context);
    }
}

// Body mẫu hợp lệ cho POST/PUT voucher trong Swagger (mặc định Swagger cho ngày bắt đầu = ngày kết thúc nên bị báo 400).
public class VoucherRequestExampleFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(VoucherRequest)) return;
        var today = DateTime.UtcNow.Date;
        schema.Example = new OpenApiObject
        {
            ["code"] = new OpenApiString("SALE10"),
            ["discountVnd"] = new OpenApiLong(50000),
            ["minSubtotalVnd"] = new OpenApiLong(300000),
            ["maxUses"] = new OpenApiInteger(100),
            ["startsAtUtc"] = new OpenApiString(today.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            ["endsAtUtc"] = new OpenApiString(today.AddDays(30).ToString("yyyy-MM-ddTHH:mm:ssZ")),
            ["discountPercent"] = new OpenApiNull(),
            ["isActive"] = new OpenApiBoolean(true)
        };
    }
}

public static class ApiAuth
{
    public static Guid UserId(this HttpContext context)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : throw new ShopException(401, "Cần đăng nhập.");
    }

    public static object Token(AppUser user, IConfiguration config)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var expires = DateTime.UtcNow.AddHours(8);
        var jwt = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims, expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new { accessToken = new JwtSecurityTokenHandler().WriteToken(jwt), expiresAtUtc = expires,
            user = new UserDto(user.Id, user.Email, user.FullName, user.Role) };
    }
}

public static class SeedData
{
    public static async Task Run(ShopDbContext db)
    {
        if (!await db.Users.AnyAsync()) db.Users.Add(new AppUser
        {
            Email = "admin@gundam.local", FullName = "Demo Admin", Role = "Admin", PasswordHash = AuthService.Hash("Admin123!")
        });
        if (!await db.Categories.AnyAsync())
        {
            var hg = new Category { Name = "High Grade", Slug = "high-grade", Description = "HG 1/144" };
            var mg = new Category { Name = "Master Grade", Slug = "master-grade", Description = "MG 1/100" };
            db.Categories.AddRange(hg, mg);
            db.Products.AddRange(
                new Product { Category = hg, Name = "RX-78-2 Gundam", Slug = "rx-78-2-hg", Grade = "HG", Scale = "1/144" },
                new Product { Category = mg, Name = "Freedom Gundam", Slug = "freedom-gundam-mg", Grade = "MG", Scale = "1/100" });
        }
        await db.SaveChangesAsync();
        if (!await db.Variants.AnyAsync())
        {
            var rx = await db.Products.SingleAsync(x => x.Slug == "rx-78-2-hg");
            var freedom = await db.Products.SingleAsync(x => x.Slug == "freedom-gundam-mg");
            db.Variants.AddRange(
                new ProductVariant { ProductId = rx.Id, Sku = "HG-RX78-001", Label = "Standard", PriceVnd = 350000, Stock = 20 },
                new ProductVariant { ProductId = freedom.Id, Sku = "MG-FREEDOM-001", Label = "Standard", PriceVnd = 950000, Stock = 10 });
            await db.SaveChangesAsync();
        }
    }
}
