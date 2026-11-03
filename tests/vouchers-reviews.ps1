#Requires -Version 7
param([string]$Base = 'http://127.0.0.1:5288')
$ErrorActionPreference = 'Stop'
function Api($Method, $Path, $Body = $null, $Token = $null) {
    $request = @{ Method = $Method; Uri = "$Base$Path"; ContentType = 'application/json; charset=utf-8' }
    if ($Token) { $request.Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 12 -Compress) }
    Invoke-RestMethod @request
}
function Call($Method, $Path, $Body = $null, $Token = $null, [string]$RawBody = $null) {
    $request = @{ Method = $Method; Uri = "$Base$Path"; ContentType = 'application/json; charset=utf-8'; SkipHttpErrorCheck = $true }
    if ($Token) { $request.Headers = @{ Authorization = "Bearer $Token" } }
    if ($RawBody) { $request.Body = $RawBody } elseif ($null -ne $Body) { $request.Body = ($Body | ConvertTo-Json -Depth 12 -Compress) }
    Invoke-WebRequest @request
}
function Check($Condition, $Message) { if (-not $Condition) { throw $Message } }
function Status($Response) { [int]$Response.StatusCode }
# pwsh trả nội dung application/problem+json dưới dạng byte[], nên tự giải mã UTF-8.
function Text($Response) { if ($Response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($Response.Content) } else { $Response.Content } }
function Title($Response) { (Text $Response | ConvertFrom-Json).title }
function Iso([DateTime]$Value) { $Value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }

$suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
$now = [DateTime]::UtcNow
$admin = (Api POST '/api/v1/auth/login' @{ email='admin@gundam.local'; password='Admin123!' }).accessToken
function VoucherBody($Code, $Extra = @{}) {
    $body = @{ code=$Code; discountVnd=20000; minSubtotalVnd=100000; maxUses=5; startsAtUtc=(Iso $now.AddHours(-1)); endsAtUtc=(Iso $now.AddDays(7)) }
    foreach ($key in $Extra.Keys) { $body[$key] = $Extra[$key] }
    $body
}
function NewVoucher($Code, $Extra = @{}) { Api POST '/api/v1/vouchers' (VoucherBody $Code $Extra) $admin }

# Dữ liệu chung: một sản phẩm 150.000 VND và một khách có địa chỉ
$category = Api POST '/api/v1/categories' @{ name="Voucher Test $suffix"; slug="voucher-test-$suffix"; description='nghiath' } $admin
$product = Api POST '/api/v1/products' @{ categoryId=$category.id; name="Review Gundam $suffix"; slug="review-gundam-$suffix"; grade='HG'; scale='1/144'; description='nghiath'; imageUrl=$null } $admin
$variant = Api POST '/api/v1/variants' @{ productId=$product.id; sku="VR-$suffix"; label='Standard'; priceVnd=150000; initialStock=100 } $admin
function NewCustomer($Name) {
    $token = (Api POST '/api/v1/auth/register' @{ email="$Name-$suffix@example.com"; password='Buyer123!'; fullName=$Name }).accessToken
    $address = Api POST '/api/v1/addresses' @{ recipient=$Name; phone='0900000000'; line1='1 Test'; ward='W'; district='D'; province='P'; isDefault=$true } $token
    @{ token=$token; addressId=$address.id }
}
function ClearCart($Customer) {
    $items = Api GET '/api/v1/cart/items' $null $Customer.token
    foreach ($item in $items) { if ($item.id) { $null = Api DELETE "/api/v1/cart/items/$($item.id)" $null $Customer.token } }
}
# Đặt hàng với voucher; giỏ được làm trống trước và sau để lần sau không bị 409 "SKU đã có trong giỏ".
function Checkout($Customer, $Code, $Quantity = 1) {
    ClearCart $Customer
    $null = Api POST '/api/v1/cart/items' @{ variantId=$variant.id; quantity=$Quantity } $Customer.token
    $response = Call POST '/api/v1/orders' @{ addressId=$Customer.addressId; voucherCode=$Code } $Customer.token
    ClearCart $Customer
    $response
}
function Deliver($Customer) {
    $order = (Checkout $Customer $null 1).Content | ConvertFrom-Json
    $null = Api POST "/api/v1/payments/mock/orders/$($order.id)/complete" @{ succeeded=$true } $Customer.token
    $null = Api PUT "/api/v1/orders/$($order.id)/status" @{ status='Shipped' } $admin
    $null = Api PUT "/api/v1/orders/$($order.id)/status" @{ status='Delivered' } $admin
}
$buyer = NewCustomer 'Khach A'

# ===== Voucher: tạo mới =====
$created = Call POST '/api/v1/vouchers' (VoucherBody "  sale-$suffix ") $admin
Check ((Status $created) -eq 201) 'Create voucher must return 201'
$voucher = $created.Content | ConvertFrom-Json
Check ($created.Headers.Location -eq "/api/v1/vouchers/$($voucher.id)") 'Location must point to the new voucher'
Check ($voucher.code -ceq "SALE-$($suffix.ToUpperInvariant())") 'Code must be trimmed and upper-cased'
Check ($voucher.status -eq 'Active' -and $null -eq $voucher.discountPercent) 'New fixed voucher must be Active'
Check ((Call GET "/api/v1/vouchers/$($voucher.id)" $null $admin).Content -match '"startsAtUtc":"[^"]+Z"') 'Voucher dates must end with Z (UTC)'
$noZone = (Call POST '/api/v1/vouchers' (VoucherBody "NOZONE-$suffix" @{ startsAtUtc='2030-01-01T00:00:00'; endsAtUtc='2030-02-01T00:00:00' }) $admin).Content
Check ($noZone -match '"startsAtUtc":"2030-01-01T00:00:00Z"') 'Date without time zone must be stored as UTC'

# Validation
Check ((Status (Call POST '/api/v1/vouchers' (VoucherBody "BAD CODE $suffix") $admin)) -eq 400) 'Code with spaces must be 400'
Check ((Status (Call POST '/api/v1/vouchers' (VoucherBody "PCT%-$suffix") $admin)) -eq 400) 'Code with symbols must be 400'
Check ((Status (Call POST '/api/v1/vouchers' @{ code="NODATE-$suffix"; discountVnd=1000; minSubtotalVnd=0; maxUses=1 } $admin)) -eq 400) 'Missing dates must be 400'
Check ((Status (Call POST '/api/v1/vouchers' (VoucherBody "BACK-$suffix" @{ startsAtUtc=(Iso $now.AddDays(3)); endsAtUtc=(Iso $now.AddDays(2)) }) $admin)) -eq 400) 'End before start must be 400'
Check ((Status (Call POST '/api/v1/vouchers' (VoucherBody "OLD-$suffix" @{ startsAtUtc=(Iso $now.AddDays(-9)); endsAtUtc=(Iso $now.AddDays(-2)) }) $admin)) -eq 400) 'New voucher that already ended must be 400'
Check ((Status (Call POST '/api/v1/vouchers' (VoucherBody "ZERO-$suffix" @{ discountVnd=0 }) $admin)) -eq 400) 'Discount 0 must be 400'
Check ((Status (Call POST '/api/v1/vouchers' (VoucherBody "P101-$suffix" @{ discountPercent=101 }) $admin)) -eq 400) 'Percent over 100 must be 400'
$badDate = Call POST '/api/v1/vouchers' $null $admin ('{"code":"DATE-' + $suffix + '","discountVnd":1000,"minSubtotalVnd":0,"maxUses":1,"startsAtUtc":"abc","endsAtUtc":"abc"}')
Check ((Status $badDate) -eq 400 -and $badDate.Headers.'Content-Type' -match 'application/problem\+json') 'Unreadable date must be 400, not 500'
Check ((Status (Call POST '/api/v1/vouchers' (VoucherBody "sale-$suffix") $admin)) -eq 409) 'Duplicate code (any case) must be 409'

# Tạo cùng một mã song song: đúng 1 request thành công, còn lại 409 tiếng Việt
$raceBody = VoucherBody "RACE-$suffix" | ConvertTo-Json
$dups = 1..8 | ForEach-Object -Parallel {
    $r = Invoke-WebRequest -Method Post -Uri "$($using:Base)/api/v1/vouchers" -Headers @{ Authorization = "Bearer $($using:admin)" } -ContentType 'application/json; charset=utf-8' -Body $using:raceBody -SkipHttpErrorCheck
    $text = if ($r.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($r.Content) } else { $r.Content }
    if ([int]$r.StatusCode -eq 201) { '201' } else { "$([int]$r.StatusCode) $(($text | ConvertFrom-Json).title)" }
} -ThrottleLimit 8
Check (($dups | Where-Object { $_ -eq '201' }).Count -eq 1 -and ($dups | Where-Object { $_ -eq '409 Mã voucher đã tồn tại.' }).Count -eq 7) "Parallel duplicate voucher: $($dups -join ', ')"

# Phân quyền
$customerToken = $buyer.token
Check ((Status (Call GET '/api/v1/vouchers' $null $customerToken)) -eq 403) 'Customer must not list vouchers'
Check ((Status (Call POST '/api/v1/vouchers' (VoucherBody "HACK-$suffix") $customerToken)) -eq 403) 'Customer must not create vouchers'
Check ((Status (Call GET '/api/v1/vouchers')) -eq 401) 'Anonymous must not list vouchers'

# ===== Voucher: dùng khi đặt hàng =====
$fixed = $voucher
$order = (Checkout $buyer $fixed.code 2).Content | ConvertFrom-Json
Check ($order.discountVnd -eq 20000 -and $order.totalVnd -eq 280000) 'Fixed voucher must take 20.000 off 300.000'
Check ((Api GET "/api/v1/vouchers/$($fixed.id)" $null $admin).usedCount -eq 1) 'Checkout must count one use'
$null = Api POST "/api/v1/orders/$($order.id)/cancel" $null $buyer.token
Check ((Api GET "/api/v1/vouchers/$($fixed.id)" $null $admin).usedCount -eq 0) 'Cancelling the order must give the use back'

$capped = NewVoucher "PCT-$suffix" @{ discountPercent=10; discountVnd=20000 }
Check ($capped.discountPercent -eq 10) 'Percent voucher must keep discountPercent'
$order = (Checkout $buyer $capped.code 2).Content | ConvertFrom-Json
Check ($order.discountVnd -eq 20000) '10% of 300.000 must be capped at 20.000'
$null = Api POST "/api/v1/orders/$($order.id)/cancel" $null $buyer.token
$percent = NewVoucher "PCT15-$suffix" @{ discountPercent=15; discountVnd=1000000 }
$order = (Checkout $buyer $percent.code 1).Content | ConvertFrom-Json
Check ($order.discountVnd -eq 22500 -and $order.totalVnd -eq 127500) '15% of 150.000 must be 22.500'
$null = Api POST "/api/v1/orders/$($order.id)/cancel" $null $buyer.token

$once = NewVoucher "ONCE-$suffix" @{ maxUses=1 }
$order = (Checkout $buyer $once.code 1).Content | ConvertFrom-Json
Check ((Api GET "/api/v1/vouchers/$($once.id)" $null $admin).status -eq 'UsedUp') 'Voucher with no uses left must be UsedUp'
$usedUp = Checkout $buyer $once.code 1
Check ((Status $usedUp) -eq 409 -and (Title $usedUp) -eq 'Voucher đã hết lượt sử dụng.') 'Used-up voucher must say so'
$lower = VoucherBody $once.code @{ maxUses=1 }
Check ((Status (Call PUT "/api/v1/vouchers/$($once.id)" $lower $admin)) -eq 200) 'MaxUses equal to used count must be allowed'
$null = Api POST "/api/v1/orders/$($order.id)/cancel" $null $buyer.token

$minimum = NewVoucher "MIN-$suffix" @{ minSubtotalVnd=500000 }
$belowMin = Checkout $buyer $minimum.code 1
Check ((Status $belowMin) -eq 409 -and (Title $belowMin) -eq 'Đơn hàng cần tối thiểu 500.000 VND để dùng voucher này.') 'Order below minimum must say the minimum'
$scheduled = NewVoucher "LATER-$suffix" @{ startsAtUtc=(Iso $now.AddDays(1)); endsAtUtc=(Iso $now.AddDays(2)) }
Check ($scheduled.status -eq 'Scheduled') 'Future voucher must be Scheduled'
Check ((Title (Checkout $buyer $scheduled.code 1)) -eq 'Voucher chưa đến thời gian sử dụng.') 'Scheduled voucher must say not started'
$expired = Api PUT "/api/v1/vouchers/$($scheduled.id)" (VoucherBody $scheduled.code @{ startsAtUtc=(Iso $now.AddDays(-3)); endsAtUtc=(Iso $now.AddDays(-1)) }) $admin
Check ($expired.status -eq 'Expired') 'Admin may move an existing voucher into the past (Expired)'
Check ((Title (Checkout $buyer $scheduled.code 1)) -eq 'Voucher đã hết hạn.') 'Expired voucher must say expired'
Check ((Status (Call DELETE "/api/v1/vouchers/$($minimum.id)" $null $admin)) -eq 204) 'Disable voucher must be 204'
Check ((Title (Checkout $buyer $minimum.code 1)) -eq 'Voucher đã ngừng sử dụng.') 'Disabled voucher must say disabled'
Check ((Status (Checkout $buyer "NOPE-$suffix" 1)) -eq 404) 'Unknown voucher code must be 404'

# ===== Voucher: sửa, bật lại, danh sách =====
$disabled = Api GET "/api/v1/vouchers/$($minimum.id)" $null $admin
Check ($disabled.status -eq 'Disabled' -and -not $disabled.isActive) 'Disabled voucher must show Disabled'
$kept = Api PUT "/api/v1/vouchers/$($minimum.id)" (VoucherBody $minimum.code @{ minSubtotalVnd=400000 }) $admin
Check (-not $kept.isActive -and $kept.minSubtotalVnd -eq 400000) 'PUT without isActive must keep the voucher disabled'
Check ((Api PUT "/api/v1/vouchers/$($minimum.id)" (VoucherBody $minimum.code @{ isActive=$true }) $admin).status -eq 'Active') 'PUT isActive=true must re-enable the voucher'
Check ((Status (Call PUT "/api/v1/vouchers/$($minimum.id)" (VoucherBody $fixed.code) $admin)) -eq 409) 'Renaming to an existing code must be 409'
$order = (Checkout $buyer $capped.code 1).Content | ConvertFrom-Json
$tooFew = Call PUT "/api/v1/vouchers/$($capped.id)" (VoucherBody $capped.code @{ discountPercent=10; maxUses=1 }) $admin
Check ((Status $tooFew) -eq 200) 'MaxUses 1 with 1 use must be allowed'
Check ((Status (Call PUT "/api/v1/vouchers/$($fixed.id)" (VoucherBody $fixed.code @{ maxUses=0 }) $admin)) -eq 400) 'MaxUses 0 must be 400'
$null = Api POST "/api/v1/orders/$($order.id)/cancel" $null $buyer.token
Check ((Status (Call PUT "/api/v1/vouchers/$([guid]::NewGuid())" (VoucherBody "GHOST-$suffix") $admin)) -eq 404) 'Updating unknown voucher must be 404'
Check ((Status (Call GET "/api/v1/vouchers/$([guid]::NewGuid())" $null $admin)) -eq 404) 'Unknown voucher must be 404'
Check ((Status (Call DELETE "/api/v1/vouchers/$([guid]::NewGuid())" $null $admin)) -eq 404) 'Deleting unknown voucher must be 404'

$search = Api GET "/api/v1/vouchers?search=pct-$suffix&pageSize=100" $null $admin
Check ($search.totalCount -eq 1 -and $search.items[0].id -eq $capped.id) 'Search by code must find only that voucher'
$active = (Api GET "/api/v1/vouchers?search=$suffix&status=active&pageSize=100" $null $admin).items.code
Check (($active -contains $capped.code) -and -not ($active -contains $scheduled.code)) 'status=Active filter is wrong'
Check (((Api GET "/api/v1/vouchers?search=$suffix&status=Expired&pageSize=100" $null $admin).items.code) -contains $scheduled.code) 'status=Expired filter is wrong'
Check ((Status (Call GET '/api/v1/vouchers?status=abc' $null $admin)) -eq 400) 'Unknown status must be 400'
Check ((Status (Call GET '/api/v1/vouchers?page=2147483647&pageSize=100' $null $admin)) -eq 400) 'Huge page must be 400, not 500'

# ===== Đánh giá =====
$buyerB = NewCustomer 'Khach B'
$stranger = NewCustomer 'Nguoi La'
Deliver $buyer
Deliver $buyerB
Check ((Status (Call POST '/api/v1/reviews' @{ productId=$product.id; rating=5; comment='Chưa mua' } $stranger.token)) -eq 403) 'Review without a delivered order must be 403'
Check ((Status (Call POST '/api/v1/reviews' @{ productId=$product.id; rating=5; comment='x' })) -eq 401) 'Anonymous review must be 401'
Check ((Status (Call POST '/api/v1/reviews' @{ productId=$product.id; rating=0; comment='x' } $buyer.token)) -eq 400) 'Rating 0 must be 400'
Check ((Status (Call POST '/api/v1/reviews' @{ productId=$product.id; rating=6; comment='x' } $buyer.token)) -eq 400) 'Rating 6 must be 400'
Check ((Status (Call POST '/api/v1/reviews' @{ productId=$product.id; rating=4; comment='   ' } $buyer.token)) -eq 400) 'Blank comment must be 400'
Check ((Status (Call POST '/api/v1/reviews' @{ productId=[guid]::NewGuid(); rating=4; comment='x' } $buyer.token)) -eq 400) 'Review of unknown product must be 400'

$createdReview = Call POST '/api/v1/reviews' @{ productId=$product.id; rating=5; comment='  Mô hình đẹp, khớp chắc  ' } $buyer.token
Check ((Status $createdReview) -eq 201) 'Create review must return 201'
$review = $createdReview.Content | ConvertFrom-Json
Check ($createdReview.Headers.Location -eq "/api/v1/reviews/$($review.id)") 'Location must point to the new review'
Check ($review.comment -eq 'Mô hình đẹp, khớp chắc' -and $review.reviewerName -eq 'Khach A') 'Review must be trimmed and show reviewer name'
Check ($createdReview.Content -match '"createdAtUtc":"[^"]+Z"') 'createdAtUtc must end with Z (UTC)'
Check ((Status (Call POST '/api/v1/reviews' @{ productId=$product.id; rating=4; comment='Lần hai' } $buyer.token)) -eq 409) 'Second review of the same product must be 409'

# Khách B gửi đánh giá song song: đúng 1 thành công, còn lại 409 tiếng Việt
$bToken = $buyerB.token
$productId = $product.id
$reviewDups = 1..6 | ForEach-Object -Parallel {
    $body = @{ productId=$using:productId; rating=3; comment="Tạm ổn $_" } | ConvertTo-Json
    $r = Invoke-WebRequest -Method Post -Uri "$($using:Base)/api/v1/reviews" -Headers @{ Authorization = "Bearer $($using:bToken)" } -ContentType 'application/json; charset=utf-8' -Body $body -SkipHttpErrorCheck
    $text = if ($r.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($r.Content) } else { $r.Content }
    if ([int]$r.StatusCode -eq 201) { '201' } else { "$([int]$r.StatusCode) $(($text | ConvertFrom-Json).title)" }
} -ThrottleLimit 6
Check (($reviewDups | Where-Object { $_ -eq '201' }).Count -eq 1 -and ($reviewDups | Where-Object { $_ -eq '409 Bạn đã đánh giá sản phẩm.' }).Count -eq 5) "Parallel duplicate review: $($reviewDups -join ', ')"

# Danh sách và tổng hợp số sao
$list = Api GET "/api/v1/products/$($product.id)/reviews"
Check ($list.totalCount -eq 2 -and $list.averageRating -eq 4 -and $list.ratingCounts.'5' -eq 1 -and $list.ratingCounts.'3' -eq 1 -and $list.ratingCounts.'1' -eq 0) 'Rating summary is wrong'
$fives = Api GET "/api/v1/products/$($product.id)/reviews?rating=5"
Check ($fives.totalCount -eq 1 -and $fives.items[0].id -eq $review.id -and $fives.averageRating -eq 4) 'rating filter must filter items but keep the summary'
Check ((Status (Call GET "/api/v1/products/$($product.id)/reviews?rating=9")) -eq 400) 'rating outside 1..5 must be 400'
Check ((Status (Call GET "/api/v1/products/$($product.id)/reviews?page=2147483647&pageSize=100")) -eq 400) 'Huge page must be 400, not 500'
Check ((Status (Call GET "/api/v1/products/$([guid]::NewGuid())/reviews")) -eq 404) 'Reviews of unknown product must be 404'
Check ((Api GET "/api/v1/reviews/$($review.id)").reviewerName -eq 'Khach A') 'Review detail failed'
Check ((Status (Call GET "/api/v1/reviews/$([guid]::NewGuid())")) -eq 404) 'Unknown review must be 404'

# Sửa và xóa: chỉ chủ đánh giá sửa được; chủ hoặc admin xóa được
$updated = Api PUT "/api/v1/reviews/$($review.id)" @{ rating=4; comment='Sau một tuần vẫn ổn' } $buyer.token
Check ($updated.rating -eq 4 -and $updated.reviewerName -eq 'Khach A') 'Owner update failed'
Check ((Status (Call PUT "/api/v1/reviews/$($review.id)" @{ rating=1; comment='Sửa hộ' } $buyerB.token)) -eq 404) 'Other customer must not edit the review'
Check ((Status (Call PUT "/api/v1/reviews/$($review.id)" @{ rating=1; comment='Admin sửa' } $admin)) -eq 404) 'Admin must not edit a customer review'
Check ((Status (Call DELETE "/api/v1/reviews/$($review.id)" $null $buyerB.token)) -eq 404) 'Other customer must not delete the review'

# Sản phẩm ngừng bán: khách không xem/viết đánh giá, admin vẫn xem
$null = Api DELETE "/api/v1/products/$($product.id)" $null $admin
Check ((Status (Call GET "/api/v1/products/$($product.id)/reviews")) -eq 404) 'Public must not list reviews of a hidden product'
Check ((Status (Call GET "/api/v1/reviews/$($review.id)")) -eq 404) 'Public must not read a review of a hidden product'
Check ((Api GET "/api/v1/products/$($product.id)/reviews" $null $admin).totalCount -eq 2) 'Admin must still list reviews of a hidden product'
Check ((Status (Call POST '/api/v1/reviews' @{ productId=$product.id; rating=5; comment='Muộn' } $stranger.token)) -eq 400) 'Review of a hidden product must be 400'
$bReview = (Api GET "/api/v1/products/$($product.id)/reviews?rating=3" $null $admin).items[0]
Check ((Status (Call DELETE "/api/v1/reviews/$($bReview.id)" $null $admin)) -eq 204) 'Admin must delete any review'
Check ((Status (Call DELETE "/api/v1/reviews/$($review.id)" $null $buyer.token)) -eq 204) 'Owner must delete own review'

# Swagger có body mẫu hợp lệ cho voucher
$swagger = Invoke-RestMethod "$Base/swagger/v1/swagger.json"
$example = $swagger.components.schemas.VoucherRequest.example
Check ($example.code -eq 'SALE10' -and $example.PSObject.Properties.Name -contains 'discountPercent') 'Swagger voucher example missing'

$null = Api DELETE "/api/v1/categories/$($category.id)" $null $admin
Write-Output 'PASS: nghiath voucher CRUD, % discount, checkout rules, status filter, reviews, rating summary, permissions'
