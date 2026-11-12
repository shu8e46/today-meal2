using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TodayMeal
{
    public class School
    {
        public string OfficeCode = "";   // 시도교육청코드
        public string OfficeName = "";
        public string Code = "";         // 행정표준코드
        public string Name = "";
        public string Kind = "";
        public string Address = "";
    }

    public class Meal
    {
        public DateTime Date;
        public int Type;                 // 1 조식, 2 중식, 3 석식
        public string TypeName = "";
        public List<string> Dishes = new List<string>();
        public string Calories = "";
    }

    /// <summary>나이스가 오류 메시지를 돌려준 경우.</summary>
    public class NeisException : Exception
    {
        public NeisException(string message) : base(message) { }
    }

    /// <summary>나이스 교육정보 개방 포털 (open.neis.go.kr) 호출.</summary>
    public static class Neis
    {
        const string Key = "6e2bad39ab46406e9bd1e39183850d27";
        const string Base = "https://open.neis.go.kr/hub/";
        const int PageSize = 1000;

        static readonly HttpClient Http = MakeClient();

        // "돈육김치찌개 (5.9.10.13)" 처럼 붙어 오는 알레르기 번호
        static readonly Regex AllergyParen = new Regex(@"\(\s*\d{1,2}(\s*[.,]\s*\d{1,2})*\s*[.,]?\s*\)", RegexOptions.Compiled);
        // 괄호 없이 "돈육김치찌개5.9.10.13." 으로 오는 학교도 있다
        static readonly Regex AllergyTail = new Regex(@"(\d{1,2}\.)+\d{0,2}\s*$", RegexOptions.Compiled);
        static readonly Regex Spaces = new Regex(@"\s{2,}", RegexOptions.Compiled);
        static readonly Regex LineBreak = new Regex(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static HttpClient MakeClient()
        {
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(15);
            return c;
        }

        public static async Task<List<School>> SearchSchoolsAsync(string name)
        {
            string url = Base + "schoolInfo?KEY=" + Key + "&Type=json&pIndex=1&pSize=50&SCHUL_NM=" + Uri.EscapeDataString(name);
            string json = await Http.GetStringAsync(url).ConfigureAwait(false);

            int total;
            var list = new List<School>();
            foreach (Dictionary<string, object> r in Rows(json, "schoolInfo", out total))
            {
                var s = new School();
                s.OfficeCode = Str(r, "ATPT_OFCDC_SC_CODE");
                s.OfficeName = Str(r, "ATPT_OFCDC_SC_NM");
                s.Code = Str(r, "SD_SCHUL_CODE");
                s.Name = Str(r, "SCHUL_NM");
                s.Kind = Str(r, "SCHUL_KND_SC_NM");
                s.Address = Str(r, "ORG_RDNMA");
                if (s.Code.Length > 0 && s.OfficeCode.Length > 0) list.Add(s);
            }
            return list;
        }

        public static async Task<List<Meal>> GetMealsAsync(School school, DateTime from, DateTime to)
        {
            var meals = new List<Meal>();
            int page = 1;
            while (true)
            {
                string url = Base + "mealServiceDietInfo?KEY=" + Key + "&Type=json&pIndex=" + page + "&pSize=" + PageSize
                    + "&ATPT_OFCDC_SC_CODE=" + Uri.EscapeDataString(school.OfficeCode)
                    + "&SD_SCHUL_CODE=" + Uri.EscapeDataString(school.Code)
                    + "&MLSV_FROM_YMD=" + from.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                    + "&MLSV_TO_YMD=" + to.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                string json = await Http.GetStringAsync(url).ConfigureAwait(false);

                int total;
                List<Dictionary<string, object>> rows = Rows(json, "mealServiceDietInfo", out total);
                foreach (Dictionary<string, object> r in rows)
                {
                    Meal m = ParseMeal(r);
                    if (m != null) meals.Add(m);
                }
                if (rows.Count == 0 || page * PageSize >= total) break;
                page++;
            }
            return meals;
        }

        /// <summary>화면에 보여 줄 오류 문장.</summary>
        public static string Describe(Exception ex)
        {
            if (ex is NeisException) return ex.Message;
            return "불러오지 못했습니다. 인터넷 연결을 확인한 뒤 새로고침을 눌러 주세요.";
        }

        static Meal ParseMeal(Dictionary<string, object> r)
        {
            DateTime date;
            if (!DateTime.TryParseExact(Str(r, "MLSV_YMD"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return null;

            var m = new Meal();
            m.Date = date;
            int type;
            m.Type = int.TryParse(Str(r, "MMEAL_SC_CODE"), out type) ? type : 2;
            m.TypeName = Str(r, "MMEAL_SC_NM");
            m.Calories = Str(r, "CAL_INFO").Replace("Kcal", "kcal").Trim();

            foreach (string raw in LineBreak.Split(Str(r, "DDISH_NM")))
            {
                string dish = CleanDish(raw);
                if (dish.Length > 0) m.Dishes.Add(dish);
            }
            return m.Dishes.Count > 0 ? m : null;
        }

        static string CleanDish(string raw)
        {
            string s = WebUtility.HtmlDecode(raw);
            s = AllergyParen.Replace(s, "");
            s = AllergyTail.Replace(s, "");
            s = Spaces.Replace(s, " ");
            return s.Trim();
        }

        // 정상:   {"서비스명":[{"head":[{"list_total_count":N},{"RESULT":{..}}]},{"row":[..]}]}
        // 그 외:  {"RESULT":{"CODE":"INFO-200","MESSAGE":"해당하는 데이터가 없습니다."}}
        static List<Dictionary<string, object>> Rows(string json, string service, out int total)
        {
            total = 0;
            var rows = new List<Dictionary<string, object>>();

            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new NeisException("나이스 응답을 읽을 수 없습니다.");

            object svc;
            if (!root.TryGetValue(service, out svc))
            {
                object res;
                if (root.TryGetValue("RESULT", out res))
                {
                    var result = res as Dictionary<string, object>;
                    if (result != null)
                    {
                        if (Str(result, "CODE") == "INFO-200") return rows;   // 데이터 없음
                        throw new NeisException("나이스: " + Str(result, "MESSAGE"));
                    }
                }
                throw new NeisException("나이스 응답을 읽을 수 없습니다.");
            }

            var parts = svc as object[];
            if (parts == null) return rows;
            foreach (object part in parts)
            {
                var d = part as Dictionary<string, object>;
                if (d == null) continue;

                object head;
                if (d.TryGetValue("head", out head) && head is object[])
                {
                    foreach (object h in (object[])head)
                    {
                        var hd = h as Dictionary<string, object>;
                        object count;
                        if (hd != null && hd.TryGetValue("list_total_count", out count))
                            total = Convert.ToInt32(count, CultureInfo.InvariantCulture);
                    }
                }

                object row;
                if (d.TryGetValue("row", out row) && row is object[])
                {
                    foreach (object r in (object[])row)
                    {
                        var rd = r as Dictionary<string, object>;
                        if (rd != null) rows.Add(rd);
                    }
                }
            }
            return rows;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            return "";
        }
    }
}
