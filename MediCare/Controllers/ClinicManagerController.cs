using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using MediCare.Models;

namespace MediCare.Controllers
{
    [Authorize(Roles = "Quản lý phòng khám,Quản lý,Admin,Admin hệ thống")]
    public class ClinicManagerController : Controller
    {
        private readonly string connectionString = ConfigurationManager.ConnectionStrings["MediCareDb"]?.ConnectionString;

        public ActionResult Index()
        {
            SetDashboard("Tổng quan");
            var model = new ClinicManagerDashboardViewModel { Today = DateTime.Today };
            try
            {
                LoadDashboard(model);
            }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("Dashboard error: " + ex);
                model.DatabaseWarning = "Không thể tải đầy đủ dữ liệu dashboard. Vui lòng kiểm tra kết nối database và các cột mở rộng của bảng Thuoc.";
            }
            return View(model);
        }

        [HttpGet]
        public FileContentResult XuatBaoCaoHoSoBenhAn(string search = null, int? doctorId = null, int? departmentId = null,
            DateTime? fromDate = null, DateTime? toDate = null, string tab = "all")
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
                return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Khoang ngay khong hop le\r\n")).ToArray(), "text/csv", "ho-so-benh-an-loi-ngay.csv");

            var model = new MedicalRecordSearchViewModel { Search = search?.Trim(), DoctorId = doctorId, DepartmentId = departmentId, FromDate = fromDate, ToDate = toDate, Tab = string.IsNullOrWhiteSpace(tab) ? "all" : tab, Page = 1, PageSize = int.MaxValue };
            LoadMedicalRecordSearch(model);
            var csv = new StringBuilder();
            csv.AppendLine("Ma benh nhan,Ho ten,Bac si phu trach,Chuyen khoa,Ngay lap,Lan kham cuoi,Trang thai");
            foreach (var item in model.Items)
                csv.AppendLine(string.Join(",", new[] { CsvExcel(item.PatientCode), CsvExcel(item.PatientName), CsvExcel(item.DoctorName), CsvExcel(item.DepartmentName), CsvExcel(item.CreatedDate.ToString("yyyy-MM-dd")), CsvExcel(item.LastVisitDate.ToString("yyyy-MM-dd")), CsvExcel(item.Status) }));
            if (model.Items.Count == 0)
                csv.AppendLine("Khong co du lieu phu hop voi bo loc hien tai");
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return File(bytes, "text/csv", "ho-so-benh-an-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
        }

        private static string CsvExcel(string value)
        {
            value = value ?? string.Empty;
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private void LoadMedicalRecordCreateOptions(MedicalRecordCreateViewModel model)
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                model.Doctors = LoadAppointmentDoctors(connection);
                model.Patients = new List<PatientOptionViewModel>();
                using (var command = new SqlCommand(@"SELECT u.UserId, u.FullName, u.Phone FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE r.RoleName = N'Bệnh nhân' AND u.IsActive = 1 ORDER BY u.FullName;", connection))
                using (var reader = command.ExecuteReader())
                while (reader.Read())
                    model.Patients.Add(new PatientOptionViewModel { UserId = reader.GetInt32(0), FullName = reader.GetString(1), Phone = reader.IsDBNull(2) ? null : reader.GetString(2) });
            }
        }

        private static void AddMedicalRecordCreateParameters(SqlCommand command, MedicalRecordCreateViewModel model)
        {
            command.Parameters.Add("@AppointmentId", SqlDbType.Int).Value = (object)model.AppointmentId ?? DBNull.Value;
            command.Parameters.Add("@PatientId", SqlDbType.Int).Value = model.PatientId.Value;
            command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = model.DoctorId.Value;
            command.Parameters.Add("@ExaminationDate", SqlDbType.DateTime).Value = model.ExaminationDate;
            command.Parameters.Add("@BloodPressure", SqlDbType.VarChar, 20).Value = (object)model.BloodPressure ?? DBNull.Value;
            command.Parameters.Add("@Pulse", SqlDbType.Int).Value = (object)model.Pulse ?? DBNull.Value;
            command.Parameters.Add("@Temperature", SqlDbType.Decimal).Value = (object)model.Temperature ?? DBNull.Value;
            command.Parameters.Add("@Weight", SqlDbType.Decimal).Value = (object)model.Weight ?? DBNull.Value;
            command.Parameters.Add("@Symptoms", SqlDbType.NVarChar, -1).Value = (object)model.Symptoms ?? DBNull.Value;
            command.Parameters.Add("@Diagnosis", SqlDbType.NVarChar, -1).Value = (object)model.Diagnosis ?? DBNull.Value;
            command.Parameters.Add("@Instructions", SqlDbType.NVarChar, -1).Value = (object)model.Instructions ?? DBNull.Value;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = model.Status;
        }

        [HttpGet]
        public ActionResult TaoHoSoBenhAn()
        {
            SetDashboard("Tạo hồ sơ bệnh án");
            var model = new MedicalRecordCreateViewModel();
            LoadMedicalRecordCreateOptions(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult TaoHoSoBenhAn(MedicalRecordCreateViewModel model)
        {
            SetDashboard("Tạo hồ sơ bệnh án");
            LoadMedicalRecordCreateOptions(model);
            if (model.Status != "Đang khám" && model.Status != "Hoàn thành" && model.Status != "Hủy")
                ModelState.AddModelError("Status", "Trạng thái hồ sơ không hợp lệ.");
            if (model.ExaminationDate > DateTime.Now.AddMinutes(5))
                ModelState.AddModelError("ExaminationDate", "Ngày khám không được ở tương lai.");
            if (!ModelState.IsValid) return View(model);
            try
            {
                using (var connection = CreateConnection())
                using (var command = new SqlCommand(@"
                    INSERT INTO dbo.PhieuKhamBenh
                        (MaLichHen, BenhNhanId, BacSiId, NgayKham, HuyetAp, Mach, NhietDo, CanNang, TrieuChung, ChanDoan, LoiDan, TrangThai)
                    SELECT @AppointmentId, @PatientId, @DoctorId, @ExaminationDate, @BloodPressure, @Pulse, @Temperature, @Weight,
                           @Symptoms, @Diagnosis, @Instructions, @Status
                    WHERE EXISTS (SELECT 1 FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE u.UserId = @PatientId AND r.RoleName = N'Bệnh nhân')
                      AND EXISTS (SELECT 1 FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE u.UserId = @DoctorId AND r.RoleName = N'Bác sĩ' AND u.IsActive = 1)
                      AND (@AppointmentId IS NULL OR EXISTS (SELECT 1 FROM dbo.LichHen WHERE MaLichHen = @AppointmentId AND BenhNhanId = @PatientId AND BacSiId = @DoctorId));
                    SELECT CAST(SCOPE_IDENTITY() AS int);", connection))
                {
                    AddMedicalRecordCreateParameters(command, model);
                    connection.Open();
                    var id = command.ExecuteScalar();
                    if (id == null || id == DBNull.Value)
                    {
                        ModelState.AddModelError("", "Bệnh nhân, bác sĩ hoặc lịch hẹn không hợp lệ. Hồ sơ chưa được lưu.");
                        return View(model);
                    }
                    TempData["MedicalRecordMessage"] = "Đã tạo hồ sơ bệnh án thành công.";
                    return RedirectToAction("HoSoBenhAn");
                }
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("Create medical record error: " + ex);
                ModelState.AddModelError("", "Không thể lưu hồ sơ bệnh án. Dữ liệu chưa được ghi vào database.");
                return View(model);
            }
        }

        public ActionResult QuanLyTaiKhoan(string tab = "doctor", string search = null, string role = null, string khoa = null, string status = null, int page = 1)
        {
            SetDashboard("Quản lý tài khoản & Nhân sự");
            var model = new UserManagementListViewModel
            {
                Tab = string.IsNullOrWhiteSpace(tab) ? "doctor" : tab.Trim().ToLowerInvariant(),
                Search = search?.Trim(),
                Role = role?.Trim(),
                Khoa = khoa?.Trim(),
                Status = status?.Trim(),
                Page = Math.Max(page, 1),
                PageSize = 10
            };

            try
            {
                LoadUserList(model);
            }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("User list error: " + ex);
                ViewBag.DatabaseError = "Không thể tải danh sách tài khoản. Hãy kiểm tra kết nối database.";
            }

            return View(model);
        }

        // Route tương thích cố định cho URL cũ /ClinicManager/QuanLyThuoc.
        public ActionResult QuanLyThuocRoute(string search = null, string group = null, string status = null, string expiryStatus = null, int page = 1)
        {
            return RedirectToAction("QuanLyThuoc", new { search, group, status, expiryStatus, page });
        }

        public ActionResult QuanLyThuoc(string search = null, string group = null, string status = null, string expiryStatus = null, int page = 1)
        {
            SetDashboard("Quản lý thuốc & Kho Dược");
            var model = new MedicineListViewModel
            {
                Search = search?.Trim(), Group = group?.Trim(), Status = status?.Trim(),
                ExpiryStatus = expiryStatus?.Trim(), Page = Math.Max(page, 1), PageSize = 10
            };
            try { LoadMedicineList(model); }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("Medicine list error: " + ex);
                ViewBag.DatabaseError = "Không thể tải dữ liệu thuốc. Hãy kiểm tra kết nối và migration thuốc.";
            }
            return View(model);
        }

        [HttpGet]
        public ActionResult QuanLyNhomThuoc()
        {
            SetDashboard("Quản lý nhóm thuốc");
            var model = new MedicineGroupListViewModel();
            try { LoadMedicineGroupList(model); }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("Medicine group list error: " + ex);
                if (ex is SqlException && ((SqlException)ex).Number == 208)
                {
                    LoadLegacyMedicineGroupList(model);
                    ViewBag.DatabaseWarning = "Danh mục nhóm thuốc chưa được tạo. Danh sách hiện lấy từ NhomThuoc trong dbo.Thuoc; hãy chạy QuanLyPhongKham_MediCare.sql để bật thêm/sửa/xóa nhóm.";
                }
                else
                {
                    ViewBag.DatabaseError = "Không thể tải nhóm thuốc. Hãy kiểm tra connection string và schema database.";
                }
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ThemNhomThuoc(MedicineGroupListViewModel model)
        {
            if (!MedicineGroupCatalogExists())
            {
                TempData["MedicineError"] = "Chưa có bảng danh mục nhóm thuốc. Hãy chạy QuanLyPhongKham_MediCare.sql trước.";
                return RedirectToAction("QuanLyNhomThuoc");
            }
            model.NewGroupName = model.NewGroupName?.Trim();
            if (!ModelState.IsValid)
            {
                LoadMedicineGroupList(model);
                return View("QuanLyNhomThuoc", model);
            }

            try
            {
                using (var connection = CreateConnection())
                using (var command = new SqlCommand("INSERT INTO dbo.DanhMucNhomThuoc (TenNhomThuoc) VALUES (@Name);", connection))
                {
                    command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = model.NewGroupName;
                    connection.Open();
                    command.ExecuteNonQuery();
                }
                TempData["MedicineMessage"] = "Đã thêm nhóm thuốc.";
                return RedirectToAction("QuanLyNhomThuoc");
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("Medicine group insert error: " + ex);
                ModelState.AddModelError("NewGroupName", ex.Number == 2601 || ex.Number == 2627 ? "Tên nhóm thuốc đã tồn tại." : "Không thể thêm nhóm thuốc.");
                LoadMedicineGroupList(model);
                return View("QuanLyNhomThuoc", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SuaNhomThuoc(int id, string tenNhomThuoc)
        {
            if (!MedicineGroupCatalogExists())
            {
                TempData["MedicineError"] = "Chưa có bảng danh mục nhóm thuốc. Hãy chạy QuanLyPhongKham_MediCare.sql trước.";
                return RedirectToAction("QuanLyNhomThuoc");
            }
            var newName = tenNhomThuoc?.Trim();
            if (string.IsNullOrWhiteSpace(newName) || newName.Length > 100)
            {
                TempData["MedicineError"] = "Tên nhóm thuốc bắt buộc và tối đa 100 ký tự.";
                return RedirectToAction("QuanLyNhomThuoc");
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        try
                        {
                            string oldName;
                            using (var find = new SqlCommand("SELECT TenNhomThuoc FROM dbo.DanhMucNhomThuoc WHERE MaNhomThuoc = @Id;", connection, transaction))
                            {
                                find.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                                oldName = Convert.ToString(find.ExecuteScalar());
                            }
                            if (string.IsNullOrEmpty(oldName))
                            {
                                transaction.Rollback();
                                TempData["MedicineError"] = "Không tìm thấy nhóm thuốc.";
                                return RedirectToAction("QuanLyNhomThuoc");
                            }

                            using (var updateGroup = new SqlCommand("UPDATE dbo.DanhMucNhomThuoc SET TenNhomThuoc = @NewName WHERE MaNhomThuoc = @Id;", connection, transaction))
                            {
                                updateGroup.Parameters.Add("@NewName", SqlDbType.NVarChar, 100).Value = newName;
                                updateGroup.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                                updateGroup.ExecuteNonQuery();
                            }
                            using (var updateMedicines = new SqlCommand("UPDATE dbo.Thuoc SET NhomThuoc = @NewName WHERE LTRIM(RTRIM(NhomThuoc)) = @OldName;", connection, transaction))
                            {
                                updateMedicines.Parameters.Add("@NewName", SqlDbType.NVarChar, 100).Value = newName;
                                updateMedicines.Parameters.Add("@OldName", SqlDbType.NVarChar, 100).Value = oldName;
                                updateMedicines.ExecuteNonQuery();
                            }
                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                TempData["MedicineMessage"] = "Đã đổi tên nhóm và cập nhật các thuốc thuộc nhóm.";
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("Medicine group update error: " + ex);
                TempData["MedicineError"] = ex.Number == 2601 || ex.Number == 2627 ? "Tên nhóm thuốc đã tồn tại." : "Không thể đổi tên nhóm thuốc.";
            }
            return RedirectToAction("QuanLyNhomThuoc");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult XoaNhomThuoc(int id)
        {
            if (!MedicineGroupCatalogExists())
            {
                TempData["MedicineError"] = "Chưa có bảng danh mục nhóm thuốc. Hãy chạy QuanLyPhongKham_MediCare.sql trước.";
                return RedirectToAction("QuanLyNhomThuoc");
            }
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        try
                        {
                            string groupName;
                            using (var find = new SqlCommand("SELECT TenNhomThuoc FROM dbo.DanhMucNhomThuoc WHERE MaNhomThuoc = @Id;", connection, transaction))
                            {
                                find.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                                groupName = Convert.ToString(find.ExecuteScalar());
                            }
                            if (string.IsNullOrEmpty(groupName))
                            {
                                transaction.Rollback();
                                TempData["MedicineError"] = "Không tìm thấy nhóm thuốc.";
                                return RedirectToAction("QuanLyNhomThuoc");
                            }

                            using (var count = new SqlCommand("SELECT COUNT(1) FROM dbo.Thuoc WHERE LTRIM(RTRIM(NhomThuoc)) = @Name;", connection, transaction))
                            {
                                count.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = groupName;
                                if (Convert.ToInt32(count.ExecuteScalar()) > 0)
                                {
                                    transaction.Rollback();
                                    TempData["MedicineError"] = "Nhóm đang có thuốc sử dụng. Hãy chuyển các thuốc sang nhóm khác trước khi xóa.";
                                    return RedirectToAction("QuanLyNhomThuoc");
                                }
                            }

                            using (var delete = new SqlCommand("DELETE FROM dbo.DanhMucNhomThuoc WHERE MaNhomThuoc = @Id;", connection, transaction))
                            {
                                delete.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                                delete.ExecuteNonQuery();
                            }
                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                TempData["MedicineMessage"] = "Đã xóa nhóm thuốc.";
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("Medicine group delete error: " + ex);
                TempData["MedicineError"] = "Không thể xóa nhóm thuốc.";
            }
            return RedirectToAction("QuanLyNhomThuoc");
        }

        [HttpGet]
        public ActionResult ThemThuoc()
        {
            var model = new MedicineEditViewModel();
            LoadMedicineGroups(model.Groups);
            return PartialView("_FormThuoc", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ThemThuoc(MedicineEditViewModel model)
        {
            NormalizeMedicine(model);
            if (!string.IsNullOrWhiteSpace(model.NhomThuoc) && !MedicineGroupExists(model.NhomThuoc))
                ModelState.AddModelError("NhomThuoc", "Hãy chọn một nhóm thuốc trong danh mục.");
            if (!ModelState.IsValid)
            {
                LoadMedicineGroups(model.Groups);
                return PartialView("_FormThuoc", model);
            }

            try
            {
                using (var connection = CreateConnection())
                using (var command = new SqlCommand(@"
                    INSERT INTO dbo.Thuoc
                        (TenThuoc, HoatChat, NhomThuoc, DonViTinh, DonGia, SoLuongTon,
                         GiaNhap, GiaBan, HanSuDung, MucTonToiThieu, IsActive)
                    VALUES
                        (@TenThuoc, @HoatChat, @NhomThuoc, @DonViTinh, @GiaBan, @SoLuongTon,
                         @GiaNhap, @GiaBan, @HanSuDung, @MucTonToiThieu, @IsActive);", connection))
                {
                    AddMedicineParameters(command, model);
                    connection.Open();
                    command.ExecuteNonQuery();
                }

                if (Request.IsAjaxRequest()) return Json(new { success = true });
                TempData["MedicineMessage"] = "Đã thêm thuốc thành công.";
                return RedirectToAction("QuanLyThuoc");
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("Medicine insert error: " + ex);
                ModelState.AddModelError("", "Không thể thêm thuốc. Hãy kiểm tra migration và dữ liệu nhập.");
                LoadMedicineGroups(model.Groups);
                return PartialView("_FormThuoc", model);
            }
        }

        [HttpGet]
        public ActionResult SuaThuoc(int id)
        {
            var model = FindMedicine(id);
            if (model == null) return HttpNotFound("Không tìm thấy thuốc.");
            LoadMedicineGroups(model.Groups);
            return PartialView("_FormThuoc", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SuaThuoc(MedicineEditViewModel model)
        {
            NormalizeMedicine(model);
            if (model.MaThuoc <= 0) return HttpNotFound("Mã thuốc không hợp lệ.");
            if (!string.IsNullOrWhiteSpace(model.NhomThuoc) && !MedicineGroupExists(model.NhomThuoc))
                ModelState.AddModelError("NhomThuoc", "Hãy chọn một nhóm thuốc trong danh mục.");
            if (!ModelState.IsValid)
            {
                LoadMedicineGroups(model.Groups);
                return PartialView("_FormThuoc", model);
            }

            try
            {
                using (var connection = CreateConnection())
                using (var command = new SqlCommand(@"
                    UPDATE dbo.Thuoc
                    SET TenThuoc = @TenThuoc, HoatChat = @HoatChat, NhomThuoc = @NhomThuoc,
                        DonViTinh = @DonViTinh, DonGia = @GiaBan, SoLuongTon = @SoLuongTon,
                        GiaNhap = @GiaNhap, GiaBan = @GiaBan, HanSuDung = @HanSuDung,
                        MucTonToiThieu = @MucTonToiThieu, IsActive = @IsActive
                    WHERE MaThuoc = @MaThuoc;", connection))
                {
                    AddMedicineParameters(command, model);
                    command.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = model.MaThuoc;
                    connection.Open();
                    if (command.ExecuteNonQuery() != 1) return HttpNotFound("Thuốc không còn tồn tại.");
                }

                if (Request.IsAjaxRequest()) return Json(new { success = true });
                TempData["MedicineMessage"] = "Đã cập nhật thuốc thành công.";
                return RedirectToAction("QuanLyThuoc");
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("Medicine update error: " + ex);
                ModelState.AddModelError("", "Không thể cập nhật thuốc. Hãy kiểm tra migration và dữ liệu nhập.");
                LoadMedicineGroups(model.Groups);
                return PartialView("_FormThuoc", model);
            }
        }

        [HttpGet]
        public ActionResult ChiTietThuoc(int id)
        {
            var item = FindMedicineItem(id);
            return item == null ? (ActionResult)HttpNotFound("Không tìm thấy thuốc.") : PartialView("_ChiTietThuoc", item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult XoaThuoc(int id)
        {
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var referenceCheck = new SqlCommand(@"
                        SELECT CASE WHEN EXISTS
                            (SELECT 1 FROM dbo.ChiTietDonThuoc WHERE MaThuoc = @MaThuoc)
                        THEN 1 ELSE 0 END;", connection))
                    {
                        referenceCheck.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = id;
                        if (Convert.ToInt32(referenceCheck.ExecuteScalar()) == 1)
                        {
                            TempData["MedicineError"] = "Không thể xóa thuốc đã xuất hiện trong đơn thuốc. Hãy sửa trạng thái thành ngừng kinh doanh để giữ lịch sử kê đơn.";
                            return RedirectToAction("QuanLyThuoc");
                        }
                    }

                    using (var command = new SqlCommand("DELETE FROM dbo.Thuoc WHERE MaThuoc = @MaThuoc;", connection))
                    {
                        command.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = id;
                        if (command.ExecuteNonQuery() != 1)
                        {
                            TempData["MedicineError"] = "Không tìm thấy thuốc cần xóa.";
                            return RedirectToAction("QuanLyThuoc");
                        }
                    }
                }

                TempData["MedicineMessage"] = "Đã xóa thuốc thành công.";
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("Medicine delete error: " + ex);
                TempData["MedicineError"] = "Không thể xóa thuốc do ràng buộc dữ liệu. Hãy ngừng kinh doanh thuốc nếu thuốc đã được sử dụng.";
            }
            return RedirectToAction("QuanLyThuoc");
        }

        [HttpGet]
        public ActionResult XuatThuocCsv(string search = null, string group = null, string status = null, string expiryStatus = null)
        {
            var model = new MedicineListViewModel
            {
                Search = search?.Trim(), Group = group?.Trim(), Status = status?.Trim(),
                ExpiryStatus = expiryStatus?.Trim(), Page = 1, PageSize = int.MaxValue
            };
            LoadMedicineList(model);
            var csv = new StringBuilder();
            csv.AppendLine("Mã,Tên thuốc,Hoạt chất,Nhóm,Đơn vị,Tồn,Giá bán,Hạn dùng,Trạng thái");
            foreach (var item in model.Items)
                csv.AppendLine(string.Join(",", new[] { Csv(item.MaThuoc.ToString()), Csv(item.TenThuoc), Csv(item.HoatChat), Csv(item.NhomThuoc), Csv(item.DonViTinh), Csv(item.SoLuongTon.ToString()), Csv(item.GiaBan.ToString("0.00", CultureInfo.InvariantCulture)), Csv(item.HanSuDung?.ToString("yyyy-MM-dd")), Csv(item.TrangThai) }));
            return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv", "medicare-thuoc.csv");
        }

        [HttpGet]
        public ActionResult ChiTietTaiKhoan(int id)
        {
            var item = FindUser(id);
            return item == null ? (ActionResult)HttpNotFound("Không tìm thấy tài khoản.") : PartialView("_ChiTietTaiKhoan", item);
        }

        [HttpGet]
        public ActionResult ThemTaiKhoan()
        {
            ModelState.Clear();
            var model = new UserEditViewModel { IsActive = true };
            LoadRoleAndDepartmentOptions(model);
            if (Request.IsAjaxRequest())
                return PartialView("_FormTaiKhoan", model);

            SetDashboard("Thêm tài khoản");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ThemTaiKhoan(UserEditViewModel model)
        {
            if (!IsRoleAllowed(model.RoleId))
            {
                ModelState.AddModelError("RoleId", "Bạn không có quyền cấp vai trò này.");
            }
            if (string.IsNullOrWhiteSpace(model.Password) || model.Password.Length < 8)
            {
                ModelState.AddModelError("Password", "Mật khẩu khởi tạo phải có ít nhất 8 ký tự.");
            }
            if (!ModelState.IsValid)
            {
                LoadRoleAndDepartmentOptions(model);
                if (Request.IsAjaxRequest())
                    return PartialView("_FormTaiKhoan", model);

                SetDashboard("Thêm tài khoản");
                return View("ThemTaiKhoan", model);
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            EnsureUserUnique(connection, transaction, model, 0);
                            using (var command = new SqlCommand(@"
                                INSERT INTO dbo.Users
                                    (Username, PasswordHash, FullName, Phone, Email, GioiTinh,
                                     NgaySinh, DiaChi, RoleId, MaKhoa, IsActive)
                                VALUES
                                    (@Username, @PasswordHash, @FullName, @Phone, @Email, @Gender,
                                     @BirthDate, @Address, @RoleId, @MaKhoa, @IsActive);", connection, transaction))
                            {
                                AddUserParameters(command, model, true);
                                command.ExecuteNonQuery();
                            }
                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }

                TempData["UserMessage"] = "Đã tạo tài khoản thành công.";
                return RedirectToAction("QuanLyTaiKhoan", new { tab = TabForRole(model.RoleId) });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                LoadRoleAndDepartmentOptions(model);
                if (Request.IsAjaxRequest())
                    return PartialView("_FormTaiKhoan", model);

                SetDashboard("Thêm tài khoản");
                return View("ThemTaiKhoan", model);
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("User insert error: " + ex);
                ModelState.AddModelError("", "Không thể tạo tài khoản. Kiểm tra dữ liệu trùng hoặc cấu trúc database.");
                LoadRoleAndDepartmentOptions(model);
                if (Request.IsAjaxRequest())
                    return PartialView("_FormTaiKhoan", model);

                SetDashboard("Thêm tài khoản");
                return View("ThemTaiKhoan", model);
            }
        }

        [HttpGet]
        public ActionResult SuaTaiKhoan(int id)
        {
            var item = FindUser(id);
            if (item == null) return HttpNotFound("Không tìm thấy tài khoản.");
            if (!CanManageUser(item.RoleName)) return new HttpUnauthorizedResult();
            var model = new UserEditViewModel
            {
                UserId = item.UserId, FullName = item.FullName, Username = item.Username,
                Email = item.Email, Phone = item.Phone, Gender = item.Gender, BirthDate = item.BirthDate,
                Address = item.Address, RoleId = item.RoleId, MaKhoa = item.MaKhoa, IsActive = item.IsActive,
                ExistingRoleName = item.RoleName
            };
            LoadRoleAndDepartmentOptions(model);
            return PartialView("_FormTaiKhoan", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SuaTaiKhoan(UserEditViewModel model)
        {
            var existing = FindUser(model.UserId);
            if (existing == null) return HttpNotFound("Tài khoản không còn tồn tại.");
            if (!CanManageUser(existing.RoleName) || !IsRoleAllowed(model.RoleId)) return new HttpUnauthorizedResult();
            if (!ModelState.IsValid)
            {
                LoadRoleAndDepartmentOptions(model);
                return PartialView("_FormTaiKhoan", model);
            }

            try
            {
                using (var connection = CreateConnection())
                using (var command = new SqlCommand(@"
                    UPDATE dbo.Users
                    SET FullName = @FullName, Email = @Email, Phone = @Phone,
                        GioiTinh = @Gender, NgaySinh = @BirthDate, DiaChi = @Address,
                        RoleId = @RoleId, MaKhoa = @MaKhoa, IsActive = @IsActive
                    WHERE UserId = @UserId;", connection))
                {
                    EnsureUserUnique(connection, null, model, model.UserId);
                    AddUserParameters(command, model, false);
                    command.Parameters.Add("@UserId", SqlDbType.Int).Value = model.UserId;
                    connection.Open();
                    if (command.ExecuteNonQuery() != 1) return HttpNotFound("Tài khoản không còn tồn tại.");

                    if (!string.IsNullOrWhiteSpace(model.Password))
                    {
                        using (var password = new SqlCommand("UPDATE dbo.Users SET PasswordHash = @PasswordHash WHERE UserId = @UserId;", connection))
                        {
                            password.Parameters.Add("@PasswordHash", SqlDbType.VarChar, 255).Value = PasswordHasher.Hash(model.Password);
                            password.Parameters.Add("@UserId", SqlDbType.Int).Value = model.UserId;
                            password.ExecuteNonQuery();
                        }
                    }
                }

                TempData["UserMessage"] = "Đã cập nhật tài khoản.";
                return RedirectToAction("QuanLyTaiKhoan", new { tab = TabForRole(model.RoleId) });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("", ex.Message);
                LoadRoleAndDepartmentOptions(model);
                return PartialView("_FormTaiKhoan", model);
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("User update error: " + ex);
                ModelState.AddModelError("", "Không thể cập nhật tài khoản.");
                LoadRoleAndDepartmentOptions(model);
                return PartialView("_FormTaiKhoan", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ToggleTaiKhoan(int id)
        {
            var user = FindUser(id);
            if (user == null) return HttpNotFound("Không tìm thấy tài khoản.");
            if (!CanManageUser(user.RoleName)) return new HttpUnauthorizedResult();

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    if (user.IsActive && IsLastActiveAdmin(connection, user))
                    {
                        TempData["UserError"] = "Không thể khóa Admin cuối cùng đang hoạt động.";
                    }
                    else
                    {
                        using (var command = new SqlCommand("UPDATE dbo.Users SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE UserId = @UserId;", connection))
                        {
                            command.Parameters.Add("@UserId", SqlDbType.Int).Value = id;
                            command.ExecuteNonQuery();
                        }
                        TempData["UserMessage"] = user.IsActive ? "Đã khóa tài khoản." : "Đã mở khóa tài khoản.";
                    }
                }
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("User status error: " + ex);
                TempData["UserError"] = "Không thể cập nhật trạng thái tài khoản.";
            }
            return RedirectToAction("QuanLyTaiKhoan", new { tab = TabForRole(user.RoleId) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult XoaTaiKhoan(int id)
        {
            var user = FindUser(id);
            if (user == null) return HttpNotFound("Không tìm thấy tài khoản.");
            if (!CanManageUser(user.RoleName)) return new HttpUnauthorizedResult();

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    if (IsLastActiveAdmin(connection, user))
                    {
                        TempData["UserError"] = "Không thể xóa Admin cuối cùng.";
                    }
                    else if (HasBusinessReferences(connection, id))
                    {
                        TempData["UserError"] = "Không thể xóa tài khoản vì đã có lịch hẹn, hồ sơ khám, đơn thuốc hoặc dữ liệu liên quan. Hãy khóa tài khoản thay thế.";
                    }
                    else
                    {
                        using (var command = new SqlCommand("DELETE FROM dbo.Users WHERE UserId = @UserId;", connection))
                        {
                            command.Parameters.Add("@UserId", SqlDbType.Int).Value = id;
                            command.ExecuteNonQuery();
                        }
                        TempData["UserMessage"] = "Đã xóa tài khoản không có dữ liệu liên quan.";
                    }
                }
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("User delete error: " + ex);
                TempData["UserError"] = "Không thể xóa tài khoản do ràng buộc dữ liệu.";
            }
            return RedirectToAction("QuanLyTaiKhoan", new { tab = TabForRole(user.RoleId) });
        }

        [HttpGet]
        public ActionResult XuatTaiKhoanCsv(string tab = "doctor", string search = null, string role = null, string khoa = null, string status = null)
        {
            var model = new UserManagementListViewModel { Tab = tab, Search = search, Role = role, Khoa = khoa, Status = status, Page = 1, PageSize = int.MaxValue };
            LoadUserList(model);
            var csv = new StringBuilder();
            csv.AppendLine("Mã,Họ tên,Tên đăng nhập,Email,Điện thoại,Vai trò,Khoa,Trạng thái");
            foreach (var user in model.Items)
                csv.AppendLine(string.Join(",", new[] { Csv(user.UserId.ToString()), Csv(user.FullName), Csv(user.Username), Csv(user.Email), Csv(user.Phone), Csv(user.RoleName), Csv(user.KhoaName), Csv(user.IsActive ? "Đang hoạt động" : "Đã khóa") }));
            return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv", "medicare-tai-khoan.csv");
        }

        private void LoadUserList(UserManagementListViewModel model)
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                model.Roles = LoadRoles(connection);
                model.Departments = LoadDepartments(connection);
                using (var count = CreateUserCommand(connection, model, true)) model.TotalItems = Convert.ToInt32(count.ExecuteScalar());
                using (var stats = new SqlCommand(@"SELECT COUNT(1), SUM(CASE WHEN r.RoleName = N'Bác sĩ' THEN 1 ELSE 0 END), SUM(CASE WHEN r.RoleName = N'Bệnh nhân' THEN 1 ELSE 0 END), SUM(CASE WHEN u.IsActive = 1 THEN 1 ELSE 0 END), SUM(CASE WHEN u.IsActive = 0 THEN 1 ELSE 0 END) FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId;", connection))
                using (var reader = stats.ExecuteReader())
                {
                    if (reader.Read()) { model.TotalUsers = reader.IsDBNull(0) ? 0 : reader.GetInt32(0); model.TotalDoctors = reader.IsDBNull(1) ? 0 : reader.GetInt32(1); model.TotalPatients = reader.IsDBNull(2) ? 0 : reader.GetInt32(2); model.ActiveUsers = reader.IsDBNull(3) ? 0 : reader.GetInt32(3); model.LockedUsers = reader.IsDBNull(4) ? 0 : reader.GetInt32(4); }
                }
                using (var list = CreateUserCommand(connection, model, false)) using (var reader = list.ExecuteReader()) while (reader.Read()) model.Items.Add(ReadUser(reader));
            }
            model.TotalPages = model.TotalItems == 0 ? 0 : (int)Math.Ceiling(model.TotalItems / (double)model.PageSize);
            if (model.TotalPages > 0 && model.Page > model.TotalPages) model.Page = model.TotalPages;
        }

        private SqlCommand CreateUserCommand(SqlConnection connection, UserManagementListViewModel model, bool countOnly)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"SELECT " + (countOnly ? "COUNT(1)" : @"u.UserId, u.Username, u.FullName, u.Phone, u.Email, u.GioiTinh, u.NgaySinh, u.DiaChi, u.RoleId, u.MaKhoa, u.IsActive, r.RoleName, k.TenKhoa") + @" FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId LEFT JOIN dbo.Khoa k ON k.MaKhoa = u.MaKhoa WHERE (@Search = '' OR CONVERT(varchar(20), u.UserId) = @Search OR u.Username LIKE @LikeSearch OR u.FullName LIKE @LikeSearch OR u.Email LIKE @LikeSearch OR u.Phone LIKE @LikeSearch) AND (@Tab = 'all' OR (@Tab = 'doctor' AND r.RoleName = N'Bác sĩ') OR (@Tab = 'patient' AND r.RoleName = N'Bệnh nhân') OR (@Tab = 'staff' AND r.RoleName NOT IN (N'Bác sĩ', N'Bệnh nhân'))) AND (@Role = '' OR r.RoleName = @Role) AND (@Khoa = '' OR k.TenKhoa = @Khoa) AND (@Status = '' OR (@Status = 'active' AND u.IsActive = 1) OR (@Status = 'locked' AND u.IsActive = 0)) " + (countOnly ? ";" : " ORDER BY u.UserId DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;");
            command.Parameters.Add("@Search", SqlDbType.VarChar, 150).Value = model.Search ?? "";
            command.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 302).Value = "%" + (model.Search ?? "") + "%";
            command.Parameters.Add("@Tab", SqlDbType.VarChar, 20).Value = model.Tab == "doctor" || model.Tab == "patient" || model.Tab == "staff" ? model.Tab : "all";
            command.Parameters.Add("@Role", SqlDbType.NVarChar, 100).Value = model.Role ?? "";
            command.Parameters.Add("@Khoa", SqlDbType.NVarChar, 100).Value = model.Khoa ?? "";
            command.Parameters.Add("@Status", SqlDbType.VarChar, 20).Value = model.Status ?? "";
            if (!countOnly) { command.Parameters.Add("@Offset", SqlDbType.Int).Value = (model.Page - 1) * model.PageSize; command.Parameters.Add("@PageSize", SqlDbType.Int).Value = model.PageSize; }
            return command;
        }

        private UserItemViewModel FindUser(int id)
        {
            using (var connection = CreateConnection()) using (var command = new SqlCommand("SELECT u.UserId, u.Username, u.FullName, u.Phone, u.Email, u.GioiTinh, u.NgaySinh, u.DiaChi, u.RoleId, u.MaKhoa, u.IsActive, r.RoleName, k.TenKhoa FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId LEFT JOIN dbo.Khoa k ON k.MaKhoa = u.MaKhoa WHERE u.UserId = @UserId;", connection)) { command.Parameters.Add("@UserId", SqlDbType.Int).Value = id; connection.Open(); using (var reader = command.ExecuteReader()) return reader.Read() ? ReadUser(reader) : null; }
        }

        private static UserItemViewModel ReadUser(IDataRecord r) { return new UserItemViewModel { UserId = Convert.ToInt32(r["UserId"]), Username = r["Username"].ToString(), FullName = r["FullName"].ToString(), Phone = r["Phone"] == DBNull.Value ? null : r["Phone"].ToString(), Email = r["Email"] == DBNull.Value ? null : r["Email"].ToString(), Gender = r["GioiTinh"] == DBNull.Value ? null : r["GioiTinh"].ToString(), BirthDate = r["NgaySinh"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["NgaySinh"]), Address = r["DiaChi"] == DBNull.Value ? null : r["DiaChi"].ToString(), RoleId = Convert.ToInt32(r["RoleId"]), MaKhoa = r["MaKhoa"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["MaKhoa"]), IsActive = Convert.ToBoolean(r["IsActive"]), RoleName = r["RoleName"].ToString(), KhoaName = r["TenKhoa"] == DBNull.Value ? null : r["TenKhoa"].ToString() }; }

        private List<RoleOptionViewModel> LoadRoles(SqlConnection c) { var list = new List<RoleOptionViewModel>(); using (var cmd = new SqlCommand("SELECT RoleId, RoleName FROM dbo.Roles ORDER BY RoleName;", c)) using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(new RoleOptionViewModel { RoleId = r.GetInt32(0), RoleName = r.GetString(1) }); return list; }
        private List<DepartmentOptionViewModel> LoadDepartments(SqlConnection c) { var list = new List<DepartmentOptionViewModel>(); using (var cmd = new SqlCommand("SELECT MaKhoa, TenKhoa FROM dbo.Khoa ORDER BY TenKhoa;", c)) using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(new DepartmentOptionViewModel { MaKhoa = r.GetInt32(0), TenKhoa = r.GetString(1) }); return list; }
        private void LoadRoleAndDepartmentOptions(UserEditViewModel m) { using (var c = CreateConnection()) { c.Open(); m.Roles = LoadRoles(c); m.Departments = LoadDepartments(c); } }

        private void EnsureUserUnique(SqlConnection c, SqlTransaction t, UserEditViewModel m, int id)
        {
            using (var cmd = new SqlCommand("SELECT TOP 1 CASE WHEN Username = @Username THEN N'Tên đăng nhập' WHEN Email = @Email THEN N'Email' ELSE N'Số điện thoại' END FROM dbo.Users WHERE UserId <> @UserId AND (Username = @Username OR (@Email <> '' AND Email = @Email) OR (@Phone <> '' AND Phone = @Phone));", c, t)) { cmd.Parameters.Add("@Username", SqlDbType.VarChar, 50).Value = m.Username.Trim(); cmd.Parameters.Add("@Email", SqlDbType.VarChar, 100).Value = m.Email?.Trim() ?? ""; cmd.Parameters.Add("@Phone", SqlDbType.VarChar, 20).Value = m.Phone?.Trim() ?? ""; cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = id; if (t == null) c.Open(); var duplicate = cmd.ExecuteScalar(); if (t == null) c.Close(); if (duplicate != null) throw new InvalidOperationException(duplicate + " đã được sử dụng."); }
        }

        private static void AddUserParameters(SqlCommand c, UserEditViewModel m, bool includePassword) { c.Parameters.Add("@Username", SqlDbType.VarChar, 50).Value = m.Username.Trim(); if (includePassword) c.Parameters.Add("@PasswordHash", SqlDbType.VarChar, 255).Value = PasswordHasher.Hash(m.Password); c.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = m.FullName.Trim(); c.Parameters.Add("@Phone", SqlDbType.VarChar, 20).Value = (object)m.Phone?.Trim() ?? DBNull.Value; c.Parameters.Add("@Email", SqlDbType.VarChar, 100).Value = (object)m.Email?.Trim() ?? DBNull.Value; c.Parameters.Add("@Gender", SqlDbType.NVarChar, 10).Value = (object)m.Gender ?? DBNull.Value; c.Parameters.Add("@BirthDate", SqlDbType.Date).Value = (object)m.BirthDate ?? DBNull.Value; c.Parameters.Add("@Address", SqlDbType.NVarChar, 255).Value = (object)m.Address ?? DBNull.Value; c.Parameters.Add("@RoleId", SqlDbType.Int).Value = m.RoleId; c.Parameters.Add("@MaKhoa", SqlDbType.Int).Value = (object)m.MaKhoa ?? DBNull.Value; c.Parameters.Add("@IsActive", SqlDbType.Bit).Value = m.IsActive; }

        private bool IsRoleAllowed(int roleId) { using (var c = CreateConnection()) using (var cmd = new SqlCommand("SELECT RoleName FROM dbo.Roles WHERE RoleId = @RoleId;", c)) { cmd.Parameters.Add("@RoleId", SqlDbType.Int).Value = roleId; c.Open(); var role = Convert.ToString(cmd.ExecuteScalar()); if (User.IsInRole("Admin") || User.IsInRole("Admin hệ thống")) return !string.IsNullOrWhiteSpace(role); return role != "Admin" && role != "Admin hệ thống" && role != "Quản trị viên" && role != "Quản lý" && role != "Quản lý phòng khám"; } }
        private bool CanManageUser(string role) { return User.IsInRole("Admin") || User.IsInRole("Admin hệ thống") || (role != "Admin" && role != "Admin hệ thống" && role != "Quản trị viên" && role != "Quản lý" && role != "Quản lý phòng khám"); }
        private bool IsLastActiveAdmin(SqlConnection c, UserItemViewModel u) { if (u.RoleName != "Admin" && u.RoleName != "Admin hệ thống" && u.RoleName != "Quản trị viên") return false; using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE u.IsActive = 1 AND r.RoleName IN (N'Admin', N'Admin hệ thống', N'Quản trị viên');", c)) return Convert.ToInt32(cmd.ExecuteScalar()) <= 1; }
        private bool HasBusinessReferences(SqlConnection c, int id) { using (var cmd = new SqlCommand("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.LichHen WHERE BenhNhanId = @Id OR BacSiId = @Id) OR EXISTS (SELECT 1 FROM dbo.PhieuKhamBenh WHERE BenhNhanId = @Id OR BacSiId = @Id) OR EXISTS (SELECT 1 FROM dbo.DonThuoc WHERE BacSiId = @Id) THEN 1 ELSE 0 END;", c)) { cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id; return Convert.ToInt32(cmd.ExecuteScalar()) == 1; } }
        private string TabForRole(int roleId) { using (var c = CreateConnection()) using (var cmd = new SqlCommand("SELECT RoleName FROM dbo.Roles WHERE RoleId = @Id;", c)) { cmd.Parameters.Add("@Id", SqlDbType.Int).Value = roleId; c.Open(); var role = Convert.ToString(cmd.ExecuteScalar()); return role == "Bác sĩ" ? "doctor" : role == "Bệnh nhân" ? "patient" : "staff"; } }
        private static string Csv(string v) { return "\"" + (v ?? "").Replace("\"", "\"\"") + "\""; }
        private void SetDashboard(string title) { ViewBag.Title = title; ViewBag.RoleName = User.IsInRole("Admin") || User.IsInRole("Admin hệ thống") ? "Admin hệ thống" : "Quản lý phòng khám"; ViewBag.UserName = User.Identity.Name; ViewBag.PageTitle = title; }

        private void LoadMedicalRecordSearch(MedicalRecordSearchViewModel model)
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                model.Doctors = LoadAppointmentDoctors(connection);
                model.Departments = LoadDepartments(connection);
                const string baseQuery = @"
                    FROM dbo.PhieuKhamBenh p
                    INNER JOIN dbo.Users bn ON bn.UserId = p.BenhNhanId
                    INNER JOIN dbo.Users bs ON bs.UserId = p.BacSiId
                    INNER JOIN dbo.Roles br ON br.RoleId = bs.RoleId AND br.RoleName = N'Bác sĩ'
                    LEFT JOIN dbo.Khoa k ON k.MaKhoa = bs.MaKhoa
                    WHERE (@Search = '' OR CONVERT(varchar(20), bn.UserId) = @Search
                           OR bn.FullName LIKE @LikeSearch OR bn.Phone LIKE @LikeSearch)
                      AND (@DoctorId IS NULL OR p.BacSiId = @DoctorId)
                      AND (@DepartmentId IS NULL OR bs.MaKhoa = @DepartmentId)
                      AND (@FromDate IS NULL OR p.NgayKham >= @FromDate)
                      AND (@ToDate IS NULL OR p.NgayKham < DATEADD(day, 1, @ToDate))
                      AND (@Tab = 'all' OR (@Tab = 'new' AND p.NgayKham >= DATEADD(day, -30, CONVERT(date, GETDATE())))
                           OR (@Tab = 'followup' AND (p.LoiDan LIKE N'%tái khám%' OR p.LoiDan LIKE N'%tái kiểm%'))
                           OR (@Tab = 'recovered' AND (p.TrangThai LIKE N'%hoàn thành%' OR p.TrangThai LIKE N'%khỏi%')))";

                using (var command = new SqlCommand(@"SELECT COUNT(DISTINCT p.BenhNhanId) " + baseQuery, connection))
                {
                    AddMedicalRecordParameters(command, model);
                    model.TotalItems = Convert.ToInt32(command.ExecuteScalar());
                }

                using (var command = new SqlCommand(@"
                    SELECT COUNT(DISTINCT p.BenhNhanId), COUNT(DISTINCT p.BacSiId),
                           COUNT(DISTINCT CASE WHEN p.TrangThai NOT LIKE N'%hoàn thành%' AND p.TrangThai NOT LIKE N'%khỏi%' THEN p.BenhNhanId END)
                    FROM dbo.PhieuKhamBenh p;", connection))
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        model.TotalRecords = Convert.ToInt32(reader[0]);
                        model.ResponsibleDoctorCount = Convert.ToInt32(reader[1]);
                        model.ActiveRecordCount = Convert.ToInt32(reader[2]);
                    }
                }

                using (var command = new SqlCommand(@"
                    SELECT x.BenhNhanId, x.PatientName, x.Gender, x.BirthDate, x.DoctorId,
                           x.DoctorName, x.DepartmentId, x.DepartmentName, x.CreatedDate,
                           x.LastVisitDate, x.Status, x.VisitCount
                    FROM (
                        SELECT p.BenhNhanId, bn.FullName AS PatientName, bn.GioiTinh AS Gender, bn.NgaySinh AS BirthDate,
                               p.BacSiId AS DoctorId, bs.FullName AS DoctorName, bs.MaKhoa AS DepartmentId,
                               k.TenKhoa AS DepartmentName, MIN(p.NgayKham) AS CreatedDate, MAX(p.NgayKham) AS LastVisitDate,
                               MAX(p.TrangThai) AS Status, COUNT(1) AS VisitCount,
                               ROW_NUMBER() OVER (PARTITION BY p.BenhNhanId ORDER BY MAX(p.NgayKham) DESC) AS RecordRank
                        " + baseQuery + @"
                        GROUP BY p.BenhNhanId, bn.FullName, bn.GioiTinh, bn.NgaySinh, p.BacSiId, bs.FullName, bs.MaKhoa, k.TenKhoa
                    ) x
                    WHERE x.RecordRank = 1
                    ORDER BY x.LastVisitDate DESC, x.BenhNhanId
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", connection))
                {
                    AddMedicalRecordParameters(command, model);
                    command.Parameters.Add("@Offset", SqlDbType.Int).Value = (model.Page - 1) * model.PageSize;
                    command.Parameters.Add("@PageSize", SqlDbType.Int).Value = model.PageSize;
                    using (var reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        var birthDate = reader[3] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader[3]);
                        model.Items.Add(new MedicalRecordItemViewModel
                        {
                            PatientId = Convert.ToInt32(reader[0]), PatientCode = "BN-" + Convert.ToInt32(reader[0]).ToString("D6"),
                            PatientName = reader[1].ToString(), Gender = reader[2] == DBNull.Value ? null : reader[2].ToString(),
                            Age = birthDate.HasValue ? (int?)CalculateAge(birthDate.Value, DateTime.Today) : null,
                            DoctorId = reader[4] == DBNull.Value ? (int?)null : Convert.ToInt32(reader[4]), DoctorName = reader[5].ToString(),
                            DepartmentId = reader[6] == DBNull.Value ? (int?)null : Convert.ToInt32(reader[6]), DepartmentName = reader[7] == DBNull.Value ? null : reader[7].ToString(),
                            CreatedDate = Convert.ToDateTime(reader[8]), LastVisitDate = Convert.ToDateTime(reader[9]), Status = reader[10].ToString(), VisitCount = Convert.ToInt32(reader[11])
                        });
                    }
                }
            }
            model.TotalPages = model.TotalItems == 0 ? 0 : (int)Math.Ceiling(model.TotalItems / (double)model.PageSize);
            if (model.Page > model.TotalPages && model.TotalPages > 0) model.Page = model.TotalPages;
        }

        private MedicalRecordItemViewModel FindMedicalRecord(int patientId)
        {
            var model = new MedicalRecordSearchViewModel { Search = patientId.ToString(), Page = 1, PageSize = 1 };
            LoadMedicalRecordSearch(model);
            return model.Items.FirstOrDefault(x => x.PatientId == patientId);
        }

        private static void AddMedicalRecordParameters(SqlCommand command, MedicalRecordSearchViewModel model)
        {
            command.Parameters.Add("@Search", SqlDbType.VarChar, 150).Value = model.Search ?? "";
            command.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 302).Value = "%" + (model.Search ?? "") + "%";
            command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = (object)model.DoctorId ?? DBNull.Value;
            command.Parameters.Add("@DepartmentId", SqlDbType.Int).Value = (object)model.DepartmentId ?? DBNull.Value;
            command.Parameters.Add("@FromDate", SqlDbType.Date).Value = (object)model.FromDate ?? DBNull.Value;
            command.Parameters.Add("@ToDate", SqlDbType.Date).Value = (object)model.ToDate ?? DBNull.Value;
            command.Parameters.Add("@Tab", SqlDbType.VarChar, 20).Value = model.Tab ?? "all";
        }

        private static int CalculateAge(DateTime birthDate, DateTime today)
        {
            var age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age)) age--;
            return age < 0 ? 0 : age;
        }

        private void LoadDashboard(ClinicManagerDashboardViewModel model)
        {
            var weekStart = model.Today.AddDays(-6);
            for (var date = weekStart; date <= model.Today; date = date.AddDays(1))
                model.WeeklyRevenue.Add(new DashboardRevenueViewModel { Date = date, DayLabel = date.ToString("ddd", CultureInfo.GetCultureInfo("vi-VN")) });

            using (var connection = CreateConnection())
            {
                connection.Open();
                using (var command = new SqlCommand(@"
                    SELECT
                        SUM(CASE WHEN r.RoleName = N'Bác sĩ' AND u.IsActive = 1 THEN 1 ELSE 0 END),
                        SUM(CASE WHEN r.RoleName = N'Bệnh nhân' AND u.IsActive = 1 THEN 1 ELSE 0 END)
                    FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId;", connection))
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        model.ActiveDoctorCount = reader[0] == DBNull.Value ? 0 : Convert.ToInt32(reader[0]);
                        model.PatientCount = reader[1] == DBNull.Value ? 0 : Convert.ToInt32(reader[1]);
                    }
                }

                using (var command = new SqlCommand(@"
                    SELECT COUNT(1),
                           SUM(CASE WHEN SoLuongTon > 0 AND SoLuongTon <= MucTonToiThieu THEN 1 ELSE 0 END)
                    FROM dbo.Thuoc WHERE IsActive = 1;", connection))
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        model.MedicineCount = reader[0] == DBNull.Value ? 0 : Convert.ToInt32(reader[0]);
                        model.LowStockMedicineCount = reader[1] == DBNull.Value ? 0 : Convert.ToInt32(reader[1]);
                    }
                }

                using (var command = new SqlCommand(@"
                    SELECT ISNULL(SUM(TongThanhToan), 0)
                    FROM dbo.HoaDon
                    WHERE TrangThai = N'Đã thanh toán'
                      AND NgayThanhToan >= DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0)
                      AND NgayThanhToan < DATEADD(month, DATEDIFF(month, 0, GETDATE()) + 1, 0);", connection))
                    model.MonthlyRevenue = Convert.ToDecimal(command.ExecuteScalar());
                model.HasMonthlyRevenue = model.MonthlyRevenue > 0;

                using (var command = new SqlCommand(@"
                    SELECT lh.GioKham, bn.FullName, lh.LyDoKham, ISNULL(bs.FullName, N'Chưa phân công'), lh.TrangThai
                    FROM dbo.LichHen lh
                    INNER JOIN dbo.Users bn ON bn.UserId = lh.BenhNhanId
                    LEFT JOIN dbo.Users bs ON bs.UserId = lh.BacSiId
                    WHERE lh.NgayKham = @Today
                    ORDER BY lh.GioKham, lh.MaLichHen;", connection))
                {
                    command.Parameters.Add("@Today", SqlDbType.Date).Value = model.Today;
                    using (var reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        model.TodayAppointments.Add(new DashboardAppointmentViewModel
                        {
                            Time = (TimeSpan)reader[0],
                            PatientName = reader[1].ToString(),
                            ServiceName = reader[2] == DBNull.Value ? "Khám bệnh" : reader[2].ToString(),
                            DoctorName = reader[3].ToString(),
                            Status = reader[4].ToString()
                        });
                    }
                }

                using (var command = new SqlCommand(@"
                    SELECT TOP 5 TenThuoc, HanSuDung, SoLuongTon, DonViTinh
                    FROM dbo.Thuoc
                    WHERE IsActive = 1 AND HanSuDung IS NOT NULL
                      AND HanSuDung >= CONVERT(date, GETDATE())
                      AND HanSuDung <= DATEADD(day, 30, CONVERT(date, GETDATE()))
                    ORDER BY HanSuDung, TenThuoc;", connection))
                using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    model.ExpiringMedicines.Add(new DashboardMedicineViewModel
                    {
                        Name = reader[0].ToString(),
                        ExpiryDate = Convert.ToDateTime(reader[1]),
                        Quantity = Convert.ToInt32(reader[2]),
                        Unit = reader[3] == DBNull.Value ? "" : reader[3].ToString(),
                        LotNumber = "Chưa có dữ liệu số lô"
                    });
                }

                using (var command = new SqlCommand(@"
                    SELECT CONVERT(date, NgayThanhToan), ISNULL(SUM(TongThanhToan), 0)
                    FROM dbo.HoaDon
                    WHERE TrangThai = N'Đã thanh toán' AND NgayThanhToan >= @Start AND NgayThanhToan < @End
                    GROUP BY CONVERT(date, NgayThanhToan);", connection))
                {
                    command.Parameters.Add("@Start", SqlDbType.DateTime).Value = weekStart;
                    command.Parameters.Add("@End", SqlDbType.DateTime).Value = model.Today.AddDays(1);
                    using (var reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        var date = Convert.ToDateTime(reader[0]).Date;
                        var item = model.WeeklyRevenue.FirstOrDefault(x => x.Date.Date == date);
                        if (item != null) item.Amount = Convert.ToDecimal(reader[1]);
                    }
                }
                model.HasWeeklyRevenue = model.WeeklyRevenue.Any(x => x.Amount > 0);
            }
        }
        private SqlConnection CreateConnection() { if (string.IsNullOrWhiteSpace(connectionString)) throw new ConfigurationErrorsException("Thiếu connection string MediCareDb."); return new SqlConnection(connectionString); }

        public ActionResult HoSoBenhAn(string search = null, int? doctorId = null, int? departmentId = null,
            DateTime? fromDate = null, DateTime? toDate = null, string tab = "all", int page = 1)
        {
            SetDashboard("Tra cứu Hồ sơ bệnh án");
            var model = new MedicalRecordSearchViewModel
            {
                Search = search?.Trim(), DoctorId = doctorId, DepartmentId = departmentId,
                FromDate = fromDate, ToDate = toDate, Tab = string.IsNullOrWhiteSpace(tab) ? "all" : tab,
                Page = Math.Max(page, 1), PageSize = 10, LookupCountDisplay = "Chưa có dữ liệu thống kê"
            };
            if (model.FromDate.HasValue && model.ToDate.HasValue && model.FromDate.Value.Date > model.ToDate.Value.Date)
            {
                model.ValidationMessage = "Ngày bắt đầu không được lớn hơn ngày kết thúc. Vui lòng chọn lại khoảng ngày.";
                LoadMedicalRecordOptions(model);
                return View(model);
            }
            try
            {
                LoadMedicalRecordSearch(model);
                try { RecordMedicalRecordLookup(model); }
                catch (SqlException ex) { System.Diagnostics.Debug.WriteLine("Lookup log unavailable: " + ex); }
            }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("Medical record search error: " + ex);
                ViewBag.DatabaseError = "Không thể tải dữ liệu hồ sơ bệnh án. Vui lòng kiểm tra kết nối database.";
            }
            return View(model);
        }

        private void LoadMedicalRecordOptions(MedicalRecordSearchViewModel model)
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                LoadMedicalRecordOptions(connection, model);
            }
        }

        private void LoadMedicalRecordOptions(SqlConnection connection, MedicalRecordSearchViewModel model)
        {
            model.Doctors = LoadAppointmentDoctors(connection);
            model.Departments = LoadDepartments(connection);
        }

        private void RecordMedicalRecordLookup(MedicalRecordSearchViewModel model)
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(@"
                IF OBJECT_ID(N'dbo.MedicalRecordLookupLog', N'U') IS NOT NULL
                BEGIN
                    DECLARE @UserId INT = (SELECT TOP 1 UserId FROM dbo.Users WHERE Username = @UserName OR Email = @UserName);
                    EXEC sys.sp_executesql
                        N'INSERT INTO dbo.MedicalRecordLookupLog (UserId, ResultCount) VALUES (@UserId, @ResultCount);',
                        N'@UserId INT, @ResultCount INT', @UserId, @ResultCount;
                    SELECT COUNT(1) FROM dbo.MedicalRecordLookupLog
                    WHERE LookupAt >= DATEADD(month, DATEDIFF(month, 0, GETUTCDATE()), 0)
                      AND LookupAt < DATEADD(month, DATEDIFF(month, 0, GETUTCDATE()) + 1, 0);
                END
                ELSE SELECT CAST(NULL AS INT);", connection))
            {
                command.Parameters.Add("@UserName", SqlDbType.NVarChar, 100).Value = User.Identity.Name ?? string.Empty;
                command.Parameters.Add("@ResultCount", SqlDbType.Int).Value = Math.Max(model.TotalItems, 0);
                connection.Open();
                var value = command.ExecuteScalar();
                if (value != null && value != DBNull.Value)
                {
                    model.HasLookupLogTable = true;
                    model.HasLookupLog = true;
                    model.LookupCountDisplay = Convert.ToInt32(value).ToString("N0");
                }
            }
        }

        [HttpGet]
        public ActionResult ChiTietHoSoBenhAn(int id)
        {
            SetDashboard("Chi tiết Hồ sơ bệnh án");
            var model = FindMedicalRecord(id);
            return model == null ? (ActionResult)HttpNotFound("Không tìm thấy hồ sơ bệnh án.") : View(model);
        }

        [HttpGet]
        public ActionResult LichSuKhamBenhNhan(int id)
        {
            SetDashboard("Lịch sử khám bệnh");
            var model = FindMedicalRecord(id);
            return model == null ? (ActionResult)HttpNotFound("Không tìm thấy bệnh nhân.") : View(model);
        }

        [HttpGet]
        public ActionResult DoanhThu(string tuNgay = null, string denNgay = null, string khoa = null, string bacSi = null)
        {
            SetDashboard("Thống kê Doanh thu");
            ViewBag.ActiveManagerMenu = "revenue";

            var model = new RevenueStatisticsViewModel
            {
                Khoa = khoa?.Trim(),
                BacSi = bacSi?.Trim()
            };

            DateTime parsedTuNgay;
            if (DateTime.TryParse(tuNgay, out parsedTuNgay))
            {
                model.TuNgay = parsedTuNgay.Date;
            }
            else
            {
                model.TuNgay = DateTime.Today.AddDays(-30);
            }

            DateTime parsedDenNgay;
            if (DateTime.TryParse(denNgay, out parsedDenNgay))
            {
                model.DenNgay = parsedDenNgay.Date;
            }
            else
            {
                model.DenNgay = DateTime.Today;
            }

            ViewBag.TuNgay = model.TuNgay.Value.ToString("yyyy-MM-dd");
            ViewBag.DenNgay = model.DenNgay.Value.ToString("yyyy-MM-dd");
            ViewBag.SelectedKhoa = model.Khoa;
            ViewBag.SelectedBacSi = model.BacSi;

            try
            {
                using (var connection = CreateConnection())
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

                    var whereClause = " WHERE 1=1 ";
                    if (model.TuNgay.HasValue)
                    {
                        whereClause += " AND hd.NgayThanhToan >= @TuNgay ";
                    }
                    if (model.DenNgay.HasValue)
                    {
                        whereClause += " AND hd.NgayThanhToan < DATEADD(day, 1, @DenNgay) ";
                    }
                    if (!string.IsNullOrWhiteSpace(model.Khoa))
                    {
                        whereClause += " AND (k.TenKhoa = @Khoa OR k.MaKhoa = @KhoaId) ";
                    }
                    if (!string.IsNullOrWhiteSpace(model.BacSi))
                    {
                        whereClause += " AND (bs.FullName = @BacSi OR bs.UserId = @BacSiId) ";
                    }

                    string kpiQuery = @"
                        SELECT
                            ISNULL(SUM(hd.TongThanhToan), 0),
                            ISNULL(SUM(hd.TienKhamAndCLS), 0),
                            ISNULL(SUM(hd.TienThuoc), 0),
                            COUNT(DISTINCT hd.MaHoaDon),
                            COUNT(DISTINCT pk.MaPhieuKham)
                        FROM dbo.HoaDon hd
                        INNER JOIN dbo.PhieuKhamBenh pk ON pk.MaPhieuKham = hd.MaPhieuKham
                        INNER JOIN dbo.Users bs ON bs.UserId = pk.BacSiId
                        LEFT JOIN dbo.Khoa k ON k.MaKhoa = bs.MaKhoa" + whereClause;

                    using (var cmd = new SqlCommand(kpiQuery, connection))
                    {
                        AddRevenueParameters(cmd, model);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                model.TongDoanhThu = r.GetDecimal(0);
                                model.DoanhThuKham = r.GetDecimal(1);
                                model.DoanhThuThuoc = r.GetDecimal(2);
                                model.TongLuotKham = r.GetInt32(4);
                            }
                        }
                    }

                    model.TrungBinhLuotKham = model.TongLuotKham > 0 ? model.TongDoanhThu / model.TongLuotKham : 0;

                    string doctorQuery = @"
                        SELECT
                            bs.UserId,
                            bs.FullName,
                            COALESCE(k.TenKhoa, N'Chưa phân khoa'),
                            COUNT(DISTINCT pk.MaPhieuKham) AS VisitCount,
                            ISNULL(SUM(hd.TienKhamAndCLS), 0) AS ExamRevenue,
                            ISNULL(SUM(hd.TienThuoc), 0) AS MedicineRevenue,
                            ISNULL(SUM(hd.TongThanhToan), 0) AS TotalRevenue
                        FROM dbo.HoaDon hd
                        INNER JOIN dbo.PhieuKhamBenh pk ON pk.MaPhieuKham = hd.MaPhieuKham
                        INNER JOIN dbo.Users bs ON bs.UserId = pk.BacSiId
                        LEFT JOIN dbo.Khoa k ON k.MaKhoa = bs.MaKhoa" + whereClause + @"
                        GROUP BY bs.UserId, bs.FullName, k.TenKhoa
                        ORDER BY TotalRevenue DESC;";

                    using (var cmd = new SqlCommand(doctorQuery, connection))
                    {
                        AddRevenueParameters(cmd, model);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var totalRev = r.GetDecimal(6);
                                decimal percentage = model.TongDoanhThu > 0 ? (totalRev / model.TongDoanhThu) * 100 : 0;
                                model.DoctorStatistics.Add(new RevenueByDoctorViewModel
                                {
                                    DoctorId = r.GetInt32(0),
                                    DoctorName = r.GetString(1),
                                    DepartmentName = r.GetString(2),
                                    VisitCount = r.GetInt32(3),
                                    ExamRevenue = r.GetDecimal(4),
                                    MedicineRevenue = r.GetDecimal(5),
                                    TotalRevenue = totalRev,
                                    Percentage = Math.Round(percentage, 1)
                                });
                            }
                        }
                    }

                    string deptQuery = @"
                        SELECT
                            COALESCE(k.MaKhoa, 0),
                            COALESCE(k.TenKhoa, N'Chưa phân khoa'),
                            COUNT(DISTINCT bs.UserId) AS DoctorCount,
                            COUNT(DISTINCT pk.MaPhieuKham) AS VisitCount,
                            ISNULL(SUM(hd.TienKhamAndCLS), 0) AS ExamRevenue,
                            ISNULL(SUM(hd.TienThuoc), 0) AS MedicineRevenue,
                            ISNULL(SUM(hd.TongThanhToan), 0) AS TotalRevenue
                        FROM dbo.HoaDon hd
                        INNER JOIN dbo.PhieuKhamBenh pk ON pk.MaPhieuKham = hd.MaPhieuKham
                        INNER JOIN dbo.Users bs ON bs.UserId = pk.BacSiId
                        LEFT JOIN dbo.Khoa k ON k.MaKhoa = bs.MaKhoa" + whereClause + @"
                        GROUP BY k.MaKhoa, k.TenKhoa
                        ORDER BY TotalRevenue DESC;";

                    using (var cmd = new SqlCommand(deptQuery, connection))
                    {
                        AddRevenueParameters(cmd, model);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var totalRev = r.GetDecimal(6);
                                decimal percentage = model.TongDoanhThu > 0 ? (totalRev / model.TongDoanhThu) * 100 : 0;
                                model.DepartmentStatistics.Add(new RevenueByDepartmentViewModel
                                {
                                    DepartmentId = r.GetInt32(0),
                                    DepartmentName = r.GetString(1),
                                    DoctorCount = r.GetInt32(2),
                                    VisitCount = r.GetInt32(3),
                                    ExamRevenue = r.GetDecimal(4),
                                    MedicineRevenue = r.GetDecimal(5),
                                    TotalRevenue = totalRev,
                                    Percentage = Math.Round(percentage, 1)
                                });
                            }
                        }
                    }

                    string invoiceQuery = @"
                        SELECT TOP 50
                            hd.MaHoaDon,
                            hd.NgayThanhToan,
                            bn.FullName,
                            'BN-' + RIGHT('000000' + CAST(bn.UserId AS VARCHAR(10)), 6),
                            bs.FullName,
                            COALESCE(k.TenKhoa, N'Chưa phân khoa'),
                            hd.PhuongThucTT,
                            hd.TongThanhToan,
                            hd.TrangThai
                        FROM dbo.HoaDon hd
                        INNER JOIN dbo.PhieuKhamBenh pk ON pk.MaPhieuKham = hd.MaPhieuKham
                        INNER JOIN dbo.Users bn ON bn.UserId = hd.BenhNhanId
                        INNER JOIN dbo.Users bs ON bs.UserId = pk.BacSiId
                        LEFT JOIN dbo.Khoa k ON k.MaKhoa = bs.MaKhoa" + whereClause + @"
                        ORDER BY hd.NgayThanhToan DESC;";

                    using (var cmd = new SqlCommand(invoiceQuery, connection))
                    {
                        AddRevenueParameters(cmd, model);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.Invoices.Add(new InvoiceItemViewModel
                                {
                                    InvoiceId = r.GetInt32(0),
                                    PaymentDate = r.IsDBNull(1) ? DateTime.Now : r.GetDateTime(1),
                                    PatientName = r.GetString(2),
                                    PatientCode = r.GetString(3),
                                    DoctorName = r.GetString(4),
                                    DepartmentName = r.GetString(5),
                                    PaymentMethod = r.IsDBNull(6) ? "Tiền mặt" : r.GetString(6),
                                    Amount = r.GetDecimal(7),
                                    Status = r.GetString(8)
                                });
                            }
                        }
                    }

                    string trendQuery = @"
                        SELECT
                            CAST(hd.NgayThanhToan AS DATE) AS RevenueDate,
                            ISNULL(SUM(hd.TongThanhToan), 0) AS TotalRev,
                            ISNULL(SUM(hd.TienThuoc), 0) AS MedRev
                        FROM dbo.HoaDon hd
                        INNER JOIN dbo.PhieuKhamBenh pk ON pk.MaPhieuKham = hd.MaPhieuKham
                        INNER JOIN dbo.Users bs ON bs.UserId = pk.BacSiId
                        LEFT JOIN dbo.Khoa k ON k.MaKhoa = bs.MaKhoa" + whereClause + @"
                        GROUP BY CAST(hd.NgayThanhToan AS DATE)
                        ORDER BY RevenueDate ASC;";

                    using (var cmd = new SqlCommand(trendQuery, connection))
                    {
                        AddRevenueParameters(cmd, model);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var dateVal = r.GetDateTime(0);
                                model.TrendData.Add(new TrendChartPointViewModel
                                {
                                    DateLabel = dateVal.ToString("dd/MM"),
                                    TotalRevenue = r.GetDecimal(1),
                                    MedicineRevenue = r.GetDecimal(2)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DoanhThu error: " + ex);
                ViewBag.DatabaseError = "Không thể tải dữ liệu thống kê doanh thu.";
            }

            return View(model);
        }

        private static void AddRevenueParameters(SqlCommand cmd, RevenueStatisticsViewModel model)
        {
            cmd.Parameters.Add("@TuNgay", SqlDbType.Date).Value = (object)model.TuNgay?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@DenNgay", SqlDbType.Date).Value = (object)model.DenNgay?.Date ?? DBNull.Value;

            int khoaId = 0;
            if (int.TryParse(model.Khoa, out khoaId))
            {
                cmd.Parameters.Add("@Khoa", SqlDbType.NVarChar, 100).Value = "";
                cmd.Parameters.Add("@KhoaId", SqlDbType.Int).Value = khoaId;
            }
            else
            {
                cmd.Parameters.Add("@Khoa", SqlDbType.NVarChar, 100).Value = model.Khoa ?? "";
                cmd.Parameters.Add("@KhoaId", SqlDbType.Int).Value = 0;
            }

            int doctorId = 0;
            if (int.TryParse(model.BacSi, out doctorId))
            {
                cmd.Parameters.Add("@BacSi", SqlDbType.VarChar, 100).Value = "";
                cmd.Parameters.Add("@BacSiId", SqlDbType.Int).Value = doctorId;
            }
            else
            {
                cmd.Parameters.Add("@BacSi", SqlDbType.NVarChar, 100).Value = model.BacSi ?? "";
                cmd.Parameters.Add("@BacSiId", SqlDbType.Int).Value = 0;
            }
        }
        [HttpGet]
        public ActionResult BaoCao()
        {
            SetDashboard("Báo cáo & Thống kê Y tế");
            ViewBag.ActiveManagerMenu = "reports";

            var model = new MedicalReportViewModel();

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();

                    using (var cmd = new SqlCommand(@"
                        SELECT COUNT(1) FROM dbo.PhieuKhamBenh
                        WHERE NgayKham >= DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0);", connection))
                    {
                        model.TotalVisitsMonth = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.DonThuoc;", connection))
                    {
                        model.TotalPrescriptions = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("BaoCao error: " + ex);
            }

            model.Reports = new List<ReportItemViewModel>
            {
                new ReportItemViewModel
                {
                    Title = "Báo cáo Hoạt động Khám Chữa bệnh (Mẫu Bộ Y tế)",
                    Description = "Thống kê tổng số ca khám, điều trị ngoại trú, BHYT theo tháng",
                    Category = "Hoạt động KCB",
                    Period = "Hàng tháng",
                    UpdatedDate = DateTime.Now.ToString("dd/MM/yyyy"),
                    Format = "Excel / CSV",
                    DownloadAction = "XuatBaoCaoKCB"
                },
                new ReportItemViewModel
                {
                    Title = "Báo cáo Sử dụng & Tồn kho Dược phẩm",
                    Description = "Thống kê tiêu thụ thuốc, thuốc cận hạn, dự trù vật tư kho",
                    Category = "Dược phẩm & Khung kho",
                    Period = "Hàng tuần",
                    UpdatedDate = DateTime.Now.ToString("dd/MM/yyyy"),
                    Format = "Excel / CSV",
                    DownloadAction = "XuatBaoCaoDuoc"
                }
            };

            return View(model);
        }

        [HttpGet]
        public ActionResult CaiDat()
        {
            SetDashboard("Cài đặt & Bảng giá Dịch vụ");
            ViewBag.ActiveManagerMenu = "settings";

            var model = new ClinicSettingsViewModel
            {
                BankCode = ConfigurationManager.AppSettings["BankCode"] ?? "MB",
                BankAccountNo = ConfigurationManager.AppSettings["BankAccountNo"] ?? "0901234567",
                BankAccountName = ConfigurationManager.AppSettings["BankAccountName"] ?? "PHONG KHAM MEDICARE"
            };

            try
            {
                using (var connection = CreateConnection())
                using (var cmd = new SqlCommand("SELECT MaDichVu, TenDichVu, LoaiDichVu, DonGia FROM dbo.DichVuCLS ORDER BY TenDichVu;", connection))
                {
                    connection.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            model.ServicePrices.Add(new ServicePriceItemViewModel
                            {
                                ServiceId = r.GetInt32(0),
                                ServiceName = r.GetString(1),
                                ServiceType = r.IsDBNull(2) ? "Chung" : r.GetString(2),
                                Price = r.GetDecimal(3),
                                IsActive = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CaiDat error: " + ex);
                ViewBag.Error = "Không thể tải bảng giá dịch vụ từ CSDL.";
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CaiDat(ClinicSettingsViewModel model)
        {
            SetDashboard("Cài đặt & Bảng giá Dịch vụ");
            ViewBag.ActiveManagerMenu = "settings";

            if (model == null || model.ServicePrices == null)
            {
                return RedirectToAction("CaiDat");
            }

            if (model.ServicePrices.Any(item => item.Price < 0))
            {
                TempData["Error"] = "Giá dịch vụ không hợp lệ (không được là số âm). Vui lòng kiểm tra lại.";
                return RedirectToAction("CaiDat");
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    foreach (var item in model.ServicePrices)
                    {
                        using (var cmd = new SqlCommand("UPDATE dbo.DichVuCLS SET DonGia = @Price WHERE MaDichVu = @Id;", connection))
                        {
                            cmd.Parameters.Add("@Price", SqlDbType.Decimal).Value = item.Price;
                            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = item.ServiceId;
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                TempData["Message"] = "Đã cập nhật bảng giá dịch vụ thành công.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CaiDat update error: " + ex);
                TempData["Error"] = "Không thể cập nhật bảng giá.";
            }

            return RedirectToAction("CaiDat");
        }

        [HttpGet]
        public FileContentResult XuatBaoCaoKCB()
        {
            var csv = new StringBuilder();
            csv.AppendLine("STT,Ma Phieu,Ngay Kham,Benh Nhan ID,Bac Si ID,Trang Thai");
            try
            {
                using (var connection = CreateConnection())
                using (var cmd = new SqlCommand("SELECT MaPhieuKham, NgayKham, BenhNhanId, BacSiId, TrangThai FROM dbo.PhieuKhamBenh ORDER BY MaPhieuKham DESC;", connection))
                {
                    connection.Open();
                    int stt = 1;
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            csv.AppendLine(string.Format("{0},{1},{2},{3},{4},\"{5}\"",
                                stt++,
                                r.GetInt32(0),
                                r.GetDateTime(1).ToString("yyyy-MM-dd HH:mm"),
                                r.GetInt32(2),
                                r.GetInt32(3),
                                r.GetString(4)
                            ));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                csv.AppendLine("Loi khi trich xuat du lieu: " + ex.Message);
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return File(bytes, "text/csv", "BaoCaoHoatDongKCB_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
        }

        [HttpGet]
        public FileContentResult XuatBaoCaoDuoc()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Ma Thuoc,Ten Thuoc,Hoat Chat,Don Vi,So Luong Ton,Gia Ban,Trang Thai");
            try
            {
                using (var connection = CreateConnection())
                using (var cmd = new SqlCommand("SELECT MaThuoc, TenThuoc, HoatChat, DonViTinh, SoLuongTon, GiaBan FROM dbo.Thuoc WHERE IsActive = 1 ORDER BY TenThuoc;", connection))
                {
                    connection.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            csv.AppendLine(string.Format("\"{0}\",\"{1}\",\"{2}\",{3},{4},{5},\"Con hang\"",
                                r.GetInt32(0),
                                r.GetString(1).Replace("\"", "\"\""),
                                r.IsDBNull(2) ? "" : r.GetString(2).Replace("\"", "\"\""),
                                r.IsDBNull(3) ? "" : r.GetString(3),
                                r.GetInt32(4),
                                r.GetDecimal(5)
                            ));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                csv.AppendLine("Loi khi trich xuat du lieu: " + ex.Message);
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return File(bytes, "text/csv", "BaoCaoTonKhoDuoc_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
        }

        private void LoadMedicineGroupList(MedicineGroupListViewModel model)
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(@"
                SELECT n.MaNhomThuoc, n.TenNhomThuoc, COUNT(t.MaThuoc) AS SoLuongThuoc
                FROM dbo.DanhMucNhomThuoc AS n
                LEFT JOIN dbo.Thuoc AS t ON LTRIM(RTRIM(t.NhomThuoc)) = n.TenNhomThuoc
                GROUP BY n.MaNhomThuoc, n.TenNhomThuoc
                ORDER BY n.TenNhomThuoc;", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    model.Items = new List<MedicineGroupViewModel>();
                    while (reader.Read())
                    {
                        model.Items.Add(new MedicineGroupViewModel
                        {
                            MaNhomThuoc = Convert.ToInt32(reader["MaNhomThuoc"]),
                            TenNhomThuoc = reader["TenNhomThuoc"].ToString(),
                            SoLuongThuoc = Convert.ToInt32(reader["SoLuongThuoc"])
                        });
                    }
                }
            }
        }

        private void LoadLegacyMedicineGroupList(MedicineGroupListViewModel model)
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(@"
                SELECT LTRIM(RTRIM(NhomThuoc)) AS TenNhomThuoc, COUNT(1) AS SoLuongThuoc
                FROM dbo.Thuoc
                WHERE NhomThuoc IS NOT NULL AND LTRIM(RTRIM(NhomThuoc)) <> N''
                GROUP BY LTRIM(RTRIM(NhomThuoc))
                ORDER BY LTRIM(RTRIM(NhomThuoc));", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    model.Items = new List<MedicineGroupViewModel>();
                    while (reader.Read())
                    {
                        model.Items.Add(new MedicineGroupViewModel
                        {
                            MaNhomThuoc = 0,
                            TenNhomThuoc = reader["TenNhomThuoc"].ToString(),
                            SoLuongThuoc = Convert.ToInt32(reader["SoLuongThuoc"])
                        });
                    }
                }
            }
        }

        private void LoadMedicineGroups(IList<string> groups)
        {
            groups.Clear();
            try
            {
                using (var connection = CreateConnection())
                using (var command = new SqlCommand("SELECT TenNhomThuoc FROM dbo.DanhMucNhomThuoc ORDER BY TenNhomThuoc;", connection))
                {
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) groups.Add(reader.GetString(0));
                }
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                using (var connection = CreateConnection())
                using (var command = new SqlCommand("SELECT DISTINCT LTRIM(RTRIM(NhomThuoc)) FROM dbo.Thuoc WHERE NhomThuoc IS NOT NULL AND LTRIM(RTRIM(NhomThuoc)) <> N'' ORDER BY LTRIM(RTRIM(NhomThuoc));", connection))
                {
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) groups.Add(reader.GetString(0));
                }
            }
        }

        private bool MedicineGroupExists(string name)
        {
            try
            {
                using (var connection = CreateConnection())
                using (var command = new SqlCommand("SELECT COUNT(1) FROM dbo.DanhMucNhomThuoc WHERE TenNhomThuoc = @Name;", connection))
                {
                    command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
                    connection.Open();
                    return Convert.ToInt32(command.ExecuteScalar()) == 1;
                }
            }
            catch (SqlException ex) when (ex.Number == 208) { return false; }
        }

        private bool MedicineGroupCatalogExists()
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand("SELECT OBJECT_ID(N'dbo.DanhMucNhomThuoc', N'U');", connection))
            {
                connection.Open();
                var objectId = command.ExecuteScalar();
                return objectId != DBNull.Value && objectId != null;
            }
        }

        private MedicineEditViewModel FindMedicine(int id)
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(@"
                SELECT MaThuoc, TenThuoc, HoatChat, NhomThuoc, DonViTinh, SoLuongTon,
                       GiaNhap, ISNULL(GiaBan, DonGia) AS GiaBan, HanSuDung,
                       MucTonToiThieu, IsActive
                FROM dbo.Thuoc WHERE MaThuoc = @MaThuoc;", connection))
            {
                command.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = id;
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new MedicineEditViewModel
                    {
                        MaThuoc = Convert.ToInt32(reader["MaThuoc"]),
                        TenThuoc = reader["TenThuoc"].ToString(),
                        HoatChat = reader["HoatChat"] == DBNull.Value ? null : reader["HoatChat"].ToString(),
                        NhomThuoc = reader["NhomThuoc"] == DBNull.Value ? null : reader["NhomThuoc"].ToString(),
                        DonViTinh = reader["DonViTinh"] == DBNull.Value ? null : reader["DonViTinh"].ToString(),
                        SoLuongTon = Convert.ToInt32(reader["SoLuongTon"]),
                        GiaNhap = reader["GiaNhap"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["GiaNhap"]),
                        GiaBan = Convert.ToDecimal(reader["GiaBan"]),
                        HanSuDung = reader["HanSuDung"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["HanSuDung"]),
                        MucTonToiThieu = Convert.ToInt32(reader["MucTonToiThieu"]),
                        IsActive = Convert.ToBoolean(reader["IsActive"])
                    };
                }
            }
        }

        private MedicineItemViewModel FindMedicineItem(int id)
        {
            var medicine = FindMedicine(id);
            if (medicine == null) return null;
            return new MedicineItemViewModel
            {
                MaThuoc = medicine.MaThuoc,
                TenThuoc = medicine.TenThuoc,
                HoatChat = medicine.HoatChat,
                NhomThuoc = medicine.NhomThuoc,
                DonViTinh = medicine.DonViTinh,
                SoLuongTon = medicine.SoLuongTon,
                GiaNhap = medicine.GiaNhap,
                GiaBan = medicine.GiaBan,
                HanSuDung = medicine.HanSuDung,
                MucTonToiThieu = medicine.MucTonToiThieu,
                IsActive = medicine.IsActive,
                TrangThai = GetMedicineStatus(medicine.SoLuongTon, medicine.MucTonToiThieu, medicine.IsActive, medicine.HanSuDung)
            };
        }

        private static string GetMedicineStatus(int quantity, int minimum, bool active, DateTime? expiry)
        {
            return !active ? "Ngừng kinh doanh" : quantity == 0 ? "Hết hàng" : quantity <= minimum ? "Sắp hết" : expiry.HasValue && expiry.Value.Date < DateTime.Today ? "Hết hạn" : "Còn hàng";
        }

        private static void NormalizeMedicine(MedicineEditViewModel model)
        {
            model.TenThuoc = model.TenThuoc?.Trim();
            model.HoatChat = string.IsNullOrWhiteSpace(model.HoatChat) ? null : model.HoatChat.Trim();
            model.NhomThuoc = string.IsNullOrWhiteSpace(model.NhomThuoc) ? null : model.NhomThuoc.Trim();
            model.DonViTinh = model.DonViTinh?.Trim();
        }

        private static void AddMedicineParameters(SqlCommand command, MedicineEditViewModel model)
        {
            command.Parameters.Add("@TenThuoc", SqlDbType.NVarChar, 150).Value = model.TenThuoc;
            command.Parameters.Add("@HoatChat", SqlDbType.NVarChar, 150).Value = (object)model.HoatChat ?? DBNull.Value;
            command.Parameters.Add("@NhomThuoc", SqlDbType.NVarChar, 100).Value = (object)model.NhomThuoc ?? DBNull.Value;
            command.Parameters.Add("@DonViTinh", SqlDbType.NVarChar, 50).Value = (object)model.DonViTinh ?? DBNull.Value;
            command.Parameters.Add("@SoLuongTon", SqlDbType.Int).Value = model.SoLuongTon;
            var purchasePrice = command.Parameters.Add("@GiaNhap", SqlDbType.Decimal);
            purchasePrice.Precision = 18;
            purchasePrice.Scale = 2;
            purchasePrice.Value = (object)model.GiaNhap ?? DBNull.Value;
            var salePrice = command.Parameters.Add("@GiaBan", SqlDbType.Decimal);
            salePrice.Precision = 18;
            salePrice.Scale = 2;
            salePrice.Value = model.GiaBan;
            command.Parameters.Add("@HanSuDung", SqlDbType.Date).Value = (object)model.HanSuDung ?? DBNull.Value;
            command.Parameters.Add("@MucTonToiThieu", SqlDbType.Int).Value = model.MucTonToiThieu;
            command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = model.IsActive;
        }
        
        private void LoadMedicineList(MedicineListViewModel model)
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                model.Groups = new List<string>();
                LoadMedicineGroups(model.Groups);

                const string filter = @"
                    FROM dbo.Thuoc
                    WHERE (@Search = '' OR CONVERT(varchar(20), MaThuoc) = @Search
                           OR TenThuoc LIKE @LikeSearch OR HoatChat LIKE @LikeSearch)
                      AND (@GroupName = '' OR NhomThuoc = @GroupName)
                      AND (@Status = '' OR (@Status = 'active' AND IsActive = 1)
                           OR (@Status = 'inactive' AND IsActive = 0)
                           OR (@Status = 'out' AND SoLuongTon = 0)
                           OR (@Status = 'low' AND SoLuongTon > 0 AND SoLuongTon <= MucTonToiThieu))
                      AND (@ExpiryStatus = '' OR (@ExpiryStatus = 'expired' AND HanSuDung < CONVERT(date, GETDATE()))
                           OR (@ExpiryStatus = 'soon' AND HanSuDung >= CONVERT(date, GETDATE())
                               AND HanSuDung <= DATEADD(day, 30, CONVERT(date, GETDATE())))
                           OR (@ExpiryStatus = 'valid' AND (HanSuDung IS NULL
                               OR HanSuDung > DATEADD(day, 30, CONVERT(date, GETDATE())))))";

                using (var count = new SqlCommand("SELECT COUNT(1) " + filter, connection))
                {
                    AddMedicineListParameters(count, model);
                    model.TotalItems = Convert.ToInt32(count.ExecuteScalar());
                }

                using (var stats = new SqlCommand(@"
                    SELECT ISNULL(SUM(SoLuongTon), 0),
                           ISNULL(SUM(CASE WHEN SoLuongTon = 0 THEN 1 ELSE 0 END), 0),
                           ISNULL(SUM(CASE WHEN SoLuongTon > 0 AND SoLuongTon <= MucTonToiThieu THEN 1 ELSE 0 END), 0),
                           ISNULL(SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END), 0)
                    FROM dbo.Thuoc;", connection))
                using (var statsReader = stats.ExecuteReader())
                {
                    if (statsReader.Read())
                    {
                        model.TotalStock = Convert.ToInt32(statsReader[0]);
                        model.OutOfStock = Convert.ToInt32(statsReader[1]);
                        model.LowStock = Convert.ToInt32(statsReader[2]);
                        model.ActiveItems = Convert.ToInt32(statsReader[3]);
                    }
                }

                using (var list = new SqlCommand(@"
                    SELECT MaThuoc, TenThuoc, HoatChat, NhomThuoc, DonViTinh, SoLuongTon,
                           GiaNhap, ISNULL(GiaBan, DonGia) AS GiaBan, HanSuDung,
                           MucTonToiThieu, IsActive " + filter + @"
                    ORDER BY MaThuoc DESC
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", connection))
                {
                    AddMedicineListParameters(list, model);
                    list.Parameters.Add("@Offset", SqlDbType.Int).Value = (model.Page - 1) * model.PageSize;
                    list.Parameters.Add("@PageSize", SqlDbType.Int).Value = model.PageSize;
                    using (var reader = list.ExecuteReader())
                    {
                        model.Items = new List<MedicineItemViewModel>();
                        while (reader.Read())
                        {
                            var quantity = Convert.ToInt32(reader["SoLuongTon"]);
                            var minimum = Convert.ToInt32(reader["MucTonToiThieu"]);
                            var active = Convert.ToBoolean(reader["IsActive"]);
                            var expiry = reader["HanSuDung"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["HanSuDung"]);
                            var status = !active ? "Ngừng kinh doanh" : quantity == 0 ? "Hết hàng" : quantity <= minimum ? "Sắp hết" : expiry.HasValue && expiry.Value.Date < DateTime.Today ? "Hết hạn" : "Còn hàng";
                            model.Items.Add(new MedicineItemViewModel
                            {
                                MaThuoc = Convert.ToInt32(reader["MaThuoc"]),
                                TenThuoc = reader["TenThuoc"].ToString(),
                                HoatChat = reader["HoatChat"] == DBNull.Value ? null : reader["HoatChat"].ToString(),
                                NhomThuoc = reader["NhomThuoc"] == DBNull.Value ? null : reader["NhomThuoc"].ToString(),
                                DonViTinh = reader["DonViTinh"] == DBNull.Value ? null : reader["DonViTinh"].ToString(),
                                SoLuongTon = quantity,
                                GiaNhap = reader["GiaNhap"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["GiaNhap"]),
                                GiaBan = Convert.ToDecimal(reader["GiaBan"]),
                                HanSuDung = expiry,
                                MucTonToiThieu = minimum,
                                IsActive = active,
                                TrangThai = status
                            });
                        }
                    }
                }
            }

            model.TotalPages = model.TotalItems == 0 ? 0 : (int)Math.Ceiling(model.TotalItems / (double)model.PageSize);
            if (model.TotalPages > 0 && model.Page > model.TotalPages) model.Page = model.TotalPages;
        }

        private static void AddMedicineListParameters(SqlCommand command, MedicineListViewModel model)
        {
            command.Parameters.Add("@Search", SqlDbType.VarChar, 150).Value = model.Search ?? string.Empty;
            command.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 302).Value = "%" + (model.Search ?? string.Empty) + "%";
            command.Parameters.Add("@GroupName", SqlDbType.NVarChar, 100).Value = model.Group ?? string.Empty;
            command.Parameters.Add("@Status", SqlDbType.VarChar, 20).Value = model.Status ?? string.Empty;
            command.Parameters.Add("@ExpiryStatus", SqlDbType.VarChar, 20).Value = model.ExpiryStatus ?? string.Empty;
        }

        [HttpGet]
        public ActionResult ChiTietLichHen(int id)
        {
            var item = FindAppointment(id);
            if (item == null)
            {
                return HttpNotFound("Không tìm thấy lịch hẹn.");
            }

            ViewBag.Title = "Chi tiết lịch hẹn";
            ViewBag.RoleName = "Quản lý phòng khám";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Chi tiết lịch hẹn";
            return View("_ChiTietLichHen", item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CapNhatTrangThaiLichHen(int id, string status)
        {
            var validStatuses = new[] { "Đã đặt", "Đã khám", "Hủy" };
            if (Array.IndexOf(validStatuses, status) < 0) return new HttpStatusCodeResult(400, "Trạng thái không hợp lệ.");
            try
            {
                using (var c = CreateConnection())
                {
                    c.Open();

                    int patientId = 0, doctorId = 0;
                    string patientEmail = "", patientName = "";
                    DateTime ngayKham = DateTime.Today;

                    using (var infoCmd = new SqlCommand(@"
                        SELECT lh.BenhNhanId, ISNULL(lh.BacSiId, 0), bn.Email, bn.FullName, lh.NgayKham
                        FROM dbo.LichHen lh
                        INNER JOIN dbo.Users bn ON bn.UserId = lh.BenhNhanId
                        WHERE lh.MaLichHen = @Id;", c))
                    {
                        infoCmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        using (var r = infoCmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                patientId = r.GetInt32(0);
                                doctorId = r.GetInt32(1);
                                patientEmail = r.IsDBNull(2) ? "" : r.GetString(2);
                                patientName = r.GetString(3);
                                ngayKham = r.GetDateTime(4);
                            }
                        }
                    }

                    using (var cmd = new SqlCommand(@"UPDATE dbo.LichHen SET TrangThai = @Status WHERE MaLichHen = @Id AND TrangThai <> N'Đã khám';", c))
                    {
                        cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = status;
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        if (cmd.ExecuteNonQuery() != 1)
                        {
                            TempData["AppointmentError"] = "Lịch không tồn tại hoặc đã hoàn thành nên không thể đổi trạng thái.";
                        }
                        else
                        {
                            TempData["AppointmentMessage"] = "Đã cập nhật trạng thái lịch hẹn.";

                            if (patientId > 0)
                            {
                                string eventType = status == "Hủy" ? "APPOINTMENT_CANCELLED" : "APPOINTMENT_UPDATED";
                                NotificationService.CreateNotification(
                                    patientId,
                                    eventType,
                                    $"Cập nhật lịch hẹn #{id}",
                                    $"Trạng thái lịch hẹn #{id} vào ngày {ngayKham:dd/MM/yyyy} đã chuyển sang '{status}'.",
                                    id
                                );

                                if (doctorId > 0)
                                {
                                    NotificationService.CreateNotification(
                                        doctorId,
                                        eventType,
                                        $"Lịch hẹn #{id} đã cập nhật trạng thái",
                                        $"Lịch hẹn #{id} của bệnh nhân {patientName} vào ngày {ngayKham:dd/MM/yyyy} đã chuyển sang '{status}'.",
                                        id
                                    );
                                }

                                if (status == "Hủy" && !string.IsNullOrWhiteSpace(patientEmail))
                                {
                                    string mailBody = $@"<h3>Thông Báo Hủy Lịch Hẹn - MediCare</h3>
                                        <p>Xin chào <strong>{patientName}</strong>,</p>
                                        <p>Lịch hẹn <strong>#{id}</strong> của bạn vào ngày <strong>{ngayKham:dd/MM/yyyy}</strong> đã chuyển sang trạng thái <strong>Hủy</strong>.</p>
                                        <p>Vui lòng đặt lại lịch mới hoặc liên hệ phòng khám nếu cần hỗ trợ.</p>";
                                    EmailService.SendEmail(patientEmail, $"[MediCare] Thông báo hủy lịch hẹn #{id}", mailBody);
                                }
                            }
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("Appointment status error: " + ex);
                TempData["AppointmentError"] = "Không thể cập nhật trạng thái lịch hẹn.";
            }
            return RedirectToAction("LichHen");
        }

        private void LoadAppointmentList(AppointmentListViewModel model)
        {
            using (var c = CreateConnection())
            {
                c.Open();
                model.Doctors = LoadAppointmentDoctors(c);
                var where = @" FROM dbo.LichHen lh
                    INNER JOIN dbo.Users bn ON bn.UserId = lh.BenhNhanId
                    LEFT JOIN dbo.Users bs ON bs.UserId = lh.BacSiId
                    WHERE (@Search = '' OR CONVERT(varchar(20), lh.MaLichHen) = @Search
                           OR CONVERT(varchar(20), lh.BenhNhanId) = @Search
                           OR bn.FullName LIKE @LikeSearch OR bn.Phone LIKE @LikeSearch)
                      AND (@DoctorId = 0 OR lh.BacSiId = @DoctorId)
                      AND (@Status = '' OR lh.TrangThai = @Status)
                      AND (@FromDate IS NULL OR lh.NgayKham >= @FromDate)
                      AND (@ToDate IS NULL OR lh.NgayKham <= @ToDate)";
                using (var count = new SqlCommand("SELECT COUNT(1)" + where, c))
                {
                    AddAppointmentFilters(count, model);
                    model.TotalItems = Convert.ToInt32(count.ExecuteScalar());
                }
                using (var stats = new SqlCommand(@"SELECT COUNT(1), COALESCE(SUM(CASE WHEN TrangThai = N'Đã đặt' THEN 1 ELSE 0 END), 0), COALESCE(SUM(CASE WHEN TrangThai = N'Đã khám' THEN 1 ELSE 0 END), 0), COALESCE(SUM(CASE WHEN TrangThai = N'Hủy' THEN 1 ELSE 0 END), 0) FROM dbo.LichHen;", c))
                using (var r = stats.ExecuteReader())
                {
                    if (r.Read()) { model.TotalAppointments = Convert.ToInt32(r[0]); model.BookedAppointments = Convert.ToInt32(r[1]); model.CompletedAppointments = Convert.ToInt32(r[2]); model.CancelledAppointments = Convert.ToInt32(r[3]); }
                }
                using (var list = new SqlCommand(@"SELECT lh.MaLichHen, lh.BenhNhanId, bn.FullName AS BenhNhanName, bn.Phone AS BenhNhanPhone, lh.BacSiId, bs.FullName AS BacSiName, lh.NgayKham, lh.GioKham, lh.SoThuTu, lh.LyDoKham, lh.TrangThai" + where + @" ORDER BY lh.NgayKham, lh.GioKham, lh.MaLichHen OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", c))
                {
                    AddAppointmentFilters(list, model);
                    list.Parameters.Add("@Offset", SqlDbType.Int).Value = (model.Page - 1) * model.PageSize;
                    list.Parameters.Add("@PageSize", SqlDbType.Int).Value = model.PageSize;
                    using (var r = list.ExecuteReader()) while (r.Read()) model.Items.Add(ReadAppointment(r));
                }
            }
            model.TotalPages = model.TotalItems == 0 ? 0 : (int)Math.Ceiling(model.TotalItems / (double)model.PageSize);
            if (model.TotalPages > 0 && model.Page > model.TotalPages) model.Page = model.TotalPages;
        }

        private static void AddAppointmentFilters(SqlCommand c, AppointmentListViewModel m)
        {
            c.Parameters.Add("@Search", SqlDbType.VarChar, 100).Value = m.Search ?? "";
            c.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 202).Value = "%" + (m.Search ?? "") + "%";
            c.Parameters.Add("@DoctorId", SqlDbType.Int).Value = m.DoctorId ?? 0;
            c.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = m.Status ?? "";
            c.Parameters.Add("@FromDate", SqlDbType.Date).Value = (object)m.FromDate?.Date ?? DBNull.Value;
            c.Parameters.Add("@ToDate", SqlDbType.Date).Value = (object)m.ToDate?.Date ?? DBNull.Value;
        }

        private static AppointmentItemViewModel ReadAppointment(IDataRecord r)
        {
            return new AppointmentItemViewModel { MaLichHen = Convert.ToInt32(r["MaLichHen"]), BenhNhanId = Convert.ToInt32(r["BenhNhanId"]), BenhNhanName = r["BenhNhanName"].ToString(), BenhNhanPhone = r["BenhNhanPhone"] == DBNull.Value ? null : r["BenhNhanPhone"].ToString(), BacSiId = r["BacSiId"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["BacSiId"]), BacSiName = r["BacSiName"] == DBNull.Value ? null : r["BacSiName"].ToString(), NgayKham = Convert.ToDateTime(r["NgayKham"]), GioKham = (TimeSpan)r["GioKham"], SoThuTu = r["SoThuTu"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["SoThuTu"]), LyDoKham = r["LyDoKham"] == DBNull.Value ? null : r["LyDoKham"].ToString(), TrangThai = r["TrangThai"].ToString() };
        }

        private List<DoctorOptionViewModel> LoadAppointmentDoctors(SqlConnection c)
        {
            var list = new List<DoctorOptionViewModel>();
            using (var cmd = new SqlCommand("SELECT u.UserId, u.FullName FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE r.RoleName = N'Bác sĩ' AND u.IsActive = 1 ORDER BY u.FullName;", c))
            using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(new DoctorOptionViewModel { UserId = r.GetInt32(0), FullName = r.GetString(1) });
            return list;
        }

        private AppointmentItemViewModel FindAppointment(int id)
        {
            using (var c = CreateConnection()) using (var cmd = new SqlCommand(@"SELECT lh.MaLichHen, lh.BenhNhanId, bn.FullName AS BenhNhanName, bn.Phone AS BenhNhanPhone, lh.BacSiId, bs.FullName AS BacSiName, lh.NgayKham, lh.GioKham, lh.SoThuTu, lh.LyDoKham, lh.TrangThai FROM dbo.LichHen lh INNER JOIN dbo.Users bn ON bn.UserId = lh.BenhNhanId LEFT JOIN dbo.Users bs ON bs.UserId = lh.BacSiId WHERE lh.MaLichHen = @Id;", c)) { cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id; c.Open(); using (var r = cmd.ExecuteReader()) return r.Read() ? ReadAppointment(r) : null; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult PhanCongBacSi(int id, int doctorId)
        {
            if (id <= 0 || doctorId <= 0) return new HttpStatusCodeResult(400, "Thông tin không hợp lệ.");

            try
            {
                using (var c = CreateConnection())
                {
                    c.Open();

                    int soThuTu = 1;
                    DateTime ngayKham = DateTime.Today;
                    using (var cmd = new SqlCommand("SELECT NgayKham FROM dbo.LichHen WHERE MaLichHen = @Id;", c))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        var val = cmd.ExecuteScalar();
                        if (val != null && val != DBNull.Value) ngayKham = Convert.ToDateTime(val);
                    }

                    using (var cmd = new SqlCommand(@"
                        SELECT ISNULL(MAX(SoThuTu), 0) + 1 FROM dbo.LichHen
                        WHERE BacSiId = @DoctorId AND NgayKham = @NgayKham;", c))
                    {
                        cmd.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                        cmd.Parameters.Add("@NgayKham", SqlDbType.Date).Value = ngayKham.Date;
                        soThuTu = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    using (var cmd = new SqlCommand(@"
                        UPDATE dbo.LichHen
                        SET BacSiId = @DoctorId, SoThuTu = @SoThuTu
                        WHERE MaLichHen = @Id AND TrangThai = N'Đã đặt';", c))
                    {
                        cmd.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                        cmd.Parameters.Add("@SoThuTu", SqlDbType.Int).Value = soThuTu;
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        if (cmd.ExecuteNonQuery() == 1)
                        {
                            int patientId = 0;
                            string patientEmail = "", patientName = "", doctorName = "";
                            TimeSpan gioKham = TimeSpan.FromHours(8);
                            using (var infoCmd = new SqlCommand(@"
                                SELECT lh.BenhNhanId, bn.Email, bn.FullName, bs.FullName, lh.GioKham
                                FROM dbo.LichHen lh
                                INNER JOIN dbo.Users bn ON bn.UserId = lh.BenhNhanId
                                INNER JOIN dbo.Users bs ON bs.UserId = @DoctorId
                                WHERE lh.MaLichHen = @Id;", c))
                            {
                                infoCmd.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                                infoCmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                                using (var r = infoCmd.ExecuteReader())
                                {
                                    if (r.Read())
                                    {
                                        patientId = r.GetInt32(0);
                                        patientEmail = r.IsDBNull(1) ? "" : r.GetString(1);
                                        patientName = r.GetString(2);
                                        doctorName = r.GetString(3);
                                        gioKham = (TimeSpan)r.GetTimeSpan(4);
                                    }
                                }
                            }

                            if (patientId > 0)
                            {
                                int existingCount = 0;
                                using (var checkDup = new SqlCommand("SELECT COUNT(1) FROM dbo.ThongBao WHERE MaLichHen = @Id AND LoaiThongBao = N'APPOINTMENT_CONFIRMED' AND NguoiNhanId = @PatientId;", c))
                                {
                                    checkDup.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                                    checkDup.Parameters.Add("@PatientId", SqlDbType.Int).Value = patientId;
                                    existingCount = Convert.ToInt32(checkDup.ExecuteScalar());
                                }

                                if (existingCount == 0)
                                {
                                    NotificationService.CreateNotification(
                                        patientId,
                                        "APPOINTMENT_CONFIRMED",
                                        "Lịch khám của bạn đã được xác nhận",
                                        $"Lịch khám #{id} vào ngày {ngayKham:dd/MM/yyyy} lúc {gioKham:hh\\:mm} đã được xác nhận. Bác sĩ phụ trách: {doctorName}.",
                                        id
                                    );

                                    NotificationService.CreateNotification(
                                        doctorId,
                                        "APPOINTMENT_NEW",
                                        "Bạn có lịch hẹn mới được phân công",
                                        $"Bạn đã được phân công khám cho bệnh nhân {patientName} (Lịch #{id}) vào ngày {ngayKham:dd/MM/yyyy}.",
                                        id
                                    );

                                    if (!string.IsNullOrWhiteSpace(patientEmail))
                                    {
                                        string mailBody = $@"<h3>Xác Nhận Lịch Hẹn Khám - Phòng Khám MediCare</h3>
                                            <p>Xin chào <strong>{patientName}</strong>,</p>
                                            <p>Lịch hẹn khám <strong>#{id}</strong> của bạn đã được Quản lý xác nhận thành công.</p>
                                            <ul>
                                                <li><strong>Bác sĩ phụ trách:</strong> {doctorName}</li>
                                                <li><strong>Ngày khám:</strong> {ngayKham:dd/MM/yyyy}</li>
                                                <li><strong>Giờ khám:</strong> {gioKham:hh\\:mm}</li>
                                                <li><strong>Số thứ tự:</strong> {soThuTu}</li>
                                            </ul>
                                            <p>Cảm ơn bạn đã lựa chọn dịch vụ y tế của MediCare!</p>";
                                        EmailService.SendEmail(patientEmail, $"[MediCare] Xác nhận lịch hẹn khám #{id}", mailBody);
                                    }
                                }
                            }

                            TempData["AppointmentMessage"] = "Đã phân công bác sĩ thành công. Số thứ tự: " + soThuTu;
                        }
                        else
                        {
                            TempData["AppointmentError"] = "Không thể phân công: lịch hẹn không tồn tại hoặc không ở trạng thái 'Đã đặt'.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("PhanCongBacSi Error: " + ex);
                TempData["AppointmentError"] = "Lỗi khi phân công bác sĩ.";
            }

            return RedirectToAction("LichHen");
        }

        [HttpGet]
        public ActionResult ThanhToan(int? examinationId, int? invoiceId)
        {
            SetDashboard("Thanh toán Hóa đơn & VietQR");
            ViewBag.ActiveManagerMenu = "appointments";

            if ((!examinationId.HasValue || examinationId.Value <= 0) && (!invoiceId.HasValue || invoiceId.Value <= 0))
            {
                return HttpNotFound("Mã phiếu khám hoặc mã hóa đơn không hợp lệ.");
            }

            var model = new InvoiceDetailViewModel();

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();

                    if (!invoiceId.HasValue || invoiceId.Value <= 0)
                    {
                        using (var cmd = new SqlCommand("sp_TaoHoaDonChoPhieuKham", connection))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.Add("@MaPhieuKham", SqlDbType.Int).Value = examinationId.Value;
                            cmd.Parameters.Add("@PhuongThucTT", SqlDbType.NVarChar, 50).Value = "Tiền mặt";
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new SqlCommand("SELECT TOP 1 MaHoaDon FROM dbo.HoaDon WHERE MaPhieuKham = @MaPhieuKham;", connection))
                        {
                            cmd.Parameters.Add("@MaPhieuKham", SqlDbType.Int).Value = examinationId.Value;
                            var val = cmd.ExecuteScalar();
                            if (val != null && val != DBNull.Value)
                            {
                                invoiceId = Convert.ToInt32(val);
                            }
                        }
                    }

                    if (!invoiceId.HasValue || invoiceId.Value <= 0)
                    {
                        return HttpNotFound("Không tìm thấy hóa đơn cho phiếu khám này.");
                    }

                    using (var cmd = new SqlCommand(@"
                        SELECT TOP 1
                            hd.MaHoaDon, hd.MaPhieuKham, hd.BenhNhanId, bn.FullName, bn.Phone,
                            bs.FullName, COALESCE(k.TenKhoa, N'Chưa phân khoa'), pk.NgayKham,
                            hd.NgayThanhToan, hd.TrangThai, hd.PhuongThucTT, hd.TienKhamAndCLS, hd.TienThuoc, hd.TongThanhToan
                        FROM dbo.HoaDon hd
                        INNER JOIN dbo.PhieuKhamBenh pk ON pk.MaPhieuKham = hd.MaPhieuKham
                        INNER JOIN dbo.Users bn ON bn.UserId = hd.BenhNhanId
                        INNER JOIN dbo.Users bs ON bs.UserId = pk.BacSiId
                        LEFT JOIN dbo.Khoa k ON k.MaKhoa = bs.MaKhoa
                        WHERE hd.MaHoaDon = @InvoiceId;", connection))
                    {
                        cmd.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = invoiceId.Value;
                        using (var r = cmd.ExecuteReader())
                        {
                            if (!r.Read()) return HttpNotFound("Hóa đơn không tồn tại.");

                            model.InvoiceId = r.GetInt32(0);
                            model.ExaminationId = r.GetInt32(1);
                            model.PatientId = r.GetInt32(2);
                            model.PatientName = r.GetString(3);
                            model.PatientPhone = r.IsDBNull(4) ? "" : r.GetString(4);
                            model.DoctorName = r.GetString(5);
                            model.DepartmentName = r.GetString(6);
                            model.ExaminationDate = r.GetDateTime(7);
                            model.PaymentDate = r.IsDBNull(8) ? (DateTime?)null : r.GetDateTime(8);
                            model.Status = r.GetString(9);
                            model.PaymentMethod = r.IsDBNull(10) ? "Tiền mặt" : r.GetString(10);
                            model.ClsFee = r.GetDecimal(11);
                            model.MedicineFee = r.GetDecimal(12);
                            model.TotalAmount = r.GetDecimal(13);
                        }
                    }

                    using (var cmd = new SqlCommand(@"
                        SELECT d.TenDichVu, c.DonGia, c.TrangThai
                        FROM dbo.ChiTietCLS c
                        INNER JOIN dbo.DichVuCLS d ON d.MaDichVu = c.MaDichVu
                        WHERE c.MaPhieuKham = @ExaminationId;", connection))
                    {
                        cmd.Parameters.Add("@ExaminationId", SqlDbType.Int).Value = model.ExaminationId;
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.ClsItems.Add(new InvoiceLineItemViewModel
                                {
                                    Name = r.GetString(0),
                                    Quantity = 1,
                                    UnitPrice = r.GetDecimal(1),
                                    Amount = r.GetDecimal(1),
                                    Note = r.GetString(2)
                                });
                            }
                        }
                    }

                    using (var cmd = new SqlCommand(@"
                        SELECT t.TenThuoc, c.SoLuong, c.DonGia, c.ThanhTien, c.CachDung
                        FROM dbo.ChiTietDonThuoc c
                        INNER JOIN dbo.DonThuoc dt ON dt.MaDonThuoc = c.MaDonThuoc
                        INNER JOIN dbo.Thuoc t ON t.MaThuoc = c.MaThuoc
                        WHERE dt.MaPhieuKham = @ExaminationId;", connection))
                    {
                        cmd.Parameters.Add("@ExaminationId", SqlDbType.Int).Value = model.ExaminationId;
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.MedicineItems.Add(new InvoiceLineItemViewModel
                                {
                                    Name = r.GetString(0),
                                    Quantity = r.GetInt32(1),
                                    UnitPrice = r.GetDecimal(2),
                                    Amount = r.GetDecimal(3),
                                    Note = r.IsDBNull(4) ? "" : r.GetString(4)
                                });
                            }
                        }
                    }
                }

                model.ReferenceCode = "MEDICARE HD" + model.InvoiceId;
                model.QrCodeUrl = "https://api.qrserver.com/v1/create-qr-code/?size=250x250&data=" + System.Web.HttpUtility.UrlEncode("https://bitly.com.vn/Jmff");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ThanhToan error: " + ex);
                ViewBag.Error = "Không thể tải thông tin hóa đơn.";
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult XacNhanThanhToanTienMat(int invoiceId)
        {
            if (invoiceId <= 0) return new HttpStatusCodeResult(400);

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        UPDATE dbo.HoaDon
                        SET TrangThai = N'Đã thanh toán',
                            PhuongThucTT = N'Tiền mặt',
                            NgayThanhToan = GETDATE()
                        WHERE MaHoaDon = @InvoiceId;", connection))
                    {
                        cmd.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = invoiceId;
                        cmd.ExecuteNonQuery();
                    }
                }
                TempData["Message"] = "Đã xác nhận thanh toán tiền mặt thành công cho hóa đơn #" + invoiceId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("XacNhanThanhToanTienMat error: " + ex);
                TempData["Error"] = "Không thể xác nhận thanh toán tiền mặt.";
            }

            return RedirectToAction("ThanhToan", new { invoiceId = invoiceId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult MoPhongThanhToanQR(int invoiceId)
        {
            if (invoiceId <= 0) return new HttpStatusCodeResult(400);

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        UPDATE dbo.HoaDon
                        SET TrangThai = N'Đã thanh toán',
                            PhuongThucTT = N'QR Chuyển khoản',
                            NgayThanhToan = GETDATE()
                        WHERE MaHoaDon = @InvoiceId;", connection))
                    {
                        cmd.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = invoiceId;
                        cmd.ExecuteNonQuery();
                    }
                }
                TempData["Message"] = "Đã xác nhận giao dịch chuyển khoản VietQR thành công cho hóa đơn #" + invoiceId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MoPhongThanhToanQR error: " + ex);
                TempData["Error"] = "Không thể xác nhận thanh toán QR.";
            }

            return RedirectToAction("ThanhToan", new { invoiceId = invoiceId });
        }

        [HttpGet]
        public ActionResult LichHen(string search = null, int? doctorId = null, string status = null, DateTime? fromDate = null, DateTime? toDate = null, string preset = null, int page = 1)
        {
            ViewBag.Title = "Lịch hẹn & Hàng đợi Tiếp nhận";
            ViewBag.RoleName = "Quản lý phòng khám";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Lịch hẹn & Điều phối Bệnh nhân";
            var today = DateTime.Today;
            if (preset == "today") { fromDate = today; toDate = today; }
            else if (preset == "tomorrow") { fromDate = today.AddDays(1); toDate = today.AddDays(1); }
            else if (preset == "week")
            {
                var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
                fromDate = today.AddDays(-daysSinceMonday);
                toDate = fromDate.Value.AddDays(6);
            }
            var model = new AppointmentListViewModel { Search = search?.Trim(), DoctorId = doctorId, Status = status?.Trim(), FromDate = fromDate, ToDate = toDate, Preset = preset, Page = Math.Max(page, 1), PageSize = 10 };
            if (model.FromDate.HasValue && model.ToDate.HasValue && model.FromDate > model.ToDate) { ViewBag.AppointmentError = "Ngày bắt đầu không được sau ngày kết thúc."; return View(model); }
            try { LoadAppointmentList(model); } catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException) { System.Diagnostics.Debug.WriteLine("Appointment list error: " + ex); ViewBag.AppointmentError = "Không thể tải dữ liệu lịch hẹn."; }
            return View(model);
        }

        [HttpGet]
        public ActionResult QuanLyTinNhanLienHe()
        {
            SetDashboard("Tiếp nhận & Xử lý Tin nhắn Liên hệ");
            ViewBag.ActiveManagerMenu = "contacts";

            var model = new List<ContactMessageViewModel>();
            try
            {
                using (var connection = CreateConnection())
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
                        SELECT MaTinNhan, HoTen, Email, SoDienThoai, TieuDe, NoiDung, TrangThai, NgayGui, GhiChuXuLy
                        FROM dbo.TinNhanLienHe
                        ORDER BY MaTinNhan DESC;", connection))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.Add(new ContactMessageViewModel
                                {
                                    MaTinNhan = r.GetInt32(0),
                                    HoTen = r.GetString(1),
                                    Email = r.IsDBNull(2) ? "" : r.GetString(2),
                                    SoDienThoai = r.IsDBNull(3) ? "" : r.GetString(3),
                                    TieuDe = r.IsDBNull(4) ? "" : r.GetString(4),
                                    NoiDung = r.GetString(5),
                                    TrangThai = r.GetString(6),
                                    NgayGui = r.GetDateTime(7),
                                    GhiChuXuLy = r.IsDBNull(8) ? "" : r.GetString(8)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("QuanLyTinNhanLienHe error: " + ex);
                ViewBag.Error = "Không thể tải danh sách tin nhắn liên hệ.";
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CapNhatTinNhanLienHe(int id, string note)
        {
            if (id <= 0) return new HttpStatusCodeResult(400);

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        UPDATE dbo.TinNhanLienHe
                        SET TrangThai = N'Đã xử lý', GhiChuXuLy = @Note
                        WHERE MaTinNhan = @Id;", connection))
                    {
                        cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = (object)note?.Trim() ?? DBNull.Value;
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        cmd.ExecuteNonQuery();
                    }
                }
                TempData["Message"] = "Đã cập nhật trạng thái xử lý cho tin nhắn #" + id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CapNhatTinNhanLienHe error: " + ex);
                TempData["Error"] = "Không thể cập nhật tin nhắn.";
            }

            return RedirectToAction("QuanLyTinNhanLienHe");
        }
    }
}
