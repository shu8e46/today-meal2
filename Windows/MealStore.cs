using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TodayMeal
{
    /// <summary>받아 온 급식을 달 단위로 기억해 두는 곳. 같은 달은 다시 요청하지 않는다.</summary>
    public class MealStore
    {
        static readonly List<Meal> None = new List<Meal>();

        readonly Dictionary<DateTime, List<Meal>> byDate = new Dictionary<DateTime, List<Meal>>();
        readonly HashSet<int> loadedMonths = new HashSet<int>();
        School school;
        int generation;

        public School School { get { return school; } }

        public void SetSchool(School s)
        {
            school = s;
            Clear();
        }

        public void Clear()
        {
            generation++;
            byDate.Clear();
            loadedMonths.Clear();
        }

        public bool IsLoaded(DateTime month)
        {
            return loadedMonths.Contains(Id(month));
        }

        /// <summary>from ~ to 가 걸친 달을 전부 받아 둔다. 이미 있으면 아무것도 안 한다.</summary>
        public async Task EnsureAsync(DateTime from, DateTime to)
        {
            if (school == null) return;

            DateTime first = new DateTime(from.Year, from.Month, 1);
            DateTime last = new DateTime(to.Year, to.Month, 1);
            DateTime? needFrom = null, needTo = null;
            for (DateTime m = first; m <= last; m = m.AddMonths(1))
            {
                if (loadedMonths.Contains(Id(m))) continue;
                if (needFrom == null) needFrom = m;
                needTo = m;
            }
            if (needFrom == null) return;

            int gen = generation;
            DateTime a = needFrom.Value;
            DateTime b = needTo.Value.AddMonths(1).AddDays(-1);
            List<Meal> meals = await Neis.GetMealsAsync(school, a, b);
            if (gen != generation) return;   // 그 사이 학교가 바뀌었거나 새로고침됨

            for (DateTime d = a; d <= b; d = d.AddDays(1)) byDate.Remove(d);
            foreach (Meal meal in meals)
            {
                List<Meal> list;
                if (!byDate.TryGetValue(meal.Date, out list))
                {
                    list = new List<Meal>();
                    byDate[meal.Date] = list;
                }
                list.Add(meal);
            }
            for (DateTime d = a; d <= b; d = d.AddDays(1))
            {
                List<Meal> list;
                if (byDate.TryGetValue(d, out list)) list.Sort(delegate(Meal x, Meal y) { return x.Type.CompareTo(y.Type); });
            }
            for (DateTime m = a; m <= b; m = m.AddMonths(1)) loadedMonths.Add(Id(m));
        }

        /// <summary>그날의 급식 전부 (조식, 중식, 석식 순). 없으면 빈 목록.</summary>
        public List<Meal> Get(DateTime date)
        {
            List<Meal> list;
            return byDate.TryGetValue(date.Date, out list) ? list : None;
        }

        /// <summary>원하는 종류가 있으면 그것, 없으면 중식, 그것도 없으면 그날 첫 급식.</summary>
        public Meal Pick(DateTime date, int preferredType)
        {
            List<Meal> list = Get(date);
            if (list.Count == 0) return null;
            foreach (Meal m in list) if (m.Type == preferredType) return m;
            foreach (Meal m in list) if (m.Type == 2) return m;
            return list[0];
        }

        static int Id(DateTime month)
        {
            return month.Year * 100 + month.Month;
        }
    }
}
