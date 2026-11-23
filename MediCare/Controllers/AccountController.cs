using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Web;
using System.Web.Security;
using System.Web.Mvc;
using MediCare.Models;

namespace MediCare.Controllers
{
    public class AccountController : Controller
    {
        private readonly string connectionString = ConfigurationManager.ConnectionStrings["MediCareDb"]?.ConnectionString;

        public ActionResult Login()
        {
            if (Request.IsAuthenticated)
            {
                var auth = HttpContext.User?.Identity as FormsIdentity;
                if (auth != null && auth.IsAuthenticated)
                {
                    var userData = auth.Ticket.UserData ?? string.Empty;
                    var parts = userData.Split(new[] { '|' }, 3);
                    var roleName = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                    if (!string.IsNullOrWhiteSpace(roleName))
                    {
                        return RedirectToRoleHome(roleName);
                    }
                }
            }

            ViewBag.Title = "Đăng nhập";
            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(LoginViewModel model, string returnUrl)
        {
            ViewBag.Title = "Đăng nhập";

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string clientIp = Request.UserHostAddress ?? "127.0.0.1";
            var identity = model.UserName.Trim();

            // 1. Firewall & Account-Level Rate Limiting Check
            if (IsIpBlocked(clientIp, identity))
            {
                AuditLogger.Log(model.UserName, "Khách", "BLOCKED_LOGIN", "Hệ thống đăng nhập", null, "Thất bại", clientIp, "Đăng nhập bị từ chối do vượt quá giới hạn đăng nhập thất bại");
                ModelState.AddModelError("", $"Tài khoản hoặc địa chỉ IP ({clientIp}) đang tạm thời bị giới hạn đăng nhập do có nhiều lần thử không thành công. Vui lòng thử lại sau 15 phút hoặc liên hệ Quản trị viên.");
                return View(model);
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();

                    // Migration an toàn cho bảng Users nếu chưa có cột 2FA
                    using (var migrateCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'IsTwoFactorEnabled')
                        BEGIN
                            ALTER TABLE dbo.Users ADD IsTwoFactorEnabled BIT NOT NULL DEFAULT 0;
                        END
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'TwoFactorSecret')
                        BEGIN
                            ALTER TABLE dbo.Users ADD TwoFactorSecret NVARCHAR(100) NULL;
                        END", connection))
                    {
                        migrateCmd.ExecuteNonQuery();
                    }

                    using (var command = new SqlCommand(@"
                        SELECT TOP (1)
                               u.UserId, u.Username, u.PasswordHash, u.FullName, r.RoleName,
                               ISNULL(u.IsTwoFactorEnabled, 0) AS IsTwoFactorEnabled, u.TwoFactorSecret
                        FROM dbo.Users AS u
                        INNER JOIN dbo.Roles AS r ON r.RoleId = u.RoleId
                        WHERE u.IsActive = 1
                          AND (u.Username = @Identity
                               OR u.Email = @Identity
                               OR u.Phone = @Identity);", connection))
                    {
                        command.Parameters.Add("@Identity", SqlDbType.VarChar, 100).Value = identity;

                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read() || !PasswordHasher.Verify(model.Password, reader["PasswordHash"].ToString()))
                            {
                                int failCount = RecordFailedLoginAttempt(clientIp, identity);
                                AuditLogger.Log(identity, "Khách", "FAILED_LOGIN", "Hệ thống đăng nhập", null, "Thất bại", clientIp, $"Đăng nhập thất bại lần thứ {failCount}");

                                if (failCount >= 5)
                                {
                                    ModelState.AddModelError("", $"Tài khoản '{identity}' đã bị tạm khóa 15 phút do nhập sai mật khẩu 5 lần liên tiếp.");
                                }
                                else
                                {
                                    ModelState.AddModelError("", $"Thông tin đăng nhập không chính xác (Lần thử {failCount}/5).");
                                }
                                return View(model);
                            }

                            var userId = reader["UserId"].ToString();
                            var username = reader["Username"].ToString();
                            var fullName = reader["FullName"].ToString();
                            var roleName = reader["RoleName"].ToString();
                            bool is2FaEnabled = Convert.ToBoolean(reader["IsTwoFactorEnabled"]);
                            string twoFactorSecret = reader["TwoFactorSecret"]?.ToString();

                            // Nâng cấp mật khẩu seed dạng plaintext (ví dụ: '123456') sang PBKDF2 ngay khi đăng nhập đúng
                            if (!PasswordHasher.IsHashed(reader["PasswordHash"]?.ToString()))
                            {
                                try
                                {
                                    using (var upgradeCmd = new SqlCommand("UPDATE dbo.Users SET PasswordHash = @Hash WHERE UserId = @UserId;", connection))
                                    {
                                        upgradeCmd.Parameters.Add("@Hash", SqlDbType.VarChar, 255).Value = PasswordHasher.Hash(model.Password);
                                        upgradeCmd.Parameters.Add("@UserId", SqlDbType.Int).Value = Convert.ToInt32(userId);
                                        upgradeCmd.ExecuteNonQuery();
                                    }
                                }
                                catch (Exception upgradeEx)
                                {
                                    System.Diagnostics.Debug.WriteLine("Password upgrade error: " + upgradeEx);
                                }
                            }

                            // Xóa bộ đếm đăng nhập thất bại của tài khoản khi đăng nhập đúng
                            ResetFailedLoginAttempts(username);

                            // Nếu tài khoản ĐÃ BẬT 2FA -> Chuyển sang bước xác minh OTP 2FA
                            if (is2FaEnabled && !string.IsNullOrWhiteSpace(twoFactorSecret))
                            {
                                Session["2FA_UserId"] = userId;
                                Session["2FA_Username"] = username;
                                Session["2FA_FullName"] = fullName;
                                Session["2FA_RoleName"] = roleName;
                                Session["2FA_Secret"] = twoFactorSecret;
                                Session["2FA_RememberMe"] = model.RememberMe;
                                Session["2FA_ReturnUrl"] = returnUrl;

                                AuditLogger.Log(username, roleName, "REQUIRE_2FA", "Xác thực 2FA", Convert.ToInt32(userId), "Chờ OTP", clientIp, "Mật khẩu đúng, chuyển sang xác minh mã OTP 2FA");
                                return RedirectToAction("VerifyTwoFactor");
                            }

                            // Nếu chưa bật 2FA -> Đăng nhập thành công ngay
                            AuditLogger.Log(username, roleName, "LOGIN", "Hệ thống đăng nhập", Convert.ToInt32(userId), "Thành công", clientIp, "Đăng nhập thành công");

                            var ticket = new FormsAuthenticationTicket(
                                1,
                                username,
                                DateTime.Now,
                                DateTime.Now.AddMinutes(model.RememberMe ? 43200 : 30),
                                model.RememberMe,
                                userId + "|" + roleName + "|" + fullName,
                                FormsAuthentication.FormsCookiePath);

                        Response.Cookies.Add(new HttpCookie(
                            FormsAuthentication.FormsCookieName,
                            FormsAuthentication.Encrypt(ticket)));

                        if (Url.IsLocalUrl(returnUrl) && IsAllowedReturnUrl(returnUrl, roleName))
                        {
                            return Redirect(returnUrl);
                        }

                        return RedirectToRoleHome(roleName);
                    }
                }
            }
            }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException)
            {
                ModelState.AddModelError("", "Không thể kết nối cơ sở dữ liệu. Kiểm tra connectionStrings trong Web.config.");
                return View(model);
            }
        }

        public ActionResult Register()
        {
            ViewBag.Title = "Đăng ký tài khoản";
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(RegisterViewModel model)
        {
            ViewBag.Title = "Đăng ký tài khoản";

            if (!model.Agree)
            {
                ModelState.AddModelError("Agree", "Vui lòng đồng ý với điều khoản dịch vụ.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var email = model.Email.Trim();
            var phone = model.Phone.Trim();
            var fullName = model.FullName.Trim();

            // Username chỉ cho phép 50 ký tự trong CSDL, Email cho phép 100.
            // Nếu email quá dài, ta lấy phần trước @ hoặc cắt bớt để làm username.
            string username = email;
            if (username.Length > 50)
            {
                username = email.Substring(0, 50);
            }

            try
            {
                using (var connection = CreateConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO dbo.Users
                            (Username, PasswordHash, FullName, Phone, Email,
                             GioiTinh, NgaySinh, DiaChi, RoleId, IsActive)
                        SELECT
                            @Username, @PasswordHash, @FullName, @Phone, @Email,
                            @Gender, @BirthDate, @Address, r.RoleId, 1
                        FROM dbo.Roles AS r
                        WHERE r.RoleName = @RoleName
                          AND NOT EXISTS
                          (
                              SELECT 1
                              FROM dbo.Users AS u
                              WHERE u.Username = @Username
                                 OR u.Email = @Email
                                 OR u.Phone = @Phone
                          );";

                    command.Parameters.Add("@Username", SqlDbType.VarChar, 50).Value = username;
                    command.Parameters.Add("@PasswordHash", SqlDbType.VarChar, 255).Value = PasswordHasher.Hash(model.Password);
                    command.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = fullName;
                    command.Parameters.Add("@Phone", SqlDbType.VarChar, 20).Value = phone;
                    command.Parameters.Add("@Email", SqlDbType.VarChar, 100).Value = email;
                    command.Parameters.Add("@Gender", SqlDbType.NVarChar, 10).Value = (object)model.Gender ?? DBNull.Value;
                    command.Parameters.Add("@BirthDate", SqlDbType.Date).Value = (object)model.BirthDate ?? DBNull.Value;
                    command.Parameters.Add("@Address", SqlDbType.NVarChar, 255).Value = (object)model.Address ?? DBNull.Value;
                    command.Parameters.Add("@RoleName", SqlDbType.NVarChar, 100).Value = "Bệnh nhân";

                    connection.Open();
                    var affectedRows = command.ExecuteNonQuery();
                    if (affectedRows != 1)
                    {
                        // Kiểm tra chi tiết nguyên nhân thất bại
                        using (var checkCmd = new SqlCommand("SELECT TOP 1 Email, Phone, Username FROM dbo.Users WHERE Email = @Email OR Phone = @Phone OR Username = @Username", connection))
                        {
                            checkCmd.Parameters.Add("@Email", SqlDbType.VarChar, 100).Value = email;
                            checkCmd.Parameters.Add("@Phone", SqlDbType.VarChar, 20).Value = phone;
                            checkCmd.Parameters.Add("@Username", SqlDbType.VarChar, 50).Value = username;
                            using (var reader = checkCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    if (reader["Email"].ToString().Equals(email, StringComparison.OrdinalIgnoreCase))
                                        ModelState.AddModelError("Email", "Email này đã được đăng ký tài khoản.");
                                    if (reader["Phone"].ToString().Equals(phone, StringComparison.OrdinalIgnoreCase))
                                        ModelState.AddModelError("Phone", "Số điện thoại này đã được đăng ký tài khoản.");
                                    if (reader["Username"].ToString().Equals(username, StringComparison.OrdinalIgnoreCase))
                                        ModelState.AddModelError("", "Tên đăng nhập (tạo từ email) đã tồn tại.");
                                }
                                else
                                {
                                    ModelState.AddModelError("", "Không tìm thấy vai trò 'Bệnh nhân' trong hệ thống. Vui lòng liên hệ quản trị viên.");
                                }
                            }
                        }
                        return View(model);
                    }
                }

                TempData["Message"] = "Tạo tài khoản thành công. Vui lòng đăng nhập.";
                return RedirectToAction("Login");
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
            {
                ModelState.AddModelError("", "Email, số điện thoại hoặc tên đăng nhập đã được sử dụng.");
                return View(model);
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("Register SQL error " + ex.Number + ": " + ex.Message);
                ModelState.AddModelError("", System.Web.HttpContext.Current.IsDebuggingEnabled
                    ? "Lỗi SQL " + ex.Number + ": " + ex.Message
                    : "Không thể tạo tài khoản. Vui lòng thử lại sau.");
                return View(model);
            }
            catch (ConfigurationErrorsException ex)
            {
                System.Diagnostics.Debug.WriteLine("Register configuration error: " + ex.Message);
                ModelState.AddModelError("", "Thiếu hoặc sai connection string MediCareDb trong Web.config.");
                return View(model);
            }
        }

        [HttpGet]
        public ActionResult Logout()
        {
            SignOutCurrentUser();
            PreventResponseCaching();
            return RedirectToAction("Login", "Account");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LogoutPost()
        {
            SignOutCurrentUser();
            PreventResponseCaching();
            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        public ActionResult ForgotPassword()
        {
            ViewBag.Title = "Quên mật khẩu";
            if (Request.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View(new ForgotPasswordViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ForgotPassword(ForgotPasswordViewModel model)
        {
            ViewBag.Title = "Quên mật khẩu";

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string identity = model.Identity.Trim();
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();

                    using (var createCmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DatLaiMatKhau')
                        BEGIN
                            CREATE TABLE DatLaiMatKhau (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                UserId INT NOT NULL,
                                Token VARCHAR(64) NOT NULL,
                                HetHan DATETIME NOT NULL,
                                DaSuDung BIT NOT NULL DEFAULT 0,
                                NgayTao DATETIME NOT NULL DEFAULT GETDATE()
                            );
                        END", connection))
                    {
                        createCmd.ExecuteNonQuery();
                    }

                    int userId = 0;
                    string email = null;
                    string fullName = null;
                    using (var cmd = new SqlCommand(@"
                        SELECT TOP 1 UserId, Email, FullName FROM dbo.Users
                        WHERE IsActive = 1 AND (Username = @Identity OR Email = @Identity OR Phone = @Identity);", connection))
                    {
                        cmd.Parameters.Add("@Identity", SqlDbType.NVarChar, 100).Value = identity;
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                userId = r.GetInt32(0);
                                email = r.IsDBNull(1) ? null : r.GetString(1);
                                fullName = r.GetString(2);
                            }
                        }
                    }

                    if (userId > 0 && !string.IsNullOrWhiteSpace(email))
                    {
                        string token = Guid.NewGuid().ToString("N");
                        using (var cmd = new SqlCommand(@"
                            INSERT INTO dbo.DatLaiMatKhau (UserId, Token, HetHan, DaSuDung, NgayTao)
                            VALUES (@UserId, @Token, DATEADD(minute, 30, GETDATE()), 0, GETDATE());", connection))
                        {
                            cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                            cmd.Parameters.Add("@Token", SqlDbType.VarChar, 64).Value = token;
                            cmd.ExecuteNonQuery();
                        }

                        string resetUrl = Url.Action("ResetPassword", "Account", new { token }, Request.Url.Scheme);
                        string body = "<h3>Dat lai mat khau MediCare</h3>" +
                            "<p>Xin chao <strong>" + fullName + "</strong>,</p>" +
                            "<p>Chung toi nhan duoc yeu cau dat lai mat khau cho tai khoan MediCare cua ban.</p>" +
                            "<p>Vui long nhap vao lien ket duoi day de dat mat khau moi (hieu luc trong 30 phut):</p>" +
                            "<p><a href=\"" + resetUrl + "\">" + resetUrl + "</a></p>" +
                            "<p>Neu ban khong thuc hien yeu cau nay, hay bo qua email nay.</p>";

                        EmailService.SendEmail(email, "[MediCare] Yeu cau dat lai mat khau", body);

                        // Che do demo: khi chua cau hinh SMTP, hien lien ket ngay tren giao dien de tien kiem thu
                        if (!IsSmtpConfigured())
                        {
                            TempData["ForgotDevLink"] = resetUrl;
                        }

                        AuditLogger.Log(identity, "Khách", "FORGOT_PASSWORD", "Đặt lại mật khẩu", userId, "Yêu cầu", Request.UserHostAddress, "Yêu cầu đặt lại mật khẩu qua email");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ForgotPassword error: " + ex);
            }

            TempData["ForgotMessage"] = "Nếu thông tin bạn nhập trùng khớp với một tài khoản, chúng tôi đã gửi email hướng dẫn đặt lại mật khẩu. Vui lòng kiểm tra hộp thư (kể cả thư rác).";
            return RedirectToAction("ForgotPassword");
        }

        [HttpGet]
        public ActionResult ResetPassword(string token)
        {
            ViewBag.Title = "Đặt lại mật khẩu";

            if (string.IsNullOrWhiteSpace(token) || !IsValidResetToken(token))
            {
                TempData["Message"] = "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn. Vui lòng gửi lại yêu cầu.";
                return RedirectToAction("ForgotPassword");
            }

            return View(new ResetPasswordViewModel { Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ResetPassword(ResetPasswordViewModel model)
        {
            ViewBag.Title = "Đặt lại mật khẩu";

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();

                    int userId = 0;
                    using (var cmd = new SqlCommand(@"
                        SELECT TOP 1 UserId FROM dbo.DatLaiMatKhau
                        WHERE Token = @Token AND DaSuDung = 0 AND HetHan > GETDATE();", connection))
                    {
                        cmd.Parameters.Add("@Token", SqlDbType.VarChar, 64).Value = model.Token;
                        var val = cmd.ExecuteScalar();
                        if (val == null || val == DBNull.Value)
                        {
                            ModelState.AddModelError("", "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
                            return View(model);
                        }
                        userId = Convert.ToInt32(val);
                    }

                    using (var cmd = new SqlCommand("UPDATE dbo.Users SET PasswordHash = @PasswordHash WHERE UserId = @UserId;", connection))
                    {
                        cmd.Parameters.Add("@PasswordHash", SqlDbType.VarChar, 255).Value = PasswordHasher.Hash(model.NewPassword);
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = new SqlCommand("UPDATE dbo.DatLaiMatKhau SET DaSuDung = 1 WHERE Token = @Token;", connection))
                    {
                        cmd.Parameters.Add("@Token", SqlDbType.VarChar, 64).Value = model.Token;
                        cmd.ExecuteNonQuery();
                    }

                    // Mở khóa đăng nhập cho tài khoản sau khi đổi mật khẩu thành công
                    ResetFailedLoginAttemptsForUserId(connection, userId);
                    AuditLogger.Log(User.Identity.Name ?? "Khách", "Khách", "RESET_PASSWORD", "Đặt lại mật khẩu", userId, "Thành công", Request.UserHostAddress, "Đặt lại mật khẩu thành công");
                }

                TempData["Message"] = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ResetPassword error: " + ex);
                ModelState.AddModelError("", "Không thể đặt lại mật khẩu. Vui lòng thử lại sau.");
                return View(model);
            }
        }

        private bool IsValidResetToken(string token)
        {
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DatLaiMatKhau') SELECT 0;
                        ELSE
                        SELECT COUNT(1) FROM dbo.DatLaiMatKhau
                        WHERE Token = @Token AND DaSuDung = 0 AND HetHan > GETDATE();", connection))
                    {
                        cmd.Parameters.Add("@Token", SqlDbType.VarChar, 64).Value = token;
                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("IsValidResetToken error: " + ex);
                return false;
            }
        }

        private void ResetFailedLoginAttemptsForUserId(SqlConnection connection, int userId)
        {
            try
            {
                using (var cmd = new SqlCommand(@"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DangNhapThatBai')
                    BEGIN
                        DELETE FROM dbo.DangNhapThatBai
                        WHERE TenDangNhap IN (SELECT Username FROM dbo.Users WHERE UserId = @UserId);
                    END", connection))
                {
                    cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ResetFailedLoginAttemptsForUserId error: " + ex);
            }
        }

        private bool IsSmtpConfigured()
        {
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CauHinhHeThong') SELECT 0;
                        ELSE
                        SELECT COUNT(1) FROM dbo.CauHinhHeThong
                        WHERE Khoa IN ('SmtpUser', 'SmtpPass') AND GiaTri IS NOT NULL AND LTRIM(RTRIM(GiaTri)) <> '';", connection))
                    {
                        return Convert.ToInt32(cmd.ExecuteScalar()) >= 2;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("IsSmtpConfigured error: " + ex);
                return false;
            }
        }

        [HttpGet]
        public ActionResult VerifyTwoFactor()
        {
            if (Session["2FA_UserId"] == null || Session["2FA_Secret"] == null)
            {
                return RedirectToAction("Login");
            }

            ViewBag.Title = "Xác thực 2FA";
            return View(new VerifyTwoFactorViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult VerifyTwoFactor(VerifyTwoFactorViewModel model)
        {
            ViewBag.Title = "Xác thực 2FA";

            if (Session["2FA_UserId"] == null || Session["2FA_Secret"] == null)
            {
                return RedirectToAction("Login");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string secret = Session["2FA_Secret"].ToString();
            string userId = Session["2FA_UserId"].ToString();
            string username = Session["2FA_Username"].ToString();
            string roleName = Session["2FA_RoleName"].ToString();
            string fullName = Session["2FA_FullName"].ToString();
            bool rememberMe = Convert.ToBoolean(Session["2FA_RememberMe"] ?? false);
            string returnUrl = Session["2FA_ReturnUrl"] as string;

            if (!TotpHelper.ValidateCode(secret, model.Code))
            {
                AuditLogger.Log(username, roleName, "FAILED_2FA", "Xác thực 2FA", Convert.ToInt32(userId), "Thất bại", Request.UserHostAddress, "Mã OTP 2FA không chính xác");
                ModelState.AddModelError("", "Mã xác thực OTP không chính xác hoặc đã hết hạn.");
                return View(model);
            }

            AuditLogger.Log(username, roleName, "LOGIN_2FA", "Xác thực 2FA", Convert.ToInt32(userId), "Thành công", Request.UserHostAddress, "Xác thực 2FA thành công");

            var ticket = new FormsAuthenticationTicket(
                1,
                username,
                DateTime.Now,
                DateTime.Now.AddMinutes(rememberMe ? 43200 : 30),
                rememberMe,
                userId + "|" + roleName + "|" + fullName,
                FormsAuthentication.FormsCookiePath);

            Response.Cookies.Add(new HttpCookie(
                FormsAuthentication.FormsCookieName,
                FormsAuthentication.Encrypt(ticket)));

            Session.Remove("2FA_UserId");
            Session.Remove("2FA_Username");
            Session.Remove("2FA_RoleName");
            Session.Remove("2FA_FullName");
            Session.Remove("2FA_Secret");
            Session.Remove("2FA_RememberMe");
            Session.Remove("2FA_ReturnUrl");

            if (Url.IsLocalUrl(returnUrl) && IsAllowedReturnUrl(returnUrl, roleName))
            {
                return Redirect(returnUrl);
            }

            return RedirectToRoleHome(roleName);
        }

        [Authorize]
        [HttpGet]
        public ActionResult TwoFactorSetup()
        {
            ViewBag.Title = "Thiết lập Xác thực 2FA";
            int currentUserId = GetCurrentUserId();
            var model = new TwoFactorSetupViewModel();

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand("SELECT ISNULL(IsTwoFactorEnabled, 0), TwoFactorSecret, Username FROM dbo.Users WHERE UserId = @UserId;", connection))
                    {
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                model.IsEnabled = r.GetBoolean(0);
                                string secret = r.IsDBNull(1) ? "" : r.GetString(1);
                                string username = r.GetString(2);

                                if (string.IsNullOrWhiteSpace(secret))
                                {
                                    secret = TotpHelper.GenerateSecretKey();
                                    Session["Setup_2FA_Secret"] = secret;
                                }
                                else
                                {
                                    Session["Setup_2FA_Secret"] = secret;
                                }

                                model.SecretKey = secret;
                                model.QrCodeUrl = TotpHelper.GenerateQrCodeUrl(username, secret);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TwoFactorSetup GET error: " + ex);
            }

            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult TwoFactorSetup(TwoFactorSetupViewModel model)
        {
            ViewBag.Title = "Thiết lập Xác thực 2FA";
            int currentUserId = GetCurrentUserId();
            string secret = Session["Setup_2FA_Secret"] as string;

            if (string.IsNullOrWhiteSpace(secret))
            {
                TempData["TwoFactorError"] = "Phiên thiết lập đã hết hạn. Vui lòng thử lại.";
                return RedirectToAction("TwoFactorSetup");
            }

            model.SecretKey = secret;
            model.QrCodeUrl = TotpHelper.GenerateQrCodeUrl(User.Identity.Name, secret);

            if (string.IsNullOrWhiteSpace(model.VerifyCode))
            {
                ModelState.AddModelError("VerifyCode", "Vui lòng nhập mã OTP 6 chữ số từ ứng dụng Authenticator.");
                return View(model);
            }

            if (!TotpHelper.ValidateCode(secret, model.VerifyCode))
            {
                ModelState.AddModelError("VerifyCode", "Mã OTP xác nhận không đúng. Vui lòng kiểm tra lại đồng hồ thiết bị.");
                return View(model);
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        UPDATE dbo.Users
                        SET IsTwoFactorEnabled = 1, TwoFactorSecret = @Secret
                        WHERE UserId = @UserId;", connection))
                    {
                        cmd.Parameters.Add("@Secret", SqlDbType.NVarChar, 100).Value = secret;
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        cmd.ExecuteNonQuery();
                    }
                }

                Session.Remove("Setup_2FA_Secret");
                TempData["TwoFactorMessage"] = "Đã bật Xác thực 2 Yếu tố (2FA) thành công cho tài khoản của bạn!";
                AuditLogger.Log(User.Identity.Name, "Thành viên", "ENABLE_2FA", "Tài khoản", currentUserId, "Thành công", Request.UserHostAddress, "Kích hoạt thành công xác thực 2FA TOTP");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TwoFactorSetup POST error: " + ex);
                TempData["TwoFactorError"] = "Lỗi khi kích hoạt 2FA.";
            }

            return RedirectToAction("TwoFactorSetup");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DisableTwoFactor()
        {
            int currentUserId = GetCurrentUserId();
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        UPDATE dbo.Users
                        SET IsTwoFactorEnabled = 0, TwoFactorSecret = NULL
                        WHERE UserId = @UserId;", connection))
                    {
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = currentUserId;
                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["TwoFactorMessage"] = "Đã tắt Xác thực 2 Yếu tố (2FA) thành công.";
                AuditLogger.Log(User.Identity.Name, "Thành viên", "DISABLE_2FA", "Tài khoản", currentUserId, "Thành công", Request.UserHostAddress, "Hủy kích hoạt xác thực 2FA");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DisableTwoFactor error: " + ex);
                TempData["TwoFactorError"] = "Lỗi khi tắt 2FA.";
            }

            return RedirectToAction("TwoFactorSetup");
        }

        // Trang "Thông tin cá nhân" cho Bác sĩ / Quản lý tự cập nhật hồ sơ và tải ảnh đại diện hiển thị ở trang chủ.
        [Authorize]
        [HttpGet]
        public ActionResult ThongTinCaNhan()
        {
            int currentUserId = GetCurrentUserId();
            var model = LoadProfile(currentUserId);
            if (model == null) return HttpNotFound("Không tìm thấy tài khoản.");

            PrepareProfileViewBag(model);
            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ThongTinCaNhan(ProfileViewModel model)
        {
            int currentUserId = GetCurrentUserId();
            var existing = LoadProfile(currentUserId);
            if (existing == null) return HttpNotFound("Không tìm thấy tài khoản.");

            // Chỉ cho phép cập nhật chính tài khoản đang đăng nhập; giữ nguyên các trường hệ thống.
            model.UserId = existing.UserId;
            model.Username = existing.Username;
            model.RoleName = existing.RoleName;
            model.IsDoctor = existing.IsDoctor;
            model.MaKhoa = existing.MaKhoa;
            model.KhoaName = existing.KhoaName;
            model.AvatarUrl = existing.AvatarUrl;
            if (!model.IsDoctor)
            {
                // Không phải bác sĩ thì không cập nhật thông tin chuyên môn
                model.SoNamKinhNghiem = existing.SoNamKinhNghiem;
                model.GioiThieu = existing.GioiThieu;
            }

            ValidateProfileUnique(model);
            if (!ModelState.IsValid)
            {
                PrepareProfileViewBag(model);
                return View(model);
            }

            // Xử lý tải ảnh đại diện (nếu có)
            string newAvatarUrl = existing.AvatarUrl;
            if (model.AvatarFile != null && model.AvatarFile.ContentLength > 0)
            {
                string avatarError;
                var savedUrl = SaveAvatar(model.AvatarFile, currentUserId, out avatarError);
                if (savedUrl == null)
                {
                    ModelState.AddModelError("AvatarFile", avatarError);
                    PrepareProfileViewBag(model);
                    return View(model);
                }
                newAvatarUrl = savedUrl;
                model.AvatarUrl = savedUrl;
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var command = new SqlCommand(@"
                        UPDATE dbo.Users
                        SET FullName = @FullName, Email = @Email, Phone = @Phone,
                            GioiTinh = @Gender, NgaySinh = @BirthDate, DiaChi = @Address,
                            AnhDaiDien = @AvatarUrl, SoNamKinhNghiem = @SoNamKinhNghiem, GioiThieu = @GioiThieu
                        WHERE UserId = @UserId;", connection))
                    {
                        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = model.FullName ?? string.Empty;
                        command.Parameters.Add("@Email", SqlDbType.VarChar, 100).Value = NullIfEmpty(model.Email);
                        command.Parameters.Add("@Phone", SqlDbType.VarChar, 20).Value = NullIfEmpty(model.Phone);
                        command.Parameters.Add("@Gender", SqlDbType.NVarChar, 10).Value = NullIfEmpty(model.Gender);
                        command.Parameters.Add("@BirthDate", SqlDbType.Date).Value = (object)model.BirthDate ?? DBNull.Value;
                        command.Parameters.Add("@Address", SqlDbType.NVarChar, 255).Value = NullIfEmpty(model.Address);
                        command.Parameters.Add("@AvatarUrl", SqlDbType.NVarChar, 500).Value = NullIfEmpty(newAvatarUrl);
                        command.Parameters.Add("@SoNamKinhNghiem", SqlDbType.Int).Value = (object)model.SoNamKinhNghiem ?? DBNull.Value;
                        command.Parameters.Add("@GioiThieu", SqlDbType.NVarChar, 500).Value = NullIfEmpty(model.GioiThieu);
                        command.Parameters.Add("@UserId", SqlDbType.Int).Value = model.UserId;
                        command.ExecuteNonQuery();
                    }
                }

                AuditLogger.Log(User.Identity.Name, model.RoleName, "UPDATE", "Hồ sơ cá nhân", model.UserId, "Thành công", Request.UserHostAddress, "Cập nhật thông tin cá nhân");
                TempData["ProfileMessage"] = "Đã cập nhật thông tin cá nhân thành công.";
                return RedirectToAction("ThongTinCaNhan");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ThongTinCaNhan POST error: " + ex);
                ModelState.AddModelError("", "Không thể lưu thông tin. Vui lòng thử lại.");
                PrepareProfileViewBag(model);
                return View(model);
            }
        }

        private ProfileViewModel LoadProfile(int userId)
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                using (var cmd = new SqlCommand(@"
                    SELECT u.UserId, u.Username, r.RoleName, u.FullName, u.Email, u.Phone, u.GioiTinh,
                           u.NgaySinh, u.DiaChi, u.AnhDaiDien, u.MaKhoa, k.TenKhoa, u.SoNamKinhNghiem, u.GioiThieu
                    FROM dbo.Users u
                    INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId
                    LEFT JOIN dbo.Khoa k ON k.MaKhoa = u.MaKhoa
                    WHERE u.UserId = @UserId;", connection))
                {
                    cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read()) return null;
                        var roleName = reader.IsDBNull(2) ? null : reader.GetString(2);
                        return new ProfileViewModel
                        {
                            UserId = reader.GetInt32(0),
                            Username = reader.IsDBNull(1) ? null : reader.GetString(1),
                            RoleName = roleName,
                            FullName = reader.IsDBNull(3) ? null : reader.GetString(3),
                            Email = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Phone = reader.IsDBNull(5) ? null : reader.GetString(5),
                            Gender = reader.IsDBNull(6) ? null : reader.GetString(6),
                            BirthDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                            Address = reader.IsDBNull(8) ? null : reader.GetString(8),
                            AvatarUrl = reader.IsDBNull(9) ? null : reader.GetString(9),
                            MaKhoa = reader.IsDBNull(10) ? (int?)null : reader.GetInt32(10),
                            KhoaName = reader.IsDBNull(11) ? null : reader.GetString(11),
                            SoNamKinhNghiem = reader.IsDBNull(12) ? (int?)null : reader.GetInt32(12),
                            GioiThieu = reader.IsDBNull(13) ? null : reader.GetString(13),
                            IsDoctor = roleName == "Bác sĩ"
                        };
                    }
                }
            }
        }

        private void PrepareProfileViewBag(ProfileViewModel model)
        {
            ViewBag.Title = "Thông tin cá nhân";
            ViewBag.PageTitle = "Thông tin cá nhân";
            ViewBag.UserName = User.Identity.Name;
            ViewBag.RoleName = model != null && !string.IsNullOrWhiteSpace(model.RoleName) ? model.RoleName : "Thành viên";
            if (model != null && model.IsDoctor)
            {
                ViewBag.ActiveDoctorMenu = "ThongTinCaNhan";
            }
            else
            {
                ViewBag.ActiveManagerMenu = "profile";
            }
        }

        private void ValidateProfileUnique(ProfileViewModel model)
        {
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    if (!string.IsNullOrWhiteSpace(model.Email))
                    {
                        using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WHERE UserId <> @UserId AND Email = @Email;", connection))
                        {
                            cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = model.UserId;
                            cmd.Parameters.Add("@Email", SqlDbType.VarChar, 100).Value = model.Email.Trim();
                            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                                ModelState.AddModelError("Email", "Email đã được sử dụng bởi tài khoản khác.");
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(model.Phone))
                    {
                        using (var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WHERE UserId <> @UserId AND Phone = @Phone;", connection))
                        {
                            cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = model.UserId;
                            cmd.Parameters.Add("@Phone", SqlDbType.VarChar, 20).Value = model.Phone.Trim();
                            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                                ModelState.AddModelError("Phone", "Số điện thoại đã được sử dụng bởi tài khoản khác.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ValidateProfileUnique error: " + ex);
            }
        }

        private string SaveAvatar(HttpPostedFileBase file, int userId, out string error)
        {
            error = null;
            try
            {
                var extension = Path.GetExtension(file.FileName);
                extension = string.IsNullOrEmpty(extension) ? string.Empty : extension.ToLowerInvariant();
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                if (Array.IndexOf(allowed, extension) < 0)
                {
                    error = "Chỉ chấp nhận ảnh định dạng JPG, PNG, GIF hoặc WEBP.";
                    return null;
                }
                if (file.ContentLength > 2 * 1024 * 1024)
                {
                    error = "Ảnh đại diện không được vượt quá 2MB.";
                    return null;
                }

                var folder = Server.MapPath("~/Content/Images");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var fileName = string.Format("user{0}_{1:yyyyMMddHHmmssfff}{2}", userId, DateTime.Now, extension);
                file.SaveAs(Path.Combine(folder, fileName));
                return Url.Content("~/Content/Images/" + fileName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SaveAvatar error: " + ex);
                error = "Không thể lưu ảnh đại diện. Vui lòng thử lại.";
                return null;
            }
        }

        private static object NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim();
        }

        private static bool IsLocalLoopback(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return true;
            ip = ip.Trim();
            return ip == "::1" || ip == "127.0.0.1" || ip == "localhost" || ip.StartsWith("127.");
        }

        private bool IsIpBlocked(string ip, string identity = null)
        {
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();

                    // Tự động mở khóa cho IP Loopback local (::1, 127.0.0.1) trong môi trường Development
                    if (IsLocalLoopback(ip) || Request.IsLocal)
                    {
                        using (var unblockCmd = new SqlCommand("UPDATE dbo.IpBlacklist SET IsActive = 0 WHERE IpAddress IN ('::1', '127.0.0.1', 'localhost');", connection))
                        {
                            unblockCmd.ExecuteNonQuery();
                        }
                    }

                    // 1. Kiểm tra khóa IP toàn cục (dành riêng cho IP public bên ngoài)
                    if (!IsLocalLoopback(ip))
                    {
                        using (var cmd = new SqlCommand(@"
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
                            END

                            SELECT COUNT(1) FROM dbo.IpBlacklist
                            WHERE IpAddress = @Ip AND IsActive = 1 AND (ThoiHanMo IS NULL OR ThoiHanMo > GETDATE());", connection))
                        {
                            cmd.Parameters.Add("@Ip", SqlDbType.VarChar, 50).Value = ip;
                            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0) return true;
                        }
                    }

                    // 2. Giới hạn đăng nhập theo từng Tài khoản cụ thể (Chống dò mật khẩu theo Account)
                    if (!string.IsNullOrWhiteSpace(identity))
                    {
                        using (var accountCmd = new SqlCommand(@"
                            IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DangNhapThatBai')
                            BEGIN
                                SELECT COUNT(1) FROM dbo.DangNhapThatBai
                                WHERE TenDangNhap = @Identity AND ThoiGian >= DATEADD(minute, -15, GETDATE());
                            END
                            ELSE SELECT 0;", connection))
                        {
                            accountCmd.Parameters.Add("@Identity", SqlDbType.NVarChar, 100).Value = identity.Trim();
                            int failedAccountAttempts = Convert.ToInt32(accountCmd.ExecuteScalar());
                            if (failedAccountAttempts >= 5)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("IsIpBlocked error: " + ex);
            }
            return false;
        }

        private int RecordFailedLoginAttempt(string ip, string identity)
        {
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DangNhapThatBai')
                        BEGIN
                            CREATE TABLE DangNhapThatBai (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                IpAddress VARCHAR(50) NOT NULL,
                                ThoiGian DATETIME NOT NULL DEFAULT GETDATE(),
                                TenDangNhap NVARCHAR(100) NULL
                            );
                        END

                        INSERT INTO dbo.DangNhapThatBai (IpAddress, ThoiGian, TenDangNhap) VALUES (@Ip, GETDATE(), @Identity);

                        SELECT COUNT(1) FROM dbo.DangNhapThatBai
                        WHERE TenDangNhap = @Identity AND ThoiGian >= DATEADD(minute, -15, GETDATE());", connection))
                    {
                        cmd.Parameters.Add("@Ip", SqlDbType.VarChar, 50).Value = ip;
                        cmd.Parameters.Add("@Identity", SqlDbType.NVarChar, 100).Value = (object)identity ?? DBNull.Value;
                        int failedAccountCount = Convert.ToInt32(cmd.ExecuteScalar());

                        // Nếu là IP public bên ngoài dò mật khẩu nhiều tài khoản khác nhau (>= 10 tài khoản), mới autolock IP
                        if (!IsLocalLoopback(ip) && !Request.IsLocal)
                        {
                            using (var stuffingCmd = new SqlCommand(@"
                                SELECT COUNT(DISTINCT TenDangNhap) FROM dbo.DangNhapThatBai
                                WHERE IpAddress = @Ip AND ThoiGian >= DATEADD(minute, -15, GETDATE());", connection))
                            {
                                stuffingCmd.Parameters.Add("@Ip", SqlDbType.VarChar, 50).Value = ip;
                                int distinctUserCount = Convert.ToInt32(stuffingCmd.ExecuteScalar());

                                if (distinctUserCount >= 10)
                                {
                                    using (var lockCmd = new SqlCommand(@"
                                        INSERT INTO dbo.IpBlacklist (IpAddress, LyDo, ThoiGianKhoa, ThoiHanMo, IsActive)
                                        VALUES (@Ip, N'Tự động khóa: Phát hiện dò mật khẩu nhiều tài khoản (Credential Stuffing)', GETDATE(), DATEADD(minute, 15, GETDATE()), 1);", connection))
                                    {
                                        lockCmd.Parameters.Add("@Ip", SqlDbType.VarChar, 50).Value = ip;
                                        lockCmd.ExecuteNonQuery();
                                    }
                                }
                            }
                        }

                        return failedAccountCount;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RecordFailedLoginAttempt error: " + ex);
                return 1;
            }
        }

        private void ResetFailedLoginAttempts(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return;
            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand("IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DangNhapThatBai') DELETE FROM dbo.DangNhapThatBai WHERE TenDangNhap = @Identity;", connection))
                    {
                        cmd.Parameters.Add("@Identity", SqlDbType.NVarChar, 100).Value = identity.Trim();
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ResetFailedLoginAttempts error: " + ex);
            }
        }

        private int GetCurrentUserId()
        {
            if (!User.Identity.IsAuthenticated) return 0;

            var formsIdentity = User.Identity as FormsIdentity;
            if (formsIdentity != null && formsIdentity.Ticket != null && !string.IsNullOrEmpty(formsIdentity.Ticket.UserData))
            {
                var parts = formsIdentity.Ticket.UserData.Split('|');
                if (parts.Length > 0 && int.TryParse(parts[0], out int id) && id > 0)
                {
                    return id;
                }
            }

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = new SqlCommand("SELECT TOP 1 UserId FROM dbo.Users WHERE Username = @Identity OR Email = @Identity OR Phone = @Identity;", connection))
                    {
                        cmd.Parameters.Add("@Identity", SqlDbType.NVarChar, 100).Value = User.Identity.Name ?? "";
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

        private void PreventResponseCaching()
        {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            Response.Cache.SetExpires(DateTime.UtcNow.AddHours(-1));
            Response.Cache.SetRevalidation(HttpCacheRevalidation.AllCaches);
            Response.Headers["Pragma"] = "no-cache";
        }

        private void SignOutCurrentUser()
        {
            FormsAuthentication.SignOut();
            HttpContext.User = new System.Security.Principal.GenericPrincipal(
                new System.Security.Principal.GenericIdentity(string.Empty),
                new string[0]);

            Session.Clear();
            Session.Abandon();

            var authCookie = new HttpCookie(FormsAuthentication.FormsCookieName, string.Empty)
            {
                Expires = DateTime.Now.AddDays(-1),
                Path = FormsAuthentication.FormsCookiePath
            };
            Response.Cookies.Add(authCookie);

            var sessionCookie = new HttpCookie("ASP.NET_SessionId", string.Empty)
            {
                Expires = DateTime.Now.AddDays(-1),
                Path = "/"
            };
            Response.Cookies.Add(sessionCookie);
        }

        private SqlConnection CreateConnection()
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ConfigurationErrorsException("Thiếu connection string MediCareDb.");
            }

            return new SqlConnection(connectionString);
        }

        private ActionResult RedirectToRoleHome(string roleName)
        {
            if (string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Admin hệ thống", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Quản trị viên", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Admin");
            }

            if (string.Equals(roleName, "Quản lý phòng khám", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Quản lý", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "ClinicManager");
            }

            if (string.Equals(roleName, "Bác sĩ", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Doctor");
            }

            return RedirectToAction("Index", "Home");
        }

        private bool IsAllowedReturnUrl(string returnUrl, string roleName)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                return false;
            }

            var normalized = returnUrl.Trim();
            var relative = normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized;

            if (string.Equals(roleName, "Bác sĩ", StringComparison.OrdinalIgnoreCase) ||
                roleName.IndexOf("bac si", StringComparison.OrdinalIgnoreCase) >= 0 ||
                roleName.IndexOf("bác sĩ", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return relative.StartsWith("/Doctor", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(roleName, "Quản lý phòng khám", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Quản lý", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Admin hệ thống", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(roleName, "Quản trị viên", StringComparison.OrdinalIgnoreCase))
            {
                return relative.StartsWith("/ClinicManager", StringComparison.OrdinalIgnoreCase) ||
                       relative.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase);
            }

            return relative.StartsWith("/Home", StringComparison.OrdinalIgnoreCase) ||
                   relative.StartsWith("/Account", StringComparison.OrdinalIgnoreCase) ||
                   relative.StartsWith("/", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("/Doctor", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("/ClinicManager", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet]
        public ActionResult SeedTestAccounts()
        {
            if (!Request.IsLocal || !HttpContext.IsDebuggingEnabled)
            {
                return HttpNotFound();
            }

            var accounts = new[]
            {
                new { Username = "admin", FullName = "Quản trị viên kiểm thử", RoleName = "Admin hệ thống" },
                new { Username = "manager", FullName = "Quản lý phòng khám kiểm thử", RoleName = "Quản lý phòng khám" },
                new { Username = "bs_nam", FullName = "Bác sĩ Nam kiểm thử", RoleName = "Bác sĩ" },
                new { Username = "bs_hoa", FullName = "Bác sĩ Hoa kiểm thử", RoleName = "Bác sĩ" }
            };

            try
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            foreach (var account in accounts)
                            {
                                using (var command = new SqlCommand(@"
                            DECLARE @RoleId INT;
                            SELECT @RoleId = RoleId
                            FROM dbo.Roles
                            WHERE RoleName = @RoleName;

                            IF @RoleId IS NULL
                            BEGIN
                                INSERT INTO dbo.Roles (RoleName, MoTa)
                                VALUES (@RoleName, N'Role kiểm thử MediCare');

                                SET @RoleId = CONVERT(INT, SCOPE_IDENTITY());
                            END;

                            IF @RoleId IS NULL
                            BEGIN
                                RAISERROR (N'Không thể tạo hoặc tìm role kiểm thử.', 16, 1);
                                RETURN;
                            END;

                            IF EXISTS (SELECT 1 FROM dbo.Users WHERE Username = @Username)
                            BEGIN
                                UPDATE dbo.Users
                                SET PasswordHash = @PasswordHash
                                WHERE Username = @Username;
                            END
                            ELSE
                            BEGIN
                                INSERT INTO dbo.Users
                                    (Username, PasswordHash, FullName, RoleId, IsActive)
                                VALUES
                                    (@Username, @PasswordHash, @FullName, @RoleId, 1);
                            END;", connection, transaction))
                                {
                                    command.Parameters.Add("@Username", SqlDbType.VarChar, 50).Value = account.Username;
                                    command.Parameters.Add("@PasswordHash", SqlDbType.VarChar, 255).Value = PasswordHasher.Hash("123");
                                    command.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = account.FullName;
                                    command.Parameters.Add("@RoleName", SqlDbType.NVarChar, 100).Value = account.RoleName;
                                    command.ExecuteNonQuery();
                                }
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

                return Content("Đã đồng bộ 4 tài khoản kiểm thử. Mật khẩu không được lưu dạng plaintext.");
            }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is SqlException || ex is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine("SeedTestAccounts error: " + ex);
                if (HttpContext.IsDebuggingEnabled)
                {
                    return Content("SeedTestAccounts lỗi: " + ex.GetBaseException().Message, "text/plain");
                }

                return new HttpStatusCodeResult(500, "Không thể seed tài khoản kiểm thử.");
            }
        }
    }
}
