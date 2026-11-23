using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Web;

namespace MediCare.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập email hoặc số điện thoại.")]
        public string UserName { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
        public bool RememberMe { get; set; }
    }

    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập email hoặc số điện thoại.")]
        public string Identity { get; set; }
    }

    public class ResetPasswordViewModel
    {
        public string Token { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Mật khẩu xác nhận không khớp.")]
        public string ConfirmPassword { get; set; }
    }

    public class DoctorPrescriptionViewModel
    {
        public int ExaminationId { get; set; }
        public int? AppointmentId { get; set; }
        public string PatientName { get; set; }
        public IList<DoctorMedicineViewModel> Medicines { get; set; } = new List<DoctorMedicineViewModel>();
        public IList<DoctorPrescriptionItemViewModel> Items { get; set; } = new List<DoctorPrescriptionItemViewModel>();
        public bool HasPrescription { get; set; }
        public int? PrescriptionId { get; set; }
        public decimal Total { get; set; }

        // Tìm kiếm + phân trang danh mục thuốc khả dụng trong kho
        public string MedicineSearch { get; set; }
        public int MedicinePage { get; set; } = 1;
        public int MedicinePageSize { get; set; } = 6;
        public int MedicineTotalItems { get; set; }
        public int MedicineTotalPages { get; set; }
    }

    public class DoctorMedicineViewModel
    {
        public int MedicineId { get; set; }
        public string Name { get; set; }
        public string ActiveIngredient { get; set; }
        public string Unit { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }

    public class DoctorPrescriptionItemViewModel
    {
        public string MedicineName { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string Instructions { get; set; }
        public decimal Amount { get; set; }
    }

    public class DoctorClsViewModel
    {
        public int ExaminationId { get; set; }
        public int? AppointmentId { get; set; }
        public string PatientName { get; set; }
        public int PatientId { get; set; }
        public DateTime ExaminationDate { get; set; }
        public string ExaminationStatus { get; set; }
        public string ErrorMessage { get; set; }
        public bool CanOrder { get; set; }
        public IList<DoctorClsServiceViewModel> Services { get; set; } = new List<DoctorClsServiceViewModel>();
        public IList<DoctorClsOrderViewModel> Orders { get; set; } = new List<DoctorClsOrderViewModel>();
    }

    public class DoctorClsServiceViewModel
    {
        public int ServiceId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public decimal Price { get; set; }
    }

    public class DoctorClsOrderViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public decimal Price { get; set; }
        public string Result { get; set; }
        public string Conclusion { get; set; }
        public string Status { get; set; }
    }

    public class DoctorMedicalRecordHistoryViewModel
    {
        public int PatientId { get; set; }
        public IList<DoctorVisitViewModel> Visits { get; set; } = new List<DoctorVisitViewModel>();
    }

    public class DoctorVisitViewModel
    {
        public int ExaminationId { get; set; }
        public string PatientName { get; set; }
        public string Phone { get; set; }
        public DateTime Date { get; set; }
        public string BloodPressure { get; set; }
        public int? Pulse { get; set; }
        public decimal? Temperature { get; set; }
        public decimal? Weight { get; set; }
        public string Symptoms { get; set; }
        public string Diagnosis { get; set; }
        public string Instructions { get; set; }
        public string Status { get; set; }
    }

    public class DoctorMedicalRecordViewModel
    {
        public string Search { get; set; }
        public IList<DoctorMedicalRecordItemViewModel> Items { get; set; } = new List<DoctorMedicalRecordItemViewModel>();
        public int TotalPatients { get; set; }
        public int TotalVisits { get; set; }
        public int CompletedVisits { get; set; }
        public int ActiveVisits { get; set; }
    }

    public class DoctorMedicalRecordItemViewModel
    {
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string Phone { get; set; }
        public DateTime LastVisit { get; set; }
        public string Diagnosis { get; set; }
        public string Status { get; set; }
        public int VisitCount { get; set; }
        public int ExaminationId { get; set; }
    }

    public class DoctorExaminationViewModel
    {
        public int AppointmentId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string Phone { get; set; }
        public DateTime ExaminationDate { get; set; }
        [StringLength(20)] public string BloodPressure { get; set; }
        [Range(1, 300)] public int? Pulse { get; set; }
        [Range(1.0, 50.0)] public decimal? Temperature { get; set; }
        [Range(0.1, 500.0)] public decimal? Weight { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập triệu chứng.")] public string Symptoms { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập chẩn đoán.")] public string Diagnosis { get; set; }
        public string Instructions { get; set; }
        public string Status { get; set; } = "Đang khám";
        public bool AlreadySaved { get; set; }
        public int? ExaminationId { get; set; }
    }

    public class MedicalRecordSearchViewModel
    {
        public IList<MedicalRecordItemViewModel> Items { get; set; } = new List<MedicalRecordItemViewModel>();
        public IList<DoctorOptionViewModel> Doctors { get; set; } = new List<DoctorOptionViewModel>();
        public IList<DepartmentOptionViewModel> Departments { get; set; } = new List<DepartmentOptionViewModel>();
        public string Search { get; set; }
        public int? DoctorId { get; set; }
        public int? DepartmentId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string Tab { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public int TotalRecords { get; set; }
        public int ResponsibleDoctorCount { get; set; }
        public int ActiveRecordCount { get; set; }
        public bool HasLookupLog { get; set; }
        public string LookupCountDisplay { get; set; }
        public string ValidationMessage { get; set; }
        public bool HasLookupLogTable { get; set; }
    }

    public class MedicalRecordItemViewModel
    {
        public int PatientId { get; set; }
        public string PatientCode { get; set; }
        public string PatientName { get; set; }
        public int? Age { get; set; }
        public string Gender { get; set; }
        public string DoctorName { get; set; }
        public int? DoctorId { get; set; }
        public string DepartmentName { get; set; }
        public int? DepartmentId { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastVisitDate { get; set; }
        public string Status { get; set; }
        public int VisitCount { get; set; }
    }

    public class MedicalRecordCreateViewModel
    {
        [Required(ErrorMessage = "Vui lòng chọn bệnh nhân.")]
        public int? PatientId { get; set; }
        [Required(ErrorMessage = "Vui lòng chọn bác sĩ.")]
        public int? DoctorId { get; set; }
        public int? AppointmentId { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập ngày khám.")]
        [DataType(DataType.DateTime)]
        public DateTime ExaminationDate { get; set; } = DateTime.Now;
        [StringLength(20)] public string BloodPressure { get; set; }
        [Range(0, 300)] public int? Pulse { get; set; }
        [Range(0, 50)] public decimal? Temperature { get; set; }
        [Range(0, 500)] public decimal? Weight { get; set; }
        public string Symptoms { get; set; }
        public string Diagnosis { get; set; }
        public string Instructions { get; set; }
        [Required(ErrorMessage = "Vui lòng chọn trạng thái.")]
        public string Status { get; set; } = "Đang khám";
        public IList<DoctorOptionViewModel> Doctors { get; set; } = new List<DoctorOptionViewModel>();
        public IList<PatientOptionViewModel> Patients { get; set; } = new List<PatientOptionViewModel>();
    }

    public class PatientOptionViewModel
    {
        public int UserId { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
        [StringLength(100)] public string FullName { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        [StringLength(20)] public string Phone { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập email.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [StringLength(100)] public string Email { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
        [DataType(DataType.Password)] public string Password { get; set; }
        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
        [Compare("Password", ErrorMessage = "Mật khẩu xác nhận không khớp.")]
        [DataType(DataType.Password)] public string ConfirmPassword { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Gender { get; set; }
        public string Address { get; set; }
        public bool Agree { get; set; }
    }

    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100000;
        public static string Hash(string password)
        {
            var salt = new byte[SaltSize];
            using (var random = new RNGCryptoServiceProvider()) random.GetBytes(salt);
            using (var deriveBytes = new Rfc2898DeriveBytes(password, salt, Iterations))
                return string.Format("PBKDF2|{0}|{1}|{2}", Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(deriveBytes.GetBytes(HashSize)));
        }
        public static bool Verify(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash)) return false;

            // Hỗ trợ mật khẩu lưu dạng plaintext trong dữ liệu seed (ví dụ: '123456').
            // Sau khi đăng nhập thành công, hệ thống sẽ tự nâng cấp sang PBKDF2.
            if (!IsHashed(storedHash))
            {
                return string.Equals(password ?? string.Empty, storedHash, StringComparison.Ordinal);
            }

            try
            {
                var parts = storedHash.Split('|');
                if (parts.Length != 4 || parts[0] != "PBKDF2") return false;
                var expected = Convert.FromBase64String(parts[3]);
                byte[] actual;
                using (var deriveBytes = new Rfc2898DeriveBytes(password, Convert.FromBase64String(parts[2]), int.Parse(parts[1]))) actual = deriveBytes.GetBytes(expected.Length);
                if (actual.Length != expected.Length) return false;
                var result = 0;
                for (var i = 0; i < actual.Length; i++) result |= actual[i] ^ expected[i];
                return result == 0;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException) { return false; }
        }

        // Kiểm tra chuỗi lưu trong CSDL đã ở dạng băm PBKDF2 hay chưa (nếu chưa thì là plaintext cần nâng cấp).
        public static bool IsHashed(string storedHash)
        {
            return !string.IsNullOrEmpty(storedHash) && storedHash.StartsWith("PBKDF2|", StringComparison.Ordinal);
        }
    }

    public class MedicineListViewModel
    {
        public IList<MedicineItemViewModel> Items { get; set; } = new List<MedicineItemViewModel>();
        public string Search { get; set; }
        public string Group { get; set; }
        public string Status { get; set; }
        public string ExpiryStatus { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public int TotalStock { get; set; }
        public int OutOfStock { get; set; }
        public int LowStock { get; set; }
        public int ActiveItems { get; set; }
        public IList<string> Groups { get; set; } = new List<string>();
    }

    public class MedicineItemViewModel
    {
        public int MaThuoc { get; set; }
        public string TenThuoc { get; set; }
        public string HoatChat { get; set; }
        public string NhomThuoc { get; set; }
        public string DonViTinh { get; set; }
        public int SoLuongTon { get; set; }
        public decimal? GiaNhap { get; set; }
        public decimal GiaBan { get; set; }
        public DateTime? HanSuDung { get; set; }
        public int MucTonToiThieu { get; set; }
        public bool IsActive { get; set; }
        public string TrangThai { get; set; }
    }

    public class MedicineEditViewModel
    {
        public IList<string> Groups { get; set; } = new List<string>();
        public int MaThuoc { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên thuốc.")]
        [StringLength(150)]
        public string TenThuoc { get; set; }

        [StringLength(150)]
        public string HoatChat { get; set; }

        [StringLength(100)]
        public string NhomThuoc { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập đơn vị tính.")]
        [StringLength(50)]
        public string DonViTinh { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn không được âm.")]
        public int SoLuongTon { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Giá nhập không được âm.")]
        public decimal? GiaNhap { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Giá bán không được âm.")]
        public decimal GiaBan { get; set; }

        [DataType(DataType.Date)]
        public DateTime? HanSuDung { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Ngưỡng tồn không được âm.")]
        public int MucTonToiThieu { get; set; } = 10;

        public bool IsActive { get; set; } = true;
    }

    public class MedicineGroupListViewModel
    {
        public IList<MedicineGroupViewModel> Items { get; set; } = new List<MedicineGroupViewModel>();

        [Required(ErrorMessage = "Vui lòng nhập tên nhóm thuốc.")]
        [StringLength(100, ErrorMessage = "Tên nhóm thuốc không được vượt quá 100 ký tự.")]
        public string NewGroupName { get; set; }
    }

    public class MedicineGroupViewModel
    {
        public int MaNhomThuoc { get; set; }
        public string TenNhomThuoc { get; set; }
        public int SoLuongThuoc { get; set; }
    }

    public class UserManagementListViewModel
    {
        public IList<UserItemViewModel> Items { get; set; } = new List<UserItemViewModel>();
        public IList<RoleOptionViewModel> Roles { get; set; } = new List<RoleOptionViewModel>();
        public IList<DepartmentOptionViewModel> Departments { get; set; } = new List<DepartmentOptionViewModel>();
        public string Tab { get; set; }
        public string Search { get; set; }
        public string Role { get; set; }
        public string Khoa { get; set; }
        public string Status { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public int TotalUsers { get; set; }
        public int TotalDoctors { get; set; }
        public int TotalPatients { get; set; }
        public int ActiveUsers { get; set; }
        public int LockedUsers { get; set; }
    }

    public class UserItemViewModel
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Gender { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Address { get; set; }
        public int RoleId { get; set; }
        public string RoleName { get; set; }
        public int? MaKhoa { get; set; }
        public string KhoaName { get; set; }
        public bool IsActive { get; set; }
    }

    public class RoleOptionViewModel
    {
        public int RoleId { get; set; }
        public string RoleName { get; set; }
    }

    public class DepartmentOptionViewModel
    {
        public int MaKhoa { get; set; }
        public string TenKhoa { get; set; }
    }

    public class UserEditViewModel
    {
        public int UserId { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
        [StringLength(50)]
        public string Username { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
        [StringLength(100)]
        public string FullName { get; set; }
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [StringLength(100)]
        public string Email { get; set; }
        [StringLength(20)]
        public string Phone { get; set; }
        [StringLength(10)]
        public string Gender { get; set; }
        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }
        [StringLength(255)]
        public string Address { get; set; }
        [Required(ErrorMessage = "Vui lòng chọn vai trò.")]
        public int RoleId { get; set; }
        public int? MaKhoa { get; set; }
        [StringLength(100)]
        public string ExistingRoleName { get; set; }
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
        public bool IsActive { get; set; } = true;
        public IList<RoleOptionViewModel> Roles { get; set; } = new List<RoleOptionViewModel>();
        public IList<DepartmentOptionViewModel> Departments { get; set; } = new List<DepartmentOptionViewModel>();
    }

    /// <summary>
    /// Dữ liệu trang "Thông tin cá nhân" cho Bác sĩ / Quản lý tự cập nhật hồ sơ
    /// và tải ảnh đại diện hiển thị ở trang chủ.
    /// </summary>
    public class ProfileViewModel
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string RoleName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
        [StringLength(100)]
        public string FullName { get; set; }

        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [StringLength(100)]
        public string Email { get; set; }

        [StringLength(20)]
        public string Phone { get; set; }

        [StringLength(10)]
        public string Gender { get; set; }

        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [StringLength(255)]
        public string Address { get; set; }

        [StringLength(500)]
        public string AvatarUrl { get; set; }

        public HttpPostedFileBase AvatarFile { get; set; }

        // Thông tin riêng của Bác sĩ (chỉ hiển thị khi IsDoctor = true)
        public bool IsDoctor { get; set; }
        public int? MaKhoa { get; set; }
        public string KhoaName { get; set; }

        [Range(0, 80, ErrorMessage = "Số năm kinh nghiệm không hợp lệ (0-80).")]
        public int? SoNamKinhNghiem { get; set; }

        [StringLength(500)]
        public string GioiThieu { get; set; }
    }

    public class AppointmentListViewModel
    {
        public IList<AppointmentItemViewModel> Items { get; set; } = new List<AppointmentItemViewModel>();
        public IList<DoctorOptionViewModel> Doctors { get; set; } = new List<DoctorOptionViewModel>();
        public string Search { get; set; }
        public int? DoctorId { get; set; }
        public string Status { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string Preset { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public int TotalAppointments { get; set; }
        public int BookedAppointments { get; set; }
        public int CompletedAppointments { get; set; }
        public int CancelledAppointments { get; set; }
    }

    public class DoctorExaminationSelectionViewModel
    {
        public int ExaminationId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public DateTime ExaminationDate { get; set; }
        public string Diagnosis { get; set; }
        public string Status { get; set; }
    }

    public class AppointmentItemViewModel
    {
        public int MaLichHen { get; set; }
        public int BenhNhanId { get; set; }
        public string BenhNhanName { get; set; }
        public string BenhNhanPhone { get; set; }
        public int? BacSiId { get; set; }
        public string BacSiName { get; set; }
        public DateTime NgayKham { get; set; }
        public TimeSpan GioKham { get; set; }
        public int? SoThuTu { get; set; }
        public string LyDoKham { get; set; }
        public string TrangThai { get; set; }
    }

    public class DoctorOptionViewModel
    {
        public int UserId { get; set; }
        public string FullName { get; set; }
    }

    public class DoctorDashboardViewModel
    {
        public DateTime Today { get; set; }
        public int TodayAppointments { get; set; }
        public int WaitingAppointments { get; set; }
        public int CompletedAppointments { get; set; }
        public int CancelledAppointments { get; set; }
        public string DoctorName { get; set; }
        public IList<AppointmentItemViewModel> UpcomingAppointments { get; set; } = new List<AppointmentItemViewModel>();
    }

    public class DoctorAppointmentListViewModel
    {
        public DateTime Date { get; set; }
        public string Search { get; set; }
        public string Status { get; set; }
        public IList<AppointmentItemViewModel> Items { get; set; } = new List<AppointmentItemViewModel>();
        public int TotalAppointments { get; set; }
        public int WaitingAppointments { get; set; }
        public int CompletedAppointments { get; set; }
        public int CancelledAppointments { get; set; }
        public IList<DoctorExaminationSelectionViewModel> ExaminationChoices { get; set; } = new List<DoctorExaminationSelectionViewModel>();
    }

    public class ClinicManagerDashboardViewModel
    {
        public int ActiveDoctorCount { get; set; }
        public int PatientCount { get; set; }
        public int MedicineCount { get; set; }
        public decimal MonthlyRevenue { get; set; }
        public int LowStockMedicineCount { get; set; }
        public DateTime Today { get; set; }
        public bool HasMonthlyRevenue { get; set; }
        public bool HasWeeklyRevenue { get; set; }
        public IList<DashboardAppointmentViewModel> TodayAppointments { get; set; } = new List<DashboardAppointmentViewModel>();
        public IList<DashboardMedicineViewModel> ExpiringMedicines { get; set; } = new List<DashboardMedicineViewModel>();
        public IList<DashboardRevenueViewModel> WeeklyRevenue { get; set; } = new List<DashboardRevenueViewModel>();
        public string DatabaseWarning { get; set; }
    }

    public class DashboardAppointmentViewModel
    {
        public TimeSpan Time { get; set; }
        public string PatientName { get; set; }
        public string ServiceName { get; set; }
        public string DoctorName { get; set; }
        public string Status { get; set; }
    }

    public class DashboardMedicineViewModel
    {
        public string Name { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
        public string Unit { get; set; }
        public string LotNumber { get; set; }
    }

    public class DashboardRevenueViewModel
    {
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string DayLabel { get; set; }
    }

    public class BookAppointmentViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
        [StringLength(100)]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        [StringLength(20)]
        public string Phone { get; set; }

        public int? DepartmentId { get; set; }
        public int? DoctorId { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ngày khám.")]
        [DataType(DataType.Date)]
        public DateTime ExaminationDate { get; set; } = DateTime.Today.AddDays(1);

        [Required(ErrorMessage = "Vui lòng chọn giờ khám.")]
        public string ExaminationTime { get; set; } = "08:30";

        [StringLength(500)]
        public string Reason { get; set; }

        public IList<DepartmentOptionViewModel> Departments { get; set; } = new List<DepartmentOptionViewModel>();
        public IList<DoctorOptionViewModel> Doctors { get; set; } = new List<DoctorOptionViewModel>();
    }

    public class DoctorPrescriptionInputViewModel
    {
        [Required]
        public int ExaminationId { get; set; }

        public IList<DoctorPrescriptionItemInputViewModel> Items { get; set; } = new List<DoctorPrescriptionItemInputViewModel>();
    }

    public class DoctorPrescriptionItemInputViewModel
    {
        [Required]
        public int MedicineId { get; set; }

        [Range(1, 1000, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        public int Quantity { get; set; }

        [StringLength(255)]
        public string Instructions { get; set; }
    }

    public class RevenueStatisticsViewModel
    {
        public DateTime? TuNgay { get; set; }
        public DateTime? DenNgay { get; set; }
        public string Khoa { get; set; }
        public string BacSi { get; set; }

        public decimal TongDoanhThu { get; set; }
        public decimal DoanhThuKham { get; set; }
        public decimal DoanhThuThuoc { get; set; }
        public decimal DoanhThuCLS { get; set; }
        public int TongLuotKham { get; set; }
        public decimal TrungBinhLuotKham { get; set; }

        public IList<RevenueByDoctorViewModel> DoctorStatistics { get; set; } = new List<RevenueByDoctorViewModel>();
        public IList<RevenueByDepartmentViewModel> DepartmentStatistics { get; set; } = new List<RevenueByDepartmentViewModel>();
        public IList<InvoiceItemViewModel> Invoices { get; set; } = new List<InvoiceItemViewModel>();
        public IList<TrendChartPointViewModel> TrendData { get; set; } = new List<TrendChartPointViewModel>();

        public IList<DepartmentOptionViewModel> Departments { get; set; } = new List<DepartmentOptionViewModel>();
        public IList<DoctorOptionViewModel> Doctors { get; set; } = new List<DoctorOptionViewModel>();
    }

    public class RevenueByDoctorViewModel
    {
        public int DoctorId { get; set; }
        public string DoctorName { get; set; }
        public string DepartmentName { get; set; }
        public int VisitCount { get; set; }
        public decimal ExamRevenue { get; set; }
        public decimal MedicineRevenue { get; set; }
        public decimal ClsRevenue { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal Percentage { get; set; }
    }

    public class RevenueByDepartmentViewModel
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; }
        public int DoctorCount { get; set; }
        public int VisitCount { get; set; }
        public decimal ExamRevenue { get; set; }
        public decimal MedicineRevenue { get; set; }
        public decimal ClsRevenue { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal Percentage { get; set; }
    }

    public class InvoiceItemViewModel
    {
        public int InvoiceId { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PatientName { get; set; }
        public string PatientCode { get; set; }
        public string DoctorName { get; set; }
        public string DepartmentName { get; set; }
        public string PaymentMethod { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; }
    }

    public class TrendChartPointViewModel
    {
        public string DateLabel { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal MedicineRevenue { get; set; }
    }

    public class InvoiceDetailViewModel
    {
        public int InvoiceId { get; set; }
        public int ExaminationId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string PatientPhone { get; set; }
        public string DoctorName { get; set; }
        public string DepartmentName { get; set; }
        public DateTime ExaminationDate { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string Status { get; set; }
        public string PaymentMethod { get; set; }
        public string ReferenceCode { get; set; }

        public decimal ExamFee { get; set; }
        public decimal ClsFee { get; set; }
        public decimal MedicineFee { get; set; }
        public decimal TotalAmount { get; set; }

        public IList<InvoiceLineItemViewModel> ClsItems { get; set; } = new List<InvoiceLineItemViewModel>();
        public IList<InvoiceLineItemViewModel> MedicineItems { get; set; } = new List<InvoiceLineItemViewModel>();

        public string BankCode { get; set; } = "MB";
        public string BankAccountNo { get; set; } = "0901234567";
        public string BankAccountName { get; set; } = "PHONG KHAM MEDICARE";
        public string QrCodeUrl { get; set; }
    }

    public class InvoiceLineItemViewModel
    {
        public string Name { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
    }

    public class ClinicSettingsViewModel
    {
        public string ClinicName { get; set; } = "Phòng khám Đa khoa Quốc tế MediCare";
        public string InsuranceCode { get; set; } = "79-021";
        public string Hotline { get; set; } = "1900 6868 - 028 3930 8888";
        public string Email { get; set; } = "contact@medicare.vn";
        public string Address { get; set; } = "Số 215 Hồng Bàng, Phường 11, Quận 5, TP. Hồ Chí Minh";

        public string BankCode { get; set; } = "MB";
        public string BankAccountNo { get; set; } = "0901234567";
        public string BankAccountName { get; set; } = "PHONG KHAM MEDICARE";

        public IList<ServicePriceItemViewModel> ServicePrices { get; set; } = new List<ServicePriceItemViewModel>();
    }

    public class ServicePriceItemViewModel
    {
        public int ServiceId { get; set; }
        public string ServiceName { get; set; }
        public string ServiceType { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class MedicalReportViewModel
    {
        public int TotalVisitsMonth { get; set; }
        public decimal InsuranceRate { get; set; } = 68.4m;
        public int TotalPrescriptions { get; set; }
        public decimal PatientSatisfaction { get; set; } = 98.2m;

        public IList<ReportItemViewModel> Reports { get; set; } = new List<ReportItemViewModel>();
    }

    public class ReportItemViewModel
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Period { get; set; }
        public string UpdatedDate { get; set; }
        public string Format { get; set; }
        public string DownloadAction { get; set; }
    }

    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int PatientCount { get; set; }
        public int DoctorCount { get; set; }
        public int ManagerCount { get; set; }
        public int StaffCount { get; set; }
        public int AdminCount { get; set; }
        public bool ServerOnline { get; set; }
        public int MonthlyVisits { get; set; }
        public int SystemWarnings { get; set; } = 0;
    }

    public class RoleManagementViewModel
    {
        public IList<RoleItemViewModel> Roles { get; set; } = new List<RoleItemViewModel>();
        public RoleEditViewModel NewRole { get; set; } = new RoleEditViewModel();
    }

    public class RoleItemViewModel
    {
        public int RoleId { get; set; }
        public string RoleName { get; set; }
        public string Description { get; set; }
        public int UserCount { get; set; }
        public bool IsSystemRole { get; set; }
    }

    public class RoleEditViewModel
    {
        public int RoleId { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập tên vai trò.")]
        public string RoleName { get; set; }
        public string Description { get; set; }
        public bool IsSystemRole { get; set; }
    }

    public class AuditLogSearchViewModel
    {
        public string Search { get; set; }
        public string ActionType { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        public IList<AuditLogItemViewModel> Items { get; set; } = new List<AuditLogItemViewModel>();
        public int TotalItems { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalPages { get; set; }
    }

    public class AuditLogItemViewModel
    {
        public int Id { get; set; }
        public DateTime ThoiGian { get; set; }
        public string NguoiThucHien { get; set; }
        public string VaiTro { get; set; }
        public string HanhDong { get; set; }
        public string DoiTuong { get; set; }
        public int? IdThamChieu { get; set; }
        public string KetQua { get; set; }
        public string IpAddress { get; set; }
        public string ChiTiet { get; set; }
    }

    public class SystemSettingsViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên hệ thống.")]
        public string SystemName { get; set; } = "MediCare - Hệ thống Quản lý Phòng khám & Bệnh viện";
        public string SystemVersion { get; set; } = "v2.5.0-Release";
        public string SmtpHost { get; set; } = "smtp.medicare.vn (Port 587)";
        public int SessionTimeout { get; set; } = 30;
        public string OpenAiApiKey { get; set; }
        public bool IsOpenAiApiKeyConfigured { get; set; }
    }

    public class SecurityManagementViewModel
    {
        public bool IsHttps { get; set; }
        public string TwoFactorStatus { get; set; } = "Chưa cấu hình / Chưa triển khai";
        public int TwoFactorEnabledCount { get; set; }
        public int TotalActiveUsers { get; set; }
        public int BlockedIpCount { get; set; }
        public IList<IpBlacklistItemViewModel> BlockedIps { get; set; } = new List<IpBlacklistItemViewModel>();
    }

    public class IpBlacklistItemViewModel
    {
        public int Id { get; set; }
        public string IpAddress { get; set; }
        public string Reason { get; set; }
        public DateTime LockedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class BranchManagementViewModel
    {
        public IList<BranchItemViewModel> Branches { get; set; } = new List<BranchItemViewModel>();
        public BranchEditViewModel NewBranch { get; set; } = new BranchEditViewModel();
    }

    public class BranchItemViewModel
    {
        public int Id { get; set; }
        public string BranchCode { get; set; }
        public string BranchName { get; set; }
        public string Address { get; set; }
        public string Director { get; set; }
        public int DoctorCount { get; set; }
        public int MonthlyVisits { get; set; }
        public bool IsActive { get; set; }
    }

    public class BranchEditViewModel
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập mã cơ sở.")]
        public string BranchCode { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập tên chi nhánh.")]
        public string BranchName { get; set; }
        public string Address { get; set; }
        public string Director { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class BackupManagementViewModel
    {
        public IList<BackupItemViewModel> Backups { get; set; } = new List<BackupItemViewModel>();
        public string BackupDirectory { get; set; }
    }

    public class BackupItemViewModel
    {
        public string FileName { get; set; }
        public DateTime CreatedAt { get; set; }
        public long FileSize { get; set; }
        public string BackupType { get; set; }
        public bool IsVerified { get; set; }
    }

    public class NotificationItemViewModel
    {
        public int MaThongBao { get; set; }
        public int NguoiNhanId { get; set; }
        public string LoaiThongBao { get; set; }
        public string TieuDe { get; set; }
        public string NoiDung { get; set; }
        public int? MaLichHen { get; set; }
        public bool DaDoc { get; set; }
        public DateTime NgayTao { get; set; }
    }

    public class ContactMessageViewModel
    {
        public int MaTinNhan { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
        public string HoTen { get; set; }
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        public string Email { get; set; }
        public string SoDienThoai { get; set; }
        public string TieuDe { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập nội dung tin nhắn.")]
        public string NoiDung { get; set; }
        public string TrangThai { get; set; }
        public DateTime NgayGui { get; set; }
        public string GhiChuXuLy { get; set; }
    }

    public static class NotificationService
    {
        private static readonly string connectionString = ConfigurationManager.ConnectionStrings["MediCareDb"]?.ConnectionString;

        public static void CreateNotification(int recipientUserId, string type, string title, string content, int? appointmentId = null)
        {
            if (recipientUserId <= 0) return;

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
                        INSERT INTO dbo.ThongBao (NguoiNhanId, LoaiThongBao, TieuDe, NoiDung, MaLichHen, DaDoc, NgayTao)
                        VALUES (@NguoiNhanId, @LoaiThongBao, @TieuDe, @NoiDung, @MaLichHen, 0, GETDATE());", connection))
                    {
                        cmd.Parameters.Add("@NguoiNhanId", SqlDbType.Int).Value = recipientUserId;
                        cmd.Parameters.Add("@LoaiThongBao", SqlDbType.NVarChar, 50).Value = type ?? "GENERAL";
                        cmd.Parameters.Add("@TieuDe", SqlDbType.NVarChar, 150).Value = title;
                        cmd.Parameters.Add("@NoiDung", SqlDbType.NVarChar, 500).Value = content;
                        cmd.Parameters.Add("@MaLichHen", SqlDbType.Int).Value = (object)appointmentId ?? DBNull.Value;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("NotificationService error: " + ex);
            }
        }
    }

    public static class EmailService
    {
        private static readonly string connectionString = ConfigurationManager.ConnectionStrings["MediCareDb"]?.ConnectionString;

        public static bool SendEmail(string toEmail, string subject, string bodyHtml)
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return false;

            string smtpHost = "smtp.gmail.com";
            int smtpPort = 587;
            string smtpUser = "";
            string smtpPass = "";

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var createCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CauHinhHeThong')
                        BEGIN
                            CREATE TABLE CauHinhHeThong (Khoa NVARCHAR(100) PRIMARY KEY, GiaTri NVARCHAR(MAX) NULL);
                        END", connection))
                    {
                        createCmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SqlCommand("SELECT Khoa, GiaTri FROM dbo.CauHinhHeThong WHERE Khoa LIKE 'Smtp%';", connection))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var k = r.GetString(0);
                                var v = r.IsDBNull(1) ? "" : r.GetString(1);
                                if (k.Equals("SmtpHost", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v)) smtpHost = v;
                                else if (k.Equals("SmtpPort", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out int p)) smtpPort = p;
                                else if (k.Equals("SmtpUser", StringComparison.OrdinalIgnoreCase)) smtpUser = v;
                                else if (k.Equals("SmtpPass", StringComparison.OrdinalIgnoreCase)) smtpPass = v;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
                {
                    System.Diagnostics.Debug.WriteLine($"[EmailService Sandbox] Send email to {toEmail} - Subject: {subject}");
                    return true;
                }

                using (var client = new SmtpClient(smtpHost, smtpPort))
                {
                    client.EnableSsl = true;
                    client.Credentials = new NetworkCredential(smtpUser, smtpPass);
                    using (var msg = new MailMessage())
                    {
                        msg.From = new MailAddress(smtpUser, "Phòng khám MediCare");
                        msg.To.Add(toEmail);
                        msg.Subject = subject;
                        msg.Body = bodyHtml;
                        msg.IsBodyHtml = true;
                        client.Send(msg);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EmailService error: " + ex);
                return false;
            }
        }
    }

    public static class AuditLogger
    {
        private static readonly string connectionString = ConfigurationManager.ConnectionStrings["MediCareDb"]?.ConnectionString;

        public static void Log(string username, string role, string action, string target, int? referenceId, string result, string ip, string details)
        {
            if (string.IsNullOrWhiteSpace(username)) username = "Hệ thống / Khách";
            if (string.IsNullOrWhiteSpace(role)) role = "Chưa xác định";
            if (string.IsNullOrWhiteSpace(action)) action = "UNKNOWN";
            if (string.IsNullOrWhiteSpace(target)) target = "Hệ thống";
            if (string.IsNullOrWhiteSpace(result)) result = "Thành công";

            try
            {
                using (var connection = new SqlConnection(connectionString))
                using (var command = new SqlCommand(@"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'NhatKyHoatDong')
                    BEGIN
                        CREATE TABLE NhatKyHoatDong (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            ThoiGian DATETIME NOT NULL DEFAULT GETDATE(),
                            NguoiThucHien NVARCHAR(100) NOT NULL,
                            VaiTro NVARCHAR(50) NULL,
                            HanhDong NVARCHAR(50) NOT NULL,
                            DoiTuong NVARCHAR(150) NOT NULL,
                            IdThamChieu INT NULL,
                            KetQua NVARCHAR(100) NOT NULL,
                            IpAddress VARCHAR(50) NULL,
                            ChiTiet NVARCHAR(500) NULL
                        );
                    END

                    INSERT INTO dbo.NhatKyHoatDong (ThoiGian, NguoiThucHien, VaiTro, HanhDong, DoiTuong, IdThamChieu, KetQua, IpAddress, ChiTiet)
                    VALUES (GETDATE(), @NguoiThucHien, @VaiTro, @HanhDong, @DoiTuong, @IdThamChieu, @KetQua, @IpAddress, @ChiTiet);", connection))
                {
                    command.Parameters.Add("@NguoiThucHien", SqlDbType.NVarChar, 100).Value = username;
                    command.Parameters.Add("@VaiTro", SqlDbType.NVarChar, 50).Value = role;
                    command.Parameters.Add("@HanhDong", SqlDbType.NVarChar, 50).Value = action;
                    command.Parameters.Add("@DoiTuong", SqlDbType.NVarChar, 150).Value = target;
                    command.Parameters.Add("@IdThamChieu", SqlDbType.Int).Value = (object)referenceId ?? DBNull.Value;
                    command.Parameters.Add("@KetQua", SqlDbType.NVarChar, 100).Value = result;
                    command.Parameters.Add("@IpAddress", SqlDbType.VarChar, 50).Value = (object)ip ?? DBNull.Value;
                    command.Parameters.Add("@ChiTiet", SqlDbType.NVarChar, 500).Value = (object)details ?? DBNull.Value;

                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AuditLogger error: " + ex);
            }
        }
    }

    public class AiChatRequestViewModel
    {
        public string SymptomText { get; set; }
        public IList<AiMessageContextViewModel> History { get; set; } = new List<AiMessageContextViewModel>();
    }

    public class AiMessageContextViewModel
    {
        public string Sender { get; set; }
        public string Message { get; set; }
    }

    public class AiChatResponseViewModel
    {
        public bool IsEmergency { get; set; }
        public string Disclaimer { get; set; } = "Thông tin tư vấn AI mang tính tham khảo y khoa. Vui lòng khám bác sĩ trực tiếp để được chẩn đoán chính xác.";
        public string DiagnosisSummary { get; set; }
        public string DepartmentName { get; set; }
        public int DepartmentId { get; set; }
        public string HealthAdvice { get; set; }
        /// <summary>Lý do khám được AI soạn sẵn để tự động điền vào form đặt lịch.</summary>
        public string SuggestedReason { get; set; }
        /// <summary>True khi không gọi được API AI và phải dùng bộ luật chẩn đoán cục bộ.</summary>
        public bool IsOfflineFallback { get; set; }
        public IList<AiDoctorRecommendationViewModel> RecommendedDoctors { get; set; } = new List<AiDoctorRecommendationViewModel>();
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
    }

    public class AiDoctorRecommendationViewModel
    {
        public int DoctorId { get; set; }
        public string DoctorName { get; set; }
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; }
    }

    public class VerifyTwoFactorViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập mã OTP 6 chữ số.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải bao gồm 6 chữ số.")]
        public string Code { get; set; }
        public bool RememberMe { get; set; }
        public string ReturnUrl { get; set; }
    }

    public class TwoFactorSetupViewModel
    {
        public bool IsEnabled { get; set; }
        public string SecretKey { get; set; }
        public string QrCodeUrl { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập mã xác nhận 6 chữ số.")]
        public string VerifyCode { get; set; }
    }

    public static class TotpHelper
    {
        private static readonly char[] Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

        public static string GenerateSecretKey()
        {
            byte[] bytes = new byte[10];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Base32Encode(bytes);
        }

        public static string GenerateQrCodeUrl(string username, string secret, string issuer = "MediCare")
        {
            string otpauth = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(username)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}";
            return $"https://chart.googleapis.com/chart?chs=200x200&cht=qr&chl={Uri.EscapeDataString(otpauth)}";
        }

        public static bool ValidateCode(string secret, string inputCode, int timeWindowStep = 1)
        {
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(inputCode)) return false;
            inputCode = inputCode.Trim().Replace(" ", "").Replace("-", "");
            if (inputCode.Length != 6 || !long.TryParse(inputCode, out _)) return false;

            byte[] secretBytes = Base32Decode(secret);
            if (secretBytes == null || secretBytes.Length == 0) return false;

            long currentStep = DateTime.UtcNow.Ticks / (30L * 10000000L);

            for (int i = -timeWindowStep; i <= timeWindowStep; i++)
            {
                long step = currentStep + i;
                string generatedCode = ComputeTotp(secretBytes, step);
                if (string.Equals(generatedCode, inputCode, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string ComputeTotp(byte[] key, long step)
        {
            byte[] stepBytes = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian) Array.Reverse(stepBytes);

            using (var hmac = new HMACSHA1(key))
            {
                byte[] hash = hmac.ComputeHash(stepBytes);
                int offset = hash[hash.Length - 1] & 0x0F;
                int binary = ((hash[offset] & 0x7F) << 24)
                           | ((hash[offset + 1] & 0xFF) << 16)
                           | ((hash[offset + 2] & 0xFF) << 8)
                           | (hash[offset + 3] & 0xFF);

                int otp = binary % 1000000;
                return otp.ToString("D6");
            }
        }

        private static string Base32Encode(byte[] data)
        {
            int outputLength = (int)Math.Ceiling(data.Length * 8 / 5.0);
            char[] result = new char[outputLength];
            int buffer = data[0];
            int next = 1;
            int bitsLeft = 8;
            int arrayIndex = 0;

            while (bitsLeft > 0 || next < data.Length)
            {
                if (bitsLeft < 5)
                {
                    if (next < data.Length)
                    {
                        buffer = (buffer << 8) | (data[next++] & 0xff);
                        bitsLeft += 8;
                    }
                    else
                    {
                        int pad = 5 - bitsLeft;
                        buffer <<= pad;
                        bitsLeft += pad;
                    }
                }
                int index = 0x1f & (buffer >> (bitsLeft - 5));
                bitsLeft -= 5;
                result[arrayIndex++] = Base32Chars[index];
            }
            return new string(result);
        }

        private static byte[] Base32Decode(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            input = input.Trim().ToUpperInvariant();

            var list = new List<byte>();
            int buffer = 0;
            int bitsLeft = 0;

            foreach (char c in input)
            {
                int val = Array.IndexOf(Base32Chars, c);
                if (val < 0) continue;

                buffer = (buffer << 5) | val;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    list.Add((byte)((buffer >> bitsLeft) & 0xFF));
                }
            }
            return list.ToArray();
        }
    }
}
