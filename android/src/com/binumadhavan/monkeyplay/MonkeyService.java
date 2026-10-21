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
import android.util.DisplayMetrics;
import android.view.Choreographer;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.Surface;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowManager;

import java.util.Calendar;

// Keeps the monkey on screen over other apps. Two windows: a big one it's drawn in, which lets every touch
// through to the app underneath, and a small invisible one over its body that catches your touches.
// A third, invisible strip notices when another app goes full screen (a video, a game) so it can step aside.
public class MonkeyService extends Service implements SensorEventListener, SharedPreferences.OnSharedPreferenceChangeListener {
    static final String ACTION_TOGGLE = "com.binumadhavan.monkeyplay.TOGGLE", ACTION_QUIT = "com.binumadhavan.monkeyplay.QUIT";
    static final String ACTION_REFRESH = "com.binumadhavan.monkeyplay.REFRESH";
    static final String CHANNEL = "monkey";
    static volatile boolean running, settingsOpen;

    // Something it shows depends on changed (the settings screen opened or closed).
    static void refresh(Context c) {
        if (running) c.startService(new Intent(c, MonkeyService.class).setAction(ACTION_REFRESH));
    }

    WindowManager wm;
    MonkeyView monkey;
    View hit, tracker;
    WindowManager.LayoutParams drawLp, hitLp;
    final Handler handler = new Handler(Looper.getMainLooper());
    SharedPreferences prefs;
    SensorManager sensors;
    AudioManager audio;
    boolean hidden, screenOn = true, shown, fullscreen, loopRunning;
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
        if (!Settings.canDrawOverlays(this)) { stopSelf(); return; }

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
        addTracker();
        updateShown();
        monkey.say(monkey.greeting(), true);
        wake();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String a = intent != null ? intent.getAction() : null;
        if (ACTION_QUIT.equals(a)) { prefs.edit().putBoolean("on", false).apply(); stopSelf(); }
        else if (ACTION_TOGGLE.equals(a)) { hidden = !hidden; updateShown(); startForegroundNotice(); }
        else if (ACTION_REFRESH.equals(a) && monkey != null) updateShown();
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        running = false;
        handler.removeCallbacksAndMessages(null);
        Choreographer.getInstance().removeFrameCallback(vsync);
        prefs.unregisterOnSharedPreferenceChangeListener(this);
        try { unregisterReceiver(screenReceiver); } catch (Exception ignored) { }
        try { unregisterReceiver(batteryReceiver); } catch (Exception ignored) { }
        sensors.unregisterListener(this);
        hide();
        if (tracker != null) try { wm.removeView(tracker); } catch (Exception ignored) { }
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
            .setColor(MainActivity.Teal)
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

    int screenWidth() {
        if (Build.VERSION.SDK_INT >= 30) return wm.getCurrentWindowMetrics().getBounds().width();
        DisplayMetrics dm = new DisplayMetrics();
        wm.getDefaultDisplay().getRealMetrics(dm);
        return dm.widthPixels;
    }

    // Monkey units to screen pixels: Small, Medium or Large, but never so big that it swings off the left edge.
    static float scaleFor(int size, float density, int screenWidth) {
        float s = density * (size == 0 ? 0.62f : size == 2 ? 0.98f : 0.8f);
        return Math.min(s, screenWidth / 410f);
    }

    void applySettings() {
        S = scaleFor(prefs.getInt("size", 1), getResources().getDisplayMetrics().density, screenWidth());
        monkey.S = S;
        monkey.woodBitmap = null;
        monkey.tiltOn = prefs.getBoolean("tilt", true);
        updateSensors();
        if (shown) { hide(); updateShown(); }
    }

    @Override
    public void onSharedPreferenceChanged(SharedPreferences p, String key) {
        if ("size".equals(key) || "tilt".equals(key)) applySettings();
        else if ("fullscreen".equals(key)) updateShown();
    }

    @Override
    public void onConfigurationChanged(Configuration c) {
        super.onConfigurationChanged(c);
        applySettings();   // turned the phone: back to the top-right corner, sized for the new width
    }

    void place() {
        int top = 0;
        int id = getResources().getIdentifier("status_bar_height", "dimen", "android");
        if (id > 0) top = getResources().getDimensionPixelSize(id);
        // The branch near the top-right, coming out from under the status bar (the tree is up there).
        winX = Math.round(screenWidth() - 230 * S - MonkeyView.PX * S);
        winY = top - Math.round(10 * S);   // sitting, its tuft just clears the status bar
    }

    // Shown unless you hid it, the settings screen is open (it has its own monkey), or another app is full screen.
    void updateShown() {
        boolean want = !hidden && !settingsOpen && !(fullscreen && prefs.getBoolean("fullscreen", true));
        if (want) show(); else hide();
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
        // From Android 12, touches only pass through an overlay to the app below when it's at most 80% opaque.
        // Before that it can be drawn in full colour.
        drawLp.alpha = Build.VERSION.SDK_INT >= 31 ? 0.8f : 1f;
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
        wake();
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

    // An invisible, untouchable strip down the left edge. When the status bar disappears for another app
    // (a full-screen video or game), it notices, and the monkey steps aside until it's back.
    void addTracker() {
        tracker = new View(this);
        WindowManager.LayoutParams lp = new WindowManager.LayoutParams(1, WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE | WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE,
            PixelFormat.TRANSLUCENT);
        lp.gravity = Gravity.TOP | Gravity.LEFT;
        lp.setTitle("Monkey fullscreen check");
        tracker.setOnApplyWindowInsetsListener((v, insets) -> {
            boolean full = Build.VERSION.SDK_INT >= 30 ? !insets.isVisible(WindowInsets.Type.statusBars()) : insets.getSystemWindowInsetTop() == 0;
            setFullscreen(full);
            return insets;
        });
        if (Build.VERSION.SDK_INT < 30)
            tracker.setOnSystemUiVisibilityChangeListener(vis -> setFullscreen((vis & View.SYSTEM_UI_FLAG_FULLSCREEN) != 0));
        try { wm.addView(tracker, lp); } catch (Exception e) { tracker = null; }
    }

    void setFullscreen(boolean full) {
        if (full == fullscreen) return;
        fullscreen = full;
        // A moment's grace, so a quick flicker of the status bar doesn't make it jump.
        handler.removeCallbacks(applyFullscreen);
        handler.postDelayed(applyFullscreen, full ? 600 : 300);
    }

    final Runnable applyFullscreen = this::updateShown;

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
                wake();
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
    // While it moves, a frame for every screen refresh (smooth); when it's calm, only as many as it needs.

    void step() {
        if (!running || !shown || !screenOn) { loopRunning = false; return; }
        long now = SystemClock.elapsedRealtimeNanos();
        double dt = lastFrameNanos == 0 ? 1 / 60.0 : (now - lastFrameNanos) / 1e9;
        lastFrameNanos = now;
        checks();
        monkey.frame(dt);
        moveHit(false);
        double wait = monkey.frameInterval();
        if (wait <= 0) Choreographer.getInstance().postFrameCallback(vsync);
        else handler.postDelayed(timed, Math.round(wait * 1000));
    }

    final Choreographer.FrameCallback vsync = n -> step();
    final Runnable timed = this::step;

    // Starts the loop, or wakes it straight away if it's waiting a while between calm frames.
    void wake() {
        if (!running || !shown || !screenOn) return;
        handler.removeCallbacks(timed);
        Choreographer.getInstance().removeFrameCallback(vsync);
        if (!loopRunning) lastFrameNanos = 0;
        loopRunning = true;
        Choreographer.getInstance().postFrameCallback(vsync);
    }

    // Once a second: music, reminders and the hourly chime.
    void checks() {
        double now = monkey.clock;
        if (now < nextChecks) return;
        nextChecks = now + 1;
        boolean music = audio.isMusicActive();
        if (music && !monkey.musicOn) monkey.say(monkey.pick("Ooh, I love this song!", "Let's dance!", "Nice beat!", "Turn it up!"), false);
        monkey.musicOn = music;

        boolean free = monkey.free();
        if (!prefs.getBoolean("reminders", true)) { nextWater = Math.max(nextWater, now + WaterEvery); nextEyes = Math.max(nextEyes, now + EyesEvery); }
        else if (free && now >= nextWater) {
            nextWater = now + WaterEvery;
            monkey.startDrink();
            monkey.say(monkey.pick("Water break! Take a sip.", "Time to drink some water!", "Hydration time! Have a sip."), true, 4.5);
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
            else if (Intent.ACTION_SCREEN_ON.equals(a)) screenOn = true;
            else if (Intent.ACTION_USER_PRESENT.equals(a) && shown) {
                // Unlocked: it's pleased to see you.
                monkey.wakeUp();
                monkey.startWave();
                monkey.say(monkey.pick("Welcome back!", "Oh, hi again!", "There you are!"), true);
            }
            updateSensors();
            wake();
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
        if (!shown || !screenOn || !prefs.getBoolean("tilt", true)) { monkey.tiltX = 0; return; }
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
        double tilt = Math.max(-1, Math.min(1, gx / SensorManager.GRAVITY_EARTH));
        if (Math.abs(tilt - monkey.tiltX) > 0.08) wake();   // it'll swing: smooth frames
        monkey.tiltX = tilt;
        // A shake throws it about.
        double quick = Math.sqrt(lx * lx + ly * ly + lz * lz);
        if (quick > 9 && !monkey.grabbed) {
            monkey.jolt(-jx * 60, -jy * 60);   // the phone moves, the monkey's left behind
            if (monkey.clock - lastShake > 4) monkey.say(monkey.pick("Whoa! Earthquake!", "Easy there!", "Wheee! Again!"), true);
            lastShake = monkey.clock;
            wake();
        }
    }

    @Override public void onAccuracyChanged(Sensor s, int a) { }
}
