package com.binumadhavan.monkeyplay;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.ScrollView;
import android.widget.Switch;
import android.widget.TextView;

// The settings screen: let it draw over other apps, start or stop it, and choose what it does.
public class MainActivity extends Activity {
    static final int Teal = Color.rgb(38, 104, 106), Brown = Color.rgb(112, 64, 32);
    SharedPreferences prefs;
    LinearLayout col;
    Button permission, startStop;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        prefs = getSharedPreferences("monkey", MODE_PRIVATE);
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[] { Manifest.permission.POST_NOTIFICATIONS }, 1);

        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(Color.rgb(250, 247, 240));
        col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        int pad = dp(20);
        col.setPadding(pad, pad + dp(16), pad, pad);
        scroll.addView(col);
        setContentView(scroll);

        TextView title = text("Monkey Play", 28, Brown);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        col.addView(title);
        col.addView(text("A cool monkey that hangs out on your screen. Created by Binu Madhavan.", 15, Color.DKGRAY));
        gap(12);
        col.addView(text("Drag it and let go to fling it. Tap it to make it grin. Double-tap for the time and battery. "
            + "Hold it to come back here. Tilt or shake your phone and it swings.", 15, Color.DKGRAY));
        gap(16);

        permission = button("Allow Monkey over other apps", v ->
            startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + getPackageName()))));
        col.addView(permission);
        startStop = button("", v -> {
            if (MonkeyService.running) {
                prefs.edit().putBoolean("on", false).apply();
                stopService(new Intent(this, MonkeyService.class));
            } else {
                prefs.edit().putBoolean("on", true).apply();
                startForegroundService(new Intent(this, MonkeyService.class));
            }
            col.postDelayed(this::refresh, 300);
        });
        col.addView(startStop);
        gap(20);

        col.addView(heading("Size"));
        RadioGroup size = new RadioGroup(this);
        size.setOrientation(RadioGroup.HORIZONTAL);
        String[] names = { "Small", "Medium", "Large" };
        for (int i = 0; i < 3; i++) {
            RadioButton r = new RadioButton(this);
            r.setText(names[i]);
            r.setId(100 + i);
            r.setTextSize(15);
            size.addView(r);
        }
        size.check(100 + prefs.getInt("size", 1));
        size.setOnCheckedChangeListener((g, id) -> prefs.edit().putInt("size", id - 100).apply());
        col.addView(size);
        gap(12);

        col.addView(heading("What it does"));
        col.addView(toggle("tilt", "Swings when you tilt or shake the phone", true));
        col.addView(toggle("reminders", "Water (every 30 min) and eye break (every 20 min) reminders", true));
        col.addView(toggle("battery", "Battery reminders (at 20% and 10%, and when it's full)", true));
        col.addView(toggle("chime", "Says the time on the hour", true));
        col.addView(toggle("boot", "Start when the phone starts", false));
        gap(16);
        col.addView(text("It sleeps from 10 PM to 6 AM, and dances when music is playing. "
            + "The notification lets you hide it or quit. It needs no internet and sends nothing anywhere.", 13, Color.GRAY));
    }

    @Override
    protected void onResume() {
        super.onResume();
        refresh();
        // Just came back from allowing it: start the monkey straight away.
        if (Settings.canDrawOverlays(this) && !MonkeyService.running && prefs.getBoolean("on", true)) {
            prefs.edit().putBoolean("on", true).apply();
            startForegroundService(new Intent(this, MonkeyService.class));
            col.postDelayed(this::refresh, 300);
        }
    }

    void refresh() {
        boolean allowed = Settings.canDrawOverlays(this);
        permission.setVisibility(allowed ? View.GONE : View.VISIBLE);
        startStop.setVisibility(allowed ? View.VISIBLE : View.GONE);
        startStop.setText(MonkeyService.running ? "Put the monkey away" : "Bring out the monkey");
    }

    int dp(float v) { return Math.round(v * getResources().getDisplayMetrics().density); }

    void gap(int h) { View v = new View(this); col.addView(v, new LinearLayout.LayoutParams(1, dp(h))); }

    TextView text(String s, float size, int color) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(size);
        t.setTextColor(color);
        t.setLineSpacing(0, 1.15f);
        return t;
    }

    TextView heading(String s) {
        TextView t = text(s, 17, Teal);
        t.setTypeface(Typeface.DEFAULT_BOLD);
        t.setPadding(0, dp(6), 0, dp(4));
        return t;
    }

    Button button(String s, View.OnClickListener click) {
        Button b = new Button(this);
        b.setText(s);
        b.setAllCaps(false);
        b.setTextSize(16);
        b.setTextColor(Color.WHITE);
        b.setBackgroundColor(Teal);
        b.setOnClickListener(click);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(52));
        lp.topMargin = dp(8);
        b.setLayoutParams(lp);
        return b;
    }

    Switch toggle(String key, String label, boolean def) {
        Switch s = new Switch(this);
        s.setText(label);
        s.setTextSize(15);
        s.setGravity(Gravity.CENTER_VERTICAL);
        s.setPadding(0, dp(8), 0, dp(8));
        s.setChecked(prefs.getBoolean(key, def));
        s.setOnCheckedChangeListener((v, on) -> prefs.edit().putBoolean(key, on).apply());
        return s;
    }
}
