package io.github.shu8e46.todaymeal2;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.drawable.ColorDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.webkit.JavascriptInterface;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.window.OnBackInvokedDispatcher;

/**
 * 오늘급식2 안드로이드 앱.
 * 화면은 웹 버전(Web 폴더)을 앱 안에 그대로 담아서 보여 준다. 폰/패드 구분은 화면 너비로 웹 쪽 CSS 가 한다.
 * 여기서는 학교·테마 저장, 상태 표시줄 색, 뒤로 버튼만 맡는다.
 */
public class MainActivity extends Activity {
    static final String START = "file:///android_asset/web/index.html";
    static final int LIGHT_BG = Color.rgb(0xFF, 0xFF, 0xFF);
    static final int DARK_BG = Color.rgb(0x14, 0x14, 0x13);

    SharedPreferences prefs;
    FrameLayout root;
    WebView web;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = getSharedPreferences("settings", MODE_PRIVATE);

        root = new FrameLayout(this);
        web = new WebView(this);
        root.addView(web, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        setContentView(root);

        // 상태 표시줄, 아래 버튼 줄, 키보드에 가려지지 않게 그만큼 안쪽으로 들인다
        if (Build.VERSION.SDK_INT >= 30) {
            getWindow().setDecorFitsSystemWindows(false);
            root.setOnApplyWindowInsetsListener((v, insets) -> {
                Insets i = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout() | WindowInsets.Type.ime());
                v.setPadding(i.left, i.top, i.right, i.bottom);
                return WindowInsets.CONSUMED;
            });
        }
        paint(prefs.getString("json", "").contains("\"theme\":\"dark\""));

        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setTextZoom(100);              // 시스템 글자 크기와 상관없이 화면이 깨지지 않게
        s.setSupportZoom(false);
        web.setOverScrollMode(View.OVER_SCROLL_NEVER);
        web.setVerticalScrollBarEnabled(false);
        web.addJavascriptInterface(new Host(), "TodayMealHost");
        web.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri uri = request.getUrl();
                if (uri.toString().startsWith("file:///android_asset/")) return false;
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW, uri));   // 바깥 주소는 브라우저로
                } catch (Exception e) {
                    // 열 앱이 없으면 아무 일도 하지 않는다
                }
                return true;
            }
        });
        web.loadUrl(START);

        if (Build.VERSION.SDK_INT >= 33) {
            getOnBackInvokedDispatcher().registerOnBackInvokedCallback(OnBackInvokedDispatcher.PRIORITY_DEFAULT, this::goBack);
        }
    }

    /** 배경과 상태 표시줄 아이콘 색을 테마에 맞춘다. */
    void paint(boolean dark) {
        int bg = dark ? DARK_BG : LIGHT_BG;
        getWindow().setBackgroundDrawable(new ColorDrawable(bg));
        root.setBackgroundColor(bg);
        web.setBackgroundColor(bg);

        if (Build.VERSION.SDK_INT >= 30) {
            if (Build.VERSION.SDK_INT < 35) {
                getWindow().setStatusBarColor(Color.TRANSPARENT);
                getWindow().setNavigationBarColor(Color.TRANSPARENT);
            }
            WindowInsetsController c = getWindow().getInsetsController();
            if (c != null) {
                int light = WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS | WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS;
                c.setSystemBarsAppearance(dark ? 0 : light, light);
            }
        } else {
            getWindow().setStatusBarColor(bg);
            getWindow().setNavigationBarColor(bg);
            int flags = dark ? 0 : View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR | View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
            getWindow().getDecorView().setSystemUiVisibility(flags);
        }
    }

    /** 뒤로: 열린 탭을 닫거나 오늘 급식으로 돌아가고, 더 할 일이 없으면 앱을 닫는다. */
    void goBack() {
        web.evaluateJavascript("window.todayMealBack ? window.todayMealBack() : false", handled -> {
            if (!"true".equals(handled)) finish();
        });
    }

    @Override
    public void onBackPressed() {
        goBack();
    }

    @Override
    protected void onDestroy() {
        root.removeView(web);
        web.destroy();
        super.onDestroy();
    }

    /** 웹 화면에서 부르는 것들 (app.js 의 TodayMealHost). */
    class Host {
        @JavascriptInterface
        public String load() {
            return prefs.getString("json", "");
        }

        @JavascriptInterface
        public void save(String json) {
            prefs.edit().putString("json", json).apply();
        }

        @JavascriptInterface
        public void setTheme(final boolean dark) {
            runOnUiThread(() -> paint(dark));
        }
    }
}
