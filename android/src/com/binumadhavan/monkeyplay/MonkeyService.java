package com.binumadhavan.monkeyplay;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.content.pm.ServiceInfo;
import android.content.res.Configuration;
import android.graphics.PixelFormat;
import android.graphics.PointF;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.media.AudioManager;
import android.os.BatteryManager;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.SystemClock;
import android.provider.Settings;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.Surface;
import android.view.View;
import android.view.WindowManager;

import java.util.Calendar;

// Keeps the monkey on screen over other apps. Two windows: a big one it's drawn in, which lets every touch
// through to the app underneath, and a small invisible one over its body that catches your touches.
public class MonkeyService extends Service implements SensorEventListener, SharedPreferences.OnSharedPreferenceChangeListener {
    static final String ACTION_TOGGLE = "com.binumadhavan.monkeyplay.TOGGLE", ACTION_QUIT = "com.binumadhavan.monkeyplay.QUIT";
    static final String CHANNEL = "monkey";
    static volatile boolean running;

    WindowManager wm;
    MonkeyView monkey;
    View hit;
    WindowManager.LayoutParams drawLp, hitLp;
    final Handler handler = new Handler(Looper.getMainLooper());
    SharedPreferences prefs;
    SensorManager sensors;
    AudioManager audio;
    boolean hidden, screenOn = true, shown;
    float S;
    int winX, winY, hitSize;
    long lastFrameNanos;
    double lastShake = -100;
    double nextWater, nextEyes, nextChecks;
    int lastChimeHour = -1, batteryWarned = 101;
    boolean fullToldOnce;

    static final double WaterEvery = 30 * 60, EyesEvery = 20 * 60;

    @Override public IBinder onBind(Intent i) { return null; }

    @Override
    public void onCreate() {
        super.onCreate();
        running = true;
        prefs = getSharedPreferences("monkey", MODE_PRIVATE);
        prefs.registerOnSharedPreferenceChangeListener(this);
        wm = (WindowManager) getSystemService(WINDOW_SERVICE);
        sensors = (SensorManager) getSystemService(SENSOR_SERVICE);
        audio = (AudioManager) getSystemService(AUDIO_SERVICE);
        startForegroundNotice();

        monkey = new MonkeyView(this);
        hit = new View(this);
        hit.setOnTouchListener(this::onHitTouch);
        applySettings();

        IntentFilter f = new IntentFilter();
        f.addAction(Intent.ACTION_SCREEN_OFF);
        f.addAction(Intent.ACTION_SCREEN_ON);
        f.addAction(Intent.ACTION_USER_PRESENT);
        registerReceiver(screenReceiver, f);
        registerReceiver(batteryReceiver, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));

        nextWater = WaterEvery;
        nextEyes = EyesEvery;
        if (Settings.canDrawOverlays(this)) show();
        else { stopSelf(); return; }
        monkey.say(monkey.greeting(), true);
        handler.post(loop);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String a = intent != null ? intent.getAction() : null;
        if (ACTION_QUIT.equals(a)) { prefs.edit().putBoolean("on", false).apply(); stopSelf(); }
        else if (ACTION_TOGGLE.equals(a)) { hidden = !hidden; if (hidden) hide(); else show(); startForegroundNotice(); }
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        running = false;
        handler.removeCallbacksAndMessages(null);
        prefs.unregisterOnSharedPreferenceChangeListener(this);
        try { unregisterReceiver(screenReceiver); } catch (Exception ignored) { }
        try { unregisterReceiver(batteryReceiver); } catch (Exception ignored) { }
        sensors.unregisterListener(this);
        hide();
        super.onDestroy();
    }

    void startForegroundNotice() {
        NotificationManager nm = getSystemService(NotificationManager.class);
        if (nm.getNotificationChannel(CHANNEL) == null)
            nm.createNotificationChannel(new NotificationChannel(CHANNEL, "Monkey on screen", NotificationManager.IMPORTANCE_MIN));
        int pf = PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT;
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), pf);
        PendingIntent toggle = PendingIntent.getService(this, 1, new Intent(this, MonkeyService.class).setAction(ACTION_TOGGLE), pf);
        PendingIntent quit = PendingIntent.getService(this, 2, new Intent(this, MonkeyService.class).setAction(ACTION_QUIT), pf);
        Notification n = new Notification.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_notify)
            .setContentTitle(hidden ? "Monkey is taking a break" : "Monkey is hanging around")
            .setContentText("Tap for settings")
            .setContentIntent(open)
            .setOngoing(true)
            .addAction(new Notification.Action.Builder(null, hidden ? "Show monkey" : "Hide monkey", toggle).build())
            .addAction(new Notification.Action.Builder(null, "Quit", quit).build())
            .build();
        if (Build.VERSION.SDK_INT >= 34) startForeground(1, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        else startForeground(1, n);
    }

    // ---- Windows ----

    void applySettings() {
        int size = prefs.getInt("size", 1);
        float density = getResources().getDisplayMetrics().density;
        S = density * (size == 0 ? 0.5f : size == 2 ? 0.75f : 0.62f);
        monkey.S = S;
        monkey.tiltOn = prefs.getBoolean("tilt", true);
        updateSensors();
        if (shown) { hide(); show(); }
    }

    @Override
    public void onSharedPreferenceChanged(SharedPreferences p, String key) {
        if ("size".equals(key) || "tilt".equals(key)) applySettings();
    }

    @Override
    public void onConfigurationChanged(Configuration c) {
        super.onConfigurationChanged(c);
        if (shown) { hide(); show(); }   // turned the phone: back to the top-right corner
    }

    void place() {
        int width;
        if (Build.VERSION.SDK_INT >= 30) width = wm.getCurrentWindowMetrics().getBounds().width();
        else {
            android.util.DisplayMetrics dm = new android.util.DisplayMetrics();
            wm.getDefaultDisplay().getRealMetrics(dm);
            width = dm.widthPixels;
        }
        int top = 0;
        int id = getResources().getIdentifier("status_bar_height", "dimen", "android");
        if (id > 0) top = getResources().getDimensionPixelSize(id);
        // The branch near the top-right, coming out from under the status bar (the tree is up there).
        winX = Math.round(width - 230 * S - MonkeyView.PX * S);
        winY = top;
    }

    void show() {
        if (shown || hidden) return;
        place();
        int type = WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY;
        drawLp = new WindowManager.LayoutParams(
            Math.round(MonkeyView.W * S), Math.round(MonkeyView.H * S), type,
            WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE | WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN
                | WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED,
            PixelFormat.TRANSLUCENT);
        drawLp.gravity = Gravity.TOP | Gravity.LEFT;
        drawLp.x = winX; drawLp.y = winY;
        // Android only lets touches through an overlay to the app below when it's at most 80% opaque.
        drawLp.alpha = 0.8f;
        drawLp.setTitle("Monkey");
        hitSize = Math.round(110 * S);
        hitLp = new WindowManager.LayoutParams(hitSize, hitSize, type,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS
                | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
            PixelFormat.TRANSLUCENT);
        hitLp.gravity = Gravity.TOP | Gravity.LEFT;
        hitLp.setTitle("Monkey touch");
        moveHit(true);
        wm.addView(monkey, drawLp);
        wm.addView(hit, hitLp);
        shown = true;
        updateSensors();
    }

    void hide() {
        if (!shown) return;
        try { wm.removeView(hit); } catch (Exception ignored) { }
        try { wm.removeView(monkey); } catch (Exception ignored) { }
        shown = false;
        updateSensors();
    }

    // The touch area follows its body.
    void moveHit(boolean force) {
        if (monkey.grabbed && !force) return;   // your finger is on it: leave it be
        PointF bc = monkey.bodyCenter();
        int nx = Math.round(winX + bc.x * S - hitSize / 2f), ny = Math.round(winY + bc.y * S - hitSize / 2f);
        if (!force && Math.abs(nx - hitLp.x) + Math.abs(ny - hitLp.y) < 4) return;
        hitLp.x = nx; hitLp.y = ny;
        if (shown) wm.updateViewLayout(hit, hitLp);
    }

    // ---- Touch ----

    final Runnable longPress = () -> {
        // Hold it: open the settings.
        if (!monkey.grabbed || monkey.moved) return;
        monkey.grabbed = false;
        monkey.fingerX = Float.NaN;
        monkey.say("Settings!", true);
        startActivity(new Intent(this, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
    };

    boolean onHitTouch(View v, MotionEvent e) {
        float wx = (e.getRawX() - winX) / S, wy = (e.getRawY() - winY) / S;
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                monkey.touchDown(wx, wy);
                handler.postDelayed(longPress, 700);
                kick();
                return true;
            case MotionEvent.ACTION_MOVE:
                monkey.touchMove(wx, wy);
                if (monkey.moved) handler.removeCallbacks(longPress);
                return true;
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_CANCEL:
                handler.removeCallbacks(longPress);
                if (monkey.touchUp(wx, wy)) tellSomething();
                moveHit(true);
                return true;
        }
        return false;
    }

    // Double tap: the time and the battery.
    void tellSomething() {
        Calendar c = Calendar.getInstance();
        int h = c.get(Calendar.HOUR), m = c.get(Calendar.MINUTE);
        String time = (h == 0 ? 12 : h) + ":" + (m < 10 ? "0" : "") + m + (c.get(Calendar.AM_PM) == Calendar.AM ? " AM" : " PM");
        BatteryManager bm = (BatteryManager) getSystemService(BATTERY_SERVICE);
        int level = bm.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY);
        monkey.say("It's " + time + ". Battery " + level + "%.", true, 3);
    }

    // ---- The frame loop ----

    final Runnable loop = new Runnable() {
        @Override public void run() {
            if (!running) return;
            long now = SystemClock.elapsedRealtimeNanos();
            double dt = lastFrameNanos == 0 ? 1 / 60.0 : (now - lastFrameNanos) / 1e9;
            lastFrameNanos = now;
            if (shown && screenOn) {
                checks();
                monkey.frame(dt);
                moveHit(false);
                handler.postDelayed(this, Math.max(4, Math.round(monkey.frameInterval() * 1000)));
            } else handler.postDelayed(this, 500);   // nobody can see it: nothing to draw
        }
    };

    void kick() {
        // Wake the loop straight away (it may be waiting a while between calm frames).
        handler.removeCallbacks(loop);
        handler.post(loop);
    }

    // Once a second: music, reminders and the hourly chime.
    void checks() {
        double now = monkey.clock;
        if (now < nextChecks) return;
        nextChecks = now + 1;
        boolean music = audio.isMusicActive();
        if (music && !monkey.musicOn) monkey.say(monkey.pick("Ooh, I love this song!", "Let's dance!", "Nice beat!", "Turn it up!"), false);
        monkey.musicOn = music;

        boolean free = !monkey.grabbed && monkey.sleepW < 0.05f && monkey.act == MonkeyView.Act.None;
        if (!prefs.getBoolean("reminders", true)) { nextWater = Math.max(nextWater, now + WaterEvery); nextEyes = Math.max(nextEyes, now + EyesEvery); }
        else if (free && now >= nextWater) {
            nextWater = now + WaterEvery;
            monkey.say(monkey.pick("Water break! Take a sip.", "Time to drink some water!", "Hydration time! Have a sip."), true, 4.5);
            monkey.grinTime = 1;
        } else if (free && now >= nextEyes) {
            nextEyes = now + EyesEvery;
            monkey.startEyeRest();
            monkey.say(monkey.pick("Blink your eyes! Rest them a moment.", "Look away from the screen for a bit.", "Eye break! Look at something far away."), true, 4);
        }

        Calendar c = Calendar.getInstance();
        int hour = c.get(Calendar.HOUR_OF_DAY);
        if (lastChimeHour < 0) lastChimeHour = hour;
        if (hour != lastChimeHour && c.get(Calendar.MINUTE) == 0) {
            lastChimeHour = hour;
            if (prefs.getBoolean("chime", true) && monkey.sleepW < 0.5f) {
                int h12 = hour % 12 == 0 ? 12 : hour % 12;
                monkey.startWave();
                monkey.say("It's " + h12 + " o'clock!", true, 4);
            }
        }
    }

    // ---- Screen, battery, tilt ----

    final BroadcastReceiver screenReceiver = new BroadcastReceiver() {
        @Override public void onReceive(Context c, Intent i) {
            String a = i.getAction();
            if (Intent.ACTION_SCREEN_OFF.equals(a)) screenOn = false;
            else if (Intent.ACTION_SCREEN_ON.equals(a)) { screenOn = true; lastFrameNanos = 0; }
            else if (Intent.ACTION_USER_PRESENT.equals(a) && shown) {
                // Unlocked: it's pleased to see you.
                monkey.wakeUp();
                monkey.startWave();
                monkey.say(monkey.pick("Welcome back!", "Oh, hi again!", "There you are!"), true);
            }
            updateSensors();
            kick();
        }
    };

    final BroadcastReceiver batteryReceiver = new BroadcastReceiver() {
        @Override public void onReceive(Context c, Intent i) {
            int level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1), scale = i.getIntExtra(BatteryManager.EXTRA_SCALE, 100);
            if (level < 0) return;
            int pct = Math.round(level * 100f / scale);
            boolean plugged = i.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0;
            if (!prefs.getBoolean("battery", true) || !shown) return;
            if (plugged) {
                batteryWarned = 101;
                if (pct >= 100 && !fullToldOnce) { fullToldOnce = true; monkey.say("Fully charged! You can unplug now.", true, 4); }
                return;
            }
            fullToldOnce = false;
            if (pct <= 10 && batteryWarned > 10) { batteryWarned = 10; monkey.wakeUp(); monkey.say("Battery " + pct + "%! Plug me in, quick!", true, 5); }
            else if (pct <= 20 && batteryWarned > 20) { batteryWarned = 20; monkey.wakeUp(); monkey.say("Battery's at " + pct + "%. Time to charge?", true, 5); }
        }
    };

    void updateSensors() {
        sensors.unregisterListener(this);
        if (!shown || !screenOn) return;
        Sensor acc = sensors.getDefaultSensor(Sensor.TYPE_ACCELEROMETER);
        if (acc != null) sensors.registerListener(this, acc, SensorManager.SENSOR_DELAY_UI);
    }

    final float[] grav = new float[3];

    @Override
    public void onSensorChanged(SensorEvent e) {
        // Split the reading into gravity (slow) and the phone's own movement (quick).
        for (int i = 0; i < 3; i++) grav[i] += (e.values[i] - grav[i]) * 0.15f;
        float lx = e.values[0] - grav[0], ly = e.values[1] - grav[1], lz = e.values[2] - grav[2];
        // Gravity across the screen, whichever way the phone is turned.
        int rot = wm.getDefaultDisplay().getRotation();
        float gx, jx, jy;   // gravity's pull sideways, and the phone's movement, across the screen
        switch (rot) {
            case Surface.ROTATION_90: gx = grav[1]; jx = -ly; jy = -lx; break;
            case Surface.ROTATION_180: gx = grav[0]; jx = -lx; jy = ly; break;
            case Surface.ROTATION_270: gx = -grav[1]; jx = ly; jy = lx; break;
            default: gx = -grav[0]; jx = lx; jy = -ly; break;
        }
        monkey.tiltX = Math.max(-1, Math.min(1, gx / SensorManager.GRAVITY_EARTH));
        // A shake throws it about.
        double quick = Math.sqrt(lx * lx + ly * ly + lz * lz);
        if (quick > 9 && monkey.tiltOn && !monkey.grabbed) {
            monkey.jolt(-jx * 60, -jy * 60);   // the phone moves, the monkey's left behind
            if (monkey.clock - lastShake > 4) monkey.say(monkey.pick("Whoa! Earthquake!", "Easy there!", "Wheee! Again!"), true);
            lastShake = monkey.clock;
            kick();
        }
    }

    @Override public void onAccuracyChanged(Sensor s, int a) { }
}
