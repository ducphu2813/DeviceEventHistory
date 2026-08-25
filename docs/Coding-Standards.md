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
- Nhiều database phải được nhóm dưới `DatabaseSettings`; mỗi database có subsection riêng, không trộn connection của các hệ thống vào một options class.
- MongoDB connection string trong file local bị ignore chỉ phục vụ development/integration; deployment phải ưu tiên environment/secret provider.
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
- Parser phải tokenize block boundary trước khi parse arguments; không split toàn bộ raw record bằng dấu phẩy.
- Mỗi block (`@`, `b`, `t`, `te`, `sp`, `u`) được parse độc lập; block không biết tạo warning, block malformed tạo parse error nhưng raw payload vẫn được giữ nguyên.
- Numeric/date parsing dùng `CultureInfo.InvariantCulture`; không phụ thuộc locale của máy chạy Worker.
- Không suy đoán EPC, `DeviceId`, `GateId` hoặc business meaning khi raw source không cung cấp; giữ raw value/null.

## 6. WP3 file discovery và framing

- Discovery duyệt các thư mục `yyyy/MM/dd` trong khoảng `LookbackDays` theo timezone của từng source.
- Existing file bắt đầu tại `End`, new file bắt đầu tại `Beginning`; policy này được giữ ở tầng orchestration đọc file, không nhúng vào adapter local/remote.
- `RawLogFileDescriptor` phải giữ đủ `SourceId`, `CompanyId`, `FolderDate`, `FileId`, mode và location để checkpoint/persistence phía sau không phải suy luận lại identity.
- Tail reader trả về `StartOffset`, `NextOffset`, `FileLength`, bytes đọc và trạng thái truncation. Offset luôn là byte offset `long`.
- WP3 không ghi MongoDB và không commit checkpoint. Chỉ persistence thành công ở work package sau mới được phép advance checkpoint.
- Canonical `eventId`/`failureId` phải deterministic từ source identity, relative path, offsets và raw payload hash; không dùng current time hoặc `WorkerId`.
- History/failure writer phải coi duplicate deterministic identity là idempotent success; không update hoặc xóa history append-only khi checkpoint lỗi.
- Checkpoint phải lưu theo `SourceId + FolderDate + FileId + RelativePath`, dùng `long` byte position và compare-and-set theo `version`; conflict không được ghi đè âm thầm.
- Trình tự bắt buộc là persist history/failure, nhận Mongo confirmation, rồi mới advance checkpoint. Mongo unavailable hoặc CAS conflict giữ nguyên checkpoint.
- Orchestration giữ một logical state cho mỗi file, nhưng giới hạn consumer bằng `MaxConcurrentFiles`; không tạo một OS thread cố định cho từng file.
- Mỗi file được xử lý theo turn budget (`MaxBytesPerTurn`, `MaxRecordsPerTurn`, `MaxTurnDuration`); backlog phải requeue cuối hàng để bảo đảm fairness.
- Partial record chỉ giữ trong framer state tạm thời; durable checkpoint luôn trỏ tới contiguous prefix đã persist xác nhận.
- File bị truncate/replaced phải chuyển sang stopped state và giữ checkpoint cũ; không tự reset hoặc bỏ qua dữ liệu.

## 7. MongoDB persistence

- MongoDB Driver chỉ được reference trong Infrastructure; Domain/Application chỉ reference persistence abstraction và checkpoint model framework-independent.
- Collection/index initializer phải idempotent và tạo đúng ba collection Sprint 1: history, failures và checkpoints.
- Không tạo TTL index cho history/failure cho tới khi retention contract và `expireAtUtc` được chốt. Checkpoint không dùng TTL.
- Retry chỉ áp dụng cho transient Mongo errors với giới hạn/backoff; duplicate key của event/failure là kết quả idempotent, còn checkpoint duplicate trong CAS là conflict.
- Không log connection string hoặc document raw payload đầy đủ trong log vận hành.

## 8. Logging và bảo mật

- Dùng structured logging với property rõ nghĩa.
- Không log connection string, password, token, full raw payload hoặc secret-bearing query string.
- Raw path môi trường thật chỉ xuất hiện trong diagnostic scope cần thiết; không đưa vào metric label có cardinality cao.
- Configuration mẫu phải dùng placeholder an toàn và không chứa credential thật.

## 9. Testing

- Mỗi rule validation quan trọng phải có unit test cho cả trường hợp hợp lệ và không hợp lệ.
- Test parser/framer phải bao phủ partial record, nhiều record, UTF-8 boundary và restart.
- Integration test mới được dùng MongoDB/filesystem thật hoặc container; unit test không giả nhận là end-to-end.
- Mongo integration test có thể opt-in bằng `DEVICE_EVENT_HISTORY_MONGODB_CONNECTION_STRING`; khi chạy local phải xác nhận index initialization, duplicate identity và checkpoint CAS.
- Không deduplicate business event chỉ vì raw payload giống nhau; idempotency dùng deterministic event identity.

## 10. Quy ước đặt tên

- Type, method, property và constant dùng PascalCase.
- Riêng message constant trong `AppConst.Messages` dùng prefix `MSG_` và chữ hoa toàn bộ để dễ nhận diện khi đọc flow xử lý lỗi.
- Field private dùng `_camelCase`.
- Interface bắt đầu bằng `I`.
- Options kết thúc bằng `Options`; validator kết thúc bằng `Validator`; adapter triển khai interface phải có tên thể hiện technology/source.
- Một file nên chứa một public type chính, trừ các record/value type nhỏ gắn chặt với type chính.

## 11. Review checklist

- Có magic string/number mới cần đưa vào `AppConst` hoặc configuration không?
- Có dependency đi ngược layer không?
- Có secret/path môi trường thật trong source hoặc log không?
- Có cancellation, retry và failure semantics rõ ràng không?
- Test có chứng minh hành vi cần tuyên bố không?
