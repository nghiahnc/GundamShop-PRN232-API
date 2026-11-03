using System.Security.Claims;
using System.Text;
using GundamShop.Api;
using GundamShop.Bll;
using GundamShop.Dal;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.OData.ModelBuilder;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Shop") ?? throw new InvalidOperationException("Thiếu ConnectionStrings:Shop.");
builder.Services.AddDbContext<ShopDbContext>(o => o.UseSqlServer(connection));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<ShoppingService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddHostedService<PendingOrderExpiryService>();
builder.Services.AddHttpClient("stripe", c => c.Timeout = TimeSpan.FromSeconds(20));
var jwtKey = builder.Configuration["Jwt:Key"] ?? "";
if (Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Jwt:Key phải dài ít nhất 32 byte.");
if (!builder.Environment.IsDevelopment() && builder.Configuration["Payments:Provider"] != "Stripe")
    throw new InvalidOperationException("Production chỉ cho phép Payments:Provider=Stripe.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), RoleClaimType = ClaimTypes.Role
    };
});
builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireRole("Admin")));
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(o => o.AddPolicy("FlutterWebDevelopment", policy => policy
        .SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1")
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        .AllowAnyHeader()
        .AllowAnyMethod()));
}
var edm = new ODataConventionModelBuilder();
edm.EntitySet<ProductODataDto>("Products");
builder.Services.AddControllers().AddOData(o => o.Filter().OrderBy().Count().SetMaxTop(50).AddRouteComponents("odata", edm.GetEdmModel()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Gundam Shop API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [] });
    o.SchemaFilter<VoucherRequestExampleFilter>();
});

var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseRouting();
if (app.Environment.IsDevelopment()) app.UseCors("FlutterWebDevelopment");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapShopRoutes();
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.Run(db);
}
app.Run();

public partial class Program { }
