using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

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
    }
}
