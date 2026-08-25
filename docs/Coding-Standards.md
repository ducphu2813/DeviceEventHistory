# Device Event History - Coding Standards

## 1. Mục tiêu

Các quy tắc này giữ cho solution dễ đọc, dễ kiểm thử và có thể mở rộng từ raw-file ingestion sang các source adapter khác mà không kéo dependency ngược vào Domain/Application.

`.editorconfig` là rule có thể tự động áp dụng trong IDE và formatter. Tài liệu này giải thích các quy tắc kiến trúc và vận hành mà formatter không thể kiểm tra.

## 2. Quy tắc cấu hình và hằng số

- `AppConst` chỉ chứa các contract constant dùng chung: configuration section, environment-variable key, tên collection, default an toàn, format marker và giới hạn kỹ thuật.
- Không đặt connection string, password, token, raw-log path của môi trường thật hoặc dữ liệu tenant thật trong `AppConst`.
- Không lặp lại section name, collection name, environment-variable name hoặc default kỹ thuật trong nhiều class.
- Options class chỉ bind configuration và cung cấp default an toàn từ `AppConst`; không tự gọi MongoDB, filesystem hoặc network.
- Giá trị theo môi trường phải đến từ configuration provider, environment variable hoặc User Secrets.
- Validation và operational error message ổn định phải được đặt trong `AppConst.Messages` với prefix `MSG_`; message có tham số dùng format placeholder và formatter chung.
- Không đặt message exception, adapter failure hoặc protocol failure trực tiếp trong source xử lý; chỉ exception type, parameter name và structured data được giữ tại nơi sử dụng.

## 3. Ranh giới project

```text
Domain <- Application <- Infrastructure <- Worker
```

- Domain không reference framework, MongoDB, filesystem hoặc Worker.
- Application định nghĩa use case, model canonical và abstraction.
- Infrastructure triển khai adapter MongoDB, filesystem, parser và metadata.
- Worker là composition root: bind options, validate startup, đăng ký DI và chạy hosted service.
- Infrastructure không reference Worker.
- Không reference source project của G-ERP, RFID.Antenna hoặc ERP legacy.

## 4. Options validation

- Options bắt buộc phải có validator và được gọi bằng `ValidateOnStart()`.
- Validator phải fail fast với cấu hình không an toàn hoặc không đủ.
- Validator không kiểm tra external state nặng như Mongo ping hoặc scan filesystem; các kiểm tra đó thuộc startup preflight/integration test.
- Source identity phải ổn định: `SourceId` phân biệt installation/stream, không thay bằng machine name tự động hoặc `FileId`.
- Không log giá trị secret. Chỉ log trạng thái như `MongoConnectionStringConfigured=true/false`.

## 5. Async, file và cancellation

- Mọi vòng lặp nền phải nhận và truyền `CancellationToken`.
- Không dùng busy loop khi feature disabled hoặc pipeline chưa được bật.
- File raw-log chỉ được mở read-only và share-compatible với writer.
- Offset file luôn là byte offset `long`; không dùng character count làm checkpoint.
- Không commit checkpoint trước khi persistence được xác nhận.
- Source có thể đọc bằng `Local` hoặc `RemoteHttp`; local dùng `RootPath`, remote dùng `RemoteBaseUrl`.
- Remote URL không chứa credential, query token hoặc fragment; remote tail reader phải dùng HTTP Range và không tải lại toàn bộ file cho mỗi offset.
- Discovery chỉ nhận diện file có dạng `File_{FileId}.txt` với `FileId` là `long`; file không xác định được identity phải bị bỏ qua và không được tạo checkpoint giả.
- Record framer làm việc trên bytes UTF-8, giữ partial record giữa các chunk và chỉ phát record sau marker `e(0)`.

## 6. WP3 file discovery và framing

- Discovery duyệt các thư mục `yyyy/MM/dd` trong khoảng `LookbackDays` theo timezone của từng source.
- Existing file bắt đầu tại `End`, new file bắt đầu tại `Beginning`; policy này được giữ ở tầng orchestration đọc file, không nhúng vào adapter local/remote.
- `RawLogFileDescriptor` phải giữ đủ `SourceId`, `CompanyId`, `FolderDate`, `FileId`, mode và location để checkpoint/persistence phía sau không phải suy luận lại identity.
- Tail reader trả về `StartOffset`, `NextOffset`, `FileLength`, bytes đọc và trạng thái truncation. Offset luôn là byte offset `long`.
- WP3 không ghi MongoDB và không commit checkpoint. Chỉ persistence thành công ở work package sau mới được phép advance checkpoint.

## 7. Logging và bảo mật

- Dùng structured logging với property rõ nghĩa.
- Không log connection string, password, token, full raw payload hoặc secret-bearing query string.
- Raw path môi trường thật chỉ xuất hiện trong diagnostic scope cần thiết; không đưa vào metric label có cardinality cao.
- Configuration mẫu phải dùng placeholder an toàn và không chứa credential thật.

## 8. Testing

- Mỗi rule validation quan trọng phải có unit test cho cả trường hợp hợp lệ và không hợp lệ.
- Test parser/framer phải bao phủ partial record, nhiều record, UTF-8 boundary và restart.
- Integration test mới được dùng MongoDB/filesystem thật hoặc container; unit test không giả nhận là end-to-end.
- Không deduplicate business event chỉ vì raw payload giống nhau; idempotency dùng deterministic event identity.

## 9. Quy ước đặt tên

- Type, method, property và constant dùng PascalCase.
- Riêng message constant trong `AppConst.Messages` dùng prefix `MSG_` và chữ hoa toàn bộ để dễ nhận diện khi đọc flow xử lý lỗi.
- Field private dùng `_camelCase`.
- Interface bắt đầu bằng `I`.
- Options kết thúc bằng `Options`; validator kết thúc bằng `Validator`; adapter triển khai interface phải có tên thể hiện technology/source.
- Một file nên chứa một public type chính, trừ các record/value type nhỏ gắn chặt với type chính.

## 10. Review checklist

- Có magic string/number mới cần đưa vào `AppConst` hoặc configuration không?
- Có dependency đi ngược layer không?
- Có secret/path môi trường thật trong source hoặc log không?
- Có cancellation, retry và failure semantics rõ ràng không?
- Test có chứng minh hành vi cần tuyên bố không?
