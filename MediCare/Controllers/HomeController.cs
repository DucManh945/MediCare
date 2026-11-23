using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using MediCare.Models;

namespace MediCare.Controllers
{
    public class HomeController : Controller
    {
        private readonly string connectionString = ConfigurationManager.ConnectionStrings["MediCareDb"]?.ConnectionString;

        public ActionResult Index(int servicePage = 1, int doctorPage = 1,
            int? prefillDepartmentId = null, int? prefillDoctorId = null, string prefillReason = null)
        {
            ViewBag.Title = "Trang chủ";
            var model = new HomeIndexViewModel
            {
                ServicePage = servicePage < 1 ? 1 : servicePage,
                DoctorPage = doctorPage < 1 ? 1 : doctorPage
            };
            LoadOptions(model);
            LoadServices(model);
            LoadDoctors(model);

            // Điền sẵn thông tin khi người dùng bấm "Đặt lịch" từ Trợ lý AI (điều hướng kèm query string)
            if (prefillDepartmentId.HasValue && prefillDepartmentId.Value > 0) model.DepartmentId = prefillDepartmentId;
            if (prefillDoctorId.HasValue && prefillDoctorId.Value > 0) model.DoctorId = prefillDoctorId;
            if (!string.IsNullOrWhiteSpace(prefillReason)) model.Reason = prefillReason;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DatLichHen(HomeIndexViewModel model)
        {
            ViewBag.Title = "Trang chủ";
            if (model.ServicePage < 1) model.ServicePage = 1;
            if (model.DoctorPage < 1) model.DoctorPage = 1;
            LoadOptions(model);
            LoadServices(model);
            LoadDoctors(model);

            if (!ModelState.IsValid)
            {
                TempData["AppointmentError"] = "Vui lòng kiểm tra lại các thông tin bắt buộc.";
                return View("Index", model);
            }

            if (model.ExaminationDate.Date < DateTime.Today)
            {
                TempData["AppointmentError"] = "Ngày khám không hợp lệ. Vui lòng chọn ngày từ hôm nay trở đi.";
                return View("Index", model);
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                TempData["AppointmentError"] = "Hệ thống chưa cấu hình kết nối cơ sở dữ liệu.";
                return View("Index", model);
            }

            TimeSpan timeSpan = TimeSpan.FromHours(8);
            if (!string.IsNullOrWhiteSpace(model.ExaminationTime))
            {
                var timeStr = model.ExaminationTime.Split('-')[0].Trim();
                if (!TimeSpan.TryParse(timeStr, out timeSpan)) timeSpan = TimeSpan.FromHours(8);
            }

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    // Bảo đảm bảng LichHen có cột MaKhoa (chuyên khoa) để lưu đầy đủ thông tin đặt lịch.
                    using (var ensureCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.LichHen') AND name = 'MaKhoa')
                        BEGIN
                            ALTER TABLE dbo.LichHen ADD MaKhoa INT NULL;
                            IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_LichHen_Khoa')
                            BEGIN
                                ALTER TABLE dbo.LichHen ADD CONSTRAINT FK_LichHen_Khoa FOREIGN KEY (MaKhoa) REFERENCES dbo.Khoa(MaKhoa);
                            END
                        END", connection))
                    {
                        ensureCmd.ExecuteNonQuery();
                    }

                    // Bác sĩ được chọn phải đang hoạt động và (nếu có chọn chuyên khoa) thuộc đúng chuyên khoa.
                    if (model.DoctorId.HasValue && model.DoctorId.Value > 0)
                    {
                        using (var cmd = new SqlCommand(@"
                            SELECT COUNT(1)
                            FROM dbo.Users u
                            INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId
                            WHERE u.UserId = @DoctorId AND u.IsActive = 1 AND r.RoleName = N'Bác sĩ'
                              AND (@MaKhoa IS NULL OR u.MaKhoa = @MaKhoa);", connection))
                        {
                            cmd.Parameters.Add("@DoctorId", SqlDbType.Int).Value = model.DoctorId.Value;
                            cmd.Parameters.Add("@MaKhoa", SqlDbType.Int).Value = (object)model.DepartmentId ?? DBNull.Value;
                            if (Convert.ToInt32(cmd.ExecuteScalar()) == 0)
                            {
                                TempData["AppointmentError"] = "Bác sĩ đã chọn không hợp lệ hoặc không thuộc chuyên khoa bạn chọn.";
                                return View("Index", model);
                            }
                        }
                    }

                    int patientId = GetCurrentUserId();
                    if (patientId == 0)
                    {
                        using (var cmd = new SqlCommand(@"
                            SELECT TOP 1 u.UserId FROM dbo.Users u
                            INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId
                            WHERE (u.Phone = @Phone OR u.Username = @Phone) AND r.RoleName = N'Bệnh nhân';", connection))
                        {
                            cmd.Parameters.Add("@Phone", SqlDbType.VarChar, 20).Value = model.Phone.Trim();
                            var val = cmd.ExecuteScalar();
                            if (val != null && val != DBNull.Value)
                            {
                                patientId = Convert.ToInt32(val);
                            }
                        }
                    }

                    if (patientId == 0)
                    {
                        using (var cmd = new SqlCommand(@"
                            INSERT INTO dbo.Users (Username, PasswordHash, FullName, Phone, RoleId, IsActive)
                            SELECT @Phone, @PasswordHash, @FullName, @Phone, r.RoleId, 1
                            FROM dbo.Roles r WHERE r.RoleName = N'Bệnh nhân';
                            SELECT CAST(SCOPE_IDENTITY() AS int);", connection))
                        {
                            cmd.Parameters.Add("@Phone", SqlDbType.VarChar, 50).Value = model.Phone.Trim();
                            cmd.Parameters.Add("@PasswordHash", SqlDbType.VarChar, 255).Value = PasswordHasher.Hash("12345678");
                            cmd.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = model.FullName.Trim();
                            var val = cmd.ExecuteScalar();
                            if (val != null && val != DBNull.Value)
                            {
                                patientId = Convert.ToInt32(val);
                            }
                        }
                    }

                    if (patientId == 0)
                    {
                        TempData["AppointmentError"] = "Không thể tạo thông tin bệnh nhân. Vui lòng thử lại.";
                        return View("Index", model);
                    }

                    // Chặn bệnh nhân đặt trùng khung giờ đã có lịch (chưa hủy).
                    using (var cmd = new SqlCommand(@"
                        SELECT COUNT(1) FROM dbo.LichHen
                        WHERE BenhNhanId = @BenhNhanId AND NgayKham = @NgayKham AND GioKham = @GioKham
                          AND TrangThai <> N'Hủy';", connection))
                    {
                        cmd.Parameters.Add("@BenhNhanId", SqlDbType.Int).Value = patientId;
                        cmd.Parameters.Add("@NgayKham", SqlDbType.Date).Value = model.ExaminationDate.Date;
                        cmd.Parameters.Add("@GioKham", SqlDbType.Time).Value = timeSpan;
                        if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                        {
                            TempData["AppointmentError"] = "Bạn đã có một lịch hẹn vào khung giờ này. Vui lòng chọn khung giờ khác.";
                            return View("Index", model);
                        }
                    }

                    // Chặn trùng khung giờ của bác sĩ (khi có chọn bác sĩ cụ thể).
                    if (model.DoctorId.HasValue && model.DoctorId.Value > 0)
                    {
                        using (var cmd = new SqlCommand(@"
                            SELECT COUNT(1) FROM dbo.LichHen
                            WHERE BacSiId = @BacSiId AND NgayKham = @NgayKham AND GioKham = @GioKham
                              AND TrangThai <> N'Hủy';", connection))
                        {
                            cmd.Parameters.Add("@BacSiId", SqlDbType.Int).Value = model.DoctorId.Value;
                            cmd.Parameters.Add("@NgayKham", SqlDbType.Date).Value = model.ExaminationDate.Date;
                            cmd.Parameters.Add("@GioKham", SqlDbType.Time).Value = timeSpan;
                            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                            {
                                TempData["AppointmentError"] = "Bác sĩ đã có lịch hẹn vào khung giờ này. Vui lòng chọn bác sĩ hoặc khung giờ khác.";
                                return View("Index", model);
                            }
                        }
                    }

                    int? soThuTu = null;
                    if (model.DoctorId.HasValue && model.DoctorId.Value > 0)
                    {
                        using (var cmd = new SqlCommand(@"
                            SELECT ISNULL(MAX(SoThuTu), 0) + 1 FROM dbo.LichHen
                            WHERE BacSiId = @DoctorId AND NgayKham = @NgayKham;", connection))
                        {
                            cmd.Parameters.Add("@DoctorId", SqlDbType.Int).Value = model.DoctorId.Value;
                            cmd.Parameters.Add("@NgayKham", SqlDbType.Date).Value = model.ExaminationDate.Date;
                            soThuTu = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                    }

                    using (var cmd = new SqlCommand(@"
                        INSERT INTO dbo.LichHen (BenhNhanId, BacSiId, MaKhoa, NgayKham, GioKham, SoThuTu, LyDoKham, TrangThai)
                        VALUES (@BenhNhanId, @BacSiId, @MaKhoa, @NgayKham, @GioKham, @SoThuTu, @LyDoKham, N'Đã đặt');
                        SELECT CAST(SCOPE_IDENTITY() AS int);", connection))
                    {
                        cmd.Parameters.Add("@BenhNhanId", SqlDbType.Int).Value = patientId;
                        cmd.Parameters.Add("@BacSiId", SqlDbType.Int).Value = (object)model.DoctorId ?? DBNull.Value;
                        cmd.Parameters.Add("@MaKhoa", SqlDbType.Int).Value = (object)model.DepartmentId ?? DBNull.Value;
                        cmd.Parameters.Add("@NgayKham", SqlDbType.Date).Value = model.ExaminationDate.Date;
                        cmd.Parameters.Add("@GioKham", SqlDbType.Time).Value = timeSpan;
                        cmd.Parameters.Add("@SoThuTu", SqlDbType.Int).Value = (object)soThuTu ?? DBNull.Value;
                        cmd.Parameters.Add("@LyDoKham", SqlDbType.NVarChar, 500).Value = (object)model.Reason ?? DBNull.Value;

                        var newId = cmd.ExecuteScalar();
                        int appointmentId = Convert.ToInt32(newId);

                        NotificationService.CreateNotification(
                            patientId,
                            "APPOINTMENT_NEW",
                            "Yêu cầu đặt lịch đã được tiếp nhận",
                            $"Yêu cầu đặt lịch khám #{appointmentId} vào ngày {model.ExaminationDate:dd/MM/yyyy} đã được tiếp nhận, đang chờ xác nhận.",
                            appointmentId
                        );

                        if (model.DoctorId.HasValue && model.DoctorId.Value > 0)
                        {
                            NotificationService.CreateNotification(
                                model.DoctorId.Value,
                                "APPOINTMENT_NEW",
                                "Có lịch hẹn mới được tiếp nhận",
                                $"Bạn có lịch hẹn mới #{appointmentId} từ bệnh nhân {model.FullName} vào ngày {model.ExaminationDate:dd/MM/yyyy}.",
                                appointmentId
                            );
                        }

                        TempData["AppointmentMessage"] = "Đặt lịch hẹn thành công! Mã lịch hẹn của bạn là: #" + appointmentId + ". Chúng tôi sẽ liên hệ sớm để xác nhận.";
                        return Redirect(Url.Action("Index", "Home") + "#appointment");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DatLichHen Error: " + ex);
                TempData["AppointmentError"] = "Không thể lưu lịch hẹn. Vui lòng thử lại sau.";
                return View("Index", model);
            }
        }

        [Authorize]
        [HttpGet]
        public ActionResult ThongBao()
        {
            ViewBag.Title = "Thông báo hệ thống";
            var model = new List<NotificationItemViewModel>();
            int currentUserId = GetCurrentUserId();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var createCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ThongBao')
                        BEGIN
                            CREATE TABLE ThongBao (
                                MaThongBao INT IDENTITY(1,1) PRIMARY KEY,
                                NguoiNhanId INT NOT NULL,
                                LoaiThongBao NVARCHAR(50) NOT NULL,
                                TieuDe NVARCHAR(150) NOT NULL,
                                NoiDung NVARCHAR(500) NOT NULL,
                                MaLichHen INT NULL,
                                DaDoc BIT NOT NULL DEFAULT 0,
                                NgayTao DATETIME NOT NULL DEFAULT GETDATE(),
                                EmailDaGui BIT NOT NULL DEFAULT 0
                            );
                        END", connection))
                    {
                        createCmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SqlCommand(@"
                        SELECT MaThongBao, NguoiNhanId, LoaiThongBao, TieuDe, NoiDung, MaLichHen, DaDoc, NgayTao
                        FROM dbo.ThongBao
                        WHERE NguoiNhanId = @UserId
                        ORDER BY MaThongBao DESC;", connection))
                    {
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.Add(new NotificationItemViewModel
                                {
                                    MaThongBao = r.GetInt32(0),
                                    NguoiNhanId = r.GetInt32(1),
                                    LoaiThongBao = r.GetString(2),
                                    TieuDe = r.GetString(3),
                                    NoiDung = r.GetString(4),
                                    MaLichHen = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
                                    DaDoc = r.GetBoolean(6),
                                    NgayTao = r.GetDateTime(7)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ThongBao error: " + ex);
            }

            return View(model);
        }

        [Authorize]
        [HttpGet]
        public JsonResult DemThongBaoChuaDoc()
        {
            int currentUserId = GetCurrentUserId();
            int count = 0;
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.ThongBao WHERE NguoiNhanId = @UserId AND DaDoc = 0;", connection))
                    {
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        var val = cmd.ExecuteScalar();
                        if (val != null && val != DBNull.Value) count = Convert.ToInt32(val);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DemThongBaoChuaDoc error: " + ex);
            }
            return Json(new { unread = count }, JsonRequestBehavior.AllowGet);
        }

        [Authorize]
        [HttpGet]
        public JsonResult ThongBaoJson()
        {
            int currentUserId = GetCurrentUserId();
            var items = new List<object>();
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        SELECT TieuDe, NoiDung, DaDoc, NgayTao, MaLichHen
                        FROM dbo.ThongBao
                        WHERE NguoiNhanId = @UserId
                        ORDER BY MaThongBao DESC;", connection))
                    {
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                items.Add(new
                                {
                                    TieuDe = r.GetString(0),
                                    NoiDung = r.GetString(1),
                                    DaDoc = r.GetBoolean(2),
                                    NgayTao = r.GetDateTime(3).ToString("dd/MM/yyyy HH:mm"),
                                    MaLichHen = r.IsDBNull(4) ? (int?)null : r.GetInt32(4)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ThongBaoJson error: " + ex);
            }
            return Json(new { items = items }, JsonRequestBehavior.AllowGet);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DanhDauDaDoc(int id)
        {
            int currentUserId = GetCurrentUserId();
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand("UPDATE dbo.ThongBao SET DaDoc = 1 WHERE MaThongBao = @Id AND NguoiNhanId = @UserId;", connection))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DanhDauDaDoc error: " + ex);
            }
            return RedirectToAction("ThongBao");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GuiTinNhanLienHe(ContactMessageViewModel model)
        {
            if (model == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(model.HoTen) || string.IsNullOrWhiteSpace(model.NoiDung))
            {
                TempData["ContactError"] = "Vui lòng điền đầy đủ họ tên và nội dung liên hệ.";
                return Redirect(Url.Action("Index", "Home") + "#contact");
            }

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var createCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TinNhanLienHe')
                        BEGIN
                            CREATE TABLE TinNhanLienHe (
                                MaTinNhan INT IDENTITY(1,1) PRIMARY KEY,
                                HoTen NVARCHAR(100) NOT NULL,
                                Email NVARCHAR(100) NULL,
                                SoDienThoai NVARCHAR(20) NULL,
                                TieuDe NVARCHAR(150) NULL,
                                NoiDung NVARCHAR(MAX) NOT NULL,
                                TrangThai NVARCHAR(50) NOT NULL DEFAULT N'Chưa xử lý',
                                NgayGui DATETIME NOT NULL DEFAULT GETDATE(),
                                GhiChuXuLy NVARCHAR(500) NULL
                            );
                        END", connection))
                    {
                        createCmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SqlCommand(@"
                        INSERT INTO dbo.TinNhanLienHe (HoTen, Email, SoDienThoai, TieuDe, NoiDung, TrangThai, NgayGui)
                        VALUES (@HoTen, @Email, @SoDienThoai, @TieuDe, @NoiDung, N'Chưa xử lý', GETDATE());", connection))
                    {
                        cmd.Parameters.Add("@HoTen", SqlDbType.NVarChar, 100).Value = model.HoTen.Trim();
                        cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 100).Value = (object)model.Email?.Trim() ?? DBNull.Value;
                        cmd.Parameters.Add("@SoDienThoai", SqlDbType.NVarChar, 20).Value = (object)model.SoDienThoai?.Trim() ?? DBNull.Value;
                        cmd.Parameters.Add("@TieuDe", SqlDbType.NVarChar, 150).Value = (object)model.TieuDe?.Trim() ?? DBNull.Value;
                        cmd.Parameters.Add("@NoiDung", SqlDbType.NVarChar, -1).Value = model.NoiDung.Trim();
                        cmd.ExecuteNonQuery();
                    }
                }
                TempData["ContactMessage"] = "Cảm ơn bạn đã liên hệ! Tin nhắn của bạn đã được gửi thành công, ban quản lý phòng khám sẽ phản hồi sớm nhất.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GuiTinNhanLienHe error: " + ex);
                TempData["ContactError"] = "Không thể gửi tin nhắn liên hệ. Vui lòng thử lại sau.";
            }

            return Redirect(Url.Action("Index", "Home") + "#contact");
        }

        [Authorize]
        [HttpGet]
        public ActionResult LichHenCuaToi()
        {
            ViewBag.Title = "Lịch hẹn của tôi";
            var model = new List<AppointmentItemViewModel>();
            int currentUserId = GetCurrentUserId();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        SELECT lh.MaLichHen, lh.BenhNhanId, bn.FullName, bn.Phone,
                               lh.BacSiId, ISNULL(bs.FullName, N'Chưa phân công'),
                               lh.NgayKham, lh.GioKham, lh.SoThuTu, lh.LyDoKham, lh.TrangThai
                        FROM dbo.LichHen lh
                        INNER JOIN dbo.Users bn ON bn.UserId = lh.BenhNhanId
                        LEFT JOIN dbo.Users bs ON bs.UserId = lh.BacSiId
                        WHERE lh.BenhNhanId = @UserId
                        ORDER BY lh.NgayKham DESC, lh.GioKham DESC;", connection))
                    {
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.Add(new AppointmentItemViewModel
                                {
                                    MaLichHen = r.GetInt32(0),
                                    BenhNhanId = r.GetInt32(1),
                                    BenhNhanName = r.GetString(2),
                                    BenhNhanPhone = r.IsDBNull(3) ? null : r.GetString(3),
                                    BacSiId = r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
                                    BacSiName = r.GetString(5),
                                    NgayKham = r.GetDateTime(6),
                                    GioKham = r.GetTimeSpan(7),
                                    SoThuTu = r.IsDBNull(8) ? (int?)null : r.GetInt32(8),
                                    LyDoKham = r.IsDBNull(9) ? null : r.GetString(9),
                                    TrangThai = r.GetString(10)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LichHenCuaToi error: " + ex);
            }

            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult HuyLichHen(int id)
        {
            int currentUserId = GetCurrentUserId();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    int? doctorId = null;
                    using (var checkCmd = new SqlCommand(@"
                        SELECT BacSiId FROM dbo.LichHen
                        WHERE MaLichHen = @Id AND BenhNhanId = @UserId AND TrangThai = N'Đã đặt';", connection))
                    {
                        checkCmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        checkCmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        var val = checkCmd.ExecuteScalar();
                        if (val == null || val == DBNull.Value)
                        {
                            TempData["AppointmentError"] = "Không thể hủy lịch hẹn này (lịch không tồn tại hoặc đã được xử lý).";
                            return RedirectToAction("LichHenCuaToi");
                        }
                        doctorId = Convert.ToInt32(val);
                    }

                    using (var cmd = new SqlCommand(@"
                        UPDATE dbo.LichHen SET TrangThai = N'Hủy'
                        WHERE MaLichHen = @Id AND BenhNhanId = @UserId AND TrangThai = N'Đã đặt';", connection))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        cmd.ExecuteNonQuery();
                    }

                    NotificationService.CreateNotification(
                        currentUserId,
                        "APPOINTMENT_CANCELLED",
                        "Lịch hẹn đã được hủy",
                        $"Bạn đã hủy lịch hẹn #{id}.",
                        id
                    );

                    if (doctorId.HasValue && doctorId.Value > 0)
                    {
                        NotificationService.CreateNotification(
                            doctorId.Value,
                            "APPOINTMENT_CANCELLED",
                            "Bệnh nhân đã hủy lịch hẹn",
                            $"Bệnh nhân đã hủy lịch hẹn #{id}.",
                            id
                        );
                    }

                    TempData["AppointmentMessage"] = "Đã hủy lịch hẹn #" + id + " thành công.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("HuyLichHen error: " + ex);
                TempData["AppointmentError"] = "Không thể hủy lịch hẹn. Vui lòng thử lại sau.";
            }

            return RedirectToAction("LichHenCuaToi");
        }

        [HttpGet]
        public JsonResult GuiEmailNhacLich(string key = null)
        {
            string cronKey = ConfigurationManager.AppSettings["CronSecretKey"] ?? "MediCareCronSecret123";
            if (!string.Equals(key, cronKey, StringComparison.Ordinal))
            {
                return Json(new { success = false, message = "Khoa secret key khong hop le." }, JsonRequestBehavior.AllowGet);
            }

            int sentCount = 0;
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        SELECT lh.MaLichHen, bn.Email, bn.FullName, ISNULL(bs.FullName, N'Chua phan cong'), lh.NgayKham, lh.GioKham
                        FROM dbo.LichHen lh
                        INNER JOIN dbo.Users bn ON bn.UserId = lh.BenhNhanId
                        LEFT JOIN dbo.Users bs ON bs.UserId = lh.BacSiId
                        WHERE lh.NgayKham = CONVERT(date, DATEADD(day, 1, GETDATE()))
                          AND lh.TrangThai IN (N'Đã đặt', N'Đã khám')
                          AND NOT EXISTS (
                              SELECT 1 FROM dbo.ThongBao tb
                              WHERE tb.MaLichHen = lh.MaLichHen
                                AND tb.LoaiThongBao = N'REMINDER'
                                AND tb.EmailDaGui = 1
                          );", connection))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            var list = new List<Tuple<int, string, string, string, DateTime, TimeSpan>>();
                            while (r.Read())
                            {
                                list.Add(new Tuple<int, string, string, string, DateTime, TimeSpan>(
                                    r.GetInt32(0),
                                    r.IsDBNull(1) ? "" : r.GetString(1),
                                    r.GetString(2),
                                    r.GetString(3),
                                    r.GetDateTime(4),
                                    (TimeSpan)r.GetTimeSpan(5)
                                ));
                            }
                            r.Close();

                            foreach (var item in list)
                            {
                                int appointmentId = item.Item1;
                                string email = item.Item2;
                                string patientName = item.Item3;
                                string doctorName = item.Item4;
                                DateTime date = item.Item5;
                                TimeSpan time = item.Item6;

                                if (!string.IsNullOrWhiteSpace(email))
                                {
                                    string mailBody = $@"<h3>Nhắc Nhở Lịch Hẹn Khám Sắp Tới - MediCare</h3>
                                        <p>Xin chào <strong>{patientName}</strong>,</p>
                                        <p>Phòng khám MediCare nhắc bạn về lịch hẹn <strong>#{appointmentId}</strong> vào ngày mai:</p>
                                        <ul>
                                            <li><strong>Ngày khám:</strong> {date:dd/MM/yyyy}</li>
                                            <li><strong>Giờ khám:</strong> {time:hh\\:mm}</li>
                                            <li><strong>Bác sĩ:</strong> {doctorName}</li>
                                        </ul>
                                        <p>Vui lòng đến đúng giờ để được phục vụ tốt nhất!</p>";

                                    bool sent = EmailService.SendEmail(email, $"[MediCare] Nhắc lịch hẹn #{appointmentId} ngày mai", mailBody);
                                    if (sent) sentCount++;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GuiEmailNhacLich error: " + ex);
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { success = true, sentCount = sentCount }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public JsonResult GetDoctorsByDepartment(int? departmentId)
        {
            var doctors = new List<object>();
            if (string.IsNullOrWhiteSpace(connectionString)) return Json(doctors, JsonRequestBehavior.AllowGet);

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = @"
                        SELECT u.UserId, u.FullName
                        FROM dbo.Users u
                        INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId
                        WHERE r.RoleName = N'Bác sĩ' AND u.IsActive = 1";

                    if (departmentId.HasValue && departmentId.Value > 0)
                    {
                        query += " AND u.MaKhoa = @DepartmentId";
                    }
                    query += " ORDER BY u.FullName;";

                    using (var cmd = new SqlCommand(query, connection))
                    {
                        if (departmentId.HasValue && departmentId.Value > 0)
                        {
                            cmd.Parameters.Add("@DepartmentId", SqlDbType.Int).Value = departmentId.Value;
                        }
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                doctors.Add(new { id = r.GetInt32(0), name = r.GetString(1) });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetDoctorsByDepartment Error: " + ex);
            }

            return Json(doctors, JsonRequestBehavior.AllowGet);
        }

        private int GetCurrentUserId()
        {
            if (!User.Identity.IsAuthenticated) return 0;
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand("SELECT UserId FROM dbo.Users WHERE Username = @Identity OR Email = @Identity OR Phone = @Identity;", connection))
                    {
                        cmd.Parameters.Add("@Identity", SqlDbType.VarChar, 100).Value = User.Identity.Name;
                        var val = cmd.ExecuteScalar();
                        if (val != null && val != DBNull.Value) return Convert.ToInt32(val);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetCurrentUserId error: " + ex);
            }
            return 0;
        }

        private void LoadOptions(BookAppointmentViewModel model)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return;

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    using (var cmd = new SqlCommand("SELECT MaKhoa, TenKhoa FROM dbo.Khoa ORDER BY TenKhoa;", connection))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            model.Departments.Add(new DepartmentOptionViewModel
                            {
                                MaKhoa = r.GetInt32(0),
                                TenKhoa = r.GetString(1)
                            });
                        }
                    }

                    using (var cmd = new SqlCommand(@"
                        SELECT u.UserId, u.FullName
                        FROM dbo.Users u
                        INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId
                        WHERE r.RoleName = N'Bác sĩ' AND u.IsActive = 1
                        ORDER BY u.FullName;", connection))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            model.Doctors.Add(new DoctorOptionViewModel
                            {
                                UserId = r.GetInt32(0),
                                FullName = r.GetString(1)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadOptions Error: " + ex);
            }
        }

        private void LoadServices(HomeIndexViewModel model)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return;

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM dbo.DichVuCLS;", connection))
                    {
                        model.ServiceTotalItems = Convert.ToInt32(countCmd.ExecuteScalar());
                    }

                    int pageSize = model.ServicePageSize > 0 ? model.ServicePageSize : 6;
                    int totalPages = (int)Math.Ceiling((double)model.ServiceTotalItems / pageSize);
                    if (totalPages < 1) totalPages = 1;
                    int page = model.ServicePage < 1 ? 1 : model.ServicePage;
                    if (page > totalPages) page = totalPages;

                    model.ServicePage = page;
                    model.ServiceTotalPages = totalPages;

                    using (var cmd = new SqlCommand(@"
                        SELECT MaDichVu, TenDichVu, LoaiDichVu, DonGia, MoTa, BieuTuong
                        FROM dbo.DichVuCLS
                        ORDER BY MaDichVu
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", connection))
                    {
                        cmd.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.Services.Add(new ServiceViewModel
                                {
                                    ServiceId = r.GetInt32(0),
                                    Name = r.GetString(1),
                                    Type = r.IsDBNull(2) ? null : r.GetString(2),
                                    Price = r.IsDBNull(3) ? 0m : r.GetDecimal(3),
                                    Description = r.IsDBNull(4) ? null : r.GetString(4),
                                    Icon = r.IsDBNull(5) ? null : r.GetString(5)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadServices Error: " + ex);
            }
        }

        private void LoadDoctors(HomeIndexViewModel model)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return;

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    using (var countCmd = new SqlCommand(@"
                        SELECT COUNT(*)
                        FROM dbo.Users u
                        INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId
                        WHERE r.RoleName = N'Bác sĩ' AND u.IsActive = 1;", connection))
                    {
                        model.DoctorTotalItems = Convert.ToInt32(countCmd.ExecuteScalar());
                    }

                    int pageSize = model.DoctorPageSize > 0 ? model.DoctorPageSize : 8;
                    int totalPages = (int)Math.Ceiling((double)model.DoctorTotalItems / pageSize);
                    if (totalPages < 1) totalPages = 1;
                    int page = model.DoctorPage < 1 ? 1 : model.DoctorPage;
                    if (page > totalPages) page = totalPages;

                    model.DoctorPage = page;
                    model.DoctorTotalPages = totalPages;

                    using (var cmd = new SqlCommand(@"
                        SELECT u.UserId, u.FullName, u.Phone, u.Email, u.GioiTinh, u.NgaySinh,
                               u.MaKhoa, k.TenKhoa, u.SoNamKinhNghiem, u.DiemDanhGia,
                               u.SoLuotDanhGia, u.GioiThieu, u.AnhDaiDien, u.IsActive
                        FROM dbo.Users u
                        INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId
                        LEFT JOIN dbo.Khoa k ON k.MaKhoa = u.MaKhoa
                        WHERE r.RoleName = N'Bác sĩ' AND u.IsActive = 1
                        ORDER BY u.FullName
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", connection))
                    {
                        cmd.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.DoctorProfiles.Add(new DoctorViewModel
                                {
                                    UserId = r.GetInt32(0),
                                    FullName = r.GetString(1),
                                    Phone = r.IsDBNull(2) ? null : r.GetString(2),
                                    Email = r.IsDBNull(3) ? null : r.GetString(3),
                                    Gender = r.IsDBNull(4) ? null : r.GetString(4),
                                    BirthDate = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5),
                                    DepartmentId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                                    DepartmentName = r.IsDBNull(7) ? null : r.GetString(7),
                                    ExperienceYears = r.IsDBNull(8) ? (int?)null : r.GetInt32(8),
                                    Rating = r.IsDBNull(9) ? (decimal?)null : r.GetDecimal(9),
                                    ReviewCount = r.IsDBNull(10) ? (int?)null : r.GetInt32(10),
                                    Description = r.IsDBNull(11) ? null : r.GetString(11),
                                    ImageUrl = r.IsDBNull(12) ? null : r.GetString(12),
                                    IsActive = r.GetBoolean(13)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadDoctors Error: " + ex);
            }
        }

        public ActionResult About()
        {
            ViewBag.Title = "Giới thiệu";
            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Title = "Liên hệ";
            return View();
        }
    }
}
