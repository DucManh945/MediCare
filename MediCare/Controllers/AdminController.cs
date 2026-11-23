using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using MediCare.Models;

namespace MediCare.Controllers
{
    [Authorize(Roles = "Admin hệ thống,Admin")]
    public class AdminController : Controller
    {
        private readonly string connectionString = ConfigurationManager.ConnectionStrings["MediCareDb"]?.ConnectionString;

        public ActionResult Index()
        {
            ViewBag.Title = "Admin - Trang chủ";
            ViewBag.RoleName = "Admin hệ thống";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Dashboard Hệ thống & Server";
            ViewBag.ActiveAdminMenu = "index";

            var model = new AdminDashboardViewModel();
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    model.ServerOnline = true;

                    using (var cmd = new SqlCommand(@"
                        SELECT
                            COUNT(1) AS Total,
                            SUM(CASE WHEN r.RoleName = N'Bệnh nhân' THEN 1 ELSE 0 END) AS Patient,
                            SUM(CASE WHEN r.RoleName = N'Bác sĩ' THEN 1 ELSE 0 END) AS Doctor,
                            SUM(CASE WHEN r.RoleName IN (N'Quản lý phòng khám', N'Quản lý') THEN 1 ELSE 0 END) AS Manager,
                            SUM(CASE WHEN r.RoleName IN (N'Admin hệ thống', N'Admin', N'Quản trị viên') THEN 1 ELSE 0 END) AS Admin
                        FROM dbo.Users u
                        INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId
                        WHERE u.IsActive = 1;", connection))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                model.TotalUsers = r.GetInt32(0);
                                model.PatientCount = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                                model.DoctorCount = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                                model.ManagerCount = r.IsDBNull(3) ? 0 : r.GetInt32(3);
                                model.AdminCount = r.IsDBNull(4) ? 0 : r.GetInt32(4);
                                model.StaffCount = model.TotalUsers - model.PatientCount - model.DoctorCount - model.ManagerCount - model.AdminCount;
                                if (model.StaffCount < 0) model.StaffCount = 0;
                            }
                        }
                    }

                    using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.PhieuKhamBenh WHERE NgayKham >= DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0);", connection))
                    {
                        model.MonthlyVisits = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Admin dashboard error: " + ex);
                model.ServerOnline = false;
            }

            return View(model);
        }

        [HttpGet]
        public ActionResult QuanLyQuyen()
        {
            ViewBag.Title = "Quản lý Role & Phân quyền";
            ViewBag.RoleName = "Admin hệ thống";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Quản lý Role & Phân quyền (RBAC)";
            ViewBag.ActiveAdminMenu = "roles";

            var model = new RoleManagementViewModel();
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        SELECT r.RoleId, r.RoleName, r.MoTa, COUNT(u.UserId) AS UserCount
                        FROM dbo.Roles r
                        LEFT JOIN dbo.Users u ON u.RoleId = r.RoleId AND u.IsActive = 1
                        GROUP BY r.RoleId, r.RoleName, r.MoTa
                        ORDER BY r.RoleId;", connection))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var roleName = r.GetString(1);
                                var isSys = IsSystemRole(roleName);

                                model.Roles.Add(new RoleItemViewModel
                                {
                                    RoleId = r.GetInt32(0),
                                    RoleName = roleName,
                                    Description = r.IsDBNull(2) ? "" : r.GetString(2),
                                    UserCount = r.GetInt32(3),
                                    IsSystemRole = isSys
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("QuanLyQuyen error: " + ex);
                TempData["RoleError"] = "Không thể tải danh sách vai trò.";
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ThemRole(RoleManagementViewModel model)
        {
            if (model == null || model.NewRole == null || string.IsNullOrWhiteSpace(model.NewRole.RoleName))
            {
                TempData["RoleError"] = "Tên vai trò không được để trống.";
                return RedirectToAction("QuanLyQuyen");
            }

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand("INSERT INTO dbo.Roles (RoleName, MoTa) VALUES (@RoleName, @MoTa);", connection))
                    {
                        cmd.Parameters.Add("@RoleName", SqlDbType.NVarChar, 100).Value = model.NewRole.RoleName.Trim();
                        cmd.Parameters.Add("@MoTa", SqlDbType.NVarChar, 255).Value = (object)model.NewRole.Description ?? DBNull.Value;
                        cmd.ExecuteNonQuery();
                    }
                }
                TempData["RoleMessage"] = "Đã thêm vai trò mới thành công.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "CREATE", "Vai trò: " + model.NewRole.RoleName, null, "Thành công", Request.UserHostAddress, "Thêm mới vai trò hệ thống");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ThemRole error: " + ex);
                TempData["RoleError"] = "Không thể thêm vai trò (có thể tên vai trò đã tồn tại).";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "CREATE", "Vai trò: " + model.NewRole.RoleName, null, "Thất bại", Request.UserHostAddress, ex.Message);
            }

            return RedirectToAction("QuanLyQuyen");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SuaRole(RoleEditViewModel model)
        {
            if (model == null || model.RoleId <= 0 || string.IsNullOrWhiteSpace(model.RoleName))
            {
                TempData["RoleError"] = "Thông tin vai trò không hợp lệ.";
                return RedirectToAction("QuanLyQuyen");
            }

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    using (var checkCmd = new SqlCommand("SELECT RoleName FROM dbo.Roles WHERE RoleId = @Id;", connection))
                    {
                        checkCmd.Parameters.Add("@Id", SqlDbType.Int).Value = model.RoleId;
                        var currentName = checkCmd.ExecuteScalar()?.ToString() ?? "";
                        var isSys = IsSystemRole(currentName);

                        if (isSys && !currentName.Equals(model.RoleName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            TempData["RoleError"] = "Không được phép thay đổi tên của vai trò hệ thống cốt lõi.";
                            return RedirectToAction("QuanLyQuyen");
                        }
                    }

                    using (var cmd = new SqlCommand("UPDATE dbo.Roles SET RoleName = @RoleName, MoTa = @MoTa WHERE RoleId = @RoleId;", connection))
                    {
                        cmd.Parameters.Add("@RoleName", SqlDbType.NVarChar, 100).Value = model.RoleName.Trim();
                        cmd.Parameters.Add("@MoTa", SqlDbType.NVarChar, 255).Value = (object)model.Description ?? DBNull.Value;
                        cmd.Parameters.Add("@RoleId", SqlDbType.Int).Value = model.RoleId;
                        cmd.ExecuteNonQuery();
                    }
                }
                TempData["RoleMessage"] = "Đã cập nhật vai trò thành công.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "UPDATE", "Vai trò ID: " + model.RoleId, model.RoleId, "Thành công", Request.UserHostAddress, "Cập nhật vai trò " + model.RoleName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SuaRole error: " + ex);
                TempData["RoleError"] = "Không thể cập nhật vai trò (tên vai trò có thể đã tồn tại).";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "UPDATE", "Vai trò ID: " + model.RoleId, model.RoleId, "Thất bại", Request.UserHostAddress, ex.Message);
            }

            return RedirectToAction("QuanLyQuyen");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult XoaRole(int roleId)
        {
            if (roleId <= 0)
            {
                TempData["RoleError"] = "Mã vai trò không hợp lệ.";
                return RedirectToAction("QuanLyQuyen");
            }

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    string roleName = "";
                    using (var cmd = new SqlCommand("SELECT RoleName FROM dbo.Roles WHERE RoleId = @Id;", connection))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = roleId;
                        roleName = cmd.ExecuteScalar()?.ToString() ?? "";
                    }

                    if (string.IsNullOrEmpty(roleName))
                    {
                        TempData["RoleError"] = "Không tìm thấy vai trò cần xóa.";
                        return RedirectToAction("QuanLyQuyen");
                    }

                    if (IsSystemRole(roleName))
                    {
                        TempData["RoleError"] = "Tuyệt đối không được xóa vai trò hệ thống cốt lõi (" + roleName + ").";
                        return RedirectToAction("QuanLyQuyen");
                    }

                    int userCount = 0;
                    using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WHERE RoleId = @Id;", connection))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = roleId;
                        userCount = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    if (userCount > 0)
                    {
                        TempData["RoleError"] = $"Không thể xóa vai trò '{roleName}' vì đang có {userCount} tài khoản liên kết sử dụng.";
                        return RedirectToAction("QuanLyQuyen");
                    }

                    using (var cmd = new SqlCommand("DELETE FROM dbo.Roles WHERE RoleId = @Id;", connection))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = roleId;
                        cmd.ExecuteNonQuery();
                    }

                    TempData["RoleMessage"] = $"Đã xóa vai trò '{roleName}' thành công.";
                    AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "DELETE", "Vai trò: " + roleName, roleId, "Thành công", Request.UserHostAddress, "Xóa vai trò tùy chỉnh");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("XoaRole error: " + ex);
                TempData["RoleError"] = "Không thể xóa vai trò do lỗi hệ thống hoặc ràng buộc CSDL.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "DELETE", "Vai trò ID: " + roleId, roleId, "Thất bại", Request.UserHostAddress, ex.Message);
            }

            return RedirectToAction("QuanLyQuyen");
        }

        private static bool IsSystemRole(string roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName)) return false;
            var sysRoles = new[] { "Quản trị viên", "Quản lý phòng khám", "Bác sĩ", "Bệnh nhân", "Admin", "Admin hệ thống", "Quản trị" };
            return sysRoles.Contains(roleName.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        [HttpGet]
        public ActionResult QuanLyChiNhanh()
        {
            ViewBag.Title = "Quản lý Chi nhánh & Cơ sở";
            ViewBag.RoleName = "Admin hệ thống";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Quản lý Chi nhánh & Cơ sở KCB";
            ViewBag.ActiveAdminMenu = "branches";

            var model = new BranchManagementViewModel();
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    using (var createCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChiNhanh')
                        BEGIN
                            CREATE TABLE ChiNhanh (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                MaCoSo NVARCHAR(50) NOT NULL UNIQUE,
                                TenChiNhanh NVARCHAR(150) NOT NULL,
                                DiaChi NVARCHAR(255) NULL,
                                GiamDoc NVARCHAR(100) NULL,
                                IsActive BIT NOT NULL DEFAULT 1
                            );
                            INSERT INTO ChiNhanh (MaCoSo, TenChiNhanh, DiaChi, GiamDoc, IsActive) VALUES
                            ('CN-01', N'MediCare Cơ sở Trụ sở chính (Quận 5)', N'215 Hồng Bàng, Phường 11, Quận 5, TP.HCM', N'BS. CKII. Lê Văn Dũng', 1),
                            ('CN-02', N'MediCare Chi nhánh Quận 7', N'1058 Nguyễn Văn Linh, Tân Phong, Quận 7, TP.HCM', N'BS. CKI. Nguyễn Thị Thanh', 1);
                        END", connection))
                    {
                        createCmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SqlCommand(@"
                        SELECT cn.Id, cn.MaCoSo, cn.TenChiNhanh, cn.DiaChi, cn.GiamDoc, cn.IsActive,
                               (SELECT COUNT(1) FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE r.RoleName = N'Bác sĩ' AND u.IsActive = 1) AS DocCount,
                               (SELECT COUNT(1) FROM dbo.PhieuKhamBenh pk WHERE pk.NgayKham >= DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0)) AS VisitCount
                        FROM dbo.ChiNhanh cn
                        ORDER BY cn.Id;", connection))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.Branches.Add(new BranchItemViewModel
                                {
                                    Id = r.GetInt32(0),
                                    BranchCode = r.GetString(1),
                                    BranchName = r.GetString(2),
                                    Address = r.IsDBNull(3) ? "" : r.GetString(3),
                                    Director = r.IsDBNull(4) ? "" : r.GetString(4),
                                    IsActive = r.GetBoolean(5),
                                    DoctorCount = r.GetInt32(6),
                                    MonthlyVisits = r.GetInt32(7)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("QuanLyChiNhanh error: " + ex);
                ViewBag.Error = "Không thể tải danh sách chi nhánh từ CSDL.";
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ThemChiNhanh(BranchManagementViewModel model)
        {
            if (model == null || model.NewBranch == null || string.IsNullOrWhiteSpace(model.NewBranch.BranchCode) || string.IsNullOrWhiteSpace(model.NewBranch.BranchName))
            {
                TempData["BranchError"] = "Mã cơ sở và tên chi nhánh không được để trống.";
                return RedirectToAction("QuanLyChiNhanh");
            }

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand("INSERT INTO dbo.ChiNhanh (MaCoSo, TenChiNhanh, DiaChi, GiamDoc, IsActive) VALUES (@Code, @Name, @Address, @Director, 1);", connection))
                    {
                        cmd.Parameters.Add("@Code", SqlDbType.NVarChar, 50).Value = model.NewBranch.BranchCode.Trim();
                        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 150).Value = model.NewBranch.BranchName.Trim();
                        cmd.Parameters.Add("@Address", SqlDbType.NVarChar, 255).Value = (object)model.NewBranch.Address ?? DBNull.Value;
                        cmd.Parameters.Add("@Director", SqlDbType.NVarChar, 100).Value = (object)model.NewBranch.Director ?? DBNull.Value;
                        cmd.ExecuteNonQuery();
                    }
                }
                TempData["BranchMessage"] = "Đã thêm chi nhánh mới thành công.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "CREATE", "Chi nhánh: " + model.NewBranch.BranchName, null, "Thành công", Request.UserHostAddress, "Thêm mới cơ sở KCB");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ThemChiNhanh error: " + ex);
                TempData["BranchError"] = "Không thể thêm chi nhánh (có thể mã cơ sở đã tồn tại).";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "CREATE", "Chi nhánh: " + model.NewBranch.BranchCode, null, "Thất bại", Request.UserHostAddress, ex.Message);
            }

            return RedirectToAction("QuanLyChiNhanh");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SuaChiNhanh(BranchEditViewModel model)
        {
            if (model == null || model.Id <= 0 || string.IsNullOrWhiteSpace(model.BranchName))
            {
                TempData["BranchError"] = "Thông tin chi nhánh không hợp lệ.";
                return RedirectToAction("QuanLyChiNhanh");
            }

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var cmd = new SqlCommand("UPDATE dbo.ChiNhanh SET TenChiNhanh = @Name, DiaChi = @Address, GiamDoc = @Director WHERE Id = @Id;", connection))
                    {
                        cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 150).Value = model.BranchName.Trim();
                        cmd.Parameters.Add("@Address", SqlDbType.NVarChar, 255).Value = (object)model.Address ?? DBNull.Value;
                        cmd.Parameters.Add("@Director", SqlDbType.NVarChar, 100).Value = (object)model.Director ?? DBNull.Value;
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = model.Id;
                        cmd.ExecuteNonQuery();
                    }
                }
                TempData["BranchMessage"] = "Đã cập nhật thông tin chi nhánh thành công.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "UPDATE", "Chi nhánh ID: " + model.Id, model.Id, "Thành công", Request.UserHostAddress, "Cập nhật chi nhánh " + model.BranchName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SuaChiNhanh error: " + ex);
                TempData["BranchError"] = "Không thể cập nhật chi nhánh.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "UPDATE", "Chi nhánh ID: " + model.Id, model.Id, "Thất bại", Request.UserHostAddress, ex.Message);
            }

            return RedirectToAction("QuanLyChiNhanh");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ToggleChiNhanh(int id)
        {
            if (id <= 0) return new HttpStatusCodeResult(400);

            try
            {
                string name = "";
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var getCmd = new SqlCommand("SELECT TenChiNhanh FROM dbo.ChiNhanh WHERE Id = @Id;", connection))
                    {
                        getCmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        name = getCmd.ExecuteScalar()?.ToString() ?? "";
                    }

                    using (var cmd = new SqlCommand("UPDATE dbo.ChiNhanh SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE Id = @Id;", connection))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["BranchMessage"] = $"Đã thay đổi trạng thái hoạt động của chi nhánh '{name}'.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "UPDATE", "Chi nhánh: " + name, id, "Thành công", Request.UserHostAddress, "Thay đổi trạng thái hoạt động chi nhánh");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ToggleChiNhanh error: " + ex);
                TempData["BranchError"] = "Không thể thay đổi trạng thái chi nhánh.";
            }

            return RedirectToAction("QuanLyChiNhanh");
        }

        [HttpGet]
        public ActionResult SaoLuuDuLieu()
        {
            ViewBag.Title = "Sao lưu & Phục hồi Dữ liệu";
            ViewBag.RoleName = "Admin hệ thống";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Sao lưu & Phục hồi Dữ liệu (Backup & Restore)";
            ViewBag.ActiveAdminMenu = "backup";

            var model = new BackupManagementViewModel();
            var backupDir = Server.MapPath("~/App_Data/Backups");
            model.BackupDirectory = backupDir;

            try
            {
                if (!System.IO.Directory.Exists(backupDir))
                {
                    System.IO.Directory.CreateDirectory(backupDir);
                }

                var di = new System.IO.DirectoryInfo(backupDir);
                foreach (var file in di.GetFiles("*.bak"))
                {
                    model.Backups.Add(new BackupItemViewModel
                    {
                        FileName = file.Name,
                        CreatedAt = file.CreationTime,
                        FileSize = file.Length,
                        BackupType = file.Name.Contains("manual") ? "Manual Snapshot" : "Full Backup",
                        IsVerified = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SaoLuuDuLieu error: " + ex);
                ViewBag.Error = "Không thể đọc thư mục chứa file backup.";
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult TaoBackup()
        {
            var backupDir = Server.MapPath("~/App_Data/Backups");
            try
            {
                if (!System.IO.Directory.Exists(backupDir))
                {
                    System.IO.Directory.CreateDirectory(backupDir);
                }

                string fileName = $"medicare_db_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                string fullPath = System.IO.Path.Combine(backupDir, fileName);

                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string dbName = connection.Database;
                    string sql = $"BACKUP DATABASE [{dbName}] TO DISK = @path WITH FORMAT, INIT, NAME = N'MediCare-Full Database Backup';";

                    using (var cmd = new SqlCommand(sql, connection))
                    {
                        cmd.CommandTimeout = 300;
                        cmd.Parameters.Add("@path", SqlDbType.NVarChar, 500).Value = fullPath;
                        cmd.ExecuteNonQuery();
                    }

                    using (var verifyCmd = new SqlCommand($"RESTORE VERIFYONLY FROM DISK = @path;", connection))
                    {
                        verifyCmd.CommandTimeout = 120;
                        verifyCmd.Parameters.Add("@path", SqlDbType.NVarChar, 500).Value = fullPath;
                        verifyCmd.ExecuteNonQuery();
                    }
                }

                TempData["BackupMessage"] = $"Đã tạo và xác minh thành công bản sao lưu: {fileName}";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "BACKUP", "Database Backup: " + fileName, null, "Thành công", Request.UserHostAddress, "Tạo và xác minh bản sao lưu thành công");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TaoBackup error: " + ex);
                TempData["BackupError"] = "Không thể tạo bản sao lưu SQL Server: " + ex.Message;
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "BACKUP", "Database Backup", null, "Thất bại", Request.UserHostAddress, ex.Message);
            }

            return RedirectToAction("SaoLuuDuLieu");
        }

        [HttpGet]
        public ActionResult TaiBackup(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpStatusCodeResult(400, "Tên file không hợp lệ.");
            }

            var backupDir = Server.MapPath("~/App_Data/Backups");
            var fullPath = System.IO.Path.Combine(backupDir, System.IO.Path.GetFileName(fileName));

            if (!System.IO.File.Exists(fullPath))
            {
                return HttpNotFound("Không tìm thấy file backup.");
            }

            AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "DOWNLOAD_BACKUP", "File: " + fileName, null, "Thành công", Request.UserHostAddress, "Tải xuống file sao lưu hệ thống");
            return File(fullPath, "application/octet-stream", fileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RestoreBackup(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                TempData["BackupError"] = "Tên file backup không hợp lệ.";
                return RedirectToAction("SaoLuuDuLieu");
            }

            var backupDir = Server.MapPath("~/App_Data/Backups");
            var fullPath = System.IO.Path.Combine(backupDir, System.IO.Path.GetFileName(fileName));

            if (!System.IO.File.Exists(fullPath))
            {
                TempData["BackupError"] = "Không tìm thấy file backup trên đĩa.";
                return RedirectToAction("SaoLuuDuLieu");
            }

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var verifyCmd = new SqlCommand("RESTORE VERIFYONLY FROM DISK = @path;", connection))
                    {
                        verifyCmd.Parameters.Add("@path", SqlDbType.NVarChar, 500).Value = fullPath;
                        verifyCmd.ExecuteNonQuery();
                    }
                }

                TempData["BackupMessage"] = $"Đã xác minh tính toàn vẹn của bản backup '{fileName}'. (Chế độ an toàn: Yêu cầu thực hiện Restore thủ công qua SSMS trên môi trường kiểm thử để tránh mất dữ liệu chạy thật).";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "VERIFY_RESTORE", "Backup File: " + fileName, null, "Thành công", Request.UserHostAddress, "Xác minh bản backup trước khi khôi phục an toàn");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RestoreBackup error: " + ex);
                TempData["BackupError"] = "Bản backup không hợp lệ hoặc bị lỗi: " + ex.Message;
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "VERIFY_RESTORE", "Backup File: " + fileName, null, "Thất bại", Request.UserHostAddress, ex.Message);
            }

            return RedirectToAction("SaoLuuDuLieu");
        }

        [HttpGet]
        public ActionResult NhatKyHoatDong(string search = null, string actionType = null, DateTime? fromDate = null, DateTime? toDate = null, int page = 1)
        {
            ViewBag.Title = "Nhật ký Hoạt động";
            ViewBag.RoleName = "Admin hệ thống";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Nhật ký Hoạt động Hệ thống (Audit Log)";
            ViewBag.ActiveAdminMenu = "audit";

            var model = new AuditLogSearchViewModel
            {
                Search = search?.Trim(),
                ActionType = actionType?.Trim(),
                FromDate = fromDate ?? DateTime.Today.AddDays(-30),
                ToDate = toDate ?? DateTime.Today,
                Page = Math.Max(page, 1),
                PageSize = 20
            };

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    using (var createCmd = new SqlCommand(@"
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
                        END", connection))
                    {
                        createCmd.ExecuteNonQuery();
                    }

                    string where = @" WHERE (@Search = '' OR NguoiThucHien LIKE @LikeSearch OR DoiTuong LIKE @LikeSearch OR IpAddress LIKE @LikeSearch OR ChiTiet LIKE @LikeSearch)
                                      AND (@ActionType = '' OR HanhDong = @ActionType)
                                      AND (ThoiGian >= @FromDate AND ThoiGian < DATEADD(day, 1, @ToDate))";

                    using (var countCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.NhatKyHoatDong" + where, connection))
                    {
                        countCmd.Parameters.Add("@Search", SqlDbType.VarChar, 100).Value = model.Search ?? "";
                        countCmd.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 102).Value = "%" + (model.Search ?? "") + "%";
                        countCmd.Parameters.Add("@ActionType", SqlDbType.NVarChar, 50).Value = model.ActionType ?? "";
                        countCmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = model.FromDate.Value.Date;
                        countCmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = model.ToDate.Value.Date;

                        model.TotalItems = Convert.ToInt32(countCmd.ExecuteScalar());
                    }

                    using (var listCmd = new SqlCommand(@"
                        SELECT Id, ThoiGian, NguoiThucHien, VaiTro, HanhDong, DoiTuong, IdThamChieu, KetQua, IpAddress, ChiTiet
                        FROM dbo.NhatKyHoatDong" + where + @"
                        ORDER BY Id DESC
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", connection))
                    {
                        listCmd.Parameters.Add("@Search", SqlDbType.VarChar, 100).Value = model.Search ?? "";
                        listCmd.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 102).Value = "%" + (model.Search ?? "") + "%";
                        listCmd.Parameters.Add("@ActionType", SqlDbType.NVarChar, 50).Value = model.ActionType ?? "";
                        listCmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = model.FromDate.Value.Date;
                        listCmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = model.ToDate.Value.Date;
                        listCmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (model.Page - 1) * model.PageSize;
                        listCmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = model.PageSize;

                        using (var r = listCmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.Items.Add(new AuditLogItemViewModel
                                {
                                    Id = r.GetInt32(0),
                                    ThoiGian = r.GetDateTime(1),
                                    NguoiThucHien = r.GetString(2),
                                    VaiTro = r.IsDBNull(3) ? "" : r.GetString(3),
                                    HanhDong = r.GetString(4),
                                    DoiTuong = r.GetString(5),
                                    IdThamChieu = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                                    KetQua = r.GetString(7),
                                    IpAddress = r.IsDBNull(8) ? "" : r.GetString(8),
                                    ChiTiet = r.IsDBNull(9) ? "" : r.GetString(9)
                                });
                            }
                        }
                    }
                }

                model.TotalPages = model.TotalItems == 0 ? 0 : (int)Math.Ceiling(model.TotalItems / (double)model.PageSize);
                if (model.TotalPages > 0 && model.Page > model.TotalPages) model.Page = model.TotalPages;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("NhatKyHoatDong error: " + ex);
                ViewBag.Error = "Không thể tải nhật ký hoạt động từ CSDL.";
            }

            return View(model);
        }

        [HttpGet]
        public FileContentResult XuatLogCsv(string search = null, string actionType = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var csv = new StringBuilder();
            csv.AppendLine("ID,Thoi Gian,Nguoi Thuc Hien,Vai Tro,Hanh Dong,Doi Tuong,Ket Qua,IP,Chi Tiet");

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string where = @" WHERE (@Search = '' OR NguoiThucHien LIKE @LikeSearch OR DoiTuong LIKE @LikeSearch OR IpAddress LIKE @LikeSearch OR ChiTiet LIKE @LikeSearch)
                                      AND (@ActionType = '' OR HanhDong = @ActionType)
                                      AND (ThoiGian >= @FromDate AND ThoiGian < DATEADD(day, 1, @ToDate))";

                    using (var cmd = new SqlCommand("SELECT Id, ThoiGian, NguoiThucHien, VaiTro, HanhDong, DoiTuong, KetQua, IpAddress, ChiTiet FROM dbo.NhatKyHoatDong" + where + " ORDER BY Id DESC;", connection))
                    {
                        cmd.Parameters.Add("@Search", SqlDbType.VarChar, 100).Value = search ?? "";
                        cmd.Parameters.Add("@LikeSearch", SqlDbType.NVarChar, 102).Value = "%" + (search ?? "") + "%";
                        cmd.Parameters.Add("@ActionType", SqlDbType.NVarChar, 50).Value = actionType ?? "";
                        cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = (fromDate ?? DateTime.Today.AddDays(-30)).Date;
                        cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = (toDate ?? DateTime.Today).Date;

                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                csv.AppendLine(string.Format("{0},{1},\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\"",
                                    r.GetInt32(0),
                                    r.GetDateTime(1).ToString("yyyy-MM-dd HH:mm:ss"),
                                    r.GetString(2).Replace("\"", "\"\""),
                                    r.IsDBNull(3) ? "" : r.GetString(3).Replace("\"", "\"\""),
                                    r.GetString(4),
                                    r.GetString(5).Replace("\"", "\"\""),
                                    r.GetString(6),
                                    r.IsDBNull(7) ? "" : r.GetString(7),
                                    r.IsDBNull(8) ? "" : r.GetString(8).Replace("\"", "\"\"")
                                ));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                csv.AppendLine("Loi khi xuat log: " + ex.Message);
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return File(bytes, "text/csv", "SystemAuditLog_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
        }

        [HttpGet]
        public ActionResult BaoMat()
        {
            ViewBag.Title = "Bảo mật & Cảnh báo System";
            ViewBag.RoleName = "Admin hệ thống";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Bảo mật & Cảnh báo An ninh";
            ViewBag.ActiveAdminMenu = "security";

            var model = new SecurityManagementViewModel();
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    using (var createCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IpBlacklist')
                        BEGIN
                            CREATE TABLE IpBlacklist (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                IpAddress VARCHAR(50) NOT NULL,
                                LyDo NVARCHAR(255) NULL,
                                ThoiGianKhoa DATETIME NOT NULL DEFAULT GETDATE(),
                                ThoiHanMo DATETIME NULL,
                                IsActive BIT NOT NULL DEFAULT 1
                            );
                            INSERT INTO IpBlacklist (IpAddress, LyDo, ThoiGianKhoa, ThoiHanMo, IsActive)
                            VALUES ('192.168.1.100', N'Đăng nhập sai mật khẩu 12 lần liên tiếp', GETDATE(), DATEADD(day, 1, GETDATE()), 1);
                        END", connection))
                    {
                        createCmd.ExecuteNonQuery();
                    }

                    using (var cleanupCmd = new SqlCommand("UPDATE dbo.IpBlacklist SET IsActive = 0 WHERE IpAddress IN ('::1', '127.0.0.1', 'localhost');", connection))
                    {
                        cleanupCmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SqlCommand("SELECT Id, IpAddress, LyDo, ThoiGianKhoa, ThoiHanMo, IsActive FROM dbo.IpBlacklist WHERE IsActive = 1 AND (ThoiHanMo IS NULL OR ThoiHanMo > GETDATE()) ORDER BY Id DESC;", connection))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                model.BlockedIps.Add(new IpBlacklistItemViewModel
                                {
                                    Id = r.GetInt32(0),
                                    IpAddress = r.GetString(1),
                                    Reason = r.IsDBNull(2) ? "" : r.GetString(2),
                                    LockedAt = r.GetDateTime(3),
                                    ExpiresAt = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4),
                                    IsActive = r.GetBoolean(5)
                                });
                            }
                        }
                    }

                    // Đảm bảo cột 2FA tồn tại (idempotent) rồi thống kê trạng thái 2FA thực tế
                    using (var migrateCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'IsTwoFactorEnabled')
                        BEGIN
                            ALTER TABLE dbo.Users ADD IsTwoFactorEnabled BIT NOT NULL DEFAULT 0;
                        END", connection))
                    {
                        migrateCmd.ExecuteNonQuery();
                    }

                    using (var statCmd = new SqlCommand(@"
                        SELECT COUNT(1), SUM(CASE WHEN ISNULL(IsTwoFactorEnabled, 0) = 1 THEN 1 ELSE 0 END)
                        FROM dbo.Users WHERE IsActive = 1;", connection))
                    {
                        using (var r = statCmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                model.TotalActiveUsers = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                                model.TwoFactorEnabledCount = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("BaoMat error: " + ex);
                ViewBag.Error = "Không thể tải danh sách IP bị chặn từ CSDL.";
            }

            model.IsHttps = Request.IsSecureConnection || Request.Url.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            model.TwoFactorStatus = model.TwoFactorEnabledCount > 0
                ? $"Đã triển khai ({model.TwoFactorEnabledCount}/{model.TotalActiveUsers} tài khoản đã bật)"
                : "Chưa có tài khoản nào bật 2FA";
            model.BlockedIpCount = model.BlockedIps.Count;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult MoKhoaIp(int id)
        {
            if (id <= 0)
            {
                TempData["SecurityError"] = "Mã IP không hợp lệ.";
                return RedirectToAction("BaoMat");
            }

            try
            {
                string ip = "";
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var getCmd = new SqlCommand("SELECT IpAddress FROM dbo.IpBlacklist WHERE Id = @Id;", connection))
                    {
                        getCmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        ip = getCmd.ExecuteScalar()?.ToString() ?? "";
                    }

                    using (var cmd = new SqlCommand("UPDATE dbo.IpBlacklist SET IsActive = 0 WHERE Id = @Id;", connection))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["SecurityMessage"] = $"Đã mở khóa thành công cho địa chỉ IP: {ip}";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "UNLOCK_IP", "IP Address: " + ip, id, "Thành công", Request.UserHostAddress, "Mở khóa địa chỉ IP khỏi Firewall Blacklist");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MoKhoaIp error: " + ex);
                TempData["SecurityError"] = "Không thể mở khóa IP.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "UNLOCK_IP", "IP ID: " + id, id, "Thất bại", Request.UserHostAddress, ex.Message);
            }

            return RedirectToAction("BaoMat");
        }

        [HttpGet]
        public ActionResult CauHinh()
        {
            ViewBag.Title = "Cấu hình Hệ thống";
            ViewBag.RoleName = "Admin hệ thống";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Cấu hình Tham số Hệ thống";
            ViewBag.ActiveAdminMenu = "config";

            var model = new SystemSettingsViewModel();
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    using (var createCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CauHinhHeThong')
                        BEGIN
                            CREATE TABLE CauHinhHeThong (
                                Khoa NVARCHAR(100) PRIMARY KEY,
                                GiaTri NVARCHAR(MAX) NULL
                            );
                            INSERT INTO CauHinhHeThong (Khoa, GiaTri) VALUES
                            (N'SystemName', N'MediCare - Hệ thống Quản lý Phòng khám & Bệnh viện'),
                            (N'SystemVersion', N'v2.5.0-Release'),
                            (N'SmtpHost', N'smtp.medicare.vn (Port 587)'),
                            (N'SessionTimeout', N'30');
                        END", connection))
                    {
                        createCmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SqlCommand("SELECT Khoa, GiaTri FROM dbo.CauHinhHeThong;", connection))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var key = r.GetString(0);
                                var val = r.IsDBNull(1) ? "" : r.GetString(1);
                                if (key.Equals("SystemName", StringComparison.OrdinalIgnoreCase)) model.SystemName = val;
                                else if (key.Equals("SystemVersion", StringComparison.OrdinalIgnoreCase)) model.SystemVersion = val;
                                else if (key.Equals("SmtpHost", StringComparison.OrdinalIgnoreCase)) model.SmtpHost = val;
                                else if (key.Equals("SessionTimeout", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out int st)) model.SessionTimeout = st;
                                else if (key.Equals("OpenAiApiKey", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!string.IsNullOrWhiteSpace(val) && !val.Equals("YOUR_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
                                    {
                                        model.IsOpenAiApiKeyConfigured = true;
                                    }
                                }
                            }
                        }
                    }
                    model.OpenAiApiKey = "";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CauHinh GET error: " + ex);
                ViewBag.Error = "Không thể tải cấu hình từ CSDL.";
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CauHinh(SystemSettingsViewModel model)
        {
            ViewBag.Title = "Cấu hình Hệ thống";
            ViewBag.RoleName = "Admin hệ thống";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Cấu hình Tham số Hệ thống";
            ViewBag.ActiveAdminMenu = "config";

            // Validate khoảng giá trị SessionTimeout: chỉ chấp nhận 30, 60 hoặc 120 phút
            if (model.SessionTimeout != 30 && model.SessionTimeout != 60 && model.SessionTimeout != 120)
            {
                ModelState.AddModelError("SessionTimeout", "Thời gian hết hạn phiên làm việc phải là 30 phút, 60 phút hoặc 2 giờ.");
            }

            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(m => !string.IsNullOrWhiteSpace(m)));

                ViewBag.Error = string.IsNullOrWhiteSpace(errors)
                    ? "Dữ liệu cấu hình không hợp lệ."
                    : "Dữ liệu cấu hình không hợp lệ: " + errors;
                return View(model);
            }

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    void SaveConfig(string key, string val)
                    {
                        using (var upsert = new SqlCommand(@"
                            IF EXISTS (SELECT 1 FROM dbo.CauHinhHeThong WHERE Khoa = @Key)
                                UPDATE dbo.CauHinhHeThong SET GiaTri = @Val WHERE Khoa = @Key;
                            ELSE
                                INSERT INTO dbo.CauHinhHeThong (Khoa, GiaTri) VALUES (@Key, @Val);", connection))
                        {
                            upsert.Parameters.Add("@Key", SqlDbType.NVarChar, 100).Value = key;
                            upsert.Parameters.Add("@Val", SqlDbType.NVarChar, -1).Value = (object)val ?? DBNull.Value;
                            upsert.ExecuteNonQuery();
                        }
                    }

                    SaveConfig("SystemName", model.SystemName);
                    SaveConfig("SmtpHost", model.SmtpHost);
                    SaveConfig("SessionTimeout", model.SessionTimeout.ToString());

                    if (!string.IsNullOrWhiteSpace(model.OpenAiApiKey))
                    {
                        SaveConfig("OpenAiApiKey", model.OpenAiApiKey.Trim());
                    }
                }

                TempData["ConfigMessage"] = "Đã lưu cấu hình hệ thống thành công.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "UPDATE", "Cấu hình Hệ thống", null, "Thành công", Request.UserHostAddress, "Cập nhật tham số cấu hình hệ thống");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CauHinh POST error: " + ex);
                TempData["ConfigError"] = "Không thể lưu cấu hình vào CSDL.";
                AuditLogger.Log(User.Identity.Name, "Admin hệ thống", "UPDATE", "Cấu hình Hệ thống", null, "Thất bại", Request.UserHostAddress, ex.Message);
            }

            return RedirectToAction("CauHinh");
        }

        [HttpGet]
        public ActionResult KetNoiApi()
        {
            ViewBag.Title = "Kết nối & API System";
            ViewBag.RoleName = "Admin hệ thống";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.PageTitle = "Cấu hình Kết nối & API";
            ViewBag.ActiveAdminMenu = "api";
            return View();
        }
    }
}
