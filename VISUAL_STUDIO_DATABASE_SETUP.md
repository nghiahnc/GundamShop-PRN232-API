# Cài đặt database trong Visual Studio sau khi tải dự án từ Git

Tài liệu dành cho từng thành viên chạy **FiveGuysGundam** trên máy Windows của mình. Dự án dùng **SQL Server**, EF Core và các migration đã có trong `GundamShop.Dal/Migrations`. Mỗi máy có thể có database riêng; tải source từ Git **không** tự mang theo dữ liệu SQL Server.

## 1. Cần cài những gì?

- Visual Studio 2022 có workload **ASP.NET and web development** và **Data storage and processing**. Workload dữ liệu cung cấp **SQL Server Object Explorer** để xem database ngay trong Visual Studio. ([Microsoft Learn](https://learn.microsoft.com/en-us/visualstudio/data-tools/create-a-sql-database-by-using-a-designer?view=visualstudio))
- **.NET 8 SDK** và **SQL Server Express LocalDB**. LocalDB phù hợp để mỗi thành viên chạy thử; người dùng SQL Server Express/Developer có thể theo mục 7.
- Git hoặc file ZIP của repository. Cần kết nối Internet ở lần restore NuGet đầu.

Mở **Visual Studio Installer → Modify** nếu thiếu workload. Có thể kiểm tra SDK bằng `dotnet --list-sdks` và LocalDB bằng `sqllocaldb info` trong Terminal/PowerShell. Instance mặc định cần thấy tên `MSSQLLocalDB`.

## 2. Tải và mở dự án

```powershell
git clone https://github.com/nghiahnc/GundamShop-PRN232-API.git
cd GundamShop-PRN232-API
```

Nếu dùng **Download ZIP**, giải nén rồi mở thư mục đó. Trong Visual Studio chọn **File → Open → Project/Solution**, chọn `GundamShop.slnx`. Đặt `GundamShop.Api` làm **Startup Project** bằng cách nhấp phải dự án trong Solution Explorer → **Set as Startup Project**. Nếu Visual Studio không mở được `.slnx`, cập nhật Visual Studio hoặc mở thư mục dự án bằng **File → Open → Folder**, rồi dùng lệnh Terminal ở mục 5.

Chọn **Build → Build Solution** hoặc nhấp phải Solution → **Restore NuGet Packages**. Nếu restore từ Visual Studio gặp lỗi nguồn gói, mở **View → Terminal** tại thư mục gốc và chạy:

```powershell
dotnet restore GundamShop.slnx --configfile NuGet.Config
```

## 3. Hiểu cấu hình database mặc định

Trong `GundamShop.Api/appsettings.json` đã có:

```json
"ConnectionStrings": {
  "Shop": "Server=(localdb)\\MSSQLLocalDB;Database=GundamShopDb;Trusted_Connection=True;TrustServerCertificate=True"
}
```

Hai dấu `\\` là cách ghi một dấu `\` trong JSON. Khi nhập **Server name** trong Visual Studio, gõ **`(localdb)\MSSQLLocalDB`**. Tên database là **`GundamShopDb`**. `Trusted_Connection=True` dùng tài khoản Windows hiện tại, không cần tài khoản `sa` hay mật khẩu.

**Không cần tự tạo bảng hoặc nhập file `.mdf`.** EF migration sẽ tạo database và bảng đúng với source code. Visual Studio SQL Server Object Explorer dùng để **xem/kiểm tra** database sau đó.

## 4. Kết nối LocalDB trong Visual Studio

1. Mở **View → SQL Server Object Explorer** (phím tắt thường là `Ctrl+\`, rồi `Ctrl+S`).
2. Chọn **Add SQL Server** ở thanh công cụ hoặc nhấp phải nút **SQL Server → Add SQL Server**.
3. Nhập **Server name:** `(localdb)\MSSQLLocalDB`; chọn **Windows Authentication**; bấm **Connect**.
4. Mở nút server → **Databases**. Nếu chưa thấy `GundamShopDb`, làm mục 5 trước rồi nhấp phải **Databases → Refresh**.

SQL Server Object Explorer hiển thị các instance và database theo dạng cây trong Visual Studio. ([Microsoft Learn](https://learn.microsoft.com/en-us/sql/ssdt/how-to-connect-to-a-database-and-browse-existing-objects?view=sql-server-ver17))

## 5. Tạo database và bảng bằng migration

### Cách A: Package Manager Console trong Visual Studio

Mở **Tools → NuGet Package Manager → Package Manager Console**. Gõ:

```powershell
Update-Database -Project GundamShop.Dal -StartupProject GundamShop.Api
```

`GundamShop.Dal` chứa `ShopDbContext` và migration; `GundamShop.Api` chứa cấu hình connection string để EF chạy. Đây là hai tham số khác nhau trong lệnh EF Core. ([Tài liệu EF Core Package Manager Console](https://learn.microsoft.com/en-us/ef/core/cli/powershell))

### Cách B: Terminal của Visual Studio

Trong **View → Terminal**, đứng ở thư mục có `GundamShop.slnx` rồi chạy:

```powershell
dotnet ef database update --project GundamShop.Dal --startup-project GundamShop.Api
```

Nếu máy báo không có `dotnet ef`, cài công cụ EF Core 8 bằng `dotnet tool install --global dotnet-ef --version 8.0.30`, mở lại Terminal và chạy lệnh trên. Chỉ cần chọn **một** trong hai cách A/B; không chạy `Add-Migration` vì migration của nhóm đã nằm trong repository.

Khi xong, ở SQL Server Object Explorer nhấp phải **Databases → Refresh**. Mở `GundamShopDb → Tables`; sẽ thấy các bảng như `dbo.Users`, `dbo.Products`, `dbo.Variants`, `dbo.Orders` và `dbo.__EFMigrationsHistory`.

## 6. Nạp dữ liệu mẫu và chạy API

Migration tạo **cấu trúc bảng**. Dữ liệu mẫu (admin, danh mục HG/MG, sản phẩm và SKU) được thêm bởi API khi chạy ở môi trường **Development** với `Database:AutoMigrate=true`.

Trong **View → Terminal** của Visual Studio, tại thư mục gốc:

```powershell
dotnet user-secrets set "Database:AutoMigrate" "true" --project GundamShop.Api
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project GundamShop.Api --launch-profile http
```

Lệnh `user-secrets` lưu thiết lập trên **máy cá nhân**, không ghi vào Git. Từ lần sau có thể bấm **F5** với profile `http` hoặc `https` của `GundamShop.Api`; profile đã đặt môi trường Development. Swagger sẽ mở tại `http://localhost:5026/swagger` với profile `http` (hoặc URL `https` được Visual Studio hiển thị). Tài khoản demo: `admin@gundam.local` / `Admin123!`; chỉ dùng để học và thử API.

Sau lần chạy đầu, trong SQL Server Object Explorer nhấp phải `GundamShopDb → Tables → dbo.Products` rồi chọn **View Data** để thấy sản phẩm mẫu. Có thể kiểm tra migration bằng **New Query**:

```sql
SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT TOP (5) Name, Slug FROM dbo.Products;
```

Nếu `dbo.Products` trống, hãy chắc chắn đã đặt `Database:AutoMigrate=true` và chạy API ở **Development**. Không cần tự `INSERT` vào các bảng để tạo dữ liệu demo.

## 7. Nếu dùng SQL Server Express/Developer thay vì LocalDB

Giữ source chung, nhưng mỗi người thay connection string bằng **User Secrets** của mình. Ví dụ Windows Authentication với instance `SQLEXPRESS`:

```powershell
dotnet user-secrets set "ConnectionStrings:Shop" "Server=.\SQLEXPRESS;Database=GundamShopDb;Trusted_Connection=True;TrustServerCertificate=True" --project GundamShop.Api
```

Nếu dùng SQL Server Authentication, thay `Trusted_Connection=True` bằng `User Id=<USER>;Password=<PASSWORD>` trong lệnh của **máy cá nhân**; không đưa mật khẩu vào `appsettings.json` hoặc commit lên Git. Trong SQL Server Object Explorer, kết nối tới đúng **Server name** và kiểu xác thực tương ứng. Sau đó chạy lại lệnh `Update-Database` ở mục 5. Database nằm trên server được ghi trong connection string, vì thế phải mở **đúng server** trong Object Explorer mới thấy `GundamShopDb`.

User Secrets được ASP.NET Core nạp trong môi trường Development. Nếu `Update-Database` chạy ở môi trường khác hoặc không đọc được cấu hình cá nhân, đặt `ASPNETCORE_ENVIRONMENT=Development` trong Terminal/Package Manager Console rồi chạy lại.

## 8. Khi pull code mới hoặc gặp lỗi

- Sau `git pull`, chạy lại `Update-Database -Project GundamShop.Dal -StartupProject GundamShop.Api` nếu nhóm thêm migration. EF chỉ áp dụng migration chưa có trong `dbo.__EFMigrationsHistory`.
- **Không thấy `GundamShopDb`:** refresh nút Databases; kiểm tra server đang mở là `(localdb)\MSSQLLocalDB` và lệnh migration đã chạy thành công.
- **Không kết nối được LocalDB:** chạy `sqllocaldb info`, rồi `sqllocaldb start MSSQLLocalDB`; kiểm tra đã cài SQL Server Express LocalDB.
- **Login failed / Cannot open database:** kiểm tra connection string, server name và chế độ Windows/SQL Authentication; với SQL Server riêng, tài khoản phải có quyền tạo/cập nhật database.
- **No DbContext was found / startup project:** dùng đúng `-Project GundamShop.Dal -StartupProject GundamShop.Api`, restore NuGet và build solution trước.
- **Có bảng nhưng không có admin/sản phẩm mẫu:** migration chỉ tạo schema; chạy API một lần theo mục 6.
- **Không sửa bảng thủ công trong Visual Studio** để khớp code. Khi thay entity/quan hệ, người phụ trách DAL tạo migration mới rồi cả nhóm pull và `Update-Database`.

Đọc thêm [hướng dẫn dev](docs/DEVELOPER_GUIDE.md) để thử endpoint, JWT, OData và thanh toán mock.
