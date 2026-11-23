using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Mvc;
using Newtonsoft.Json;
using MediCare.Models;

namespace MediCare.Controllers
{
    public class AiController : Controller
    {
        private readonly string connectionString = ConfigurationManager.ConnectionStrings["MediCareDb"]?.ConnectionString;

        /// <summary>
        /// Tiếp nhận mô tả triệu chứng, trả về chẩn đoán sơ bộ + lời khuyên + bác sĩ/chuyên khoa phù hợp.
        /// Ưu tiên gọi OpenAI khi có API Key; nếu chưa cấu hình key (hoặc gọi lỗi) sẽ tự động
        /// dùng bộ luật chẩn đoán cục bộ để tính năng vẫn hoạt động khi demo.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ProcessSymptom(AiChatRequestViewModel model)
        {
            var response = new AiChatResponseViewModel();

            if (model == null || string.IsNullOrWhiteSpace(model.SymptomText))
            {
                response.Success = false;
                response.ErrorMessage = "Vui lòng nhập mô tả triệu chứng sức khỏe.";
                return Json(response);
            }

            string text = model.SymptomText.Trim();
            string normalized = RemoveDiacritics(text).ToLowerInvariant();

            try
            {
                // 1. Cảnh báo cấp cứu (red flags) - luôn kiểm tra trước khi gọi AI
                if (ContainsAny(normalized, EmergencyKeywords))
                {
                    response.IsEmergency = true;
                    response.DiagnosisSummary = "CẢNH BÁO CẤP CỨU: Triệu chứng bạn mô tả có thể là dấu hiệu của tình trạng nguy hiểm (tim mạch cấp, đột quỵ hoặc suy hô hấp cấp).";
                    response.DepartmentName = "Khoa Tim Mạch";
                    response.HealthAdvice = "Hãy gọi cấp cứu 115 hoặc đến cơ sở y tế gần nhất NGAY. Không nên chờ đợi đặt lịch khám thông thường.";
                    response.SuggestedReason = BuildReason(text, "Cảnh báo cấp cứu - cần thăm khám ngay");
                    LoadRecommendedDoctors(response, "Tim Mạch");
                    return Json(response);
                }

                // 2. Lấy kết quả từ OpenAI, nếu không được thì dùng bộ luật cục bộ
                AiTriageResult triage = null;
                string apiKey = GetOpenAiApiKey();

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    try
                    {
                        string rawAiText = await CallOpenAiAsync(apiKey, text, model.History);
                        triage = ParseAiResult(rawAiText);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("OpenAI call failed, dùng bộ luật cục bộ: " + ex.Message);
                    }
                }

                if (triage == null)
                {
                    triage = LocalTriage(normalized);
                    response.IsOfflineFallback = true;
                }

                response.DiagnosisSummary = triage.Diagnosis;
                response.HealthAdvice = triage.Advice;
                response.SuggestedReason = BuildReason(text, triage.Diagnosis);

                // 3. Gợi ý bác sĩ thật trong CSDL theo chuyên khoa vừa xác định
                LoadRecommendedDoctors(response, triage.DepartmentKeyword);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ProcessSymptom error: " + ex);
                response.Success = false;
                response.ErrorMessage = "Không thể xử lý yêu cầu tư vấn. Vui lòng thử lại sau.";
            }

            return Json(response);
        }

        // ============================ BỘ LUẬT CHẨN ĐOÁN CỤC BỘ ============================

        private static readonly string[] EmergencyKeywords = {
            "dau nguc", "tuc nguc", "kho tho nang", "bat tinh", "ngat", "dot quy",
            "liet", "co giat", "chay mau xoi xa", "nong nguc", "kho noi"
        };

        private class TriageRule
        {
            public string DepartmentKeyword;
            public string[] Keywords;
            public string Diagnosis;
            public string Advice;
        }

        private static readonly List<TriageRule> LocalRules = new List<TriageRule>
        {
            new TriageRule
            {
                DepartmentKeyword = "Nhi",
                Keywords = new[] { "tre", "tre em", "be", "em be", "con toi", "so sinh", "chau", "be nha", "nhi" },
                Diagnosis = "Tình trạng của bé có thể liên quan đến bệnh lý nhi khoa thường gặp (sốt virus, viêm hô hấp trên, rối loạn tiêu hóa ở trẻ).",
                Advice = "Cho bé nghỉ ngơi, uống đủ nước, theo dõi nhiệt độ mỗi 4 giờ. Đưa bé đi khám ngay nếu sốt trên 39°C, bỏ ăn, lơ mơ hoặc khó thở."
            },
            new TriageRule
            {
                DepartmentKeyword = "Tim Mạch",
                Keywords = new[] { "tim", "huyet ap", "hoi hop", "danh trong nguc", "dau nguc", "phu chan", "mo mau", "cholesterol", "mach", "kho tho khi nam" },
                Diagnosis = "Các triệu chứng gợi ý bệnh lý tim mạch như rối loạn nhịp, tăng huyết áp hoặc thiếu máu cơ tim (cần đo điện tim để xác định).",
                Advice = "Hạn chế muối, cà phê và thuốc lá, tránh gắng sức. Đến cơ sở y tế ngay nếu đau ngực lan tay trái, vã mồ hôi hoặc khó thở khi nghỉ."
            },
            new TriageRule
            {
                DepartmentKeyword = "Tai Mũi Họng",
                Keywords = new[] { "hong", "mui", "xoang", "u tai", "nghet mui", "so mui", "ho", "amidan", "khan tieng", "dau tai", "chay tai", "rat hong" },
                Diagnosis = "Tình trạng có thể liên quan đến bệnh lý tai mũi họng: viêm họng, viêm amidan, viêm mũi xoang hoặc viêm tai giữa.",
                Advice = "Súc miệng nước muối sinh lý, uống nhiều nước ấm, giữ ấm cổ họng. Đi khám sớm nếu sốt cao trên 3 ngày, đau tai hoặc khó nuốt."
            },
            new TriageRule
            {
                DepartmentKeyword = "Ngoại",
                Keywords = new[] { "xuong", "khop", "lung", "gay xuong", "vet thuong", "sung", "thoat vi", "chan thuong", "khoi u", "dau vai", "bong gao", "chay mau", "bong" },
                Diagnosis = "Triệu chứng có thể liên quan đến bệnh lý cơ xương khớp hoặc ngoại khoa (chấn thương, thoái hóa khớp, đau lưng, viêm mô mềm).",
                Advice = "Hạn chế vận động mạnh vùng đau, chườm ấm và nghỉ ngơi hợp lý. Đi khám ngay nếu có vết thương hở, biến dạng chi hoặc đau tăng dần."
            },
            new TriageRule
            {
                DepartmentKeyword = "Nội",
                Keywords = new[] { "da day", "dau bung", "tieu chay", "buon non", "non", "dau dau", "sot", "met", "tieu duong", "gan", "than", "chong mat", "man ngua", "dai trang", "tao bon", "day hoi", "an khong tieu" },
                Diagnosis = "Triệu chứng có thể liên quan đến bệnh lý nội khoa tổng quát (tiêu hóa, hô hấp, thần kinh hoặc rối loạn chuyển hóa).",
                Advice = "Ăn uống thanh đạm, nghỉ ngơi đầy đủ, uống đủ nước. Đi khám sớm nếu sốt kéo dài, sụt cân, đau bụng dữ dội hoặc khó thở."
            }
        };

        /// <summary>Chẩn đoán mặc định khi không có từ khoá nào khớp.</summary>
        private static readonly TriageRule DefaultRule = new TriageRule
        {
            DepartmentKeyword = "Nội",
            Diagnosis = "Triệu chứng bạn mô tả chưa đủ đặc trưng để xác định bệnh lý cụ thể, cần bác sĩ thăm khám trực tiếp để chẩn đoán chính xác.",
            Advice = "Theo dõi diễn biến triệu chứng, nghỉ ngơi và uống đủ nước. Đặt lịch khám nội tổng quát để được kiểm tra toàn diện."
        };

        private AiTriageResult LocalTriage(string normalizedText)
        {
            foreach (var rule in LocalRules)
            {
                if (ContainsAny(normalizedText, rule.Keywords))
                {
                    return new AiTriageResult
                    {
                        DepartmentKeyword = rule.DepartmentKeyword,
                        Diagnosis = rule.Diagnosis,
                        Advice = rule.Advice
                    };
                }
            }

            return new AiTriageResult
            {
                DepartmentKeyword = DefaultRule.DepartmentKeyword,
                Diagnosis = DefaultRule.Diagnosis,
                Advice = DefaultRule.Advice
            };
        }

        private class AiTriageResult
        {
            public string Diagnosis { get; set; }
            public string Advice { get; set; }
            public string DepartmentKeyword { get; set; }
        }

        // ============================ GỌI OPENAI ============================

        private static string SystemPrompt =
            "Bạn là trợ lý AI y khoa của phòng khám đa khoa MediCare (Việt Nam). Nhiệm vụ: phân tích triệu chứng người bệnh mô tả, " +
            "đưa ra chẩn đoán SƠ BỘ mang tính tham khảo, lời khuyên chăm sóc sức khỏe và chọn chuyên khoa phù hợp nhất.\n" +
            "departmentName CHỈ được nhận một trong các giá trị sau (ghi y nguyên): \"Khoa Nội\", \"Khoa Ngoại\", \"Khoa Nhi\", \"Khoa Tai Mũi Họng\", \"Khoa Tim Mạch\".\n" +
            "Chỉ trả về DUY NHẤT một đối tượng JSON, không kèm markdown, không giải thích thêm, gồm 3 trường:\n" +
            "{\n" +
            "  \"diagnosisSummary\": \"chẩn đoán sơ bộ bằng tiếng Việt, 2-3 câu\",\n" +
            "  \"departmentName\": \"một trong các chuyên khoa ở trên\",\n" +
            "  \"healthAdvice\": \"lời khuyên chăm sóc và dấu hiệu cần đi khám ngay, 2-3 câu tiếng Việt\"\n" +
            "}";

        private string GetOpenAiApiKey()
        {
            string key = ConfigurationManager.AppSettings["OpenAI_ApiKey"];

            if (IsPlaceholderKey(key) && !string.IsNullOrWhiteSpace(connectionString))
            {
                try
                {
                    using (var connection = new SqlConnection(connectionString))
                    {
                        connection.Open();
                        using (var cmd = new SqlCommand("SELECT GiaTri FROM dbo.CauHinhHeThong WHERE Khoa = N'OpenAiApiKey';", connection))
                        {
                            var val = cmd.ExecuteScalar();
                            if (val != null && val != DBNull.Value)
                            {
                                string dbKey = val.ToString().Trim();
                                if (!IsPlaceholderKey(dbKey)) key = dbKey;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Không đọc được OpenAiApiKey từ CSDL: " + ex.Message);
                }
            }

            return IsPlaceholderKey(key) ? null : key.Trim();
        }

        private static bool IsPlaceholderKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return true;
            key = key.Trim();
            if (key.Equals("YOUR_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.Equals("YOUR_REAL_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.Equals("DÁN_KHÓA_API_THẬT_CỦA_BẠN_TẠI_ĐÂY", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private async Task<string> CallOpenAiAsync(string apiKey, string latestMessage, IList<AiMessageContextViewModel> history)
        {
            string endpoint = ConfigurationManager.AppSettings["OpenAI_BaseUrl"];
            if (string.IsNullOrWhiteSpace(endpoint)) endpoint = "https://api.openai.com/v1/chat/completions";

            string model = ConfigurationManager.AppSettings["OpenAI_Model"];
            if (string.IsNullOrWhiteSpace(model)) model = "gpt-4o-mini";

            var messages = new List<object> { new { role = "system", content = SystemPrompt } };

            // Chỉ gửi 4 lượt hội thoại gần nhất để tiết kiệm token
            if (history != null && history.Count > 0)
            {
                var validHistory = history.Where(h => !string.IsNullOrWhiteSpace(h.Message)).ToList();
                var recentHistory = validHistory.Skip(Math.Max(0, validHistory.Count - 4)).ToList();
                foreach (var h in recentHistory)
                {
                    string role = string.Equals(h.Sender, "user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant";
                    messages.Add(new { role = role, content = h.Message.Trim() });
                }
            }

            messages.Add(new { role = "user", content = latestMessage });

            var payload = new { model = model, messages = messages, temperature = 0.4 };
            string jsonPayload = JsonConvert.SerializeObject(payload);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);

                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    request.Headers.Add("Authorization", "Bearer " + apiKey);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    HttpResponseMessage httpResponse;
                    try
                    {
                        httpResponse = await client.SendAsync(request);
                    }
                    catch (TaskCanceledException)
                    {
                        throw new Exception("Kết nối đến OpenAI bị quá thời gian chờ (30s).");
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new Exception("Không thể kết nối đến OpenAI: " + ex.Message);
                    }

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        int statusCode = (int)httpResponse.StatusCode;
                        if (statusCode == 401 || statusCode == 403)
                            throw new Exception("API Key OpenAI không hợp lệ hoặc bị từ chối (mã " + statusCode + ").");
                        if (statusCode == 429)
                            throw new Exception("OpenAI đã vượt quá giới hạn lượt gọi (mã 429).");

                        throw new Exception("OpenAI trả về lỗi HTTP " + statusCode + ".");
                    }

                    string respString = await httpResponse.Content.ReadAsStringAsync();
                    var openAiResp = JsonConvert.DeserializeObject<OpenAiChatResponse>(respString);
                    return openAiResp?.choices?.FirstOrDefault()?.message?.content;
                }
            }
        }

        private AiTriageResult ParseAiResult(string rawAiText)
        {
            if (string.IsNullOrWhiteSpace(rawAiText)) return null;

            string cleanJson = rawAiText.Replace("```json", "").Replace("```", "").Trim();
            int firstBrace = cleanJson.IndexOf('{');
            int lastBrace = cleanJson.LastIndexOf('}');
            if (firstBrace < 0 || lastBrace <= firstBrace) return null;
            cleanJson = cleanJson.Substring(firstBrace, lastBrace - firstBrace + 1);

            AiJsonResult parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<AiJsonResult>(cleanJson);
            }
            catch
            {
                return null;
            }

            if (parsed == null || string.IsNullOrWhiteSpace(parsed.DiagnosisSummary)) return null;

            return new AiTriageResult
            {
                Diagnosis = parsed.DiagnosisSummary.Trim(),
                Advice = string.IsNullOrWhiteSpace(parsed.HealthAdvice)
                    ? "Vui lòng đến khám trực tiếp để bác sĩ thăm khám chi tiết."
                    : parsed.HealthAdvice.Trim(),
                DepartmentKeyword = MapDepartmentKeyword(parsed.DepartmentName)
            };
        }

        /// <summary>Chuẩn hoá tên chuyên khoa do AI trả về về từ khoá khớp với bảng Khoa trong CSDL.</summary>
        private static string MapDepartmentKeyword(string departmentName)
        {
            string name = RemoveDiacritics(departmentName ?? "").ToLowerInvariant();

            if (name.Contains("tim mach")) return "Tim Mạch";
            if (name.Contains("tai mui hong")) return "Tai Mũi Họng";
            if (name.Contains("nhi")) return "Nhi";
            if (name.Contains("ngoai")) return "Ngoại";
            return "Nội";
        }

        // ============================ GỢI Ý BÁC SĨ TỪ CSDL ============================

        private void LoadRecommendedDoctors(AiChatResponseViewModel response, string departmentKeyword)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return;

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                int departmentId = 0;
                string departmentName = null;

                using (var cmd = new SqlCommand(@"
                    SELECT TOP 1 MaKhoa, TenKhoa
                    FROM dbo.Khoa
                    WHERE TenKhoa LIKE @Keyword
                    ORDER BY MaKhoa;", connection))
                {
                    cmd.Parameters.Add("@Keyword", SqlDbType.NVarChar, 100).Value = "%" + (departmentKeyword ?? "") + "%";
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            departmentId = r.GetInt32(0);
                            departmentName = r.GetString(1);
                        }
                    }
                }

                // Không tìm thấy chuyên khoa phù hợp thì mặc định về Khoa Nội
                if (departmentId == 0)
                {
                    using (var cmd = new SqlCommand("SELECT TOP 1 MaKhoa, TenKhoa FROM dbo.Khoa WHERE TenKhoa LIKE N'%Nội%' ORDER BY MaKhoa;", connection))
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            departmentId = r.GetInt32(0);
                            departmentName = r.GetString(1);
                        }
                    }
                }

                response.DepartmentId = departmentId;
                if (!string.IsNullOrWhiteSpace(departmentName))
                {
                    response.DepartmentName = departmentName;
                }
                else if (string.IsNullOrWhiteSpace(response.DepartmentName))
                {
                    response.DepartmentName = "Khoa Nội";
                }

                using (var cmd = new SqlCommand(@"
                    SELECT TOP 4 u.UserId, u.FullName, ISNULL(k.TenKhoa, N'Khám tổng quát'), ISNULL(k.MaKhoa, 0)
                    FROM dbo.Users u
                    INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId
                    LEFT JOIN dbo.Khoa k ON k.MaKhoa = u.MaKhoa
                    WHERE r.RoleName = N'Bác sĩ' AND u.IsActive = 1
                    ORDER BY CASE WHEN u.MaKhoa = @DepartmentId THEN 0 ELSE 1 END,
                             ISNULL(u.DiemDanhGia, 0) DESC,
                             u.FullName;", connection))
                {
                    cmd.Parameters.Add("@DepartmentId", SqlDbType.Int).Value = departmentId;

                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            response.RecommendedDoctors.Add(new AiDoctorRecommendationViewModel
                            {
                                DoctorId = r.GetInt32(0),
                                DoctorName = r.GetString(1),
                                DepartmentName = r.GetString(2),
                                DepartmentId = r.GetInt32(3)
                            });
                        }
                    }
                }
            }
        }

        // ============================ TIỆN ÍCH ============================

        private static string BuildReason(string symptomText, string diagnosis)
        {
            string symptom = symptomText.Trim();
            if (symptom.Length > 220) symptom = symptom.Substring(0, 220).Trim() + "...";

            string reason = "Triệu chứng: " + symptom + " | Chẩn đoán sơ bộ (AI): " + (diagnosis ?? "").Trim();
            if (reason.Length > 500) reason = reason.Substring(0, 497).Trim() + "...";
            return reason;
        }

        private static bool ContainsAny(string normalizedText, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (Regex.IsMatch(normalizedText, @"\b" + Regex.Escape(keyword) + @"\b"))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Bỏ dấu tiếng Việt để so khớp từ khoá linh hoạt (có dấu hay không dấu đều nhận).</summary>
        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = text.Replace('đ', 'd').Replace('Đ', 'D');
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private class AiJsonResult
        {
            [JsonProperty("diagnosisSummary")]
            public string DiagnosisSummary { get; set; }
            [JsonProperty("departmentName")]
            public string DepartmentName { get; set; }
            [JsonProperty("healthAdvice")]
            public string HealthAdvice { get; set; }
        }

        private class OpenAiChatResponse
        {
            public List<OpenAiChoice> choices { get; set; }
        }

        private class OpenAiChoice
        {
            public OpenAiMessage message { get; set; }
        }

        private class OpenAiMessage
        {
            public string content { get; set; }
        }
    }
}
