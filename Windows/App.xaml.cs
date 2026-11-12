using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TodayMeal
{
    public partial class App : Application
    {
        /// <summary>"light" 또는 "dark". 색 사전을 통째로 바꿔 끼운다.</summary>
        public static void ApplyTheme(string theme)
        {
            string file = theme == "dark" ? "Dark" : "Light";
            var dict = new ResourceDictionary();
            dict.Source = new Uri("/TodayMeal;component/Themes/" + file + ".xaml", UriKind.Relative);
            Current.Resources.MergedDictionaries[0] = dict;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            DispatcherUnhandledException += OnUnhandled;

            ShotOptions shot = ShotOptions.Parse(e.Args);
            AppSettings settings = shot != null ? shot.MakeSettings() : AppSettings.Load();
            ApplyTheme(settings.Theme);

            var window = new MainWindow(settings, shot != null ? shot.Today : (DateTime?)null);
            MainWindow = window;

            if (shot != null)
            {
                RunShot(window, shot);
                return;
            }
            window.Show();
            Task start = window.StartAsync();
        }

        void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("문제가 생겨서 앱을 닫습니다.\n\n" + e.Exception.Message, "오늘급식2", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown(1);
        }

        async void RunShot(MainWindow window, ShotOptions o)
        {
            try
            {
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -30000;
                window.Top = -30000;
                if (o.Width > 0 && o.Height > 0)
                {
                    window.Width = o.Width;
                    window.Height = o.Height;
                }
                window.Show();

                await window.StartAsync();
                if (o.School != null && o.View == "month") await window.ShowMonthAsync(window.Today);
                if (o.School != null && o.View == "year") await window.ShowYearAsync(window.Today.Year);
                if (o.Query != null) window.SetYearQuery(o.Query);
                if (o.Panel != null) window.OpenPanelNow(o.Panel);
                if (o.Search != null) await window.SearchSchoolNow(o.Search);

                await Task.Delay(400);
                window.SaveSnapshot(o.Path);
            }
            catch (Exception ex)
            {
                File.WriteAllText(o.Path + ".error.txt", ex.ToString());
            }
            Shutdown();
        }
    }
}
