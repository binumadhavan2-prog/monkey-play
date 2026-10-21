package com.binumadhavan.monkeyplay;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Switch;
import android.widget.TextView;

// The settings screen: the monkey itself at the top (live, you can play with it), whether it's out, its size,
// and what it does.
public class MainActivity extends Activity {
    static final int Teal = Color.rgb(38, 104, 106), TealDark = Color.rgb(24, 74, 76), Brown = Color.rgb(92, 52, 26);
    static final int Page = Color.rgb(247, 243, 234), Card = Color.WHITE, Ink = Color.rgb(40, 34, 30), Muted = Color.rgb(118, 110, 100);
    static final int Line = Color.rgb(236, 230, 218), Green = Color.rgb(64, 170, 90);
    SharedPreferences prefs;
    Typeface font;
    LinearLayout col;
    View permissionCard, statusCard;
    TextView statusText, statusButton;
    View statusDot;
    TextView[] sizeButtons = new TextView[3];
    MonkeyView preview;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        prefs = getSharedPreferences("monkey", MODE_PRIVATE);
        font = MonkeyView.font(this);
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[] { Manifest.permission.POST_NOTIFICATIONS }, 1);

        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(Page);
        scroll.setClipToPadding(false);
        col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        col.setPadding(dp(18), dp(28), dp(18), dp(28));
        scroll.addView(col);
        setContentView(scroll);

        // The monkey, live, hanging in a little sky.
        FrameLayout hero = new FrameLayout(this);
        GradientDrawable sky = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
            new int[] { Color.rgb(196, 230, 228), Color.rgb(236, 246, 240), Color.rgb(252, 248, 238) });
        sky.setCornerRadius(dp(28));
        hero.setBackground(sky);
        hero.setClipToOutline(true);
        hero.setElevation(dp(2));
        preview = new MonkeyView(this);
        preview.preview = true;
        preview.nextSit = 25;
        preview.say("Hi! Drag me, tap me!", true, 3);
        hero.addView(preview, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        hero.addOnLayoutChangeListener((v, l, t, r, bt, ol, ot, or, ob) -> {
            // Sized so its full swing fits; the top of the branch runs off the card like it runs off the screen.
            int w = r - l, h = Math.round(w * MonkeyView.H / (float) MonkeyView.W);
            ViewGroup.LayoutParams p = preview.getLayoutParams();
            if (p.height != h) { p.height = h; preview.setLayoutParams(p); }
        });
        LinearLayout.LayoutParams hp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0);
        col.addView(hero, hp);
        col.post(() -> { hp.height = Math.round(hero.getWidth() * 380f / MonkeyView.W); hero.setLayoutParams(hp); });

        TextView title = text("Monkey Play", 32, Brown, 900);
        title.setPadding(dp(4), dp(18), 0, 0);
        col.addView(title);
        TextView by = text("A cool monkey for your screen, by Binu Madhavan", 15, Muted, 600);
        by.setPadding(dp(4), 0, 0, dp(14));
        col.addView(by);

        // Not allowed over other apps yet: the one thing to do first.
        LinearLayout perm = card();
        perm.addView(text("One step to go", 18, Ink, 800));
        TextView pt = text("Let Monkey Play show over other apps, so the monkey can hang out on your screen.", 15, Muted, 600);
        pt.setPadding(0, dp(4), 0, dp(14));
        perm.addView(pt);
        perm.addView(pill("Allow Monkey over other apps", v ->
            startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + getPackageName())))));
        permissionCard = perm;
        col.addView(perm);

        // Out or put away.
        LinearLayout status = card();
        LinearLayout row = new LinearLayout(this);
        row.setGravity(Gravity.CENTER_VERTICAL);
        statusDot = new View(this);
        row.addView(statusDot, new LinearLayout.LayoutParams(dp(12), dp(12)));
        statusText = text("", 18, Ink, 800);
        statusText.setPadding(dp(10), 0, 0, 0);
        row.addView(statusText);
        status.addView(row);
        TextView st = text("When it's out, it hangs at the top-right of your screen over every app.", 15, Muted, 600);
        st.setPadding(0, dp(4), 0, dp(14));
        status.addView(st);
        statusButton = pill("", v -> {
            if (MonkeyService.running) {
                prefs.edit().putBoolean("on", false).apply();
                stopService(new Intent(this, MonkeyService.class));
            } else {
                prefs.edit().putBoolean("on", true).apply();
                startForegroundService(new Intent(this, MonkeyService.class));
            }
            col.postDelayed(this::refresh, 250);
        });
        status.addView(statusButton);
        statusCard = status;
        col.addView(status);

        // Size.
        LinearLayout size = card();
        size.addView(text("Size", 18, Ink, 800));
        LinearLayout seg = new LinearLayout(this);
        seg.setPadding(dp(4), dp(4), dp(4), dp(4));
        seg.setBackground(round(Color.rgb(240, 236, 228), dp(24), 0, 0));
        String[] names = { "Small", "Medium", "Large" };
        for (int i = 0; i < 3; i++) {
            int which = i;
            TextView s = text(names[i], 15, Ink, 800);
            s.setGravity(Gravity.CENTER);
            s.setOnClickListener(v -> { prefs.edit().putInt("size", which).apply(); refreshSize(); });
            seg.addView(s, new LinearLayout.LayoutParams(0, dp(42), 1));
            sizeButtons[i] = s;
        }
        LinearLayout.LayoutParams sp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        sp.topMargin = dp(12);
        size.addView(seg, sp);
        col.addView(size);

        // What it does.
        LinearLayout does = card();
        does.addView(text("What it does", 18, Ink, 800));
        toggle(does, "tilt", "Swings when you tilt the phone", "Shake it and it gets thrown about", true, true);
        toggle(does, "reminders", "Water and eye breaks", "A drink every 30 minutes, an eye break every 20", true, false);
        toggle(does, "battery", "Battery reminders", "At 20% and 10%, and when it's fully charged", true, false);
        toggle(does, "chime", "Says the time", "Waves and tells you on the hour", true, false);
        toggle(does, "fullscreen", "Hides for videos and games", "Steps aside when an app goes full screen", true, false);
        toggle(does, "boot", "Starts with the phone", "Comes back after a restart", false, false);
        col.addView(does);

        // How to play.
        LinearLayout how = card();
        how.addView(text("Playing with it", 18, Ink, 800));
        String[][] tips = {
            { "Drag", "and let go to fling it" }, { "Tap", "it to make it grin" },
            { "Double-tap", "for the time and battery" }, { "Hold", "it to open these settings" },
            { "Tilt or shake", "your phone and it swings" } };
        for (String[] tip : tips) {
            TextView tv = text("", 15, Muted, 600);
            android.text.SpannableString s = new android.text.SpannableString(tip[0] + "  " + tip[1]);
            s.setSpan(new android.text.style.ForegroundColorSpan(Teal), 0, tip[0].length(), 0);
            tv.setText(s);
            tv.setPadding(0, dp(8), 0, 0);
            how.addView(tv);
        }
        TextView more = text("It also eats bananas, drinks water, sits on its branch, sings, dances when music plays and sleeps at night.", 14, Muted, 600);
        more.setPadding(0, dp(12), 0, 0);
        how.addView(more);
        col.addView(how);

        TextView foot = text("No internet. Nothing leaves your phone.\nMonkey Play © 2026 Binu Madhavan", 13, Muted, 600);
        foot.setGravity(Gravity.CENTER);
        foot.setPadding(0, dp(18), 0, 0);
        col.addView(foot, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        refreshSize();
    }

    @Override
    protected void onResume() {
        super.onResume();
        // Just came back from allowing it: start the monkey straight away.
        if (Settings.canDrawOverlays(this) && !MonkeyService.running && prefs.getBoolean("on", true)) {
            prefs.edit().putBoolean("on", true).apply();
            startForegroundService(new Intent(this, MonkeyService.class));
        }
        MonkeyService.settingsOpen = true;   // the one up top is enough while you're here
        MonkeyService.refresh(this);
        refresh();
        col.postDelayed(this::refresh, 300);
    }

    @Override
    protected void onPause() {
        super.onPause();
        MonkeyService.settingsOpen = false;
        MonkeyService.refresh(this);
    }

    void refresh() {
        boolean allowed = Settings.canDrawOverlays(this);
        permissionCard.setVisibility(allowed ? View.GONE : View.VISIBLE);
        statusCard.setVisibility(allowed ? View.VISIBLE : View.GONE);
        boolean on = MonkeyService.running;
        statusText.setText(on ? "Monkey is out" : "Monkey is resting");
        statusDot.setBackground(round(on ? Green : Color.rgb(200, 192, 180), dp(6), 0, 0));
        statusButton.setText(on ? "Put the monkey away" : "Bring out the monkey");
        statusButton.setBackground(on ? round(Color.TRANSPARENT, dp(26), dp(2), Teal) : round(Teal, dp(26), 0, 0));
        statusButton.setTextColor(on ? Teal : Color.WHITE);
    }

    void refreshSize() {
        int sel = prefs.getInt("size", 1);
        for (int i = 0; i < 3; i++) {
            boolean on = i == sel;
            sizeButtons[i].setBackground(on ? round(Teal, dp(20), 0, 0) : null);
            sizeButtons[i].setTextColor(on ? Color.WHITE : Muted);
        }
    }

    // ---- Building blocks ----

    int dp(float v) { return Math.round(v * getResources().getDisplayMetrics().density); }

    static GradientDrawable round(int fillColor, float radius, int stroke, int strokeColor) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(fillColor);
        d.setCornerRadius(radius);
        if (stroke > 0) d.setStroke(stroke, strokeColor);
        return d;
    }

    TextView text(String s, float size, int color, int weight) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(size);
        t.setTextColor(color);
        t.setTypeface(font);
        t.setFontVariationSettings("'wght' " + weight);
        t.setLineSpacing(0, 1.15f);
        t.setIncludeFontPadding(false);
        return t;
    }

    LinearLayout card() {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setPadding(dp(20), dp(18), dp(20), dp(18));
        c.setBackground(round(Card, dp(22), 0, 0));
        c.setElevation(dp(1));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.topMargin = dp(12);
        c.setLayoutParams(lp);
        return c;
    }

    TextView pill(String s, View.OnClickListener click) {
        TextView b = text(s, 16, Color.WHITE, 800);
        b.setGravity(Gravity.CENTER);
        b.setBackground(round(Teal, dp(26), 0, 0));
        b.setForeground(getDrawable(android.R.drawable.list_selector_background));
        b.setClickable(true);
        b.setOnClickListener(click);
        b.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(52)));
        return b;
    }

    void toggle(LinearLayout parent, String key, String label, String detail, boolean def, boolean first) {
        if (!first) {
            View line = new View(this);
            line.setBackgroundColor(Line);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(1));
            parent.addView(line, lp);
        }
        LinearLayout row = new LinearLayout(this);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(0, dp(12), 0, dp(12));
        LinearLayout words = new LinearLayout(this);
        words.setOrientation(LinearLayout.VERTICAL);
        words.addView(text(label, 16, Ink, 700));
        TextView d = text(detail, 13, Muted, 600);
        d.setPadding(0, dp(3), 0, 0);
        words.addView(d);
        row.addView(words, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));
        Switch s = new Switch(this);
        s.setChecked(prefs.getBoolean(key, def));
        int[][] states = { { android.R.attr.state_checked }, {} };
        s.setThumbTintList(new ColorStateList(states, new int[] { Color.WHITE, Color.WHITE }));
        s.setTrackTintList(new ColorStateList(states, new int[] { Teal, Color.rgb(205, 198, 186) }));
        s.setTrackTintMode(android.graphics.PorterDuff.Mode.SRC);
        s.setOnCheckedChangeListener((v, on) -> prefs.edit().putBoolean(key, on).apply());
        row.setOnClickListener(v -> s.toggle());
        row.addView(s);
        parent.addView(row);
    }
}
