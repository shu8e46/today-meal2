using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TodayMeal
{
    /// <summary>학교, 테마 저장. %AppData%\TodayMeal\settings.json</summary>
    public class AppSettings
    {
        public string OfficeCode = "";
        public string OfficeName = "";
        public string SchoolCode = "";
        public string SchoolName = "";
        public string SchoolAddress = "";
        public string Theme = "light";   // "light" | "dark"
        public int MealType = 2;         // 여러 끼가 있는 학교에서 기본으로 볼 것

        /// <summary>true 면 디스크에 저장하지 않는다 (화면 캡처용 실행).</summary>
        public bool Volatile;

        public bool HasSchool
        {
            get { return SchoolCode.Length > 0 && OfficeCode.Length > 0; }
        }

        static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TodayMeal"); }
        }

        static string FilePath
        {
            get { return Path.Combine(Folder, "settings.json"); }
        }

        public School ToSchool()
        {
            var s = new School();
            s.OfficeCode = OfficeCode;
            s.OfficeName = OfficeName;
            s.Code = SchoolCode;
            s.Name = SchoolName;
            s.Address = SchoolAddress;
            return s;
        }

        public void SetSchool(School s)
        {
            OfficeCode = s.OfficeCode;
            OfficeName = s.OfficeName;
            SchoolCode = s.Code;
            SchoolName = s.Name;
            SchoolAddress = s.Address;
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (d == null) return s;

                s.OfficeCode = Get(d, "officeCode");
                s.OfficeName = Get(d, "officeName");
                s.SchoolCode = Get(d, "schoolCode");
                s.SchoolName = Get(d, "schoolName");
                s.SchoolAddress = Get(d, "schoolAddress");
                if (Get(d, "theme") == "dark") s.Theme = "dark";
                int type;
                if (int.TryParse(Get(d, "mealType"), out type) && type >= 1 && type <= 3) s.MealType = type;
            }
            catch (Exception)
            {
                // 파일이 깨졌으면 처음 켠 것처럼 시작한다
                return new AppSettings();
            }
            return s;
        }

        public void Save()
        {
            if (Volatile) return;
            try
            {
                var d = new Dictionary<string, object>();
                d["officeCode"] = OfficeCode;
                d["officeName"] = OfficeName;
                d["schoolCode"] = SchoolCode;
                d["schoolName"] = SchoolName;
                d["schoolAddress"] = SchoolAddress;
                d["theme"] = Theme;
                d["mealType"] = MealType;
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 저장 실패는 앱을 멈출 일이 아니다. 다음 실행 때 다시 고르면 된다.
            }
        }

        static string Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }
    }
}
