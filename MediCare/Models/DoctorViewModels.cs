using System;
using System.Collections.Generic;

namespace MediCare.Models
{
    /// <summary>
    /// Thông tin bác sĩ dùng để hiển thị (lấy từ bảng Users kết hợp bảng Khoa).
    /// </summary>
    public class DoctorViewModel
    {
        public int UserId { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Gender { get; set; }
        public DateTime? BirthDate { get; set; }

        public int? DepartmentId { get; set; }
        public string DepartmentName { get; set; }

        public int? ExperienceYears { get; set; }
        public decimal? Rating { get; set; }
        public int? ReviewCount { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public bool IsActive { get; set; } = true;

        public string DisplayDepartment
        {
            get { return string.IsNullOrWhiteSpace(DepartmentName) ? "Bác sĩ đa khoa" : DepartmentName; }
        }

        public string DisplayRating
        {
            get { return (Rating ?? 5.0m).ToString("0.0"); }
        }

        public int DisplayReviewCount
        {
            get { return ReviewCount ?? 0; }
        }

        public int DisplayExperienceYears
        {
            get { return ExperienceYears ?? 0; }
        }

        public string DisplayEmail
        {
            get { return string.IsNullOrWhiteSpace(Email) ? "#" : "mailto:" + Email; }
        }
    }

    /// <summary>
    /// Dịch vụ dùng để hiển thị trên trang chủ (lấy từ bảng DichVuCLS).
    /// </summary>
    public class ServiceViewModel
    {
        public int ServiceId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }

        public string DisplayIcon
        {
            get { return string.IsNullOrWhiteSpace(Icon) ? "fas fa-stethoscope" : Icon; }
        }

        /// <summary>Hiển thị giá rút gọn dạng "600k+", dịch vụ miễn phí hiển thị "Liên hệ".</summary>
        public string DisplayPrice
        {
            get
            {
                if (Price <= 0) return "Liên hệ";
                if (Price >= 1000000m) return (Price / 1000000m).ToString("0.#") + "tr+";
                return (Price / 1000m).ToString("0") + "k+";
            }
        }
    }

    /// <summary>
    /// Model cho trang chủ: bao gồm form đặt lịch (kế thừa BookAppointmentViewModel),
    /// danh sách dịch vụ và danh sách bác sĩ lấy từ database (có phân trang).
    /// </summary>
    public class HomeIndexViewModel : BookAppointmentViewModel
    {
        // Danh sách bác sĩ + phân trang (8 bác sĩ/trang)
        public IList<DoctorViewModel> DoctorProfiles { get; set; } = new List<DoctorViewModel>();
        public int DoctorPage { get; set; } = 1;
        public int DoctorPageSize { get; set; } = 8;
        public int DoctorTotalItems { get; set; }
        public int DoctorTotalPages { get; set; }

        // Danh sách dịch vụ + phân trang (6 dịch vụ/trang)
        public IList<ServiceViewModel> Services { get; set; } = new List<ServiceViewModel>();
        public int ServicePage { get; set; } = 1;
        public int ServicePageSize { get; set; } = 6;
        public int ServiceTotalItems { get; set; }
        public int ServiceTotalPages { get; set; }
    }
}
