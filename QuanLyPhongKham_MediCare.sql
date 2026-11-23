-- ============================================================================
-- HỆ THỐNG QUẢN LÝ PHÒNG KHÁM MEDICARE
-- Tích hợp AI Chatbot phân tích triệu chứng & gợi ý bác sĩ/chuyên khoa
-- FILE SQL DUY NHẤT: Cơ sở dữ liệu, Bảng, Ràng buộc, Index, Triggers,
--                    Functions, Procedures & Dữ liệu mẫu
-- Hệ quản trị CSDL: Microsoft SQL Server (T-SQL)
--
-- HƯỚNG DẪN: Xóa database cũ (nếu có) rồi chạy toàn bộ file này một lần.
--   DROP DATABASE IF EXISTS QuanLyPhongKham_MediCare;
-- ============================================================================

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'QuanLyPhongKham_MediCare')
BEGIN
    CREATE DATABASE QuanLyPhongKham_MediCare;
END
GO

USE QuanLyPhongKham_MediCare;
GO

-- ============================================================================
-- 1. TẠO CÁC BẢNG DỮ LIỆU
-- ============================================================================

-- 1.1. Bảng Roles (Quản lý vai trò / phân quyền)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Roles')
BEGIN
    CREATE TABLE Roles (
        RoleId INT IDENTITY(1,1) PRIMARY KEY,
        RoleName NVARCHAR(100) NOT NULL UNIQUE,
        MoTa NVARCHAR(255) NULL
    );
END
GO

-- 1.2. Bảng Khoa (Quản lý các chuyên khoa phòng khám)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Khoa')
BEGIN
    CREATE TABLE Khoa (
        MaKhoa INT IDENTITY(1,1) PRIMARY KEY,
        TenKhoa NVARCHAR(100) NOT NULL UNIQUE,
        MoTa NVARCHAR(255) NULL
    );
END
GO

-- 1.3. Bảng Users (Tài khoản người dùng: Quản trị viên, Bác sĩ, Bệnh nhân, Lễ tân...)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE Users (
        UserId INT IDENTITY(1,1) PRIMARY KEY,
        Username VARCHAR(50) NOT NULL UNIQUE,
        PasswordHash VARCHAR(255) NOT NULL,
        FullName NVARCHAR(100) NOT NULL,
        Phone VARCHAR(20) NULL,
        Email VARCHAR(100) NULL,
        GioiTinh NVARCHAR(10) NULL,
        NgaySinh DATE NULL,
        DiaChi NVARCHAR(255) NULL,
        RoleId INT NOT NULL,
        MaKhoa INT NULL, -- Liên kết chuyên khoa dành cho Bác sĩ
        SoNamKinhNghiem INT NULL, -- Số năm kinh nghiệm (dành cho Bác sĩ)
        DiemDanhGia DECIMAL(3,1) NULL, -- Điểm đánh giá trung bình (0-5)
        SoLuotDanhGia INT NULL, -- Số lượt đánh giá
        GioiThieu NVARCHAR(500) NULL, -- Giới thiệu ngắn về bác sĩ
        AnhDaiDien NVARCHAR(500) NULL, -- Ảnh đại diện
        IsTwoFactorEnabled BIT NOT NULL DEFAULT 0, -- Trạng thái bật xác thực 2 yếu tố (TOTP)
        TwoFactorSecret NVARCHAR(100) NULL, -- Khóa bí mật TOTP dùng sinh mã OTP 2FA
        IsActive BIT NOT NULL DEFAULT 1,
        CONSTRAINT FK_Users_Roles FOREIGN KEY (RoleId) REFERENCES Roles(RoleId),
        CONSTRAINT FK_Users_Khoa FOREIGN KEY (MaKhoa) REFERENCES Khoa(MaKhoa)
    );
END
GO

-- 1.3.1. Bổ sung cột 2FA cho CSDL đã tồn tại (migration an toàn, idempotent)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'IsTwoFactorEnabled')
BEGIN
    ALTER TABLE dbo.Users ADD IsTwoFactorEnabled BIT NOT NULL DEFAULT 0;
END
GO
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'TwoFactorSecret')
BEGIN
    ALTER TABLE dbo.Users ADD TwoFactorSecret NVARCHAR(100) NULL;
END
GO

-- 1.4. Bảng LichHen (Quản lý lịch hẹn khám bệnh)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LichHen')
BEGIN
    CREATE TABLE LichHen (
        MaLichHen INT IDENTITY(1,1) PRIMARY KEY,
        BenhNhanId INT NOT NULL,
        BacSiId INT NULL,
        NgayKham DATE NOT NULL,
        GioKham TIME NOT NULL,
        SoThuTu INT NULL,
        LyDoKham NVARCHAR(500) NULL,
        TrangThai NVARCHAR(50) NOT NULL DEFAULT N'Đã đặt', -- Đã đặt, Đã khám, Hủy
        CONSTRAINT FK_LichHen_BenhNhan FOREIGN KEY (BenhNhanId) REFERENCES Users(UserId),
        CONSTRAINT FK_LichHen_BacSi FOREIGN KEY (BacSiId) REFERENCES Users(UserId)
    );
END
GO

-- 1.5. Bảng PhieuKhamBenh (Phiếu khám bệnh của bác sĩ)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PhieuKhamBenh')
BEGIN
    CREATE TABLE PhieuKhamBenh (
        MaPhieuKham INT IDENTITY(1,1) PRIMARY KEY,
        MaLichHen INT NULL,
        BenhNhanId INT NOT NULL,
        BacSiId INT NOT NULL,
        NgayKham DATETIME NOT NULL DEFAULT GETDATE(),
        HuyetAp VARCHAR(20) NULL,
        Mach INT NULL,
        NhietDo DECIMAL(4,1) NULL,
        CanNang DECIMAL(5,2) NULL,
        TrieuChung NVARCHAR(MAX) NULL,
        ChanDoan NVARCHAR(MAX) NULL,
        LoiDan NVARCHAR(MAX) NULL,
        TrangThai NVARCHAR(50) NOT NULL DEFAULT N'Đang khám', -- Đang khám, Hoàn thành, Hủy
        CONSTRAINT FK_PhieuKham_LichHen FOREIGN KEY (MaLichHen) REFERENCES LichHen(MaLichHen),
        CONSTRAINT FK_PhieuKham_BenhNhan FOREIGN KEY (BenhNhanId) REFERENCES Users(UserId),
        CONSTRAINT FK_PhieuKham_BacSi FOREIGN KEY (BacSiId) REFERENCES Users(UserId)
    );
END
GO

-- 1.6. Bảng DichVuCLS (Danh mục dịch vụ cận lâm sàng & đơn giá)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DichVuCLS')
BEGIN
    CREATE TABLE DichVuCLS (
        MaDichVu INT IDENTITY(1,1) PRIMARY KEY,
        TenDichVu NVARCHAR(150) NOT NULL,
        LoaiDichVu NVARCHAR(100) NULL, -- Xét nghiệm, Chẩn đoán hình ảnh...
        DonGia DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (DonGia >= 0),
        MoTa NVARCHAR(500) NULL,        -- Mô tả ngắn hiển thị trên trang chủ
        BieuTuong NVARCHAR(100) NULL    -- Class icon Font Awesome hiển thị trên trang chủ
    );
END
GO

-- 1.7. Bảng ChiTietCLS (Chi tiết chỉ định cận lâm sàng trong từng phiếu khám)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChiTietCLS')
BEGIN
    CREATE TABLE ChiTietCLS (
        MaChiTietCLS INT IDENTITY(1,1) PRIMARY KEY,
        MaPhieuKham INT NOT NULL,
        MaDichVu INT NOT NULL,
        KetQua NVARCHAR(MAX) NULL,
        KetLuan NVARCHAR(MAX) NULL,
        DonGia DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (DonGia >= 0),
        TrangThai NVARCHAR(50) NOT NULL DEFAULT N'Chưa thực hiện', -- Chưa thực hiện, Đã thực hiện
        CONSTRAINT FK_ChiTietCLS_PhieuKham FOREIGN KEY (MaPhieuKham) REFERENCES PhieuKhamBenh(MaPhieuKham),
        CONSTRAINT FK_ChiTietCLS_DichVu FOREIGN KEY (MaDichVu) REFERENCES DichVuCLS(MaDichVu)
    );
END
GO

-- 1.8. Bảng Thuoc (Danh mục thuốc, nhóm thuốc & số lượng tồn kho)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Thuoc')
BEGIN
    CREATE TABLE Thuoc (
        MaThuoc INT IDENTITY(1,1) PRIMARY KEY,
        TenThuoc NVARCHAR(150) NOT NULL,
        HoatChat NVARCHAR(150) NULL,
        DonViTinh NVARCHAR(50) NULL, -- Viên, Chai, Vỉ, Hộp...
        DonGia DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (DonGia >= 0),
        SoLuongTon INT NOT NULL DEFAULT 0 CHECK (SoLuongTon >= 0),
        NhomThuoc NVARCHAR(100) NULL, -- Nhóm dược lý
        GiaNhap DECIMAL(18,2) NULL,
        GiaBan DECIMAL(18,2) NULL,
        HanSuDung DATE NULL,
        MucTonToiThieu INT NOT NULL CONSTRAINT DF_Thuoc_MucTonToiThieu DEFAULT (10),
        IsActive BIT NOT NULL CONSTRAINT DF_Thuoc_IsActive DEFAULT (1)
    );
END
GO

-- 1.9. Bảng DanhMucNhomThuoc (Danh mục nhóm thuốc dùng cho module quản lý thuốc)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DanhMucNhomThuoc')
BEGIN
    CREATE TABLE DanhMucNhomThuoc (
        MaNhomThuoc INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DanhMucNhomThuoc PRIMARY KEY,
        TenNhomThuoc NVARCHAR(100) NOT NULL CONSTRAINT UQ_DanhMucNhomThuoc_Ten UNIQUE
    );
END
GO

-- 1.10. Bảng DonThuoc (Đơn thuốc tổng quát)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DonThuoc')
BEGIN
    CREATE TABLE DonThuoc (
        MaDonThuoc INT IDENTITY(1,1) PRIMARY KEY,
        MaPhieuKham INT NOT NULL,
        BacSiId INT NOT NULL,
        NgayKeDon DATETIME NOT NULL DEFAULT GETDATE(),
        TongTienThuoc DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (TongTienThuoc >= 0),
        CONSTRAINT FK_DonThuoc_PhieuKham FOREIGN KEY (MaPhieuKham) REFERENCES PhieuKhamBenh(MaPhieuKham),
        CONSTRAINT FK_DonThuoc_BacSi FOREIGN KEY (BacSiId) REFERENCES Users(UserId)
    );
END
GO

-- 1.11. Bảng ChiTietDonThuoc (Chi tiết từng thuốc trong đơn)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChiTietDonThuoc')
BEGIN
    CREATE TABLE ChiTietDonThuoc (
        MaChiTietDon INT IDENTITY(1,1) PRIMARY KEY,
        MaDonThuoc INT NOT NULL,
        MaThuoc INT NOT NULL,
        SoLuong INT NOT NULL CHECK (SoLuong > 0),
        DonGia DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (DonGia >= 0),
        CachDung NVARCHAR(255) NULL,
        ThanhTien DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (ThanhTien >= 0),
        CONSTRAINT FK_ChiTietDon_DonThuoc FOREIGN KEY (MaDonThuoc) REFERENCES DonThuoc(MaDonThuoc),
        CONSTRAINT FK_ChiTietDon_Thuoc FOREIGN KEY (MaThuoc) REFERENCES Thuoc(MaThuoc)
    );
END
GO

-- 1.12. Bảng HoaDon (Hóa đơn thanh toán khám chữa bệnh)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'HoaDon')
BEGIN
    CREATE TABLE HoaDon (
        MaHoaDon INT IDENTITY(1,1) PRIMARY KEY,
        MaPhieuKham INT NOT NULL,
        BenhNhanId INT NOT NULL,
        TienKhamAndCLS DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (TienKhamAndCLS >= 0),
        TienThuoc DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (TienThuoc >= 0),
        TongThanhToan DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (TongThanhToan >= 0),
        PhuongThucTT NVARCHAR(50) NULL, -- Tiền mặt, Chuyển khoản, Thẻ
        NgayThanhToan DATETIME NULL DEFAULT GETDATE(),
        TrangThai NVARCHAR(50) NOT NULL DEFAULT N'Chưa thanh toán', -- Chưa thanh toán, Đã thanh toán
        CONSTRAINT FK_HoaDon_PhieuKham FOREIGN KEY (MaPhieuKham) REFERENCES PhieuKhamBenh(MaPhieuKham),
        CONSTRAINT FK_HoaDon_BenhNhan FOREIGN KEY (BenhNhanId) REFERENCES Users(UserId)
    );
END
GO

-- 1.13. Bảng MedicalRecordLookupLog (Nhật ký tra cứu hồ sơ bệnh án)
--       Chỉ lưu metadata tối thiểu, không lưu nội dung hồ sơ hay thông tin sức khỏe.
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MedicalRecordLookupLog')
BEGIN
    CREATE TABLE MedicalRecordLookupLog (
        LookupLogId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MedicalRecordLookupLog PRIMARY KEY,
        UserId INT NULL,
        LookupAt DATETIME2(0) NOT NULL CONSTRAINT DF_MedicalRecordLookupLog_LookupAt DEFAULT SYSUTCDATETIME(),
        ResultCount INT NOT NULL CONSTRAINT CK_MedicalRecordLookupLog_ResultCount CHECK (ResultCount >= 0),
        CONSTRAINT FK_MedicalRecordLookupLog_User FOREIGN KEY (UserId) REFERENCES Users(UserId)
    );
END
GO

-- ============================================================================
-- 2. CÁC INDEX CẦN THIẾT
-- ============================================================================

-- 2.1. Username đã có UNIQUE trong thiết kế gốc. Email và Phone chỉ duy nhất khi có giá trị.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_Email' AND object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    CREATE UNIQUE INDEX UX_Users_Email
        ON dbo.Users (Email)
        WHERE Email IS NOT NULL AND Email <> '';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_Phone' AND object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    CREATE UNIQUE INDEX UX_Users_Phone
        ON dbo.Users (Phone)
        WHERE Phone IS NOT NULL AND Phone <> '';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Thuoc_Ten_HoatChat' AND object_id = OBJECT_ID(N'dbo.Thuoc'))
BEGIN
    CREATE INDEX IX_Thuoc_Ten_HoatChat
        ON dbo.Thuoc (TenThuoc, HoatChat);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Thuoc_Nhom_IsActive' AND object_id = OBJECT_ID(N'dbo.Thuoc'))
BEGIN
    CREATE INDEX IX_Thuoc_Nhom_IsActive
        ON dbo.Thuoc (NhomThuoc, IsActive);
END
GO

-- ============================================================================
-- 3. CÁC TRIGGER CẦN THIẾT (TRIGGERS)
-- ============================================================================

-- 3.1. Trigger: Kiểm tra & Tự động trừ số lượng tồn kho Thuoc khi kê đơn thuốc
IF OBJECT_ID('trg_ChiTietDonThuoc_TruKho', 'TR') IS NOT NULL
    DROP TRIGGER trg_ChiTietDonThuoc_TruKho;
GO

CREATE TRIGGER trg_ChiTietDonThuoc_TruKho
ON ChiTietDonThuoc
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    -- Kiểm tra nếu tồn kho không đủ
    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN Thuoc t ON i.MaThuoc = t.MaThuoc
        WHERE t.SoLuongTon < i.SoLuong
    )
    BEGIN
        RAISERROR (N'Số lượng thuốc trong kho không đủ để kê đơn!', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- Trừ số lượng kho
    UPDATE t
    SET t.SoLuongTon = t.SoLuongTon - i.SoLuong
    FROM Thuoc t
    JOIN inserted i ON t.MaThuoc = i.MaThuoc;
END;
GO

-- 3.2. Trigger: Tự động tính ThànhTiền và cập nhật TongTienThuoc trong bảng DonThuoc
IF OBJECT_ID('trg_ChiTietDonThuoc_CapNhatTongTien', 'TR') IS NOT NULL
    DROP TRIGGER trg_ChiTietDonThuoc_CapNhatTongTien;
GO

CREATE TRIGGER trg_ChiTietDonThuoc_CapNhatTongTien
ON ChiTietDonThuoc
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- Tính Thành tiền = Số lượng * Đơn giá
    UPDATE c
    SET c.ThanhTien = c.SoLuong * c.DonGia
    FROM ChiTietDonThuoc c
    JOIN inserted i ON c.MaChiTietDon = i.MaChiTietDon;

    -- Cập nhật tổng tiền đơn thuốc
    DECLARE @MaDonThuoc INT;
    SELECT TOP 1 @MaDonThuoc = MaDonThuoc FROM inserted;
    IF @MaDonThuoc IS NULL
        SELECT TOP 1 @MaDonThuoc = MaDonThuoc FROM deleted;

    IF @MaDonThuoc IS NOT NULL
    BEGIN
        UPDATE DonThuoc
        SET TongTienThuoc = ISNULL((
            SELECT SUM(ThanhTien)
            FROM ChiTietDonThuoc
            WHERE MaDonThuoc = @MaDonThuoc
        ), 0)
        WHERE MaDonThuoc = @MaDonThuoc;
    END
END;
GO

-- 3.3. Trigger: Tự động chuyển TrangThai của LichHen sang 'Đã khám' khi tạo PhieuKhamBenh
IF OBJECT_ID('trg_PhieuKhamBenh_CapNhatLichHen', 'TR') IS NOT NULL
    DROP TRIGGER trg_PhieuKhamBenh_CapNhatLichHen;
GO

CREATE TRIGGER trg_PhieuKhamBenh_CapNhatLichHen
ON PhieuKhamBenh
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE lh
    SET lh.TrangThai = N'Đã khám'
    FROM LichHen lh
    JOIN inserted i ON lh.MaLichHen = i.MaLichHen
    WHERE i.MaLichHen IS NOT NULL;
END;
GO

-- ============================================================================
-- 4. CÁC FUNCTION CẦN THIẾT (FUNCTIONS)
-- ============================================================================

-- 4.1. Function: Tính tổng tiền dịch vụ CLS + Đơn thuốc cho một Phiếu khám
IF OBJECT_ID('fn_TinhTongTienPhieuKham', 'FN') IS NOT NULL
    DROP FUNCTION fn_TinhTongTienPhieuKham;
GO

CREATE FUNCTION fn_TinhTongTienPhieuKham (@MaPhieuKham INT)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @TienCLS DECIMAL(18,2) = 0;
    DECLARE @TienThuoc DECIMAL(18,2) = 0;

    SELECT @TienCLS = ISNULL(SUM(DonGia), 0)
    FROM ChiTietCLS
    WHERE MaPhieuKham = @MaPhieuKham;

    SELECT @TienThuoc = ISNULL(SUM(TongTienThuoc), 0)
    FROM DonThuoc
    WHERE MaPhieuKham = @MaPhieuKham;

    RETURN (@TienCLS + @TienThuoc);
END;
GO

-- 4.2. Function: Tự động cấp Số thứ tự khám tiếp theo trong ngày cho Bác sĩ
IF OBJECT_ID('fn_LaySoThuTuKhamTiepTheo', 'FN') IS NOT NULL
    DROP FUNCTION fn_LaySoThuTuKhamTiepTheo;
GO

CREATE FUNCTION fn_LaySoThuTuKhamTiepTheo (
    @BacSiId INT,
    @NgayKham DATE
)
RETURNS INT
AS
BEGIN
    DECLARE @MaxSTT INT = 0;

    SELECT @MaxSTT = ISNULL(MAX(SoThuTu), 0)
    FROM LichHen
    WHERE BacSiId = @BacSiId AND NgayKham = @NgayKham;

    RETURN (@MaxSTT + 1);
END;
GO

-- ============================================================================
-- 5. STORED PROCEDURE CẦN THIẾT (PROCEDURES)
-- ============================================================================

-- 5.1. Procedure: Tự động lập Hóa đơn thanh toán dựa trên Phiếu khám
IF OBJECT_ID('sp_TaoHoaDonChoPhieuKham', 'P') IS NOT NULL
    DROP PROCEDURE sp_TaoHoaDonChoPhieuKham;
GO

CREATE PROCEDURE sp_TaoHoaDonChoPhieuKham
    @MaPhieuKham INT,
    @PhuongThucTT NVARCHAR(50) = N'Tiền mặt'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BenhNhanId INT;
    DECLARE @TienCLS DECIMAL(18,2) = 0;
    DECLARE @TienThuoc DECIMAL(18,2) = 0;
    DECLARE @TongThanhToan DECIMAL(18,2) = 0;

    SELECT @BenhNhanId = BenhNhanId
    FROM PhieuKhamBenh
    WHERE MaPhieuKham = @MaPhieuKham;

    IF @BenhNhanId IS NULL
    BEGIN
        RAISERROR(N'Không tìm thấy phiếu khám bệnh chỉ định!', 16, 1);
        RETURN;
    END

    SELECT @TienCLS = ISNULL(SUM(DonGia), 0)
    FROM ChiTietCLS
    WHERE MaPhieuKham = @MaPhieuKham;

    SELECT @TienThuoc = ISNULL(SUM(TongTienThuoc), 0)
    FROM DonThuoc
    WHERE MaPhieuKham = @MaPhieuKham;

    SET @TongThanhToan = @TienCLS + @TienThuoc;

    IF EXISTS (SELECT 1 FROM HoaDon WHERE MaPhieuKham = @MaPhieuKham)
    BEGIN
        UPDATE HoaDon
        SET TienKhamAndCLS = @TienCLS,
            TienThuoc = @TienThuoc,
            TongThanhToan = @TongThanhToan,
            PhuongThucTT = @PhuongThucTT,
            NgayThanhToan = GETDATE()
        WHERE MaPhieuKham = @MaPhieuKham;
    END
    ELSE
    BEGIN
        INSERT INTO HoaDon (MaPhieuKham, BenhNhanId, TienKhamAndCLS, TienThuoc, TongThanhToan, PhuongThucTT, TrangThai)
        VALUES (@MaPhieuKham, @BenhNhanId, @TienCLS, @TienThuoc, @TongThanhToan, @PhuongThucTT, N'Chưa thanh toán');
    END
END;
GO

-- ============================================================================
-- 6. CHÈN DỮ LIỆU MẪU (SEED DATA KIỂM THỬ)
-- ============================================================================

-- 6.1. Dữ liệu Roles
--      'Admin hệ thống' được dùng bởi [Authorize] trong các controller.
INSERT INTO Roles (RoleName, MoTa) VALUES
(N'Quản trị viên', N'Toàn quyền quản trị hệ thống phòng khám'),
(N'Quản lý phòng khám', N'Quản lý doanh thu, nhân sự, thuốc và báo cáo'),
(N'Bác sĩ', N'Khám bệnh, chỉ định cận lâm sàng và kê đơn thuốc'),
(N'Bệnh nhân', N'Đặt lịch hẹn, xem lịch sử khám và hóa đơn'),
(N'Admin hệ thống', N'Quản trị viên hệ thống');

-- 6.2. Dữ liệu Chuyên Khoa
INSERT INTO Khoa (TenKhoa, MoTa) VALUES
(N'Khoa Nội', N'Khám và điều trị các bệnh lý nội khoa tổng quát'),
(N'Khoa Ngoại', N'Khám và xử trí các bệnh lý ngoại khoa'),
(N'Khoa Nhi', N'Chăm sóc sức khỏe và khám bệnh cho trẻ em'),
(N'Khoa Tai Mũi Họng', N'Chẩn đoán và điều trị bệnh lý tai mũi họng'),
(N'Khoa Tim Mạch', N'Chẩn đoán và điều trị bệnh lý tim mạch');

-- 6.3. Dữ liệu Người dùng (Users)
-- Mật khẩu đăng nhập cho TẤT CẢ tài khoản seed bên dưới: 123456
-- Mật khẩu được lưu dạng plaintext trong seed; web sẽ tự động mã hóa (PBKDF2)
-- ngay ở lần đăng nhập thành công đầu tiên.
INSERT INTO Users (Username, PasswordHash, FullName, Phone, Email, GioiTinh, NgaySinh, DiaChi, RoleId, MaKhoa) VALUES
('admin', '123456', N'Nguyễn Văn Quản Trị', '0901234567', 'admin@medicare.vn', N'Nam', '1985-05-10', N'TP. Hồ Chí Minh', 1, NULL),
('manager', '123456', N'Trần Thị Quản Lý', '0902345678', 'manager@medicare.vn', N'Nữ', '1988-08-15', N'TP. Hồ Chí Minh', 2, NULL),
('bs_nam', '123456', N'BS. Lê Văn Nam', '0903456789', 'nambs@medicare.vn', N'Nam', '1980-01-20', N'TP. Hồ Chí Minh', 3, 1),
('bs_hoa', '123456', N'BS. Phạm Thị Hoa', '0904567890', 'hoabs@medicare.vn', N'Nữ', '1982-11-12', N'TP. Hồ Chí Minh', 3, 4),
('bs_tuan', '123456', N'BS. Trần Anh Tuấn', '0905678901', 'tuanbs@medicare.vn', N'Nam', '1978-04-08', N'TP. Hồ Chí Minh', 3, 2),
('bs_lan', '123456', N'BS. Nguyễn Thị Lan', '0906789012', 'lanbs@medicare.vn', N'Nữ', '1985-09-30', N'TP. Hồ Chí Minh', 3, 3),
('bs_minh', '123456', N'BS. Đỗ Hoàng Minh', '0907890123', 'minhbs@medicare.vn', N'Nam', '1976-06-15', N'TP. Hồ Chí Minh', 3, 5),
('bs_huong', '123456', N'BS. Vũ Thu Hương', '0908901234', 'huongbs@medicare.vn', N'Nữ', '1987-12-03', N'TP. Hồ Chí Minh', 3, 1),
('bs_dung', '123456', N'BS. Bùi Tiến Dũng', '0909012345', 'dungbs@medicare.vn', N'Nam', '1981-02-22', N'TP. Hồ Chí Minh', 3, 4),
('bs_thao', '123456', N'BS. Hoàng Thanh Thảo', '0910123456', 'thaobs@medicare.vn', N'Nữ', '1983-07-11', N'TP. Hồ Chí Minh', 3, 5),
('bn_an', '123456', N'Nguyễn Văn An', '0912345678', 'an.nguyen@gmail.com', N'Nam', '1995-03-25', N'Bình Dương', 4, NULL),
('bn_binh', '123456', N'Lê Thị Bình', '0913579246', 'binh.le@gmail.com', N'Nữ', '1998-07-19', N'Đồng Nai', 4, NULL);

-- 6.3.1. Hồ sơ bác sĩ (kinh nghiệm, đánh giá, giới thiệu, ảnh) cho trang chủ
UPDATE Users
SET SoNamKinhNghiem = 18,
	DiemDanhGia = 4.9,
	SoLuotDanhGia = 248,
	GioiThieu = N'Bác sĩ chuyên khoa Nội với hơn 18 năm kinh nghiệm khám và điều trị các bệnh lý nội khoa tổng quát.',
	AnhDaiDien = N'https://coresg-normal.trae.ai/api/ide/v1/text_to_image?prompt=professional%20portrait%20of%20a%20male%20Vietnamese%20doctor%20wearing%20white%20coat%20and%20stethoscope%2C%20bright%20clinic%20background%2C%20realistic%20photo&image_size=portrait_4_3'
WHERE Username = 'bs_nam';

UPDATE Users
SET SoNamKinhNghiem = 15,
	DiemDanhGia = 5.0,
	SoLuotDanhGia = 312,
	GioiThieu = N'Bác sĩ chuyên khoa Tai Mũi Họng, hơn 15 năm khám và điều trị các bệnh lý tai mũi họng cho người lớn và trẻ em.',
	AnhDaiDien = N'https://coresg-normal.trae.ai/api/ide/v1/text_to_image?prompt=professional%20portrait%20of%20a%20female%20Vietnamese%20doctor%20wearing%20white%20coat%2C%20friendly%20smile%2C%20bright%20clinic%20background%2C%20realistic%20photo&image_size=portrait_4_3'
WHERE Username = 'bs_hoa';

UPDATE Users
SET SoNamKinhNghiem = 20,
	DiemDanhGia = 4.8,
	SoLuotDanhGia = 196,
	GioiThieu = N'Bác sĩ chuyên khoa Ngoại với 20 năm kinh nghiệm phẫu thuật tiêu hóa và xử trí các bệnh lý ngoại khoa.',
	AnhDaiDien = N'https://coresg-normal.trae.ai/api/ide/v1/text_to_image?prompt=professional%20portrait%20of%20a%20senior%20male%20Vietnamese%20surgeon%20wearing%20white%20coat%2C%20confident%20look%2C%20hospital%20background%2C%20realistic%20photo&image_size=portrait_4_3'
WHERE Username = 'bs_tuan';

UPDATE Users
SET SoNamKinhNghiem = 12,
	DiemDanhGia = 4.9,
	SoLuotDanhGia = 274,
	GioiThieu = N'Bác sĩ chuyên khoa Nhi, hơn 12 năm chăm sóc sức khỏe trẻ sơ sinh và trẻ nhỏ, tư vấn dinh dưỡng và tiêm chủng.',
	AnhDaiDien = N'https://coresg-normal.trae.ai/api/ide/v1/text_to_image?prompt=professional%20portrait%20of%20a%20female%20Vietnamese%20pediatrician%20wearing%20white%20coat%2C%20warm%20smile%2C%20bright%20clinic%20background%2C%20realistic%20photo&image_size=portrait_4_3'
WHERE Username = 'bs_lan';

UPDATE Users
SET SoNamKinhNghiem = 24,
	DiemDanhGia = 5.0,
	SoLuotDanhGia = 421,
	GioiThieu = N'Chuyên gia Tim mạch hơn 24 năm kinh nghiệm, chuyên điều trị tăng huyết áp, bệnh mạch vành và suy tim.',
	AnhDaiDien = N'https://coresg-normal.trae.ai/api/ide/v1/text_to_image?prompt=professional%20portrait%20of%20an%20experienced%20male%20Vietnamese%20cardiologist%20wearing%20white%20coat%20with%20stethoscope%2C%20hospital%20background%2C%20realistic%20photo&image_size=portrait_4_3'
WHERE Username = 'bs_minh';

UPDATE Users
SET SoNamKinhNghiem = 10,
	DiemDanhGia = 4.7,
	SoLuotDanhGia = 158,
	GioiThieu = N'Bác sĩ Nội khoa 10 năm kinh nghiệm, tận tâm với người bệnh, chuyên khám và quản lý bệnh lý mạn tính.',
	AnhDaiDien = N'https://coresg-normal.trae.ai/api/ide/v1/text_to_image?prompt=professional%20portrait%20of%20a%20young%20female%20Vietnamese%20physician%20wearing%20white%20coat%2C%20friendly%20expression%2C%20bright%20clinic%20background%2C%20realistic%20photo&image_size=portrait_4_3'
WHERE Username = 'bs_huong';

UPDATE Users
SET SoNamKinhNghiem = 16,
	DiemDanhGia = 4.8,
	SoLuotDanhGia = 209,
	GioiThieu = N'Bác sĩ Tai Mũi Họng 16 năm kinh nghiệm, chuyên nội soi và điều trị các bệnh lý viêm xoang, viêm họng mạn.',
	AnhDaiDien = N'https://coresg-normal.trae.ai/api/ide/v1/text_to_image?prompt=professional%20portrait%20of%20a%20male%20Vietnamese%20ENT%20doctor%20wearing%20white%20coat%2C%20calm%20expression%2C%20clinic%20background%2C%20realistic%20photo&image_size=portrait_4_3'
WHERE Username = 'bs_dung';

UPDATE Users
SET SoNamKinhNghiem = 14,
	DiemDanhGia = 4.9,
	SoLuotDanhGia = 265,
	GioiThieu = N'Bác sĩ Tim mạch 14 năm kinh nghiệm, chuyên siêu âm tim và tầm soát các bệnh lý tim mạch ở nữ giới.',
	AnhDaiDien = N'https://coresg-normal.trae.ai/api/ide/v1/text_to_image?prompt=professional%20portrait%20of%20a%20female%20Vietnamese%20cardiologist%20wearing%20white%20coat%2C%20confident%20smile%2C%20modern%20hospital%20background%2C%20realistic%20photo&image_size=portrait_4_3'
WHERE Username = 'bs_thao';

-- 6.4. Dữ liệu Dịch vụ (kèm mô tả & biểu tượng hiển thị trên trang chủ)
INSERT INTO DichVuCLS (TenDichVu, LoaiDichVu, DonGia, MoTa, BieuTuong) VALUES
(N'Khám tổng quát', N'Khám bệnh', 600000, N'Gói khám sức khỏe tổng quát với đầy đủ các xét nghiệm cơ bản và chuyên sâu, giúp phát hiện sớm các nguy cơ tiềm ẩn.', N'fas fa-stethoscope'),
(N'Chuyên khoa Tim mạch', N'Khám bệnh', 500000, N'Khám, tư vấn, điều trị các bệnh lý về tim mạch, huyết áp cao, cholesterol cao, đo điện tim, siêu âm tim.', N'fas fa-heart-circle-check'),
(N'Chuyên khoa Nhi', N'Khám bệnh', 350000, N'Khám và điều trị bệnh cho trẻ sơ sinh, trẻ em và vị thành niên, tiêm chủng, tư vấn dinh dưỡng cho bé.', N'fas fa-baby'),
(N'Chuyên khoa Sản', N'Khám bệnh', 450000, N'Khám thai định kỳ, siêu âm thai 2D/3D/4D, khám phụ khoa, sàng lọc trước sinh, tư vấn kế hoạch hóa gia đình.', N'fas fa-person-pregnant'),
(N'Chuyên khoa Thần kinh', N'Khám bệnh', 500000, N'Khám, điều trị các bệnh về thần kinh trung ương và ngoại vi, đau đầu mãn tính, động kinh, tai biến mạch máu não.', N'fas fa-brain'),
(N'Chuyên khoa Hô hấp', N'Khám bệnh', 400000, N'Khám và điều trị các bệnh về đường hô hấp, hen suyễn, viêm phế quản, lao phổi, khám sức khỏe nghề nghiệp.', N'fas fa-lungs'),
(N'Chuyên khoa Tiêu hóa', N'Khám bệnh', 420000, N'Khám, nội soi tiêu hóa, điều trị các bệnh dạ dày, đại tràng, gan mật, đường tiêu hóa, tư vấn dinh dưỡng.', N'fas fa-utensils'),
(N'Dịch vụ tiêm chủng', N'Tiêm chủng', 0, N'Tiêm chủng đầy đủ các loại vaccine cho trẻ em và người lớn, nguồn gốc rõ ràng, quy trình đảm bảo an toàn.', N'fas fa-syringe'),
(N'Xét nghiệm y khoa', N'Xét nghiệm', 50000, N'Xét nghiệm máu, nước tiểu, sinh hóa, miễn dịch, vi sinh... với máy móc hiện đại, kết quả nhanh, chính xác.', N'fas fa-flask-vial'),
(N'Chẩn đoán hình ảnh', N'Chẩn đoán hình ảnh', 200000, N'Siêu âm màu 4D, X-quang số, CT scan, MRI 1.5T với đội ngũ bác sĩ nhiều kinh nghiệm đọc kết quả.', N'fas fa-x-ray'),
(N'Chuyên khoa Răng hàm mặt', N'Khám bệnh', 300000, N'Khám tổng quát, nha khoa thẩm mỹ, bọc răng sứ, cấy ghép implant, niềng răng, chữa tủy, nhổ răng khôn.', N'fas fa-tooth'),
(N'Nhà thuốc Online', N'Dược phẩm', 0, N'Mua thuốc theo đơn, tư vấn dược miễn phí, giao tận nơi trong 2 giờ nội thành, đóng gói cẩn thận, giá tốt.', N'fas fa-pills'),
(N'Xét nghiệm công thức máu toàn phần', N'Xét nghiệm', 150000, N'Phân tích các chỉ số hồng cầu, bạch cầu, tiểu cầu giúp tầm soát thiếu máu và nhiễm trùng.', N'fas fa-droplet'),
(N'Chụp X-Quang Ngực thẳng', N'Chẩn đoán hình ảnh', 200000, N'Chụp phim phổi và lồng ngực, hỗ trợ chẩn đoán viêm phổi, lao phổi và các bất thường lồng ngực.', N'fas fa-x-ray'),
(N'Siêu âm tổng quát ổ bụng', N'Chẩn đoán hình ảnh', 250000, N'Siêu âm gan, mật, tụy, thận, lách và các cơ quan ổ bụng, phát hiện sỏi và u bất thường.', N'fas fa-wave-square'),
(N'Nội soi Tai Mũi Họng', N'Thủ thuật CLS', 300000, N'Nội soi ống mềm quan sát tai, mũi, họng giúp chẩn đoán chính xác viêm xoang, polyp và các tổn thương.', N'fas fa-ear-listen'),
(N'Đo điện tâm đồ (ECG)', N'Chẩn đoán hình ảnh', 180000, N'Ghi lại hoạt động điện của tim, phát hiện rối loạn nhịp và các dấu hiệu thiếu máu cơ tim.', N'fas fa-heart-pulse');

-- 6.5. Dữ liệu Thuốc (kèm nhóm thuốc, giá nhập/giá bán, tồn kho tối thiểu)
INSERT INTO Thuoc (TenThuoc, HoatChat, DonViTinh, DonGia, SoLuongTon, NhomThuoc, GiaNhap, GiaBan) VALUES
(N'Paracetamol 500mg', N'Paracetamol', N'Viên', 1500, 1000, N'Giảm đau - Hạ sốt', 1500, 1500),
(N'Amoxicillin 500mg', N'Amoxicillin', N'Viên', 3000, 500, N'Kháng sinh', 3000, 3000),
(N'Ibuprofen 400mg', N'Ibuprofen', N'Viên', 2500, 400, N'Giảm đau - Kháng viêm', 2500, 2500),
(N'Siro Ho Prospan', N'Cao lá thường xuân', N'Chai', 85000, 100, N'Thuốc ho - Cảm', 85000, 85000),
(N'Sorbifer Durules', N'Sắt & Acid Folic', N'Viên', 4500, 300, N'Vitamin - Khoáng chất', 4500, 4500);

-- 6.6. Dữ liệu Danh mục nhóm thuốc (lấy từ nhóm thuốc đã có)
INSERT INTO DanhMucNhomThuoc (TenNhomThuoc)
SELECT DISTINCT LTRIM(RTRIM(t.NhomThuoc))
FROM Thuoc AS t
WHERE t.NhomThuoc IS NOT NULL
	AND LTRIM(RTRIM(t.NhomThuoc)) <> N''
	AND NOT EXISTS
	(
		SELECT 1
		FROM DanhMucNhomThuoc AS n
		WHERE n.TenNhomThuoc = LTRIM(RTRIM(t.NhomThuoc))
	);

-- 6.7. Dữ liệu Lịch hẹn mẫu
INSERT INTO LichHen (BenhNhanId, BacSiId, NgayKham, GioKham, SoThuTu, LyDoKham, TrangThai) VALUES
(5, 3, CAST(GETDATE() AS DATE), '08:30:00', 1, N'Sốt nhẹ, đau họng 2 ngày nay', N'Đã đặt'),
(6, 4, CAST(GETDATE() AS DATE), '09:00:00', 1, N'Ù tai, đau tai trái', N'Đã đặt');

GO

-- ============================================================================
-- 7. KIỂM TRA NHANH SAU KHI CHẠY
-- ============================================================================

SELECT DB_NAME() AS DatabaseHienTai;

SELECT RoleId, RoleName FROM Roles ORDER BY RoleId;

SELECT u.UserId, u.Username, u.FullName, u.Phone, u.Email, u.RoleId, u.IsActive,
       k.TenKhoa, u.SoNamKinhNghiem, u.DiemDanhGia, u.SoLuotDanhGia
FROM Users u
LEFT JOIN Khoa k ON k.MaKhoa = u.MaKhoa
ORDER BY u.UserId;

SELECT MaNhomThuoc, TenNhomThuoc FROM DanhMucNhomThuoc ORDER BY TenNhomThuoc;

-- Hai bảng dưới đây do ứng dụng tự tạo khi chạy (nếu chưa chạy app thì bỏ qua).
IF OBJECT_ID(N'dbo.TinNhanLienHe', N'U') IS NOT NULL
    SELECT COUNT(*) AS SoTinNhan FROM dbo.TinNhanLienHe;

IF OBJECT_ID(N'dbo.ThongBao', N'U') IS NOT NULL
    SELECT COUNT(*) AS SoThongBao FROM dbo.ThongBao;
GO
UPDATE Users
SET AnhDaiDien = N'./Content/Images/bs_nam.jpg'
WHERE Username = 'bs_nam';

UPDATE Users
SET AnhDaiDien = N'./Content/Images/bs_hoa.jpg'
WHERE Username = 'bs_hoa';

UPDATE Users
SET AnhDaiDien = N'./Content/Images/bs_tuan.jpg'
WHERE Username = 'bs_tuan';

UPDATE Users
SET AnhDaiDien = N'./Content/Images/bs_lan.jpg'
WHERE Username = 'bs_lan';

UPDATE Users
SET AnhDaiDien = N'./Content/Images/bs_minh.jpg'
WHERE Username = 'bs_minh';

UPDATE Users
SET AnhDaiDien = N'./Content/Images/bs_huong.jpg'
WHERE Username = 'bs_huong';

UPDATE Users
SET AnhDaiDien = N'./Content/Images/bs_dung.jpg'
WHERE Username = 'bs_dung';

UPDATE Users
SET AnhDaiDien = N'./Content/Images/bs_thao.jpg'
WHERE Username = 'bs_thao';

select * from Users