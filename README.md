# Pacman KLTN Project

## Giới thiệu

Đây là đồ án Pacman phục vụ khóa luận, phát triển bằng Unity. Dự án gồm hai phần chính:

- **Gameplay system**: điều khiển Pacman và hệ ghost trong trò chơi thực thời gian.
- **Simulation system**: chạy mô phỏng hàng loạt để đo hiệu năng, xuất CSV và phân tích các chỉ số như capture rate, gap, coverage ratio, revisit ratio.

## Yêu cầu hệ thống

- **Unity Editor**: `2022.3.62f2` LTS
- **Unity Hub** để mở và quản lý phiên bản Unity
- **Visual Studio 2022** hoặc **Rider** để chỉnh sửa mã C#

## Cài đặt dự án

### 1. Lấy mã nguồn

Clone dự án từ repository:

Sau đó mở thư mục gốc của dự án:

- `Pacman KLTN`

### 2. Mở bằng Unity Hub

1. Mở **Unity Hub**.
2. Chọn **Add** và trỏ tới thư mục gốc của project.
3. Chọn đúng phiên bản **Unity 2022.3.62f2**.
4. Chờ Unity đồng bộ `Packages/manifest.json` và import asset.

### 3. Đồng bộ package

Dự án đang dùng các package chính sau:

- `com.unity.feature.2d`
- `com.unity.render-pipelines.universal`
- `com.unity.textmeshpro`
- `com.unity.test-framework`
- `com.unity.timeline`
- `com.unity.visualscripting`

Nếu Unity báo thiếu package, hãy mở **Window > Package Manager** để cài hoặc đồng bộ lại.

## Cấu trúc dự án

- `Assets/Scripts/Player/` - logic di chuyển của Pacman
- `Assets/Scripts/Ghost/` - AI và pathfinding của ghost
- `Assets/Scripts/Ghost/MultiAgent/` - logic multi-agent dùng chung cho gameplay và simulation
- `Assets/Scripts/Grid/` - truy vấn lưới và tile
- `Assets/Scripts/Logic/` - quản lý game, coin, trạng thái chơi
- `Assets/Scripts/Simulate/` - chạy mô phỏng và thống kê
- `Assets/Scenes/` - scene chính của project
- `Documentation/` - các tài liệu sử dụng trong quá trình phát triển

Scene mẫu hiện có trong project là:

- `Assets/Scenes/SampleScene.unity`

## Chạy thử trong Unity Editor

### Bước 1: Mở scene

- Mở `Assets/Scenes/SampleScene.unity`

### Bước 2: Chạy game

- Nhấn nút **Play** trong Unity Editor.
- Quan sát Pacman, ghost và hệ thống điều phối AI.

### Bước 3: Kiểm tra kết quả

Có thể kiểm tra:

- trạng thái bắt Pacman
- thời gian phát hiện và bắt
- hành vi phối hợp giữa các ghost
- nhấn V để mở debug xem đường di chuyển của Ghost
- file CSV mô phỏng trong các thư mục dữ liệu của project

## Chạy mô phỏng và xuất dữ liệu

Để chạy simulation, hãy:

1. Mở scene chứa hệ thống mô phỏng.
2. Chạy script `SimpleSimulate` từ Inspector hoặc từ hệ thống điều khiển hiện có.
3. Thiết lập số episode, số bước tối đa và chế độ mô phỏng theo nhu cầu luận văn.
4. Chạy batch bằng cách chuột phải vào script `SimpleSimulate` chọn Run Batch Simulate để sinh dữ liệu CSV.

Các chỉ số thường được ghi nhận:

- `seed`
- `captured`
- `detectStep`
- `captureStep`
- `gap`
- `coverageRatio`
- `revisitRatio`
- `targetConflictCount`
- `avgIdleness`
- `maxIdleness`
