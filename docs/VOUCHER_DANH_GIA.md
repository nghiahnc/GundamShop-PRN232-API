# Voucher và Đánh giá (nghiath)

Tài liệu cho phần Voucher (UC39–43) và Đánh giá (UC44–48) trong backlog `GundamShop_Backlog_Balanced_v2.xlsx`, người phụ trách **nghiath**. Trong file phân công cũ của repo, phần này thuộc Thành viên 5. Đọc cùng [hướng dẫn dev](DEVELOPER_GUIDE.md).

## 1. Phạm vi và code nằm ở đâu

| Lớp | File | Nội dung |
| --- | --- | --- |
| API | `GundamShop.Api/ShopRoutes.cs` | Nhóm `/api/v1/vouchers` (Swagger **5 - Voucher**), `/api/v1/reviews` (**5 - Đánh giá**) và `GET /api/v1/products/{id}/reviews` |
| API | `GundamShop.Api/Infrastructure.cs` | `VoucherRequestExampleFilter`: body mẫu hợp lệ cho POST/PUT voucher trong Swagger |
| BLL | `GundamShop.Bll/PromotionService.cs` | `VoucherRules` (quy tắc dùng voucher, cách tính tiền giảm) và `PromotionService` (CRUD voucher, đánh giá) |
| BLL | `GundamShop.Bll/Common.cs` | `VoucherRequest`, `VoucherDto`, `ReviewRequest`, `ReviewUpdateRequest`, `ReviewDto`, `ReviewPage` |
| DAL | `GundamShop.Dal/Entities.cs` | `Voucher` (có `DiscountPercent`), `Review` |
| DAL | `GundamShop.Dal/ShopDbContext.cs` | Mã voucher duy nhất, một đánh giá cho mỗi khách và sản phẩm, các CHECK constraint, đọc ngày giờ là UTC |
| DAL | `GundamShop.Dal/Migrations/*_VoucherPercentAndRules.cs` | Thêm cột `DiscountPercent` và CHECK constraint |
| Test | `tests/vouchers-reviews.ps1` | Kiểm thử riêng phần này |

## 2. Endpoint

| UC | Method | Endpoint | Quyền | Việc làm | Mã lỗi chính |
| --- | --- | --- | --- | --- | --- |
| UC39 | GET | `/api/v1/vouchers` | Admin | Danh sách, lọc `search` (mã) và `status` | 400 `status` sai |
| UC40 | GET | `/api/v1/vouchers/{id}` | Admin | Chi tiết: hạn dùng, giảm cố định hoặc %, điều kiện, lượt đã dùng, `status` | 404 |
| UC41 | POST | `/api/v1/vouchers` | Admin | Phát hành voucher, trả 201 và `Location: /api/v1/vouchers/{id}` | 400 dữ liệu sai, 409 trùng mã |
| UC42 | PUT | `/api/v1/vouchers/{id}` | Admin | Sửa hạn dùng, số lượt, mức giảm; bật lại bằng `isActive: true` | 400, 404, 409 trùng mã, 409 `maxUses` nhỏ hơn số lượt đã dùng |
| UC43 | DELETE | `/api/v1/vouchers/{id}` | Admin | Ngừng voucher (`isActive = false`), không xóa dòng để giữ lịch sử đơn | 404 |
| UC44 | GET | `/api/v1/products/{id}/reviews` | Public | Đánh giá của sản phẩm, mới nhất trước, kèm điểm trung bình và số lượng từng mức sao; lọc `rating` | 400 `rating` sai, 404 sản phẩm không có hoặc đã ẩn |
| UC45 | GET | `/api/v1/reviews/{id}` | Public | Chi tiết một đánh giá | 404 |
| UC46 | POST | `/api/v1/reviews` | Customer | Đánh giá sản phẩm đã nhận, trả 201 và `Location: /api/v1/reviews/{id}` | 400, 403 chưa nhận hàng, 409 đã đánh giá |
| UC47 | PUT | `/api/v1/reviews/{id}` | Chủ đánh giá | Sửa số sao và nội dung của mình | 400, 404 không phải của mình |
| UC48 | DELETE | `/api/v1/reviews/{id}` | Chủ hoặc Admin | Xóa đánh giá | 404 |

Mọi danh sách trả `{ items, pageNumber, pageSize, totalCount }`, `pageSize` tối đa 100. Danh sách đánh giá có thêm `averageRating` (làm tròn 1 chữ số, `null` khi chưa có đánh giá) và `ratingCounts` (`{"1": 0, ..., "5": 3}`). Hai số này tính trên mọi đánh giá của sản phẩm, không đổi khi lọc `rating`.

## 3. Quy tắc voucher

- **Mã**: chỉ gồm chữ, số, `-`, `_`, tối đa 64 ký tự. API cắt khoảng trắng đầu/cuối rồi đổi sang chữ hoa, nên ` sale10 ` và `SALE10` là một mã. Không có hai voucher trùng mã, kể cả hai request tạo cùng lúc.
- **Mức giảm**:
  - `discountPercent` để trống hoặc `0`: giảm cố định `discountVnd`.
  - `discountPercent` 1–100: giảm theo % tạm tính, làm tròn xuống, **tối đa** `discountVnd`. Ví dụ `discountPercent = 10`, `discountVnd = 20000`: đơn 300.000 được giảm 20.000 (10% là 30.000 nhưng chạm mức tối đa), đơn 150.000 được giảm 15.000.
  - Không bao giờ giảm quá tạm tính.
- **Mỗi khách dùng mỗi voucher một lần.** Khách đã có đơn dùng mã này mà đơn chưa hủy thì nhận 409 "Bạn đã dùng voucher này.". Đơn đã hủy, hết hạn hoặc thanh toán thất bại đã trả lại lượt, nên khách dùng lại được.
- **Giới hạn**: `discountVnd` 1–1.000.000.000, `minSubtotalVnd` 0–1.000.000.000, `maxUses` 1–1.000.000.
- **Thời gian (UTC)**: `startsAtUtc` phải trước `endsAtUtc`.
  - Ngày giờ không kèm múi giờ (`2026-12-01T00:00:00`) được hiểu là UTC. Kèm múi giờ (`2026-12-01T07:00:00+07:00`) thì đổi sang UTC.
  - Kết quả không phụ thuộc múi giờ của máy chạy API, và API luôn trả về dạng có `Z`.
  - Voucher **mới** phải kết thúc ở tương lai. Khi **sửa**, admin được đặt thời gian về quá khứ để kết thúc sớm.
- **Trạng thái** `status`, tính tại lúc đọc, xét theo thứ tự:
  1. `Disabled`: đã ngừng.
  2. `Expired`: đã hết hạn.
  3. `Scheduled`: chưa tới ngày bắt đầu.
  4. `UsedUp`: hết lượt.
  5. `Active`: dùng được.
- **Ngừng và bật lại**: DELETE chỉ ngừng voucher. PUT có `isActive: true` bật lại, `false` ngừng.
- **Sửa (PUT)**: hai trường không gửi thì giữ nguyên.
  - Không gửi `isActive`: giữ trạng thái bật/ngừng.
  - Không gửi `discountPercent`: giữ cách giảm. Gửi `0` để chuyển voucher % sang giảm cố định.
  - Nhờ vậy, client chỉ gia hạn ngày hết hạn (UC42) không vô tình biến voucher "10% tối đa 50.000" thành "giảm thẳng 50.000".
- **Số lượt**: không hạ `maxUses` xuống dưới số lượt đã dùng (409).
- **Database** có CHECK: `DiscountVnd > 0`, `DiscountPercent` rỗng hoặc 1–100, `MinSubtotalVnd >= 0`, `0 <= UsedCount <= MaxUses`, `StartsAtUtc < EndsAtUtc`.

## 4. Lượt dùng voucher thay đổi ở đâu

| Sự kiện | Ai phụ trách | `usedCount` |
| --- | --- | --- |
| Khách đặt hàng có voucher (`POST /orders`) | dattg, gọi `VoucherRules.Apply` | +1, trong cùng transaction SERIALIZABLE với đơn |
| Hủy đơn chưa trả, đơn quá 24 giờ chưa trả | dattg | −1 |
| Thanh toán thất bại | tainh | −1 |
| Đơn đã trả | | Không trả lại lượt |

Khi đặt hàng, `VoucherRules.Apply` báo đúng lý do voucher không dùng được (409):

| Trường hợp | Thông báo |
| --- | --- |
| Đã ngừng | Voucher đã ngừng sử dụng. |
| Chưa tới ngày | Voucher chưa đến thời gian sử dụng. |
| Hết hạn | Voucher đã hết hạn. |
| Hết lượt | Voucher đã hết lượt sử dụng. |
| Khách đã dùng mã này (đơn chưa hủy) | Bạn đã dùng voucher này. |
| Chưa đủ giá trị đơn | Đơn hàng cần tối thiểu 500.000 VND để dùng voucher này. |

Mã không tồn tại trả 404 "Không tìm thấy voucher.".

## 5. Quy tắc đánh giá

- Chỉ khách có đơn **Delivered** chứa sản phẩm mới được đánh giá (403 nếu chưa nhận hàng).
- Mỗi khách một đánh giá cho mỗi sản phẩm. Unique index `(UserId, ProductId)` chặn cả hai request gửi cùng lúc, request sau nhận 409 "Bạn đã đánh giá sản phẩm.". Xóa đánh giá thì được viết lại.
- `rating` 1–5 (database cũng có CHECK). `comment` bắt buộc, tối đa 1000 ký tự, được cắt khoảng trắng đầu/cuối.
- Chỉ chủ đánh giá được sửa (admin cũng không sửa hộ). Chủ hoặc admin được xóa, xóa hẳn khỏi database.
- `reviewerName` lấy từ họ tên tài khoản, để app hiển thị người viết.
- Sản phẩm đã ngừng bán hoặc nằm trong danh mục đã ẩn: khách không xem danh sách hay chi tiết đánh giá (404), không viết thêm (400) và không sửa đánh giá cũ (409), nhưng vẫn xóa được đánh giá của mình. Admin vẫn xem để kiểm duyệt.

## 6. Điểm kỹ thuật nên giải thích khi vấn đáp

**Một chỗ cho quy tắc voucher.** `VoucherRules.Apply` gom mọi điều kiện (đang bật, trong thời hạn, còn lượt, khách chưa dùng, đủ giá trị đơn) và cách tính tiền giảm (cố định hoặc %, có mức tối đa). `VoucherRules.UsedBy` kiểm tra khách đã có đơn dùng mã này chưa, bỏ qua đơn đã trả lại lượt. `Checkout` chỉ gọi một dòng, nên đổi quy tắc voucher không phải sửa code đặt hàng. Tiền giảm tính bằng `decimal` để không tràn số với đơn lớn.

**Giờ UTC.** SQL Server `datetime2` không lưu múi giờ. Vì vậy API chuẩn hóa mọi ngày giờ gửi lên về UTC trước khi kiểm tra và lưu. Khi đọc, value converter đánh dấu giá trị là UTC để JSON có `Z`. Trước đây, chạy API trên máy Windows đặt giờ Việt Nam thì `2030-01-01T00:00:00` bị lưu thành `2029-12-31T17:00:00`, voucher mở sớm 7 tiếng.

**Chạy song song.** Kiểm tra trùng bằng `AnyAsync` rồi mới `Add` vẫn có khe hở khi hai request đến cùng lúc. Unique index trong database là chốt chặn cuối. Code bắt lỗi SQL 2601/2627 để trả 409 tiếng Việt thay vì thông báo tiếng Anh của EF. Tương tự, CHECK `UsedCount <= MaxUses` chặn trường hợp admin hạ `maxUses` đúng lúc có khách vừa dùng voucher (lỗi SQL 547, trả 409).

## 7. Demo nhanh bằng Swagger

1. Chạy API theo README, mở `/swagger`, gọi `POST /api/v1/auth/login` với `admin@gundam.local` / `Admin123!`, bấm **Authorize** và dán `accessToken`.
2. `POST /api/v1/vouchers`: body mẫu có sẵn (mã `SALE10`, giảm 50.000 cho đơn từ 300.000). Đổi `"discountPercent": 10` để có voucher giảm 10% tối đa 50.000.
3. `GET /api/v1/vouchers?status=Active` và `GET /api/v1/vouchers/{id}` để xem `status`, `usedCount`.
4. Đặt hàng với `voucherCode` theo [mục 4 của hướng dẫn dev](DEVELOPER_GUIDE.md#4-thử-một-đơn-hàng-từ-đầu-đến-cuối). Xem `discountVnd` trong đơn, rồi xem `usedCount` của voucher tăng 1.
5. Sau khi đơn `Delivered`, khách gọi `POST /api/v1/reviews`. Xem `GET /api/v1/products/{id}/reviews` có `averageRating`, `ratingCounts`; thêm `?rating=5` để lọc.

## 8. Kiểm thử

Khi API chạy ở `http://127.0.0.1:5288` trong Development (script dùng endpoint thanh toán mock), mở PowerShell 7 (`pwsh`) tại thư mục gốc:

```powershell
./tests/vouchers-reviews.ps1
./tests/smoke.ps1
```

Kết quả đúng là dòng `PASS: ...` ở cuối mỗi script. `vouchers-reviews.ps1` thử tạo/sửa/ngừng/bật lại voucher, giảm cố định và theo %, PUT giữ nguyên cách giảm, từng lý do voucher bị từ chối khi đặt hàng, mỗi khách một lần, lượt dùng được trả lại khi hủy đơn, lọc `status`, tạo trùng mã song song. Phần đánh giá thử quyền, chưa nhận hàng, trùng đánh giá song song, tổng hợp số sao, sản phẩm đã ẩn.

## 9. Sau khi pull code này

Code có migration mới `VoucherPercentAndRules`. Mỗi thành viên chạy lại một lần:

```powershell
dotnet ef database update --project GundamShop.Dal --startup-project GundamShop.Api
```

Hoặc trong Package Manager Console của Visual Studio: `Update-Database -Project GundamShop.Dal -StartupProject GundamShop.Api`. Nếu dùng `Database__AutoMigrate=true` ở Development, API tự áp migration khi chạy.

## 10. Chỗ đụng tới code dùng chung

- `ShoppingService.Checkout` (dattg): thay 3 dòng kiểm tra voucher bằng `VoucherRules.Apply(...)`, có thêm điều kiện mỗi khách dùng mỗi voucher một lần. Tổng tiền đơn với voucher cố định không đổi.
- `ApiExceptionMiddleware`: JSON sai cú pháp hoặc giá trị không đọc được (ví dụ ngày giờ `"abc"`) trả 400 thay vì 500. Lỗi trả `Content-Type: application/problem+json; charset=utf-8`.
- `PageHelper`: `page` quá lớn trả 400 thay vì 500.
