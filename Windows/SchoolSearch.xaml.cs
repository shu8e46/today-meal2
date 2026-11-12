using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TodayMeal
{
    public partial class SchoolSearch : UserControl
    {
        /// <summary>목록에서 학교를 골랐을 때.</summary>
        public event Action<School> Picked;

        readonly DispatcherTimer typingPause;
        int searchSeq;

        public SchoolSearch()
        {
            InitializeComponent();
            // 글자를 칠 때마다 요청하지 않고, 잠깐 멈추면 검색한다
            typingPause = new DispatcherTimer();
            typingPause.Interval = TimeSpan.FromMilliseconds(450);
            typingPause.Tick += delegate { Search(); };
        }

        public void FocusQuery()
        {
            Query.Focus();
        }

        public void Reset()
        {
            typingPause.Stop();
            searchSeq++;
            Query.Text = "";
            Results.Children.Clear();
            Status.Visibility = Visibility.Collapsed;
        }

        /// <summary>검색어를 넣고 바로 검색한다 (개발용 화면 캡처에서 쓴다).</summary>
        public Task SearchNowAsync(string q)
        {
            Query.Text = q;
            return SearchAsync();
        }

        void Query_TextChanged(object sender, TextChangedEventArgs e)
        {
            typingPause.Stop();
            typingPause.Start();
        }

        void Query_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Search();
        }

        void Go_Click(object sender, RoutedEventArgs e)
        {
            Search();
        }

        async void Search()
        {
            await SearchAsync();
        }

        async Task SearchAsync()
        {
            typingPause.Stop();
            string q = Query.Text.Trim();
            int seq = ++searchSeq;

            if (q.Length < 2)
            {
                Results.Children.Clear();
                if (q.Length == 0) Status.Visibility = Visibility.Collapsed;
                else Say("두 글자 이상 입력해 주세요.");
                return;
            }

            Say("찾는 중…");
            List<School> schools;
            try
            {
                schools = await Neis.SearchSchoolsAsync(q);
            }
            catch (Exception ex)
            {
                if (seq == searchSeq)
                {
                    Results.Children.Clear();
                    Say(Neis.Describe(ex));
                }
                return;
            }
            if (seq != searchSeq) return;   // 그 사이 다른 검색이 시작됨

            Results.Children.Clear();
            if (schools.Count == 0)
            {
                Say("‘" + q + "’ 검색 결과가 없습니다.");
                return;
            }
            Status.Visibility = Visibility.Collapsed;
            foreach (School s in schools) Results.Children.Add(MakeRow(s));
            Scroll.ScrollToTop();
        }

        void Say(string text)
        {
            Status.Text = text;
            Status.Visibility = Visibility.Visible;
        }

        Button MakeRow(School school)
        {
            var name = new TextBlock { Text = school.Name, FontSize = 15, FontWeight = FontWeights.Bold };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");

            string where = school.Address.Length > 0 ? school.Address : school.OfficeName;
            var sub = new TextBlock { Text = where, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");

            var panel = new StackPanel();
            panel.Children.Add(name);
            panel.Children.Add(sub);

            var row = new Button { Style = (Style)FindResource("ResultButton"), Content = panel };
            row.Click += delegate
            {
                Action<School> handler = Picked;
                if (handler != null) handler(school);
            };
            return row;
        }
    }
}
