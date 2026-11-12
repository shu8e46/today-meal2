using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TodayMeal
{
    public partial class MainWindow : Window
    {
        enum Mode { Setup, Day, Month, Year }

        class YearCell
        {
            public DateTime Date;
            public Border Box;
            public TextBlock Label;
            public List<Meal> Meals;
        }

        static readonly CultureInfo Ko = new CultureInfo("ko-KR");

        const double PanelWidth = 320;
        const int StripMaxLines = 7;     // 위쪽 칸 하나에 보여 줄 메뉴 줄 수
        const int MonthMaxLines = 9;     // 달력 칸 하나에 보여 줄 메뉴 줄 수
        const double HeroFontSize = 60;  // 가운데 메뉴 기준 크기 (창에 맞춰 늘거나 줄어든다)
        const double HeroMaxLine = 104;  // 한 줄이 이보다 커지지는 않게
        const double YearCellW = 30, YearCellH = 26;

        readonly AppSettings settings;
        readonly MealStore store = new MealStore();
        readonly bool fixedToday;        // 캡처용 실행에서 날짜를 고정했는지
        bool starting = true;

        Mode mode = Mode.Setup;
        DateTime today;
        DateTime selected;               // 하루 화면에서 보고 있는 날
        DateTime monthCursor;            // 달력 화면에서 보고 있는 달의 1일
        int yearCursor;
        int loadSeq;                     // 늦게 도착한 응답을 버리기 위한 번호
        string openPanel;                // null | "menu" | "settings"

        readonly List<DateTime> stripDays = new List<DateTime>();
        readonly List<Button> stripButtons = new List<Button>();
        readonly List<Rectangle> stripBars = new List<Rectangle>();
        readonly List<YearCell> yearCells = new List<YearCell>();
        DateTime? yearHover;
        int stripLines = StripMaxLines;  // 지금 창 높이에서 위쪽 칸에 보여 줄 줄 수

        public DateTime Today { get { return today; } }

        public MainWindow(AppSettings appSettings, DateTime? pretendToday)
        {
            settings = appSettings;
            fixedToday = pretendToday.HasValue;
            today = pretendToday ?? DateTime.Today;
            selected = today;
            monthCursor = FirstOfMonth(today);
            yearCursor = today.Year;

            InitializeComponent();
            GearPath.Data = Icons.Gear;

            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(Width, work.Width - 40);
            Height = Math.Min(Height, work.Height - 40);

            if (settings.Theme == "dark") ThemeDark.IsChecked = true;
            else ThemeLight.IsChecked = true;
            starting = false;

            SetupSearch.Picked += OnSchoolPicked;
            PanelSearch.Picked += OnSchoolPicked;
            ContentHost.PreviewMouseDown += delegate { ClosePanel(); };   // 탭 바깥을 누르면 닫힌다
            ContentHost.SizeChanged += delegate { FitStripToWindow(); };
            SidePanel.MouseLeave += delegate { ClosePanel(); };            // 마우스가 탭 밖으로 나가면 닫힌다
            SourceInitialized += delegate { PaintTitleBar(); };
            Activated += delegate { CatchUpDate(); };
            PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>저장된 학교가 있으면 오늘 급식, 없으면 학교 고르는 화면.</summary>
        public Task StartAsync()
        {
            ShowSchool();
            if (!settings.HasSchool)
            {
                SetMode(Mode.Setup);
                SetupSearch.FocusQuery();
                return Task.FromResult(0);
            }
            store.SetSchool(settings.ToSchool());
            return ShowDayAsync(today);
        }

        // ───────────────────────── 공통 ─────────────────────────

        static DateTime FirstOfMonth(DateTime d)
        {
            return new DateTime(d.Year, d.Month, 1);
        }

        static bool IsWeekend(DateTime d)
        {
            return d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
        }

        static string NameOf(Meal m)
        {
            if (m.TypeName.Length > 0) return m.TypeName;
            return m.Type == 1 ? "조식" : m.Type == 3 ? "석식" : "중식";
        }

        /// <summary>테마 색을 따라가는 글자.</summary>
        static TextBlock Txt(string text, double size, string brushKey, bool bold)
        {
            var t = new TextBlock { Text = text, FontSize = size };
            if (bold) t.FontWeight = FontWeights.Bold;
            t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return t;
        }

        static void AddDishLines(Panel panel, Meal meal, int maxLines, string brushKey)
        {
            int shown = meal.Dishes.Count > maxLines ? maxLines - 1 : meal.Dishes.Count;
            for (int i = 0; i < shown; i++)
            {
                TextBlock t = Txt(meal.Dishes[i], 12.5, brushKey, false);
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                t.Margin = new Thickness(0, 1, 0, 1);
                panel.Children.Add(t);
            }
            if (shown < meal.Dishes.Count)
            {
                TextBlock more = Txt("외 " + (meal.Dishes.Count - shown) + "개", 12.5, "TextFaint", false);
                more.Margin = new Thickness(0, 1, 0, 1);
                panel.Children.Add(more);
            }
        }

        void SetMode(Mode m)
        {
            mode = m;
            SetupView.Visibility = m == Mode.Setup ? Visibility.Visible : Visibility.Collapsed;
            DayView.Visibility = m == Mode.Day ? Visibility.Visible : Visibility.Collapsed;
            MonthView.Visibility = m == Mode.Month ? Visibility.Visible : Visibility.Collapsed;
            YearView.Visibility = m == Mode.Year ? Visibility.Visible : Visibility.Collapsed;

            NavToday.Tag = m == Mode.Day ? "on" : null;
            NavMonth.Tag = m == Mode.Month ? "on" : null;
            NavYear.Tag = m == Mode.Year ? "on" : null;

            bool has = settings.HasSchool;
            NavToday.IsEnabled = has;
            NavMonth.IsEnabled = has;
            NavYear.IsEnabled = has;
            RefreshBtn.IsEnabled = has;
        }

        void SetBusy(bool busy)
        {
            if (!busy)
            {
                RefreshSpin.BeginAnimation(RotateTransform.AngleProperty, null);
                return;
            }
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9));
            spin.RepeatBehavior = RepeatBehavior.Forever;
            RefreshSpin.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        void ShowSchool()
        {
            SchoolLabel.Text = settings.SchoolName;
            CurrentSchool.Text = settings.HasSchool ? settings.SchoolName : "아직 고르지 않았습니다";
            CurrentSchoolSub.Text = settings.SchoolAddress.Length > 0 ? settings.SchoolAddress : settings.OfficeName;
            CurrentSchoolSub.Visibility = CurrentSchoolSub.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void FillTypeToggles(Panel host, SortedDictionary<int, string> types, int current)
        {
            host.Children.Clear();
            if (types.Count < 2) return;   // 한 끼만 주는 학교면 고를 것이 없다

            foreach (KeyValuePair<int, string> kv in types)
            {
                int type = kv.Key;
                var b = new Button { Content = kv.Value, Style = (Style)FindResource("TypeToggle") };
                if (type == current) b.Tag = "on";
                b.Click += delegate
                {
                    settings.MealType = type;
                    settings.Save();
                    if (mode == Mode.Month) BuildMonth();
                    else
                    {
                        BuildStrip();
                        RenderDay();
                    }
                };
                host.Children.Add(b);
            }
        }

        // ───────────────────────── 하루 화면 ─────────────────────────

        public async Task ShowDayAsync(DateTime date)
        {
            selected = date.Date;
            SetMode(Mode.Day);
            int seq = ++loadSeq;

            // 위쪽 5칸을 채우려면 이번 달과 다음 달이 필요하다
            DateTime from = FirstOfMonth(today);
            DateTime to = from.AddMonths(2).AddDays(-1);
            bool cached = store.IsLoaded(from) && store.IsLoaded(from.AddMonths(1)) && store.IsLoaded(selected);
            if (!cached)
            {
                ClearStrip();
                DayTitle.Text = TitleFor(selected);
                DayTypes.Children.Clear();
                DayCalories.Text = "";
                SayDay("불러오는 중…");
            }

            SetBusy(true);
            try
            {
                await store.EnsureAsync(from, to);
                await store.EnsureAsync(selected, selected);
                if (seq != loadSeq) return;
                BuildStrip();
                RenderDay();
            }
            catch (Exception ex)
            {
                if (seq != loadSeq) return;
                ClearStrip();
                DayTypes.Children.Clear();
                DayCalories.Text = "";
                SayDay(Neis.Describe(ex));
            }
            finally
            {
                if (seq == loadSeq) SetBusy(false);
            }
        }

        string TitleFor(DateTime d)
        {
            string title = d.ToString("M월 d일 dddd", Ko);
            if (d == today) return "오늘 · " + title;
            if (d == today.AddDays(1)) return "내일 · " + title;
            return title;
        }

        void SayDay(string text)
        {
            DishBox.Visibility = Visibility.Collapsed;
            DayMessage.Text = text;
            DayMessage.Visibility = Visibility.Visible;
        }

        void ClearStrip()
        {
            Strip.Children.Clear();
            stripDays.Clear();
            stripButtons.Clear();
            stripBars.Clear();
        }

        /// <summary>첫 칸은 오늘, 나머지는 급식이 있는 날만 골라서 4개.</summary>
        void BuildStrip()
        {
            ClearStrip();
            stripDays.Add(today);
            DateTime end = FirstOfMonth(today).AddMonths(2);
            for (DateTime d = today.AddDays(1); d < end && stripDays.Count < 5; d = d.AddDays(1))
            {
                if (store.Get(d).Count > 0) stripDays.Add(d);
            }

            for (int i = 0; i < 5; i++)
            {
                if (i < stripDays.Count)
                {
                    Strip.Children.Add(MakeStripCell(i, stripDays[i]));
                }
                else
                {
                    var blank = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
                    blank.SetResourceReference(Border.BorderBrushProperty, "Line");
                    Strip.Children.Add(blank);
                }
            }
        }

        FrameworkElement MakeStripCell(int index, DateTime date)
        {
            Meal meal = store.Pick(date, settings.MealType);

            // 날짜 + 요일, 오른쪽에 오늘/내일
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            string mark = date == today ? "오늘" : date == today.AddDays(1) ? "내일" : "";
            TextBlock badge = Txt(mark, 12, "Accent", true);
            badge.VerticalAlignment = VerticalAlignment.Bottom;
            badge.Margin = new Thickness(0, 0, 0, 2);
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);

            var when = new StackPanel { Orientation = Orientation.Horizontal };
            when.Children.Add(Txt(date.ToString("M.d", Ko), 18, "Text", true));
            TextBlock dow = Txt(date.ToString("ddd", Ko), 13, "TextMid", false);
            dow.Margin = new Thickness(6, 0, 0, 2);
            dow.VerticalAlignment = VerticalAlignment.Bottom;
            when.Children.Add(dow);
            head.Children.Add(when);

            var panel = new StackPanel { Margin = new Thickness(18, 12, 14, 14) };
            panel.Children.Add(head);
            // 칸에는 날짜와 요일만 보여 준다. 메뉴는 칸을 눌러 가운데에서 크게 본다.
            head.Margin = new Thickness(0);
            if (meal == null) dow.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");

            var button = new Button { Style = (Style)FindResource("CellButton"), Content = panel };
            button.Click += delegate
            {
                selected = date;
                RenderDay();
            };

            // 고른 칸 표시: 위쪽 굵은 줄
            var bar = new Rectangle { Height = 2, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Hidden };
            bar.SetResourceReference(Shape.FillProperty, "Accent");

            var grid = new Grid();
            grid.Children.Add(button);
            grid.Children.Add(bar);

            var outer = new Border { BorderThickness = new Thickness(index == 0 ? 0 : 1, 0, 0, 0), Child = grid };
            outer.SetResourceReference(Border.BorderBrushProperty, "Line");

            stripButtons.Add(button);
            stripBars.Add(bar);
            return outer;
        }

        /// <summary>
        /// 창이 낮으면 위쪽 칸의 메뉴 줄 수를 줄여서, 가운데 큰 글씨가 자리를 뺏기지 않게 한다.
        /// 아주 낮으면 날짜와 요일만 남긴다.
        /// </summary>
        static int StripLinesFor(double contentHeight)
        {
            return 0;   // 지금은 항상 날짜만 보여 준다
        }

        void FitStripToWindow()
        {
            int lines = StripLinesFor(ContentHost.ActualHeight);
            if (lines == stripLines) return;
            stripLines = lines;
            if (mode == Mode.Day && stripButtons.Count > 0)
            {
                BuildStrip();
                MarkStrip();
            }
        }

        void MarkStrip()
        {
            for (int i = 0; i < stripButtons.Count; i++)
            {
                bool on = stripDays[i] == selected;
                stripButtons[i].Tag = on ? "on" : null;
                stripBars[i].Visibility = on ? Visibility.Visible : Visibility.Hidden;
            }
        }

        void RenderDay()
        {
            List<Meal> meals = store.Get(selected);
            Meal meal = store.Pick(selected, settings.MealType);

            string title = TitleFor(selected);
            if (meals.Count == 1 && meal.Type != 2) title += " · " + NameOf(meal);
            DayTitle.Text = title;

            var types = new SortedDictionary<int, string>();
            foreach (Meal m in meals) types[m.Type] = NameOf(m);
            FillTypeToggles(DayTypes, types, meal != null ? meal.Type : 0);

            DishList.Children.Clear();
            if (meal == null)
            {
                DayCalories.Text = "";
                SayDay(selected == today ? "오늘은 급식이 없습니다" : "급식이 없는 날입니다");
            }
            else
            {
                foreach (string dish in meal.Dishes)
                {
                    TextBlock t = Txt(dish, HeroFontSize, "Text", true);
                    t.HorizontalAlignment = HorizontalAlignment.Center;
                    t.TextAlignment = TextAlignment.Center;
                    t.Margin = new Thickness(0, 3, 0, 3);
                    DishList.Children.Add(t);
                }
                DishBox.MaxHeight = meal.Dishes.Count * HeroMaxLine;
                DishBox.Visibility = Visibility.Visible;
                DayMessage.Visibility = Visibility.Collapsed;
                DayCalories.Text = meal.Calories;
            }
            MarkStrip();
        }

        /// <summary>← → 키: 급식이 있는 앞뒤 날로.</summary>
        void StepDay(int direction)
        {
            DateTime d = selected;
            for (int i = 0; i < 45; i++)
            {
                d = d.AddDays(direction);
                if (d == today || store.Get(d).Count > 0)
                {
                    selected = d;
                    RenderDay();
                    return;
                }
            }
        }

        // ───────────────────────── 달력 화면 ─────────────────────────

        public async Task ShowMonthAsync(DateTime month)
        {
            monthCursor = FirstOfMonth(month);
            SetMode(Mode.Month);
            MonthTitle.Text = monthCursor.ToString("yyyy년 M월", Ko);
            int seq = ++loadSeq;

            if (!store.IsLoaded(monthCursor))
            {
                ClearMonth();
                SayMonth("불러오는 중…");
            }

            SetBusy(true);
            try
            {
                await store.EnsureAsync(monthCursor, monthCursor);
                if (seq != loadSeq) return;
                BuildMonth();
            }
            catch (Exception ex)
            {
                if (seq != loadSeq) return;
                ClearMonth();
                SayMonth(Neis.Describe(ex));
            }
            finally
            {
                if (seq == loadSeq) SetBusy(false);
            }
        }

        void ClearMonth()
        {
            MonthGrid.Children.Clear();
            MonthGrid.RowDefinitions.Clear();
            MonthGrid.ColumnDefinitions.Clear();
            MonthHead.Children.Clear();
            MonthHead.ColumnDefinitions.Clear();
            MonthTypes.Children.Clear();
            MonthMessage.Visibility = Visibility.Collapsed;
        }

        void SayMonth(string text)
        {
            MonthMessage.Text = text;
            MonthMessage.Visibility = Visibility.Visible;
        }

        static DateTime WeekStart(DateTime d, bool sundayFirst)
        {
            int back = sundayFirst ? (int)d.DayOfWeek : ((int)d.DayOfWeek + 6) % 7;
            return d.AddDays(-back);
        }

        void BuildMonth()
        {
            ClearMonth();
            DateTime first = monthCursor;
            DateTime last = monthCursor.AddMonths(1).AddDays(-1);

            var types = new SortedDictionary<int, string>();
            bool weekend = false;
            int mealDays = 0;
            for (DateTime d = first; d <= last; d = d.AddDays(1))
            {
                foreach (Meal m in store.Get(d)) types[m.Type] = NameOf(m);
                if (store.Get(d).Count == 0) continue;
                mealDays++;
                if (IsWeekend(d)) weekend = true;
            }
            if (mealDays == 0)
            {
                SayMonth("이 달에는 급식 정보가 없습니다");
                return;
            }

            int current = types.ContainsKey(settings.MealType) ? settings.MealType : 2;
            FillTypeToggles(MonthTypes, types, current);

            // 주말 급식이 없는 달은 월~금 다섯 칸만 그려서 칸을 넓게 쓴다
            string[] names = weekend
                ? new[] { "일", "월", "화", "수", "목", "금", "토" }
                : new[] { "월", "화", "수", "목", "금" };
            int cols = names.Length;
            for (int c = 0; c < cols; c++)
            {
                MonthHead.ColumnDefinitions.Add(new ColumnDefinition());
                MonthGrid.ColumnDefinitions.Add(new ColumnDefinition());
                TextBlock name = Txt(names[c], 12.5, "TextFaint", true);
                name.Margin = new Thickness(10, 2, 0, 7);
                Grid.SetColumn(name, c);
                MonthHead.Children.Add(name);
            }

            DateTime firstShown = first, lastShown = last;
            if (!weekend)
            {
                while (IsWeekend(firstShown)) firstShown = firstShown.AddDays(1);
                while (IsWeekend(lastShown)) lastShown = lastShown.AddDays(-1);
            }
            DateTime origin = WeekStart(firstShown, weekend);
            int rows = (WeekStart(lastShown, weekend) - origin).Days / 7 + 1;

            // 각 주는 그 주에서 가장 긴 메뉴가 들어갈 만큼은 확보하고, 창이 크면 남는 높이를 똑같이 나눠 갖는다
            var rowLines = new int[rows];
            for (DateTime d = firstShown; d <= lastShown; d = d.AddDays(1))
            {
                Meal meal = store.Pick(d, settings.MealType);
                if (meal == null || (!weekend && IsWeekend(d))) continue;
                int r = (WeekStart(d, weekend) - origin).Days / 7;
                rowLines[r] = Math.Max(rowLines[r], Math.Min(meal.Dishes.Count, MonthMaxLines));
            }
            for (int r = 0; r < rows; r++)
            {
                MonthGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 46 + rowLines[r] * 18 });
            }

            FrameworkElement todayCell = null;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    DateTime d = origin.AddDays(r * 7 + c);
                    FrameworkElement cell = MakeMonthCell(d, d >= first && d <= last, c, r == rows - 1);
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    MonthGrid.Children.Add(cell);
                    if (d == today) todayCell = cell;
                }
            }

            // 달력이 창보다 길어서 오늘이 가려지면, 오늘이 있는 주가 보일 만큼만 내려 둔다
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (todayCell == null || !MonthGrid.Children.Contains(todayCell))
                {
                    MonthScroll.ScrollToTop();
                    return;
                }
                double top = todayCell.TranslatePoint(new Point(0, 0), MonthGrid).Y;
                double bottom = top + todayCell.ActualHeight;
                double view = MonthScroll.ViewportHeight;
                MonthScroll.ScrollToVerticalOffset(bottom <= view ? 0 : Math.Min(top, bottom - view));
            }));
        }

        FrameworkElement MakeMonthCell(DateTime date, bool inMonth, int column, bool lastRow)
        {
            var outer = new Border { BorderThickness = new Thickness(column == 0 ? 0 : 1, 1, 0, lastRow ? 1 : 0) };
            outer.SetResourceReference(Border.BorderBrushProperty, "Line");
            if (!inMonth) return outer;

            Meal meal = store.Pick(date, settings.MealType);
            bool past = date < today;
            var panel = new StackPanel { Margin = new Thickness(10, 8, 8, 9) };

            if (date == today)
            {
                var chip = new Border
                {
                    Child = Txt(date.Day.ToString(), 13, "OnAccent", true),
                    Padding = new Thickness(6, 1, 6, 2),
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                chip.SetResourceReference(Border.BackgroundProperty, "Accent");
                panel.Children.Add(chip);
            }
            else
            {
                TextBlock number = Txt(date.Day.ToString(), 13, meal == null || past ? "TextFaint" : "Text", true);
                number.Margin = new Thickness(0, 1, 0, 8);
                panel.Children.Add(number);
            }

            if (meal == null)
            {
                outer.Child = panel;
                return outer;
            }

            AddDishLines(panel, meal, MonthMaxLines, past ? "TextFaint" : "TextMid");
            var button = new Button { Style = (Style)FindResource("CellButton"), Content = panel };
            button.Click += delegate { Task t = ShowDayAsync(date); };
            outer.Child = button;
            return outer;
        }

        void MonthScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 창이 크면 달력이 남는 높이를 다 쓰고, 작으면 스크롤된다
            MonthGrid.MinHeight = Math.Max(0, MonthScroll.ActualHeight - 2);
        }

        void MonthPrev_Click(object sender, RoutedEventArgs e)
        {
            Task t = ShowMonthAsync(monthCursor.AddMonths(-1));
        }

        void MonthNext_Click(object sender, RoutedEventArgs e)
        {
            Task t = ShowMonthAsync(monthCursor.AddMonths(1));
        }

        // ───────────────────────── 올해 화면 ─────────────────────────

        public async Task ShowYearAsync(int year)
        {
            yearCursor = Math.Max(2000, Math.Min(2099, year));
            SetMode(Mode.Year);
            YearTitle.Text = yearCursor + "년";
            int seq = ++loadSeq;

            DateTime from = new DateTime(yearCursor, 1, 1);
            DateTime to = new DateTime(yearCursor, 12, 31);
            bool cached = true;
            for (DateTime m = from; m <= to; m = m.AddMonths(1))
            {
                if (!store.IsLoaded(m)) cached = false;
            }
            if (!cached)
            {
                ClearYear();
                SayYear("불러오는 중…");
            }

            SetBusy(true);
            try
            {
                await store.EnsureAsync(from, to);
                if (seq != loadSeq) return;
                BuildYear();
            }
            catch (Exception ex)
            {
                if (seq != loadSeq) return;
                ClearYear();
                SayYear(Neis.Describe(ex));
            }
            finally
            {
                if (seq == loadSeq) SetBusy(false);
            }
        }

        void ClearYear()
        {
            YearGrid.Children.Clear();
            yearCells.Clear();
            YearDetail.Children.Clear();
            YearResults.Children.Clear();
            YearStat.Text = "";
            YearMessage.Visibility = Visibility.Collapsed;
        }

        void SayYear(string text)
        {
            YearMessage.Text = text;
            YearMessage.Visibility = Visibility.Visible;
        }

        void BuildYear()
        {
            ClearYear();
            for (int month = 1; month <= 12; month++) YearGrid.Children.Add(MakeMiniMonth(yearCursor, month));

            int mealDays = 0;
            foreach (YearCell c in yearCells)
            {
                if (c.Meals.Count > 0) mealDays++;
            }
            YearStat.Text = mealDays > 0 ? "급식 있는 날 " + mealDays + "일" : "급식 정보 없음";

            // 옆 칸에는 일단 오늘 급식을 띄워 둔다
            if (yearHover == null || yearHover.Value.Year != yearCursor || store.Get(yearHover.Value).Count == 0)
            {
                yearHover = today.Year == yearCursor && store.Get(today).Count > 0 ? today : (DateTime?)null;
            }
            RenderYearDetail();
            ApplyYearSearch();
        }

        FrameworkElement MakeMiniMonth(int year, int month)
        {
            DateTime first = new DateTime(year, month, 1);

            var title = new Button
            {
                Content = month + "월",
                Style = (Style)FindResource("LinkButton"),
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(5, 0, 0, 6)
            };
            title.Click += delegate { Task t = ShowMonthAsync(first); };

            var grid = new Grid { Width = 7 * YearCellW, Height = 6 * YearCellH };
            for (int c = 0; c < 7; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 6; r++) grid.RowDefinitions.Add(new RowDefinition());

            int offset = (int)first.DayOfWeek;   // 일요일 시작
            int days = DateTime.DaysInMonth(year, month);
            for (int day = 1; day <= days; day++)
            {
                DateTime date = new DateTime(year, month, day);
                var label = new TextBlock
                {
                    Text = day.ToString(),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var box = new Border { Margin = new Thickness(1), Child = label };
                if (date == today)
                {
                    box.BorderThickness = new Thickness(1);
                    box.SetResourceReference(Border.BorderBrushProperty, "Accent");
                }
                int slot = offset + day - 1;
                Grid.SetColumn(box, slot % 7);
                Grid.SetRow(box, slot / 7);
                grid.Children.Add(box);

                var cell = new YearCell { Date = date, Box = box, Label = label, Meals = store.Get(date) };
                yearCells.Add(cell);
                if (cell.Meals.Count > 0)
                {
                    box.Cursor = Cursors.Hand;
                    box.MouseEnter += delegate
                    {
                        yearHover = cell.Date;
                        RenderYearDetail();
                    };
                    box.MouseLeftButtonUp += delegate { Task t = ShowDayAsync(cell.Date); };
                }
            }

            var panel = new StackPanel { Margin = new Thickness(14, 8, 14, 12) };
            panel.Children.Add(title);
            panel.Children.Add(grid);
            return panel;
        }

        /// <summary>오른쪽 칸 위쪽: 마우스를 올린 날의 급식.</summary>
        void RenderYearDetail()
        {
            YearDetail.Children.Clear();
            bool searching = YearSearch.Text.Trim().Length > 0;
            YearResults.Margin = new Thickness(0, yearHover == null && searching ? 0 : 22, 0, 0);
            if (yearHover == null)
            {
                if (searching) return;   // 검색 중에는 결과 목록이 맨 위로 온다
                TextBlock hint = Txt("색이 칠해진 날이 급식 있는 날입니다. 날짜에 마우스를 올리면 그날 급식이 여기에 나오고, 누르면 크게 봅니다.", 13, "TextFaint", false);
                hint.TextWrapping = TextWrapping.Wrap;
                hint.LineHeight = 21;
                YearDetail.Children.Add(hint);
                return;
            }

            DateTime date = yearHover.Value;
            List<Meal> meals = store.Get(date);
            YearDetail.Children.Add(Txt(date.ToString("M월 d일 dddd", Ko), 16, "Accent", true));
            foreach (Meal m in meals)
            {
                TextBlock kind = Txt(meals.Count > 1 ? NameOf(m) : "", 12.5, "TextFaint", true);
                kind.Margin = new Thickness(0, meals.Count > 1 ? 14 : 4, 0, 3);
                YearDetail.Children.Add(kind);

                foreach (string dish in m.Dishes)
                {
                    TextBlock t = Txt(dish, 14, "Text", false);
                    t.TextWrapping = TextWrapping.Wrap;
                    t.Margin = new Thickness(0, 2, 0, 2);
                    YearDetail.Children.Add(t);
                }
                if (m.Calories.Length > 0)
                {
                    TextBlock kcal = Txt(m.Calories, 12.5, "TextFaint", false);
                    kcal.Margin = new Thickness(0, 6, 0, 0);
                    YearDetail.Children.Add(kcal);
                }
            }
        }

        /// <summary>검색어가 들어간 메뉴가 나오는 날을 진하게 칠하고, 오른쪽에 목록으로 보여 준다.</summary>
        void ApplyYearSearch()
        {
            string q = YearSearch.Text.Trim();
            var hits = new List<YearCell>();

            foreach (YearCell c in yearCells)
            {
                bool has = c.Meals.Count > 0;
                bool hit = q.Length > 0 && has && MatchingDishes(c.Meals, q).Count > 0;
                if (hit) hits.Add(c);

                if (hit) c.Box.SetResourceReference(Border.BackgroundProperty, "Accent");
                else if (has) c.Box.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                else c.Box.Background = null;
                c.Label.SetResourceReference(TextBlock.ForegroundProperty, hit ? "OnAccent" : has ? "Text" : "TextFaint");
                c.Label.FontWeight = hit ? FontWeights.Bold : FontWeights.Normal;
            }

            YearResults.Children.Clear();
            if (q.Length == 0) return;

            TextBlock head = Txt("‘" + q + "’ 나오는 날 " + hits.Count + "일", 13, "TextMid", true);
            head.TextWrapping = TextWrapping.Wrap;
            head.Margin = new Thickness(0, 0, 0, 6);
            YearResults.Children.Add(head);

            foreach (YearCell c in hits)
            {
                YearCell cell = c;
                var line = new StackPanel();
                line.Children.Add(Txt(cell.Date.ToString("M월 d일 (ddd)", Ko), 13, "Text", true));
                TextBlock names = Txt(string.Join(", ", MatchingDishes(cell.Meals, q)), 13, "TextMid", false);
                names.TextWrapping = TextWrapping.Wrap;
                names.Margin = new Thickness(0, 2, 0, 0);
                line.Children.Add(names);

                var row = new Button { Style = (Style)FindResource("ResultButton"), Content = line };
                row.Click += delegate { Task t = ShowDayAsync(cell.Date); };
                YearResults.Children.Add(row);
            }
        }

        static List<string> MatchingDishes(List<Meal> meals, string q)
        {
            var found = new List<string>();
            foreach (Meal m in meals)
            {
                foreach (string dish in m.Dishes)
                {
                    if (dish.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 && !found.Contains(dish)) found.Add(dish);
                }
            }
            return found;
        }

        void YearSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (mode != Mode.Year) return;
            // 검색을 시작하면 옆 칸을 결과 목록에 내주고, 지우면 다시 오늘 급식을 띄운다
            if (YearSearch.Text.Trim().Length > 0) yearHover = null;
            else if (today.Year == yearCursor && store.Get(today).Count > 0) yearHover = today;
            RenderYearDetail();
            ApplyYearSearch();
        }

        void YearPrev_Click(object sender, RoutedEventArgs e)
        {
            Task t = ShowYearAsync(yearCursor - 1);
        }

        void YearNext_Click(object sender, RoutedEventArgs e)
        {
            Task t = ShowYearAsync(yearCursor + 1);
        }

        // ───────────────────────── 오른쪽 탭 (메뉴 / 설정) ─────────────────────────

        void Menu_Click(object sender, RoutedEventArgs e)
        {
            TogglePanel("menu");
        }

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            TogglePanel("settings");
        }

        void TogglePanel(string which)
        {
            if (openPanel == which)
            {
                ClosePanel();
                return;
            }
            SetPanelContent(which);
            AnimatePanel(1, 180, EasingMode.EaseOut, null);
        }

        /// <summary>탭은 누른 버튼 바로 아래를 기준점으로 커지고 줄어든다.</summary>
        void AnimatePanel(double to, int ms, EasingMode ease, EventHandler done)
        {
            var grow = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            grow.EasingFunction = new CubicEase { EasingMode = ease };
            if (done != null) grow.Completed += done;
            PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            SidePanel.BeginAnimation(OpacityProperty, grow);
        }

        void SetPanelContent(string which)
        {
            openPanel = which;
            MenuContent.Visibility = which == "menu" ? Visibility.Visible : Visibility.Collapsed;
            SettingsContent.Visibility = which == "settings" ? Visibility.Visible : Visibility.Collapsed;
            MenuBtn.Tag = which == "menu" ? "on" : null;
            SettingsBtn.Tag = which == "settings" ? "on" : null;
            // 기준점: 탭 오른쪽 끝에서 설정 버튼 가운데까지 19px, 메뉴 버튼 가운데까지 59px
            double fromRight = which == "settings" ? 19 : 59;
            SidePanel.RenderTransformOrigin = new Point((PanelWidth - fromRight) / PanelWidth, 0);
            SidePanel.Visibility = Visibility.Visible;
        }

        void ClosePanel()
        {
            if (openPanel == null) return;
            openPanel = null;
            MenuBtn.Tag = null;
            SettingsBtn.Tag = null;

            AnimatePanel(0, 140, EasingMode.EaseIn, delegate
            {
                if (openPanel != null) return;   // 닫히는 도중에 다시 열렸다
                SidePanel.Visibility = Visibility.Collapsed;
                HideSchoolSearch();
            });
        }

        void NavToday_Click(object sender, RoutedEventArgs e)
        {
            ClosePanel();
            Task t = ShowDayAsync(today);
        }

        void NavMonth_Click(object sender, RoutedEventArgs e)
        {
            ClosePanel();
            Task t = ShowMonthAsync(today);
        }

        void NavYear_Click(object sender, RoutedEventArgs e)
        {
            ClosePanel();
            Task t = ShowYearAsync(today.Year);
        }

        void ChangeSchool_Click(object sender, RoutedEventArgs e)
        {
            if (PanelSearch.Visibility == Visibility.Visible)
            {
                HideSchoolSearch();
                return;
            }
            PanelSearch.Visibility = Visibility.Visible;
            ChangeSchoolBtn.Content = "취소";
            PanelSearch.FocusQuery();
        }

        void HideSchoolSearch()
        {
            PanelSearch.Reset();
            PanelSearch.Visibility = Visibility.Collapsed;
            ChangeSchoolBtn.Content = "학교 변경";
        }

        void OnSchoolPicked(School school)
        {
            settings.SetSchool(school);
            settings.Save();
            store.SetSchool(school);
            ShowSchool();
            SetupSearch.Reset();
            HideSchoolSearch();
            ClosePanel();
            Task t = ShowDayAsync(today);
        }

        void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (starting) return;
            settings.Theme = ThemeDark.IsChecked == true ? "dark" : "light";
            settings.Save();
            App.ApplyTheme(settings.Theme);
            PaintTitleBar();
        }

        // ───────────────────────── 새로고침, 키, 날짜 ─────────────────────────

        async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        Task RefreshAsync()
        {
            if (!settings.HasSchool) return Task.FromResult(0);
            if (!fixedToday) today = DateTime.Today;
            store.Clear();
            return ReloadAsync();
        }

        Task ReloadAsync()
        {
            if (mode == Mode.Month) return ShowMonthAsync(monthCursor);
            if (mode == Mode.Year) return ShowYearAsync(yearCursor);
            return ShowDayAsync(selected);
        }

        /// <summary>켜 둔 채로 자정이 지났으면 오늘을 다시 맞춘다.</summary>
        void CatchUpDate()
        {
            if (fixedToday || !settings.HasSchool || DateTime.Today == today) return;
            bool wasOnToday = selected == today;
            today = DateTime.Today;
            if (wasOnToday) selected = today;
            Task t = ReloadAsync();
        }

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && openPanel != null)
            {
                ClosePanel();
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                Task t = RefreshAsync();
                e.Handled = true;
            }
            else if ((e.Key == Key.Left || e.Key == Key.Right) && mode == Mode.Day && openPanel == null
                     && !(Keyboard.FocusedElement is TextBox))
            {
                StepDay(e.Key == Key.Left ? -1 : 1);
                e.Handled = true;
            }
        }

        // ───────────────────────── 창 제목줄 색 ─────────────────────────

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>제목줄을 앱 배경과 같은 색으로 (Windows 11). 안 되는 버전에서는 그냥 기본 제목줄.</summary>
        void PaintTitleBar()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int dark = settings.Theme == "dark" ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));      // 어두운 제목줄
                int caption = ColorRef("Bg");
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));   // 제목줄 배경
                int text = ColorRef("Text");
                DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));      // 제목줄 글자
            }
            catch (Exception)
            {
                // dwmapi 가 없거나 지원하지 않는 속성: 색만 못 바꿀 뿐이다
            }
        }

        int ColorRef(string brushKey)
        {
            Color c = ((SolidColorBrush)FindResource(brushKey)).Color;
            return c.R | (c.G << 8) | (c.B << 16);
        }

        // ───────────────────────── 개발용 화면 캡처 (DevShot.cs) ─────────────────────────

        public void OpenPanelNow(string which)
        {
            SetPanelContent(which);
            PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            SidePanel.BeginAnimation(OpacityProperty, null);
            PanelScale.ScaleX = 1;
            PanelScale.ScaleY = 1;
            SidePanel.Opacity = 1;
        }

        public void SetYearQuery(string q)
        {
            YearSearch.Text = q;
        }

        public Task SearchSchoolNow(string q)
        {
            if (openPanel == "settings")
            {
                PanelSearch.Visibility = Visibility.Visible;
                return PanelSearch.SearchNowAsync(q);
            }
            return SetupSearch.SearchNowAsync(q);
        }

        public void SaveSnapshot(string path)
        {
            Root.UpdateLayout();
            int w = (int)Math.Ceiling(Root.ActualWidth);
            int h = (int)Math.Ceiling(Root.ActualHeight);
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(Root);

            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream fs = File.Create(path)) png.Save(fs);
        }
    }
}
