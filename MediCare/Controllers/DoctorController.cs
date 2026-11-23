using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using MediCare.Models;

namespace MediCare.Controllers
{
    [Authorize(Roles = "Bác sĩ")]
    public class DoctorController : Controller
    {
        private readonly string connectionString = ConfigurationManager.ConnectionStrings["MediCareDb"]?.ConnectionString;

        public ActionResult Index()
        {
            SetDashboard("Tổng quan Ca trực");
            ViewBag.ActiveDoctorMenu = "Index";
            var model = new DoctorDashboardViewModel { Today = DateTime.Today };
            try { LoadDoctorDashboard(model); }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("Doctor dashboard error: " + ex);
                ViewBag.DatabaseError = "Không thể tải dữ liệu lịch khám.";
            }
            return View(model);
        }

        public ActionResult PhieuKham(string destination = null)
        {
            SetDashboard("Chọn phiếu khám");
            if (destination != "KhamBenh" && destination != "ChiDinhCLS" && destination != "KeDonThuoc") destination = "ChiDinhCLS";
            var model = new DoctorAppointmentListViewModel { Date = DateTime.Today };
            try
            {
                if (destination == "KhamBenh")
                {
                    LoadDoctorAppointments(model);
                }
                else
                {
                    model.ExaminationChoices = LoadExaminationChoices();
                }
            }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("Doctor examination selection error: " + ex);
                ViewBag.DatabaseError = "Không thể tải danh sách phiếu khám.";
            }
            ViewBag.Destination = destination;
            ViewBag.ActiveDoctorMenu = destination;
            return View("LichHen", model);
        }

        public ActionResult LichHen(DateTime? date = null, string search = null, string status = null)
        {
            SetDashboard("Lịch hẹn & Hàng đợi Tiếp nhận");
            ViewBag.ActiveDoctorMenu = "LichHen";
            var model = new DoctorAppointmentListViewModel { Date = date?.Date ?? DateTime.Today, Search = search?.Trim(), Status = status?.Trim() };
            try { LoadDoctorAppointments(model); }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("Doctor appointment error: " + ex);
                ViewBag.DatabaseError = "Không thể tải lịch hẹn.";
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CapNhatTrangThaiLichHen(int id, string status, string returnUrl)
        {
            if (id <= 0 || status != "Hủy") return new HttpStatusCodeResult(400, "Chỉ cho phép hủy lịch đang ở trạng thái Đã đặt.");
            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);
                using (var command = new SqlCommand(@"
                    UPDATE lh SET TrangThai = @Status
                    FROM dbo.LichHen lh
                    WHERE lh.MaLichHen = @Id AND lh.TrangThai = N'Đã đặt'
                      AND (lh.BacSiId = @DoctorId OR @DoctorId = 0);", connection))
                {
                    command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = status;
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                    if (command.ExecuteNonQuery() != 1) return new HttpUnauthorizedResult("Lịch hẹn không thuộc bác sĩ hiện tại.");
                }
            }
            if (Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
            return RedirectToAction("LichHen");
        }

        private DoctorExaminationViewModel LoadExamination(int appointmentId)
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);
                using (var command = new SqlCommand(@"
                    SELECT TOP 1 lh.MaLichHen, lh.BenhNhanId, bn.FullName, bn.Phone,
                           COALESCE(pk.NgayKham, lh.NgayKham), pk.MaPhieuKham,
                           pk.HuyetAp, pk.Mach, pk.NhietDo, pk.CanNang,
                           pk.TrieuChung, pk.ChanDoan, pk.LoiDan, pk.TrangThai
                    FROM dbo.LichHen lh
                    INNER JOIN dbo.Users bn ON bn.UserId = lh.BenhNhanId
                    LEFT JOIN dbo.PhieuKhamBenh pk ON pk.MaLichHen = lh.MaLichHen
                    WHERE lh.MaLichHen = @Id AND (lh.BacSiId = @DoctorId OR @DoctorId = 0);", connection))
                {
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = appointmentId;
                    command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) return null;
                        return new DoctorExaminationViewModel
                        {
                            AppointmentId = Convert.ToInt32(reader[0]),
                            PatientId = Convert.ToInt32(reader[1]),
                            PatientName = reader[2].ToString(),
                            Phone = reader[3] == DBNull.Value ? null : reader[3].ToString(),
                            ExaminationDate = Convert.ToDateTime(reader[4]),
                            ExaminationId = reader[5] == DBNull.Value ? (int?)null : Convert.ToInt32(reader[5]),
                            AlreadySaved = reader[5] != DBNull.Value,
                            BloodPressure = reader[6] == DBNull.Value ? null : reader[6].ToString(),
                            Pulse = reader[7] == DBNull.Value ? (int?)null : Convert.ToInt32(reader[7]),
                            Temperature = reader[8] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader[8]),
                            Weight = reader[9] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader[9]),
                            Symptoms = reader[10] == DBNull.Value ? null : reader[10].ToString(),
                            Diagnosis = reader[11] == DBNull.Value ? null : reader[11].ToString(),
                            Instructions = reader[12] == DBNull.Value ? null : reader[12].ToString(),
                            Status = reader[13] == DBNull.Value ? "Đang khám" : reader[13].ToString()
                        };
                    }
                }
            }
        }

        public ActionResult KhamBenh(int? appointmentId)
        {
            SetDashboard("Khám bệnh Lâm sàng (SOAP)");
            ViewBag.ActiveDoctorMenu = "KhamBenh";
            if (!appointmentId.HasValue || appointmentId.Value <= 0)
            {
                var soapSelectionModel = new DoctorAppointmentListViewModel { Date = DateTime.Today };
                try { LoadDoctorAppointments(soapSelectionModel); }
                catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
                {
                    System.Diagnostics.Debug.WriteLine("Doctor SOAP selection error: " + ex);
                    ViewBag.DatabaseError = "Không thể tải danh sách lịch hẹn để mở SOAP.";
                }
                ViewBag.Destination = "KhamBenh";
                TempData["Message"] = "Chọn lịch hẹn được phân công để mở phiếu khám SOAP.";
                return View("LichHen", soapSelectionModel);
            }
            var loadedExamination = LoadExamination(appointmentId.Value);
            if (loadedExamination == null) return HttpNotFound("Lịch hẹn không tồn tại hoặc không thuộc bác sĩ hiện tại.");
            return View(loadedExamination);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult KhamBenh(DoctorExaminationViewModel model)
        {
            SetDashboard("Khám bệnh Lâm sàng (SOAP)");
            ViewBag.ActiveDoctorMenu = "KhamBenh";
            if (model == null || model.AppointmentId <= 0) return new HttpStatusCodeResult(400, "Mã lịch hẹn không hợp lệ.");
            if (!ModelState.IsValid)
            {
                var appointment = LoadExamination(model.AppointmentId);
                if (appointment == null) return HttpNotFound("Lịch hẹn không tồn tại.");
                appointment.BloodPressure = model.BloodPressure;
                appointment.Pulse = model.Pulse;
                appointment.Temperature = model.Temperature;
                appointment.Weight = model.Weight;
                appointment.Symptoms = model.Symptoms;
                appointment.Diagnosis = model.Diagnosis;
                appointment.Instructions = model.Instructions;
                ModelState.AddModelError("", "Vui lòng kiểm tra các trường bắt buộc và giá trị sinh hiệu.");
                return View(appointment);
            }

            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);

                if (!IsAppointmentOwnedByDoctor(connection, model.AppointmentId, doctorId))
                {
                    return new HttpUnauthorizedResult("Lịch hẹn không thuộc bác sĩ hiện tại.");
                }

                bool exists = false;
                using (var checkCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.PhieuKhamBenh WHERE MaLichHen = @AppointmentId;", connection))
                {
                    checkCmd.Parameters.Add("@AppointmentId", SqlDbType.Int).Value = model.AppointmentId;
                    exists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
                }

                if (exists)
                {
                    using (var command = new SqlCommand(@"
                        UPDATE dbo.PhieuKhamBenh
                        SET HuyetAp = @BloodPressure,
                            Mach = @Pulse,
                            NhietDo = @Temperature,
                            CanNang = @Weight,
                            TrieuChung = @Symptoms,
                            ChanDoan = @Diagnosis,
                            LoiDan = @Instructions
                        WHERE MaLichHen = @AppointmentId AND TrangThai <> N'Hủy';", connection))
                    {
                        command.Parameters.Add("@AppointmentId", SqlDbType.Int).Value = model.AppointmentId;
                        command.Parameters.Add("@BloodPressure", SqlDbType.VarChar, 20).Value = (object)model.BloodPressure ?? DBNull.Value;
                        command.Parameters.Add("@Pulse", SqlDbType.Int).Value = (object)model.Pulse ?? DBNull.Value;
                        var temperature = command.Parameters.Add("@Temperature", SqlDbType.Decimal);
                        temperature.Precision = 4; temperature.Scale = 1;
                        temperature.Value = (object)model.Temperature ?? DBNull.Value;
                        var weight = command.Parameters.Add("@Weight", SqlDbType.Decimal);
                        weight.Precision = 5; weight.Scale = 2;
                        weight.Value = (object)model.Weight ?? DBNull.Value;
                        command.Parameters.Add("@Symptoms", SqlDbType.NVarChar, -1).Value = model.Symptoms?.Trim() ?? "";
                        command.Parameters.Add("@Diagnosis", SqlDbType.NVarChar, -1).Value = model.Diagnosis?.Trim() ?? "";
                        command.Parameters.Add("@Instructions", SqlDbType.NVarChar, -1).Value = (object)model.Instructions?.Trim() ?? DBNull.Value;

                        command.ExecuteNonQuery();
                    }
                    TempData["Message"] = "Đã cập nhật phiếu khám thành công.";
                }
                else
                {
                    using (var command = new SqlCommand(@"
                        INSERT INTO dbo.PhieuKhamBenh
                            (MaLichHen, BenhNhanId, BacSiId, NgayKham, HuyetAp, Mach, NhietDo, CanNang, TrieuChung, ChanDoan, LoiDan, TrangThai)
                        SELECT @AppointmentId, lh.BenhNhanId, COALESCE(lh.BacSiId, @DoctorId), GETDATE(), @BloodPressure, @Pulse, @Temperature, @Weight,
                                @Symptoms, @Diagnosis, @Instructions, N'Đang khám'
                        FROM dbo.LichHen lh
                        WHERE lh.MaLichHen = @AppointmentId AND lh.TrangThai <> N'Hủy';", connection))
                    {
                        command.Parameters.Add("@AppointmentId", SqlDbType.Int).Value = model.AppointmentId;
                        command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                        command.Parameters.Add("@BloodPressure", SqlDbType.VarChar, 20).Value = (object)model.BloodPressure ?? DBNull.Value;
                        command.Parameters.Add("@Pulse", SqlDbType.Int).Value = (object)model.Pulse ?? DBNull.Value;
                        var temperature = command.Parameters.Add("@Temperature", SqlDbType.Decimal);
                        temperature.Precision = 4; temperature.Scale = 1;
                        temperature.Value = (object)model.Temperature ?? DBNull.Value;
                        var weight = command.Parameters.Add("@Weight", SqlDbType.Decimal);
                        weight.Precision = 5; weight.Scale = 2;
                        weight.Value = (object)model.Weight ?? DBNull.Value;
                        command.Parameters.Add("@Symptoms", SqlDbType.NVarChar, -1).Value = model.Symptoms?.Trim() ?? "";
                        command.Parameters.Add("@Diagnosis", SqlDbType.NVarChar, -1).Value = model.Diagnosis?.Trim() ?? "";
                        command.Parameters.Add("@Instructions", SqlDbType.NVarChar, -1).Value = (object)model.Instructions?.Trim() ?? DBNull.Value;

                        command.ExecuteNonQuery();
                    }
                    TempData["Message"] = "Đã lưu phiếu khám thành công.";
                }
            }

            return RedirectToAction("KhamBenh", new { appointmentId = model.AppointmentId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult HoanThanhKham(int appointmentId)
        {
            if (appointmentId <= 0) return new HttpStatusCodeResult(400);
            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);

                if (!IsAppointmentOwnedByDoctor(connection, appointmentId, doctorId))
                {
                    return new HttpUnauthorizedResult("Lịch hẹn không thuộc bác sĩ hiện tại.");
                }

                using (var command = new SqlCommand(@"
                    UPDATE dbo.PhieuKhamBenh
                    SET TrangThai = N'Hoàn thành'
                    WHERE MaLichHen = @AppointmentId AND (BacSiId = @DoctorId OR @DoctorId = 0);

                    UPDATE dbo.LichHen
                    SET TrangThai = N'Đã khám'
                    WHERE MaLichHen = @AppointmentId;", connection))
                {
                    command.Parameters.Add("@AppointmentId", SqlDbType.Int).Value = appointmentId;
                    command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                    command.ExecuteNonQuery();
                }
            }
            TempData["Message"] = "Đã hoàn thành lượt khám.";
            return RedirectToAction("KhamBenh", new { appointmentId = appointmentId });
        }

        public ActionResult HoSoBenhAn(string search = null)
        {
            SetDashboard("Hồ sơ Bệnh án Điện tử (EMR)");
            ViewBag.ActiveDoctorMenu = "HoSoBenhAn";
            var model = new DoctorMedicalRecordViewModel { Search = search?.Trim() };
            try { LoadMedicalRecords(model); }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("Doctor medical records error: " + ex);
                ViewBag.DatabaseError = "Không thể tải hồ sơ bệnh án.";
            }
            return View(model);
        }

        public ActionResult LichSuKham(int id)
        {
            SetDashboard("Lịch sử khám bệnh");
            ViewBag.ActiveDoctorMenu = "HoSoBenhAn";
            var model = LoadMedicalHistory(id);
            if (model == null) return HttpNotFound("Bệnh nhân chưa có lịch sử khám.");
            return View(model);
        }

        private bool HasValidExaminationId(int? examinationId, out int validExaminationId)
        {
            validExaminationId = examinationId ?? 0;
            return examinationId.HasValue && examinationId.Value > 0;
        }

        public ActionResult ChiDinhCLS(int? examinationId)
        {
            SetDashboard("Chỉ định & Kết quả Cận lâm sàng");
            ViewBag.ActiveDoctorMenu = "ChiDinhCLS";
            if (!HasValidExaminationId(examinationId, out var validExaminationId))
            {
                if (examinationId.HasValue)
                {
                    ViewBag.InvalidExaminationId = true;
                    return View(new DoctorClsViewModel());
                }

                TempData["Message"] = "Vui lòng chọn phiếu khám hợp lệ trước khi mở chức năng chỉ định CLS.";
                return RedirectToAction("PhieuKham", new { destination = "ChiDinhCLS" });
            }

            var model = LoadCls(validExaminationId);
            if (model == null) return HttpNotFound("Phiếu khám không tồn tại.");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChiDinhCLS(int examinationId, int serviceId)
        {
            SetDashboard("Chỉ định & Kết quả Cận lâm sàng");
            ViewBag.ActiveDoctorMenu = "ChiDinhCLS";
            if (examinationId <= 0 || serviceId <= 0) return new HttpStatusCodeResult(400, "Mã phiếu khám hoặc dịch vụ không hợp lệ.");
            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);
                if (!IsExaminationOwnedByDoctor(connection, examinationId, doctorId))
                {
                    return new HttpUnauthorizedResult("Phiếu khám không thuộc bác sĩ hiện tại.");
                }

                using (var command = new SqlCommand(@"
                    INSERT INTO dbo.ChiTietCLS (MaPhieuKham, MaDichVu, DonGia, TrangThai)
                    SELECT pk.MaPhieuKham, dv.MaDichVu, dv.DonGia, N'Chưa thực hiện'
                    FROM dbo.PhieuKhamBenh pk
                    INNER JOIN dbo.DichVuCLS dv ON dv.MaDichVu = @ServiceId
                    WHERE pk.MaPhieuKham = @ExaminationId
                      AND pk.TrangThai = N'Đang khám'
                      AND NOT EXISTS (SELECT 1 FROM dbo.ChiTietCLS c WHERE c.MaPhieuKham = pk.MaPhieuKham AND c.MaDichVu = dv.MaDichVu);", connection))
                {
                    command.Parameters.Add("@ExaminationId", SqlDbType.Int).Value = examinationId;
                    command.Parameters.Add("@ServiceId", SqlDbType.Int).Value = serviceId;
                    int affectedRows = command.ExecuteNonQuery();
                    if (affectedRows != 1)
                    {
                        var model = LoadCls(examinationId);
                        if (model != null)
                        {
                            model.ErrorMessage = model.ExaminationStatus != "Đang khám"
                                ? "Chỉ có thể tạo chỉ định cho phiếu đang khám."
                                : "Dịch vụ không hợp lệ hoặc đã được chỉ định cho phiếu này.";
                            return View(model);
                        }
                    }
                }
            }
            TempData["Message"] = "Đã tạo chỉ định CLS.";
            return RedirectToAction("ChiDinhCLS", new { examinationId = examinationId });
        }

        public ActionResult KeDonThuoc(int? examinationId)
        {
            SetDashboard("Kê đơn thuốc & Dược lâm sàng");
            ViewBag.ActiveDoctorMenu = "KeDonThuoc";
            if (!HasValidExaminationId(examinationId, out var validExaminationId))
            {
                var model = new DoctorPrescriptionViewModel { ExaminationId = 0 };
                if (examinationId.HasValue)
                {
                    ViewBag.InvalidExaminationId = true;
                    return View(model);
                }

                TempData["Message"] = "Vui lòng chọn phiếu khám hợp lệ trước khi mở chức năng kê đơn thuốc.";
                return RedirectToAction("PhieuKham", new { destination = "KeDonThuoc" });
            }

            var prescriptionModel = LoadPrescription(validExaminationId);
            if (string.IsNullOrWhiteSpace(prescriptionModel.PatientName)) return HttpNotFound("Phiếu khám không tồn tại.");
            return View(prescriptionModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult KeDonThuoc(DoctorPrescriptionInputViewModel input)
        {
            SetDashboard("Kê đơn thuốc & Dược lâm sàng");
            ViewBag.ActiveDoctorMenu = "KeDonThuoc";

            if (input == null || input.ExaminationId <= 0)
            {
                return new HttpStatusCodeResult(400, "Mã phiếu khám không hợp lệ.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Message"] = "Dữ liệu đơn thuốc không hợp lệ. Vui lòng kiểm tra lại.";
                return RedirectToAction("KeDonThuoc", new { examinationId = input.ExaminationId });
            }

            // Cho phép đơn thuốc rỗng: một số phiếu khám không cần kê thuốc.
            var activeItems = input.Items?.Where(i => i.Quantity > 0).ToList()
                              ?? new List<DoctorPrescriptionItemInputViewModel>();

            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);
                int examinationId = input.ExaminationId;

                if (!IsExaminationOwnedByDoctor(connection, examinationId, doctorId))
                {
                    return new HttpUnauthorizedResult("Phiếu khám không thuộc bác sĩ hiện tại.");
                }

                string examinationStatus = null;
                using (var cmd = new SqlCommand("SELECT TrangThai FROM dbo.PhieuKhamBenh WHERE MaPhieuKham = @Id;", connection))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = examinationId;
                    var statusValue = cmd.ExecuteScalar();
                    examinationStatus = statusValue == null || statusValue == DBNull.Value ? null : statusValue.ToString();
                }
                if (examinationStatus != "Đang khám")
                {
                    TempData["Message"] = "Chỉ có thể kê đơn thuốc cho phiếu đang khám.";
                    return RedirectToAction("KeDonThuoc", new { examinationId });
                }

                using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.DonThuoc WHERE MaPhieuKham = @Id;", connection))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = examinationId;
                    if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                    {
                        TempData["Message"] = "Phiếu khám đã có đơn thuốc. Vui lòng chỉnh sửa đơn hiện có thay vì kê thêm.";
                        return RedirectToAction("KeDonThuoc", new { examinationId });
                    }
                }

                // Kiểm tra tồn kho cho từng thuốc được kê trước khi ghi đơn
                foreach (var item in activeItems)
                {
                    using (var cmd = new SqlCommand("SELECT SoLuongTon FROM dbo.Thuoc WHERE MaThuoc = @MaThuoc;", connection))
                    {
                        cmd.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = item.MedicineId;
                        var stockValue = cmd.ExecuteScalar();
                        if (stockValue == null)
                        {
                            TempData["Message"] = "Thuốc được chọn không tồn tại trong kho.";
                            return RedirectToAction("KeDonThuoc", new { examinationId });
                        }
                        if (item.Quantity > Convert.ToInt32(stockValue))
                        {
                            TempData["Message"] = "Số lượng thuốc vượt quá tồn kho hiện có. Vui lòng kiểm tra lại.";
                            return RedirectToAction("KeDonThuoc", new { examinationId });
                        }
                    }
                }

                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        int prescriptionId = 0;
                        using (var cmd = new SqlCommand(@"
                            INSERT INTO dbo.DonThuoc (MaPhieuKham, BacSiId, NgayKeDon, TongTienThuoc)
                            VALUES (@MaPhieuKham, @BacSiId, GETDATE(), 0);
                            SELECT CAST(SCOPE_IDENTITY() AS int);", connection, transaction))
                        {
                            cmd.Parameters.Add("@MaPhieuKham", SqlDbType.Int).Value = examinationId;
                            cmd.Parameters.Add("@BacSiId", SqlDbType.Int).Value = doctorId;
                            prescriptionId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        foreach (var item in activeItems)
                        {
                            decimal price = 0;
                            using (var cmd = new SqlCommand("SELECT DonGia FROM dbo.Thuoc WHERE MaThuoc = @MaThuoc;", connection, transaction))
                            {
                                cmd.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = item.MedicineId;
                                price = Convert.ToDecimal(cmd.ExecuteScalar());
                            }

                            using (var cmd = new SqlCommand(@"
                                INSERT INTO dbo.ChiTietDonThuoc (MaDonThuoc, MaThuoc, SoLuong, DonGia, CachDung, ThanhTien)
                                VALUES (@MaDonThuoc, @MaThuoc, @SoLuong, @DonGia, @CachDung, @ThanhTien);", connection, transaction))
                            {
                                cmd.Parameters.Add("@MaDonThuoc", SqlDbType.Int).Value = prescriptionId;
                                cmd.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = item.MedicineId;
                                cmd.Parameters.Add("@SoLuong", SqlDbType.Int).Value = item.Quantity;
                                cmd.Parameters.Add("@DonGia", SqlDbType.Decimal).Value = price;
                                cmd.Parameters.Add("@CachDung", SqlDbType.NVarChar, 255).Value = (object)item.Instructions ?? DBNull.Value;
                                cmd.Parameters.Add("@ThanhTien", SqlDbType.Decimal).Value = item.Quantity * price;

                                cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                        TempData["Message"] = "Kê đơn thuốc thành công! Mã đơn thuốc: #" + prescriptionId;
                    }
                    catch (SqlException ex)
                    {
                        transaction.Rollback();
                        System.Diagnostics.Debug.WriteLine("KeDonThuoc Error: " + ex);
                        TempData["Message"] = "Lỗi kê đơn: " + ex.Message;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        System.Diagnostics.Debug.WriteLine("KeDonThuoc Error: " + ex);
                        TempData["Message"] = "Không thể lưu đơn thuốc. Vui lòng kiểm tra lại.";
                    }
                }
            }

            return RedirectToAction("KeDonThuoc", new { examinationId = input.ExaminationId });
        }

        private IList<DoctorExaminationSelectionViewModel> LoadExaminationChoices()
        {
            var choices = new List<DoctorExaminationSelectionViewModel>();
            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);
                using (var command = new SqlCommand(@"
                    SELECT pk.MaPhieuKham, pk.BenhNhanId, bn.FullName, pk.NgayKham, pk.ChanDoan, pk.TrangThai
                    FROM dbo.PhieuKhamBenh pk
                    INNER JOIN dbo.Users bn ON bn.UserId = pk.BenhNhanId
                    WHERE (pk.BacSiId = @DoctorId OR @DoctorId = 0)
                    ORDER BY pk.NgayKham DESC;", connection))
                {
                    command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            choices.Add(new DoctorExaminationSelectionViewModel
                            {
                                ExaminationId = Convert.ToInt32(reader[0]),
                                PatientId = Convert.ToInt32(reader[1]),
                                PatientName = reader[2].ToString(),
                                ExaminationDate = Convert.ToDateTime(reader[3]),
                                Diagnosis = reader[4] == DBNull.Value ? null : reader[4].ToString(),
                                Status = reader[5].ToString()
                            });
                        }
                    }
                }
            }
            return choices;
        }

        private int GetCurrentDoctorId(SqlConnection connection)
        {
            var identity = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(identity)) return 0;

            using (var command = new SqlCommand(@"
                SELECT TOP 1 UserId FROM dbo.Users
                WHERE IsActive = 1 AND (Username = @Identity OR Email = @Identity OR Phone = @Identity);", connection))
            {
                command.Parameters.Add("@Identity", SqlDbType.VarChar, 100).Value = identity;
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
            }
        }

        private static bool IsAppointmentOwnedByDoctor(SqlConnection connection, int appointmentId, int doctorId)
        {
            using (var command = new SqlCommand(@"
                SELECT COUNT(1) FROM dbo.LichHen
                WHERE MaLichHen = @AppointmentId AND (BacSiId = @DoctorId OR @DoctorId = 0);", connection))
            {
                command.Parameters.Add("@AppointmentId", SqlDbType.Int).Value = appointmentId;
                command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private static bool IsExaminationOwnedByDoctor(SqlConnection connection, int examinationId, int doctorId)
        {
            using (var command = new SqlCommand(@"
                SELECT COUNT(1) FROM dbo.PhieuKhamBenh
                WHERE MaPhieuKham = @Id AND (BacSiId = @DoctorId OR @DoctorId = 0);", connection))
            {
                command.Parameters.Add("@Id", SqlDbType.Int).Value = examinationId;
                command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private void LoadDoctorDashboard(DoctorDashboardViewModel model)
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);
                if (doctorId <= 0)
                {
                    model.DoctorName = User?.Identity?.Name ?? string.Empty;
                    return;
                }

                using (var command = new SqlCommand("SELECT TOP 1 FullName FROM dbo.Users WHERE UserId = @DoctorId AND IsActive = 1;", connection))
                {
                    command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                    model.DoctorName = Convert.ToString(command.ExecuteScalar());
                }

                using (var command = new SqlCommand(@"
                    SELECT COUNT(1),
                           SUM(CASE WHEN lh.TrangThai = N'Đã đặt' THEN 1 ELSE 0 END),
                           SUM(CASE WHEN lh.TrangThai = N'Đã khám' THEN 1 ELSE 0 END),
                           SUM(CASE WHEN lh.TrangThai = N'Hủy' THEN 1 ELSE 0 END)
                    FROM dbo.LichHen lh
                    WHERE CAST(lh.NgayKham AS date) = @Date AND lh.BacSiId = @DoctorId;", connection))
                {
                    command.Parameters.Add("@Date", SqlDbType.Date).Value = model.Today.Date;
                    command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            model.TodayAppointments = Convert.ToInt32(reader[0]);
                            model.WaitingAppointments = reader[1] == DBNull.Value ? 0 : Convert.ToInt32(reader[1]);
                            model.CompletedAppointments = reader[2] == DBNull.Value ? 0 : Convert.ToInt32(reader[2]);
                            model.CancelledAppointments = reader[3] == DBNull.Value ? 0 : Convert.ToInt32(reader[3]);
                        }
                    }
                }
                model.UpcomingAppointments = ReadAppointments(connection, model.Today.Date, null, 8, null, doctorId);
            }
        }

        private DoctorPrescriptionViewModel LoadPrescription(int? examinationId)
        {
            if (!examinationId.HasValue || examinationId.Value <= 0)
            {
                return new DoctorPrescriptionViewModel { ExaminationId = 0 };
            }

            var model = new DoctorPrescriptionViewModel { ExaminationId = examinationId.Value };
            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);

                using (var command = new SqlCommand(@"
                    SELECT TOP 1 bn.FullName, pk.BenhNhanId, pk.NgayKham, pk.TrangThai, pk.MaLichHen
                    FROM dbo.PhieuKhamBenh pk
                    INNER JOIN dbo.Users bn ON bn.UserId = pk.BenhNhanId
                    WHERE pk.MaPhieuKham = @Id AND (pk.BacSiId = @DoctorId OR @DoctorId = 0);", connection))
                {
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = examinationId.Value;
                    command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) return model;
                        model.PatientName = reader[0].ToString();
                        model.AppointmentId = reader[4] == DBNull.Value ? (int?)null : Convert.ToInt32(reader[4]);
                    }
                }

                using (var command = new SqlCommand(@"
                    SELECT TOP 1 MaDonThuoc FROM dbo.DonThuoc
                    WHERE MaPhieuKham = @Id ORDER BY NgayKeDon DESC;", connection))
                {
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = examinationId.Value;
                    var value = command.ExecuteScalar();
                    if (value != null) { model.HasPrescription = true; model.PrescriptionId = Convert.ToInt32(value); }
                }

                if (model.PrescriptionId.HasValue)
                {
                    using (var command = new SqlCommand(@"
                        SELECT t.TenThuoc, c.SoLuong, c.DonGia, c.CachDung, c.ThanhTien
                        FROM dbo.ChiTietDonThuoc c
                        INNER JOIN dbo.Thuoc t ON t.MaThuoc = c.MaThuoc
                        WHERE c.MaDonThuoc = @Id;", connection))
                    {
                        command.Parameters.Add("@Id", SqlDbType.Int).Value = model.PrescriptionId.Value;
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                model.Items.Add(new DoctorPrescriptionItemViewModel
                                {
                                    MedicineName = reader[0].ToString(),
                                    Quantity = Convert.ToInt32(reader[1]),
                                    Price = Convert.ToDecimal(reader[2]),
                                    Instructions = reader[3] == DBNull.Value ? null : reader[3].ToString(),
                                    Amount = Convert.ToDecimal(reader[4])
                                });
                            }
                        }
                    }
                }

                LoadMedicineCatalog(model, connection);
            }
            model.Total = model.Items.Sum(item => item.Amount);
            return model;
        }

        /// <summary>
        /// Nạp danh mục thuốc còn tồn kho kèm tìm kiếm (tên/h hoạt chất) và phân trang
        /// để bác sĩ tra cứu trước khi thêm vào đơn.
        /// </summary>
        private void LoadMedicineCatalog(DoctorPrescriptionViewModel model, SqlConnection connection)
        {
            var search = string.IsNullOrWhiteSpace(model.MedicineSearch) ? null : model.MedicineSearch.Trim();

            using (var countCmd = new SqlCommand(@"
                SELECT COUNT(1) FROM dbo.Thuoc
                WHERE SoLuongTon > 0
                  AND (@Search IS NULL OR TenThuoc LIKE @Like OR HoatChat LIKE @Like);", connection))
            {
                countCmd.Parameters.Add("@Search", SqlDbType.NVarChar, 150).Value = (object)search ?? DBNull.Value;
                countCmd.Parameters.Add("@Like", SqlDbType.NVarChar, 200).Value = "%" + (search ?? string.Empty) + "%";
                model.MedicineTotalItems = Convert.ToInt32(countCmd.ExecuteScalar());
            }

            int pageSize = model.MedicinePageSize > 0 ? model.MedicinePageSize : 6;
            int totalPages = (int)Math.Ceiling((double)model.MedicineTotalItems / pageSize);
            if (totalPages < 1) totalPages = 1;
            int page = model.MedicinePage < 1 ? 1 : model.MedicinePage;
            if (page > totalPages) page = totalPages;
            model.MedicinePage = page;
            model.MedicineTotalPages = totalPages;

            using (var cmd = new SqlCommand(@"
                SELECT MaThuoc, TenThuoc, HoatChat, DonViTinh, DonGia, SoLuongTon
                FROM dbo.Thuoc
                WHERE SoLuongTon > 0
                  AND (@Search IS NULL OR TenThuoc LIKE @Like OR HoatChat LIKE @Like)
                ORDER BY TenThuoc
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", connection))
            {
                cmd.Parameters.Add("@Search", SqlDbType.NVarChar, 150).Value = (object)search ?? DBNull.Value;
                cmd.Parameters.Add("@Like", SqlDbType.NVarChar, 200).Value = "%" + (search ?? string.Empty) + "%";
                cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (page - 1) * pageSize;
                cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        model.Medicines.Add(new DoctorMedicineViewModel
                        {
                            MedicineId = Convert.ToInt32(reader[0]),
                            Name = reader[1].ToString(),
                            ActiveIngredient = reader[2] == DBNull.Value ? null : reader[2].ToString(),
                            Unit = reader[3] == DBNull.Value ? null : reader[3].ToString(),
                            Price = Convert.ToDecimal(reader[4]),
                            Stock = Convert.ToInt32(reader[5])
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Partial view trả về danh mục thuốc (dùng cho AJAX tìm kiếm/phân trang
        /// ngay trong trang kê đơn mà không làm mất các thuốc đã chọn).
        /// </summary>
        [HttpGet]
        public ActionResult KeDonThuocThuocList(int examinationId, string search = null, int page = 1)
        {
            var model = new DoctorPrescriptionViewModel
            {
                ExaminationId = examinationId,
                MedicineSearch = search,
                MedicinePage = page
            };
            using (var connection = CreateConnection())
            {
                connection.Open();
                LoadMedicineCatalog(model, connection);
            }
            return PartialView("_KeDonThuocThuocList", model);
        }

        private DoctorClsViewModel LoadCls(int examinationId)
        {
            if (examinationId <= 0) return new DoctorClsViewModel { ExaminationId = 0 };

            var model = new DoctorClsViewModel { ExaminationId = examinationId };
            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);
                using (var command = new SqlCommand(@"
                    SELECT TOP 1 bn.FullName, pk.BenhNhanId, pk.NgayKham, pk.TrangThai, pk.MaLichHen
                    FROM dbo.PhieuKhamBenh pk
                    INNER JOIN dbo.Users bn ON bn.UserId = pk.BenhNhanId
                    WHERE pk.MaPhieuKham = @Id AND (pk.BacSiId = @DoctorId OR @DoctorId = 0);", connection))
                {
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = examinationId;
                    command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = doctorId;
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) return null;
                        model.PatientName = reader[0].ToString();
                        model.PatientId = Convert.ToInt32(reader[1]);
                        model.ExaminationDate = Convert.ToDateTime(reader[2]);
                        model.ExaminationStatus = reader[3].ToString();
                        model.AppointmentId = reader[4] == DBNull.Value ? (int?)null : Convert.ToInt32(reader[4]);
                        model.CanOrder = model.ExaminationStatus == "Đang khám";
                    }
                }
                using (var command = new SqlCommand("SELECT MaDichVu, TenDichVu, LoaiDichVu, DonGia FROM dbo.DichVuCLS ORDER BY TenDichVu;", connection))
                using (var reader = command.ExecuteReader()) while (reader.Read()) model.Services.Add(new DoctorClsServiceViewModel { ServiceId = Convert.ToInt32(reader[0]), Name = reader[1].ToString(), Type = reader[2] == DBNull.Value ? null : reader[2].ToString(), Price = Convert.ToDecimal(reader[3]) });
                using (var command = new SqlCommand(@"SELECT c.MaChiTietCLS, d.TenDichVu, d.LoaiDichVu, c.DonGia, c.KetQua, c.KetLuan, c.TrangThai FROM dbo.ChiTietCLS c INNER JOIN dbo.DichVuCLS d ON d.MaDichVu = c.MaDichVu WHERE c.MaPhieuKham = @Id ORDER BY c.MaChiTietCLS;", connection))
                {
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = examinationId;
                    using (var reader = command.ExecuteReader()) while (reader.Read()) model.Orders.Add(new DoctorClsOrderViewModel { Id = Convert.ToInt32(reader[0]), Name = reader[1].ToString(), Type = reader[2] == DBNull.Value ? null : reader[2].ToString(), Price = Convert.ToDecimal(reader[3]), Result = reader[4] == DBNull.Value ? null : reader[4].ToString(), Conclusion = reader[5] == DBNull.Value ? null : reader[5].ToString(), Status = reader[6].ToString() });
                }
            }
            return model;
        }

        private void LoadMedicalRecords(DoctorMedicalRecordViewModel model)
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(@"
                SELECT pk.BenhNhanId, bn.FullName, bn.Phone, MAX(pk.NgayKham),
                    (SELECT TOP 1 pk2.ChanDoan FROM dbo.PhieuKhamBenh pk2
                     WHERE pk2.BenhNhanId = pk.BenhNhanId ORDER BY pk2.NgayKham DESC),
                    (SELECT TOP 1 pk3.TrangThai FROM dbo.PhieuKhamBenh pk3
                     WHERE pk3.BenhNhanId = pk.BenhNhanId ORDER BY pk3.NgayKham DESC),
                    COUNT(1), MAX(pk.MaPhieuKham)
                FROM dbo.PhieuKhamBenh pk
                INNER JOIN dbo.Users bn ON bn.UserId = pk.BenhNhanId
                WHERE (@Search = '' OR bn.FullName LIKE @LikeSearch OR bn.Phone LIKE @LikeSearch OR CONVERT(varchar(20), bn.UserId) = @Search)
                GROUP BY pk.BenhNhanId, bn.FullName, bn.Phone
                ORDER BY MAX(pk.NgayKham) DESC;", connection))
            {
                command.Parameters.Add("@Search", SqlDbType.VarChar, 100).Value = model.Search ?? "";
                command.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 202).Value = "%" + (model.Search ?? "") + "%";
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        model.Items.Add(new DoctorMedicalRecordItemViewModel
                        {
                            PatientId = Convert.ToInt32(reader[0]),
                            PatientName = reader[1].ToString(),
                            Phone = reader[2] == DBNull.Value ? null : reader[2].ToString(),
                            LastVisit = Convert.ToDateTime(reader[3]),
                            Diagnosis = reader[4] == DBNull.Value ? null : reader[4].ToString(),
                            Status = reader[5] == DBNull.Value ? null : reader[5].ToString(),
                            VisitCount = Convert.ToInt32(reader[6]),
                            ExaminationId = Convert.ToInt32(reader[7])
                        });
                    }
                }
            }
            model.TotalPatients = model.Items.Count;
            model.TotalVisits = model.Items.Sum(item => item.VisitCount);
            model.CompletedVisits = model.Items.Count(item => item.Status == "Hoàn thành");
            model.ActiveVisits = model.Items.Count(item => item.Status == "Đang khám");
        }

        private DoctorMedicalRecordHistoryViewModel LoadMedicalHistory(int patientId)
        {
            var model = new DoctorMedicalRecordHistoryViewModel { PatientId = patientId };
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(@"
                SELECT bn.FullName, bn.Phone, pk.MaPhieuKham, pk.NgayKham, pk.HuyetAp, pk.Mach, pk.NhietDo,
                       pk.CanNang, pk.TrieuChung, pk.ChanDoan, pk.LoiDan, pk.TrangThai
                FROM dbo.PhieuKhamBenh pk
                INNER JOIN dbo.Users bn ON bn.UserId = pk.BenhNhanId
                WHERE pk.BenhNhanId = @PatientId
                ORDER BY pk.NgayKham DESC;", connection))
            {
                command.Parameters.Add("@PatientId", SqlDbType.Int).Value = patientId;
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        model.Visits.Add(new DoctorVisitViewModel
                        {
                            PatientName = reader[0].ToString(),
                            Phone = reader[1] == DBNull.Value ? null : reader[1].ToString(),
                            ExaminationId = Convert.ToInt32(reader[2]),
                            Date = Convert.ToDateTime(reader[3]),
                            BloodPressure = reader[4] == DBNull.Value ? null : reader[4].ToString(),
                            Pulse = reader[5] == DBNull.Value ? (int?)null : Convert.ToInt32(reader[5]),
                            Temperature = reader[6] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader[6]),
                            Weight = reader[7] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader[7]),
                            Symptoms = reader[8] == DBNull.Value ? null : reader[8].ToString(),
                            Diagnosis = reader[9] == DBNull.Value ? null : reader[9].ToString(),
                            Instructions = reader[10] == DBNull.Value ? null : reader[10].ToString(),
                            Status = reader[11].ToString()
                        });
                    }
                }
            }
            return model.Visits.Count == 0 ? null : model;
        }

        private void LoadDoctorAppointments(DoctorAppointmentListViewModel model)
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                var doctorId = GetCurrentDoctorId(connection);
                var items = ReadAppointments(connection, model.Date.Date, model.Status, 0, model.Search, doctorId);
                model.Items = items;
                model.TotalAppointments = items.Count;
                foreach (var item in items)
                {
                    if (string.Equals(item.TrangThai, "Đã đặt", StringComparison.OrdinalIgnoreCase)) model.WaitingAppointments++;
                    else if (string.Equals(item.TrangThai, "Đã khám", StringComparison.OrdinalIgnoreCase)) model.CompletedAppointments++;
                    else if (string.Equals(item.TrangThai, "Hủy", StringComparison.OrdinalIgnoreCase)) model.CancelledAppointments++;
                }
            }
        }

        private List<AppointmentItemViewModel> ReadAppointments(SqlConnection connection, DateTime date, string status, int limit, string search = null, int? doctorId = null)
        {
            var list = new List<AppointmentItemViewModel>();
            var top = limit > 0 ? "TOP " + limit + " " : "";
            var currentDoctorId = doctorId ?? GetCurrentDoctorId(connection);

            using (var command = new SqlCommand(@"SELECT " + top + @"
                    lh.MaLichHen, lh.BenhNhanId, bn.FullName, bn.Phone, lh.BacSiId, COALESCE(bs.FullName, N'Chưa phân công'),
                    lh.NgayKham, lh.GioKham, lh.SoThuTu, lh.LyDoKham, lh.TrangThai
                FROM dbo.LichHen lh
                INNER JOIN dbo.Users bn ON bn.UserId = lh.BenhNhanId
                LEFT JOIN dbo.Users bs ON bs.UserId = lh.BacSiId
                WHERE CAST(lh.NgayKham AS date) = CAST(@Date AS date)
                  AND (lh.BacSiId = @DoctorId OR @DoctorId = 0)
                  AND (@Status = '' OR lh.TrangThai = @Status)
                  AND (@Search = '' OR bn.FullName LIKE @LikeSearch OR bn.Phone LIKE @LikeSearch OR CONVERT(varchar(20), lh.BenhNhanId) = @Search)
                ORDER BY lh.GioKham, lh.MaLichHen;", connection))
            {
                command.Parameters.Add("@Date", SqlDbType.Date).Value = date.Date;
                command.Parameters.Add("@DoctorId", SqlDbType.Int).Value = currentDoctorId;
                command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = status ?? "";
                command.Parameters.Add("@Search", SqlDbType.VarChar, 100).Value = search ?? "";
                command.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 202).Value = "%" + (search ?? "") + "%";
                using (var reader = command.ExecuteReader()) while (reader.Read()) list.Add(ReadAppointment(reader));
            }
            return list;
        }

        private static AppointmentItemViewModel ReadAppointment(IDataRecord r)
        {
            return new AppointmentItemViewModel
            {
                MaLichHen = Convert.ToInt32(r[0]),
                BenhNhanId = Convert.ToInt32(r[1]),
                BenhNhanName = r[2].ToString(),
                BenhNhanPhone = r[3] == DBNull.Value ? null : r[3].ToString(),
                BacSiId = r[4] == DBNull.Value ? (int?)null : Convert.ToInt32(r[4]),
                BacSiName = r[5].ToString(),
                NgayKham = Convert.ToDateTime(r[6]),
                GioKham = (TimeSpan)r[7],
                SoThuTu = r[8] == DBNull.Value ? (int?)null : Convert.ToInt32(r[8]),
                LyDoKham = r[9] == DBNull.Value ? null : r[9].ToString(),
                TrangThai = r[10].ToString()
            };
        }

        private bool IsExaminationOwnedByCurrentDoctor(int examinationId)
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(@"
                SELECT COUNT(1)
                FROM dbo.PhieuKhamBenh pk
                INNER JOIN dbo.Users bs ON bs.UserId = pk.BacSiId
                WHERE pk.MaPhieuKham = @Id AND (bs.Username = @Identity OR bs.Email = @Identity);", connection))
            {
                command.Parameters.Add("@Id", SqlDbType.Int).Value = examinationId;
                command.Parameters.Add("@Identity", SqlDbType.VarChar, 100).Value = User.Identity.Name;
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar()) == 1;
            }
        }

        private bool IsInvoiceOwnedByCurrentDoctor(int invoiceId)
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(@"
                SELECT COUNT(1)
                FROM dbo.HoaDon hd
                INNER JOIN dbo.PhieuKhamBenh pk ON pk.MaPhieuKham = hd.MaPhieuKham
                INNER JOIN dbo.Users bs ON bs.UserId = pk.BacSiId
                WHERE hd.MaHoaDon = @InvoiceId AND (bs.Username = @Identity OR bs.Email = @Identity);", connection))
            {
                command.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = invoiceId;
                command.Parameters.Add("@Identity", SqlDbType.VarChar, 100).Value = User.Identity.Name;
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        [Authorize(Roles = "Bác sĩ,Quản lý phòng khám,Quản lý,Admin,Admin hệ thống")]
        [HttpGet]
        public ActionResult ThanhToan(int? examinationId, int? invoiceId)
        {
            SetDashboard("Thanh toán Hóa đơn & VietQR");
            ViewBag.ActiveDoctorMenu = "KeDonThuoc";

            if ((!examinationId.HasValue || examinationId.Value <= 0) && (!invoiceId.HasValue || invoiceId.Value <= 0))
            {
                return HttpNotFound("Mã phiếu khám hoặc mã hóa đơn không hợp lệ.");
            }

            if (examinationId.HasValue && examinationId.Value > 0 && User.IsInRole("Bác sĩ") && !User.IsInRole("Admin") && !User.IsInRole("Quản lý phòng khám"))
            {
                if (!IsExaminationOwnedByCurrentDoctor(examinationId.Value))
                {
                    return new HttpUnauthorizedResult("Phiếu khám không thuộc phạm vi phân công của bác sĩ.");
                }
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

        [Authorize(Roles = "Bác sĩ,Quản lý phòng khám,Quản lý,Admin,Admin hệ thống")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult XacNhanThanhToanTienMat(int invoiceId)
        {
            if (invoiceId <= 0) return new HttpStatusCodeResult(400);

            if (User.IsInRole("Bác sĩ") && !User.IsInRole("Admin") && !User.IsInRole("Quản lý phòng khám") && !IsInvoiceOwnedByCurrentDoctor(invoiceId))
            {
                TempData["Error"] = "Hóa đơn không thuộc phạm vi phân công của bác sĩ.";
                return RedirectToAction("ThanhToan", new { invoiceId });
            }

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

        [Authorize(Roles = "Bác sĩ,Quản lý phòng khám,Quản lý,Admin,Admin hệ thống")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult MoPhongThanhToanQR(int invoiceId)
        {
            if (invoiceId <= 0) return new HttpStatusCodeResult(400);

            if (User.IsInRole("Bác sĩ") && !User.IsInRole("Admin") && !User.IsInRole("Quản lý phòng khám") && !IsInvoiceOwnedByCurrentDoctor(invoiceId))
            {
                TempData["Error"] = "Hóa đơn không thuộc phạm vi phân công của bác sĩ.";
                return RedirectToAction("ThanhToan", new { invoiceId });
            }

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

        private SqlConnection CreateConnection() { if (string.IsNullOrWhiteSpace(connectionString)) throw new ConfigurationErrorsException("Thiếu connection string MediCareDb."); return new SqlConnection(connectionString); }
        private void SetDashboard(string title) { ViewBag.Title = title; ViewBag.RoleName = "Bác sĩ"; ViewBag.UserName = User.Identity.Name; ViewBag.PageTitle = title; }
    }
}
