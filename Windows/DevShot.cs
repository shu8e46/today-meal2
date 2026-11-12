using System;
using System.Globalization;

namespace TodayMeal
{
    /// <summary>
    /// 개발용: 창 내용을 PNG 로 저장하고 바로 종료한다. 저장된 설정은 읽지도 쓰지도 않는다.
    ///   TodayMeal.exe --shot out.png --school "B10|7010083|서울고등학교" --view month --theme dark
    /// 옵션: --view day|month|year, --panel menu|settings, --date yyyyMMdd (오늘인 척할 날짜),
    ///       --query 검색어 (올해 급식 화면), --search 학교이름 (학교 검색), --size 1160x820
    /// --school 을 빼면 처음 켰을 때의 학교 고르는 화면이 찍힌다.
    /// </summary>
    public class ShotOptions
    {
        public string Path;
        public string View = "day";
        public string Theme = "light";
        public string Panel;
        public string Query;
        public string Search;
        public School School;
        public DateTime? Today;
        public double Width;
        public double Height;

        public static ShotOptions Parse(string[] args)
        {
            var o = new ShotOptions();
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                string key = args[i];
                string value = args[i + 1];
                if (key == "--shot") o.Path = value;
                else if (key == "--view") o.View = value;
                else if (key == "--theme") o.Theme = value;
                else if (key == "--panel") o.Panel = value;
                else if (key == "--query") o.Query = value;
                else if (key == "--search") o.Search = value;
                else if (key == "--school")
                {
                    string[] p = value.Split('|');
                    if (p.Length >= 3)
                    {
                        o.School = new School();
                        o.School.OfficeCode = p[0];
                        o.School.Code = p[1];
                        o.School.Name = p[2];
                    }
                }
                else if (key == "--date")
                {
                    DateTime d;
                    if (DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) o.Today = d;
                }
                else if (key == "--size")
                {
                    string[] p = value.Split('x');
                    double w, h;
                    if (p.Length == 2 && double.TryParse(p[0], out w) && double.TryParse(p[1], out h))
                    {
                        o.Width = w;
                        o.Height = h;
                    }
                }
            }
            return o.Path != null ? o : null;
        }

        public AppSettings MakeSettings()
        {
            var s = new AppSettings();
            s.Volatile = true;
            s.Theme = Theme == "dark" ? "dark" : "light";
            if (School != null) s.SetSchool(School);
            return s;
        }
    }
}
