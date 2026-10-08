# Gundam Shop API

Repository độc lập cho môn PRN232: backend cửa hàng mô hình Gundam viết bằng ASP.NET Core Web API (.NET 8), EF Core và SQL Server. API dùng JSON, có Swagger và không chứa mã Flutter.

## Bắt đầu nhanh trên Windows

1. Cài .NET SDK 8 và SQL Server LocalDB (hoặc SQL Server Express/Developer). Máy này đã có cả hai.
2. Trong thư mục vừa clone có `GundamShop.slnx`, chạy:

   ```powershell
   dotnet restore GundamShop.slnx --configfile NuGet.Config
   dotnet ef database update --project GundamShop.Dal --startup-project GundamShop.Api
   $env:ASPNETCORE_ENVIRONMENT = 'Development'
   $env:Database__AutoMigrate = 'true'
   dotnet run --project GundamShop.Api
   ```

3. Mở URL Swagger được in khi ứng dụng chạy, thêm `/swagger`. Tài khoản demo chỉ được tạo trong Development: `admin@gundam.local` / `Admin123!`.
4. Đọc [hướng dẫn dev](docs/DEVELOPER_GUIDE.md) để hiểu kiến trúc, cách thử API và Stripe.

Nếu mới clone dự án và muốn cài database **trong Visual Studio**, làm theo [hướng dẫn Visual Studio + SQL Server Object Explorer](VISUAL_STUDIO_DATABASE_SETUP.md).

File [phân công nhóm](outputs/01a0eab7-3c3c-79c1-9b9d-7651a6ec1cf4/GundamShop_PhanCong.xlsx) có sheet **Tổng kết** và **Chức năng thành viên**. Chạy `tests/smoke.ps1` khi API đang mở ở `http://127.0.0.1:5288` để kiểm thử luồng chính.
