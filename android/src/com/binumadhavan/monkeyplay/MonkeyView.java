package com.binumadhavan.monkeyplay;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.DashPathEffect;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PointF;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.os.SystemClock;
import android.text.Layout;
import android.text.StaticLayout;
import android.text.TextPaint;
import android.view.MotionEvent;
import android.view.View;

import java.util.ArrayList;
import java.util.Calendar;
import java.util.HashMap;
import java.util.Random;

// Monkey Play for Android, created by Binu Madhavan. Copyright (c) 2026 Binu Madhavan. All rights reserved.
// The same cool cartoon monkey (shades, puffer jacket) as the Windows app, hanging by its tail from a branch
// at the top-right of the screen. This view draws it and runs its springy physics and habits; MonkeyService
// puts it on screen over other apps and feeds it touches, the phone's tilt, the battery and the time.
// Drawing is in the monkey's own units (as on Windows at 100%), scaled by S to the phone's screen.
public class MonkeyView extends View {
    static final int W = 440, H = 470;
    static final float PX = W / 2f, PY = 124;   // the branch is this far down, leaving room to sit on top of it
    static final int BodyReach = 100;
    static final double L = 110;
    static final double Gravity = 800, Stiffness = 120, Damping = 0.6;
    static final double SwayForce = 30, SwayPeriod = 2.4;
    static final double CalmSpeed = 0.825;
    static final int NightStart = 22, NightEnd = 6;
    static final double StayAwake = 25;
    static final float OL = 1.7f;   // outline thickness
    static final float PivotX = 99, PivotY = -24;   // where the branch comes in from the tree (relative to the wrap)

    static final int Fur = Color.rgb(112, 64, 32);
    static final int Skin = Color.rgb(244, 206, 158);
    static final int Ink = Color.rgb(45, 25, 10);
    static final int Outline = Color.rgb(58, 31, 12);
    static final int Jacket = Color.rgb(38, 104, 106);      // teal puffer
    static final int Sherpa = Color.rgb(241, 233, 214);     // fluffy cream collar and cuffs
    static final int Silver = Color.rgb(205, 208, 215);     // silver glasses frame
    static final int Mouth = Color.rgb(105, 30, 28);
    static final int Tongue = Color.rgb(236, 110, 120);
    static final int BananaYellow = Color.rgb(250, 212, 58), BananaFruit = Color.rgb(252, 244, 212), BananaBrown = Color.rgb(105, 72, 30);
    static final int[] NoteColors = { Color.rgb(255, 120, 170), Color.rgb(90, 210, 240), Color.rgb(255, 210, 70), Color.rgb(160, 120, 255) };

    static final String[] IdleLines = {
        "Just hanging around!", "Ooh ooh ah ah!", "Is it snack time yet?", "Hang in there!", "You're doing great!",
        "Sit up straight! (I'm upside down, but you know.)", "I could swing all day.", "Anyone seen my bananas?",
        "Looking sharp today!", "Stretch break? I'm already stretching.", "These shades? Very cool.",
        "Time flies when you're swinging.", "Tilt your phone, I'll swing!", "Put the phone down for a bit?" };
    static final String[] Lyrics = {
        "La la la ♪", "Ooh ooh, ah ah ♫", "Shoo-be-doo-bop ♪", "Ba-na-na-na ♫", "Dum dee dum ♪",
        "Doo-wop, doo-wop ♫", "Yeah, yeah, yeah! ♪", "Hmm hmm hmmm ♫", "Na na na na, hey! ♪", "Swing it! ♫" };
    static final String[][] Songs = {
        { "Bananas in the morning ♪", "Swinging all the night ♫", "Shades on, feeling cool ♪" },
        { "Hang on, hang on ♫", "Tail around the tree ♪", "Nobody swings like me! ♫" },
        { "Ooh ooh, ah ah ♪", "Tap me once, I grin ♫", "Shake the phone, here we go! ♪" },
        { "Little monkey up so high ♫", "Waving at you passing by ♪", "Ooh ooh ah ah! ♫" } };

    enum Act { None, Scratch, Look, Pump, Climb, Wave, Eat, Drink, EyeRest, Shades, Sing }
    enum Perch { None, Up, Over, Sit, Down }

    static final class Zz { PointF p; double age; float drift; boolean note; String ch = "Z"; int col; }

    final Random rnd = new Random();
    float S = 2;   // screen pixels per monkey unit
    boolean preview;   // drawn in the settings screen: it runs itself and takes its own touches

    // Physics: the hips' position relative to where the tail wraps the branch, and their velocity.
    double x = 0, y = L + Gravity / Stiffness, vx, vy, rope = L;
    double bodyAngle, bodyAngVel, bendPx, bendVel;
    float wrapDy;
    double t, motionNow, frameDt = 1 / 60.0, clock;
    double tiltX;   // sideways pull from the phone's tilt, -1..1

    // Touch.
    boolean grabbed, moved;
    float downX, downY;
    double downTime, grabDx, grabDy, lastTapAt = -10;
    float fingerX = Float.NaN, fingerY;   // in window units, while touched

    // Face.
    float lookX, lookY, faceShift, lag;
    float lookAtX = PX, lookAtY = H;   // where its eyes wander to (window units)
    double nextGlance, blinkTime, nextBlink = 3, grinTime;
    PointF headWin = new PointF(PX, PY + 180);
    boolean shadesOn = true, shadesTarget = true;

    // Habits.
    Act act = Act.None, lastAct = Act.None;
    double actStart, actLen, nextAct = 5, nextSing = 120;
    float actW;
    PointF[] tailBez = { new PointF(), new PointF(), new PointF(), new PointF() };
    static final double ClimbTop = 1.3, ClimbLetGo = 1.9, ClimbRate = 1.8;
    static final double[] RestBlinks = { 0.8, 1.5, 2.2 };

    // Sitting on the branch now and then: it climbs its tail, swings up over the top of the branch,
    // sits a while (legs dangling, hands on the wood), then swings back down and hangs again.
    Perch perch = Perch.None;
    double perchStart, sitUntil, nextSit = 40;
    float perchK;            // 0 hanging .. 1 sitting: drives the swing over the top
    PointF perchFrom = new PointF();
    static final double OverTime = 0.45;

    // Banana snack: bites at these times (seconds into the habit), the peel is tossed at the end. Before that, the
    // free hand peels it, one strip at a time. It starts by fetching the banana from its jacket pocket.
    static final double[] BiteTimes = { 2.95, 3.85, 4.75 };
    static final double TossTime = 5.85, EatLen = 6.55, PeelStart = 0.95, PeelSlot = 0.55;
    static final double GrabTime = 0.4, PullOutTime = 0.15, CarryTime = 0.35;
    static final PointF PocketGrip = new PointF(13, -6), PocketOut = new PointF(17, -26);
    static final RectF PocketRect = new RectF(3.5f, -2, 23.5f, 18);
    static final float PocketScale = 0.75f;
    static final PointF BiteMouth = new PointF(0, 81);
    static final PointF[] PeelClosed = { new PointF(-1.5f, -30), new PointF(5, -29), new PointF(2, -31) };
    static final PointF[] PeelOpen = { new PointF(-13, -1), new PointF(15, 0), new PointF(2, 2) };
    static final PointF[] PeelRoots = { new PointF(-2, -9), new PointF(4, -9), new PointF(1, -9.5f) };
    boolean chewing, biteOpen, peelTossed, peelFlying;
    PointF peelPos = new PointF(), peelVel = new PointF();   // window units
    float peelAngle, peelSpin, peelW;
    double peelAge;
    PointF peelHand = new PointF();
    int peelStrip; boolean peelReaching;

    // Drinking, start to finish: reach for the bottle in its holder, pull it out, bring it round, unscrew the cap,
    // three gulps, "Ahh!", cap back on, bottle back in the holder.
    static final double DrinkLen = 6.0;
    static final double DkReach = 0.35, DkOut = 0.6, DkFront = 1.05, DkCapOff = 1.65, DkLift = 2.05,
                        DkLower = 3.95, DkDown = 4.35, DkCapOn = 4.9, DkBack = 5.35, DkIn = 5.65;
    static final double[] GulpTimes = { 2.35, 2.95, 3.55 };
    static final PointF HolsterNeck = new PointF(15.5f, 22), HolsterOut = new PointF(18, -2), DrinkHold = new PointF(26, 130);
    static final PointF AroundHead = new PointF(56, 58);   // carried round the side of its head, not across its face
    static final RectF HolsterRect = new RectF(8, 27, 23, 47);
    static final float HolsterScale = 0.75f, NeckGrip = 22;
    static final PointF SipLips = new PointF(0.5f, 84);
    static final float SipNeck = 26;
    boolean ahhSaid, gulping;
    float capW;
    PointF capHand = new PointF();

    // Sunglasses on its eyes or pushed up on its head. Either way: hand up, move them, hand down.
    static final double ShadesLen = 1.1, ShadesLift = 0.35, ShadesSwap = 0.6;

    // Singing into a microphone from its pocket, a line of its song at a time.
    static final double SgReach = 0.35, SgOut = 0.6, SgUp = 1.1, SgLine = 2.6, SgDownLen = 1.1;
    static final PointF MicHold = new PointF(17, 116), MicHead = new PointF(9, 97);
    String[] song = Songs[0];
    int songShown;

    // Sleep, dance, speech.
    boolean night, wasAsleep, musicOn, singing;
    double wakeUntil, nightCheckAt, nextZ, breathPhase;
    float sleepW, danceW, singLevel;
    double lastBeat, beatInterval = 0.5, beatPhase, singUntil, singRestUntil;
    int beatCount;
    final ArrayList<Zz> zzz = new ArrayList<>();
    String sayText;
    double sayStart, sayLen, lastSay = -100, nextChat = 20;
    StaticLayout sayLayout;

    // Settings (set by the service).
    boolean tiltOn = true;

    public MonkeyView(Context c) {
        super(c);
        pf.setStyle(Paint.Style.FILL);
        ps.setStyle(Paint.Style.STROKE);
        ps.setStrokeCap(Paint.Cap.ROUND);
        ps.setStrokeJoin(Paint.Join.ROUND);
        Typeface face = font(c);
        textPaint.setTypeface(face);
        textPaint.setFontVariationSettings("'wght' 800");
        textPaint.setTextSize(13f);
        textPaint.setColor(Ink);
        zPaint.setTypeface(face);
        zPaint.setFontVariationSettings("'wght' 900");
    }

    // Nunito, a friendly rounded font that comes with the app, so it looks the same on every phone.
    static Typeface font(Context c) {
        try { return c.getResources().getFont(R.font.nunito); }
        catch (Exception e) { return Typeface.create("sans-serif", Typeface.BOLD); }
    }

    // ---- Speech ----

    String pick(String... options) { return options[rnd.nextInt(options.length)]; }

    // Shows a bubble. 'urgent' lines (reactions to you) skip the polite pause between lines.
    void say(String text, boolean urgent) { say(text, urgent, 0); }

    void say(String text, boolean urgent, double minLen) {
        if (!urgent && clock - lastSay < 6) return;
        sayText = text;
        sayStart = clock;
        sayLen = Math.max(minLen, 1.8 + text.length() * 0.06);
        lastSay = clock;
        sayLayout = null;
    }

    boolean talking() { return sayText != null && clock - sayStart < sayLen * 0.6; }

    String greeting() {
        int h = Calendar.getInstance().get(Calendar.HOUR_OF_DAY);
        if (h < 12) return pick("Good morning!", "Morning! Coffee first?");
        if (h < 17) return pick("Good afternoon!", "Lunch was good? Mine was a banana.");
        return pick("Good evening!", "Long day? You've got this.");
    }

    // ---- Touch (window units; the service converts) ----

    void touchDown(float wx, float wy) {
        if (perchedPose() || perch == Perch.Up) leavePerchNow();   // pulled off its seat
        grabbed = true; moved = false;
        downX = wx; downY = wy; downTime = clock;
        fingerX = wx; fingerY = wy;
        if (sleepW > 0.5f) say(pick("Huh? Five more minutes...", "Wha-? I'm awake! I'm awake!"), true);
        else say(pick("Whoa!", "Hey!", "Eep!", "Where are we going?"), true);
        // Keep the spot you touched under your finger (no jump).
        grabDx = (wx - PX) - x;
        grabDy = (wy - PY) - y;
        vx = vy = 0;
    }

    void touchMove(float wx, float wy) {
        fingerX = wx; fingerY = wy;
        if (Math.abs(wx - downX) + Math.abs(wy - downY) > 8) moved = true;
    }

    // Returns true for a double tap (the service answers it).
    boolean touchUp(float wx, float wy) {
        if (!grabbed) return false;
        grabbed = false;
        fingerX = Float.NaN;
        boolean doubleTap = false;
        if (!moved && clock - downTime < 0.35) {
            // Tap: bump it away from where it was touched.
            if (clock - lastTapAt < 0.45) { lastTapAt = -10; doubleTap = true; }
            else lastTapAt = clock;
            vx += (wx - PX < x ? 1 : -1) * 450;
            vy -= 120;
            grinTime = 1.0;
            if (!doubleTap) say(pick("Hehe!", "That tickles!", "Again! Again!", "Boop!"), true);
        }
        else if (Math.sqrt(vx * vx + vy * vy) > 900) say(pick("Wheee!", "Woohoo!", "Yeehaw!"), true);
        vx = Math.max(-3000, Math.min(3000, vx));
        vy = Math.max(-3000, Math.min(3000, vy));
        return doubleTap;
    }

    void wakeUp() { wakeUntil = Math.max(wakeUntil, clock + StayAwake); }

    // A shake of the phone: a jolt, in window units per second.
    void jolt(double jx, double jy) {
        if (perchedPose()) return;   // sitting, it holds on tight
        vx += jx; vy += jy;
        wakeUp();
        grinTime = Math.max(grinTime, 0.8);
    }

    boolean free() { return !grabbed && sleepW < 0.05f && act == Act.None && (perch == Perch.None || perch == Perch.Sit); }

    void startWave() {
        if (grabbed || perch == Perch.Up) return;
        startAct(Act.Wave, 2.2);
        grinTime = 2.2;
    }

    void startEyeRest() { if (free() && danceW < 0.05f) startAct(Act.EyeRest, 3.4); }

    // The water reminder: it has a drink too.
    void startDrink() { if (free() && danceW < 0.05f) { startAct(Act.Drink, DrinkLen); ahhSaid = false; } }

    void startSing() {
        if (!free() || danceW > 0.05f) return;
        song = Songs[rnd.nextInt(Songs.length)];
        songShown = 0;
        startAct(Act.Sing, SgUp + song.length * SgLine + SgDownLen + 0.3);
        say(pick("Here's a song for you!", "A little song!", "Ahem... mic check!"), true, 1.2);
    }

    // ---- Preview mode (the settings screen): runs itself and takes its own touches ----

    long lastNanos;
    final Runnable tick = new Runnable() {
        @Override public void run() {
            if (!preview || !isAttachedToWindow()) return;
            long now = SystemClock.elapsedRealtimeNanos();
            frame(lastNanos == 0 ? 1 / 60.0 : (now - lastNanos) / 1e9);
            lastNanos = now;
            postOnAnimation(this);
        }
    };

    @Override
    protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        if (preview) { lastNanos = 0; postOnAnimation(tick); }
    }

    @Override
    protected void onSizeChanged(int w, int h, int ow, int oh) {
        super.onSizeChanged(w, h, ow, oh);
        if (preview && w > 0) { S = Math.min(w / (float) W, h / (float) H); woodBitmap = null; }
    }

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        if (!preview) return false;
        float wx = e.getX() / S, wy = e.getY() / S;
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                if (Math.hypot(wx - bodyCenter().x, wy - bodyCenter().y) > 70) return false;
                getParent().requestDisallowInterceptTouchEvent(true);
                touchDown(wx, wy); return true;
            case MotionEvent.ACTION_MOVE: touchMove(wx, wy); return true;
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_CANCEL:
                if (touchUp(wx, wy)) startSing();
                return true;
        }
        return false;
    }

    // ---- Animation ----

    static float ease(double rate, double dt) { return (float) (1 - Math.exp(-rate * dt)); }
    static float smooth(double v) { v = Math.max(0, Math.min(1, v)); return (float) (v * v * (3 - 2 * v)); }
    static float phase(double ap, double a, double b) { return smooth((ap - a) / (b - a)); }
    static PointF lerp(PointF a, PointF b, float k) { return new PointF(a.x + (b.x - a.x) * k, a.y + (b.y - a.y) * k); }
    static PointF P(float x, float y) { return new PointF(x, y); }
    static PointF curve(PointF a, PointF via, PointF b, float k) {
        float u = 1 - k;
        return P(u * u * a.x + 2 * u * k * via.x + k * k * b.x, u * u * a.y + 2 * u * k * via.y + k * k * b.y);
    }
    // Which way to turn something held at 'from' so it points at 'to' (degrees from upright).
    static float angleTo(PointF from, PointF to) { return (float) (Math.atan2(to.x - from.x, -(to.y - from.y)) * 180 / Math.PI); }

    double actAge() { return motionNow - actStart; }
    Act shownAct() { return act != Act.None ? act : lastAct; }
    boolean perchedPose() { return perch == Perch.Over || perch == Perch.Sit || perch == Perch.Down; }
    boolean micSinging() { return act == Act.Sing && actAge() >= SgUp && actAge() < actLen - SgDownLen; }

    // A point placed relative to the face (designed for hanging, head below the hips). Sitting, the body is
    // turned over but the head stays upright, so it's mirrored top-to-bottom about the head's centre.
    static final float HeadY = 70;
    PointF face(PointF p) { return perch == Perch.Sit ? P(p.x, 2 * HeadY - p.y) : p; }

    void startAct(Act a, double len) {
        act = lastAct = a;
        actStart = motionNow;
        actLen = len;
    }

    // Nothing fast going on: fewer frames look the same and save battery.
    boolean calm() {
        return !grabbed && danceW < 0.05f && act != Act.Climb && act != Act.Pump && !peelFlying
            && (perch == Perch.None || perch == Perch.Sit)
            && Math.abs(vx) + Math.abs(vy) < 160 && Math.abs(bodyAngVel) < 1.5 && Math.abs(bendVel) < 20;
    }

    // Seconds until the next frame: every screen refresh when it moves fast, fewer when it's calm.
    double frameInterval() {
        if (!calm()) return 0;
        double fps = 30;
        if (act == Act.None && zzz.isEmpty() && sayText == null) {
            double r = Math.max(1, Math.sqrt(x * x + y * y));
            double speed = Math.max(Math.sqrt(vx * vx + vy * vy), Math.abs(bodyAngVel) * (r + 70)) * S;
            fps = Math.max(15, Math.min(30, speed / 3));
        }
        if (sleepW > 0.9f && zzz.isEmpty()) fps = 10;
        return 1 / fps;
    }

    void frame(double dt) {
        dt = Math.min(dt, 0.12);
        if (dt <= 0) return;
        clock += dt;
        boolean calmMotion = !grabbed && danceW < 0.05f;
        double mdt = calmMotion ? dt * CalmSpeed : dt;   // its own movements, a little slower
        motionNow += mdt;
        t += mdt;
        frameDt = dt;
        updateSleep(dt);
        updateDance(dt);
        updateHabits(dt);
        updatePerch();
        updateChat(dt);
        if (act != Act.Shades && shadesOn != shadesTarget) shadesOn = shadesTarget;
        else if (act == Act.Shades && actAge() >= ShadesSwap) shadesOn = shadesTarget;

        double ropeTarget = L, ropeRate = 7;
        if (act == Act.Climb) {
            // Climb up its own tail, hang there a moment, then let go and drop.
            double cp = actAge(), top = perch == Perch.Up ? 30 : 58;   // right up to the branch when it's going to sit
            if (cp < ClimbTop) ropeTarget = L - (L - top) * cp / ClimbTop;
            else if (cp < ClimbLetGo) ropeTarget = top;
            else ropeRate = 16;
        }
        rope += (ropeTarget - rope) * ease(ropeRate, mdt);

        if (perchedPose()) {
            PointF hip = perchHip();
            x = hip.x; y = hip.y; vx = vy = 0;
        } else if (grabbed && !Float.isNaN(fingerX)) {
            double[] n = keepInside(fingerX - PX - grabDx, fingerY - PY - grabDy);
            double k = ease(25, dt);
            vx += ((n[0] - x) / dt - vx) * k;   // smoothed, used for the fling
            vy += ((n[1] - y) / dt - vy) * k;
            x = n[0]; y = n[1];
        } else {
            int steps = (int) Math.ceil(mdt / 0.004);
            double h = mdt / steps;
            double tilt = tiltOn ? tiltX : 0;
            for (int i = 0; i < steps; i++) {
                double r = Math.max(1, Math.sqrt(x * x + y * y));
                double pull = -Stiffness * (r - rope) / r;          // stretchy tail
                double sway = SwayForce * (act == Act.Pump ? 1 + 5 * actW : 1) * (1 - 0.75 * sleepW);
                double fx = pull * x - Damping * vx + sway * Math.sin(2 * Math.PI * t / SwayPeriod) + Gravity * 0.9 * tilt;
                double fy = pull * y - Damping * vy + Gravity;
                vx += fx * h; vy += fy * h;
                x += vx * h; y += vy * h;
                double[] n = keepInside(x, y);
                if (n[0] != x) { x = n[0]; vx = -vx * 0.45; }
                if (n[1] != y) { y = n[1]; vy = -vy * 0.45; }
            }
        }
        updateBranchBend(mdt);

        // The body follows the tail's angle with a little weight and wobble.
        int springSteps = (int) Math.ceil(mdt / (1 / 60.0));
        double sh = mdt / springSteps, angTarget = Math.atan2(x, y);
        if (!perchedPose())
            for (int i = 0; i < springSteps; i++) {
                bodyAngVel += (140 * (angTarget - bodyAngle) - 11 * bodyAngVel) * sh;
                bodyAngle += bodyAngVel * sh;
            }

        // Its eyes follow your finger while you hold it, and otherwise wander: mostly at you, now and then elsewhere.
        if (!Float.isNaN(fingerX)) { lookAtX = fingerX; lookAtY = fingerY; }
        else if (clock >= nextGlance) {
            boolean atYou = rnd.nextInt(3) > 0;
            lookAtX = atYou ? headWin.x : rnd.nextFloat() * W;
            lookAtY = atYou ? headWin.y + 400 : rnd.nextFloat() * H;
            nextGlance = clock + 1.5 + rnd.nextDouble() * 3;
        }

        // Head turns a little toward what it looks at, and during the look-around habit.
        float turn = Math.max(-3.5f, Math.min(3.5f, lookX * 1.3f));
        if (shownAct() == Act.Look) {
            double lp = actAge();
            turn += (lp < 1.0 ? -5 : lp < 2.1 ? 5 : 0) * actW;
        }
        if (shownAct() == Act.Drink) turn *= 1 - actW;   // drinking: face straight ahead, at the bottle
        faceShift += (turn * (1 - sleepW) - faceShift) * ease(8, dt);

        if (peelFlying) {
            peelVel.y += (float) (Gravity * dt);
            peelPos.x += peelVel.x * (float) dt;
            peelPos.y += peelVel.y * (float) dt;
            peelAngle += peelSpin * (float) dt;
            peelAge += dt;
            if (peelAge > 1.8) peelFlying = false;
        }

        grinTime = Math.max(0, grinTime - dt);
        if (blinkTime > 0) blinkTime -= dt;
        else if ((nextBlink -= dt) <= 0) { blinkTime = 0.14; nextBlink = 2.5 + rnd.nextDouble() * 4; }
        invalidate();
    }

    double[] keepInside(double nx, double ny) {
        nx = Math.max(-PX + 60, Math.min(PX - 60, nx));
        ny = Math.max(30, Math.min(H - PY - BodyReach - 4, ny));
        // The head is about 70 further out along the tail than the hips; keep it (and its ears) in the window.
        double r = Math.sqrt(nx * nx + ny * ny);
        if (r > 1) {
            double reach = 1 + 70 / r, room = PX - 46;
            if (Math.abs(nx * reach) > room) nx = Math.signum(nx) * room / reach;
        }
        return new double[] { nx, ny };
    }

    void updateBranchBend(double dt) {
        double target;
        if (perchedPose()) target = 2.5 * perchK;   // its weight on the wood
        else {
            // How much harder than usual the tail is pulling (at rest it stretches Gravity / Stiffness).
            double stretch = Math.sqrt(x * x + y * y) - rope - Gravity / Stiffness;
            target = Math.max(-1, Math.min(4, stretch * 0.05));
        }
        int steps = (int) Math.ceil(dt / 0.004);
        double h = dt / steps;
        for (int i = 0; i < steps; i++) {
            bendVel += (-320 * (bendPx - target) - 7 * bendVel) * h;   // stiff, springy wood
            bendPx += bendVel * h;
        }
        wrapDy = (float) bendPx;
    }

    static boolean isNightNow() {
        int h = Calendar.getInstance().get(Calendar.HOUR_OF_DAY);
        return h >= NightStart || h < NightEnd;
    }

    void updateSleep(double dt) {
        if (clock >= nightCheckAt) { night = isNightNow(); nightCheckAt = clock + 1; }
        if (grabbed || musicOn || preview) wakeUntil = Math.max(wakeUntil, clock + StayAwake);   // music wakes it too
        boolean asleep = night && clock > wakeUntil;
        if (asleep && !wasAsleep) say(pick("Good night...", "*yawn* Sleepy time...", "Nap time. Zzz..."), true);
        wasAsleep = asleep;
        // Dozes off slowly, wakes up quickly.
        sleepW += ((asleep ? 1 : 0) - sleepW) * ease(asleep ? 0.9 : 7, dt);
        breathPhase += dt * (2.4 - 1.2 * sleepW);

        // Little Zs float up beside its head while it sleeps.
        if (sleepW > 0.8f && clock >= nextZ) {
            Zz z = new Zz();
            z.p = new PointF(headWin.x + 20, headWin.y - 12);
            z.drift = 14 + rnd.nextFloat() * 10;
            zzz.add(z);
            nextZ = clock + 1.1 + rnd.nextDouble() * 0.5;
        }
        for (int i = zzz.size() - 1; i >= 0; i--) {
            Zz z = zzz.get(i);
            z.age += dt;
            z.p.x += z.drift * (float) dt + (float) Math.sin(z.age * 3) * 0.3f;
            z.p.y -= 30 * (float) dt;
            if (z.age > 2.6) zzz.remove(i);
        }
    }

    void addNote(float px, float py, float side) {
        Zz z = new Zz();
        z.p = new PointF(px, py);
        z.drift = side * (14 + rnd.nextFloat() * 14);
        z.note = true;
        z.ch = rnd.nextInt(2) == 0 ? "♫" : "♪";
        z.col = NoteColors[rnd.nextInt(NoteColors.length)];
        zzz.add(z);
    }

    // Music playing on the phone (the service checks): it bounces and sings along to its own groove,
    // since apps can't hear what other apps play. With the microphone it sings its song.
    void updateDance(double dt) {
        boolean mic = micSinging();
        boolean dance = musicOn && !grabbed && sleepW < 0.5f && !perchedPose() && act != Act.Sing;
        danceW += ((dance ? 1 : 0) - danceW) * ease(dance ? 4 : 3, dt);

        boolean canSing = danceW > 0.6f && sleepW < 0.1f && act == Act.None;
        if (mic) singing = true;
        else if (!canSing) { if (singing) singRestUntil = clock + 1; singing = false; }
        else if (singing && clock > singUntil) { singing = false; singRestUntil = clock + 1.5 + rnd.nextDouble() * 2.5; }
        else if (!singing && clock > singRestUntil) {
            singing = true;
            singUntil = clock + 3 + rnd.nextDouble() * 4;
            if (rnd.nextInt(3) == 0) say(Lyrics[rnd.nextInt(Lyrics.length)], false, 2.5);
        }
        singLevel += ((mic ? 0.8f : singing ? 0.75f : 0) - singLevel) * ease(10, dt);
        if (mic && songShown < song.length && (actAge() - SgUp) / SgLine >= songShown)
            say(song[songShown++], true, SgLine / CalmSpeed * 0.95);   // the song, a line at a time

        boolean beat = clock - lastBeat > beatInterval;
        if (beat) {
            lastBeat = clock;
            beatCount++;
            float side = beatCount % 2 == 0 ? 1 : -1;
            if (danceW > 0.3f) {
                vy -= 220 * danceW;                       // bounce on the tail
                vx += side * 100 * danceW;               // sway side to side
                if (!singing) grinTime = Math.max(grinTime, 0.5);
                if (singing) addNote(headWin.x + side * 8, headWin.y + 12, side);
                else if (beatCount % 2 == 0) addNote(headWin.x + side * 26, headWin.y - 8, side);
            } else if (mic) {
                if (!perchedPose()) vx += side * 55;     // swaying with the song
                addNote(headWin.x + side * 34, headWin.y + 8, side);
            }
        }
        beatPhase = Math.min(1, (clock - lastBeat) / beatInterval);
    }

    void updateHabits(double dt) {
        boolean calm = !grabbed && sleepW < 0.05f && danceW < 0.05f && perch != Perch.Over && perch != Perch.Down;
        if (act != Act.None && (actAge() > actLen || !calm)) act = Act.None;
        actW += ((act == Act.None ? 0 : 1) - actW) * ease(7, dt);
        if (!calm || act != Act.None) return;

        // Now and then it goes and sits on the branch for a while (not at night).
        if (perch == Perch.None && clock >= nextSit && !night) { startSit(); return; }
        if (perch == Perch.Up) return;
        if (clock >= nextSing && !night) { nextSing = clock + 240 + rnd.nextDouble() * 240; startSing(); return; }

        if ((nextAct -= dt) <= 0 && perch == Perch.Sit) {
            // Sitting: just the calm habits (no swinging or tail climbing up there).
            switch (rnd.nextInt(5)) {
                case 0: startEat(); say(pick("Snack with a view!", "Banana break!"), false); break;
                case 1: startAct(Act.Scratch, 2.0); break;
                case 2: startWave(); say(pick("Hi there!", "Hello!", "Hey you!"), false); break;
                default: startAct(Act.Look, 3.0); break;
            }
            nextAct = 4 + rnd.nextDouble() * 5;
        } else if (nextAct <= 0) {
            switch (rnd.nextInt(8)) {
                case 0: startEat(); say(pick("Snack time!", "Banana break!"), false); break;
                case 1: startAct(Act.Scratch, 2.0); break;
                case 2: startAct(Act.Look, 3.0); break;
                case 3: startAct(Act.Pump, 4.5); break;
                case 4: startAct(Act.Climb, 2.6); break;
                case 5: shadesTarget = !shadesOn; startAct(Act.Shades, ShadesLen);
                        if (!shadesTarget) say(pick("Let me see you properly.", "Shades off!"), false); break;
                case 6: startWave(); say(pick("Hi there!", "Hello!", "Hey you!", "Oh, hi!"), false); break;
                default: startAct(Act.Look, 3.0); break;
            }
            nextAct = 4 + rnd.nextDouble() * 5;
            if (!shadesOn && rnd.nextInt(3) == 0 && act == Act.None) { shadesTarget = true; startAct(Act.Shades, ShadesLen); }
        }
    }

    void startEat() { startAct(Act.Eat, EatLen); peelTossed = false; }

    void startSit() {
        perch = Perch.Up;
        perchStart = motionNow;
        startAct(Act.Climb, ClimbTop + 0.3);   // first, hand over hand up the tail
    }

    void updatePerch() {
        if (perch == Perch.None) return;
        boolean leave = grabbed || musicOn || sleepW > 0.3f;
        double el = motionNow - perchStart;
        switch (perch) {
            case Up:
                if (leave || act != Act.Climb) { perch = Perch.None; break; }
                if (el >= ClimbTop) {
                    perch = Perch.Over; perchStart = motionNow;
                    perchFrom = P((float) x, (float) y);
                    act = Act.None;
                }
                break;
            case Over:
                perchK = smooth(el / OverTime);
                if (el >= OverTime) {
                    perch = Perch.Sit; perchStart = motionNow; perchK = 1;
                    sitUntil = clock + 15 + rnd.nextDouble() * 20;
                    say(pick("Nice view from up here.", "Ahh, a seat!", "Time for a little sit.", "Comfy!"), false);
                }
                break;
            case Sit:
                if (leave || (clock >= sitUntil && act == Act.None)) { perch = Perch.Down; perchStart = motionNow; perchFrom = P(0, 36); }
                break;
            case Down:
                perchK = 1 - smooth(el / OverTime);
                if (el >= OverTime) { leavePerchNow(); vy = 120; }   // drops into a hang and bounces
                break;
            default: break;
        }
    }

    // Back to hanging by the tail right under the branch.
    void leavePerchNow() {
        perch = Perch.None; perchK = 0;
        x = 0; y = 36; vx = vy = 0;
        bodyAngle = bodyAngVel = 0;
        nextSit = clock + 60 + rnd.nextDouble() * 90;
    }

    // Where the hips are (relative to the branch) while swinging up over it or sitting on it.
    PointF perchHip() {
        double ang = Math.PI * perchK;
        float r0 = (float) Math.hypot(perchFrom.x, perchFrom.y);
        float r = r0 + (6 - r0) * perchK;
        return P(perchFrom.x * (1 - perchK) + 14 * (float) Math.sin(ang), r * (float) Math.cos(ang));
    }

    void updateChat(double dt) {
        if (sayText != null && clock - sayStart > sayLen) sayText = null;
        boolean calm = !grabbed && sleepW < 0.05f && danceW < 0.05f && act == Act.None;
        if (!calm) return;
        if ((nextChat -= dt) <= 0) {
            say(rnd.nextDouble() < 0.2 ? greeting() : IdleLines[rnd.nextInt(IdleLines.length)], false);
            nextChat = 25 + rnd.nextDouble() * 35;
        }
    }

    // Where its body is, for the service's touch area (window units): halfway between hips and head.
    PointF bodyCenter() { return P((float) (PX + x + (headWin.x - PX - x) * 0.55), (float) (PY + y + (headWin.y - PY - y) * 0.55)); }

    // ---- Eating and drinking timing ----

    static float carryK(double ap) { return smooth((ap - GrabTime - PullOutTime) / CarryTime); }
    static boolean bananaInPocket(double ap) { return ap < GrabTime + PullOutTime; }
    static float bananaScale(double ap) { return bananaInPocket(ap) ? PocketScale : PocketScale + (1 - PocketScale) * carryK(ap); }
    static float bananaAngle(double ap, PointF hand, PointF mouth) {
        float aim = angleTo(hand, mouth);
        return bananaInPocket(ap) ? 180 : 180 + (aim - 180) * carryK(ap);
    }

    // 0 = banana held under the chin, 1 = at the mouth taking a bite.
    static float biteReach(double p) {
        double best = 0;
        for (double bt : BiteTimes) {
            double d = p - bt, v = 0;
            if (d > -0.3 && d <= 0) v = 1 + d / 0.3;
            else if (d > 0 && d < 0.35) v = 1 - d / 0.35;
            best = Math.max(best, v * v * (3 - 2 * v));
        }
        return (float) best;
    }

    static boolean isChewing(double p) {
        for (double bt : BiteTimes) if (p > bt + 0.15 && p < bt + 0.9) return true;
        return false;
    }

    // Peeling: strips come down one at a time (0..3); sets peelStrip and peelReaching.
    float peelProgress(double ap) {
        double slot = (ap - PeelStart) / PeelSlot;
        peelStrip = (int) Math.max(0, Math.min(2, Math.floor(slot)));
        double u = slot - peelStrip;
        peelReaching = slot < 3 && u < 0.35;
        if (slot <= 0) return 0;
        if (slot >= 3) return 3;
        return peelStrip + (float) Math.max(0, Math.min(1, (u - 0.35) / 0.65));
    }

    // A point on the held banana or bottle, in the body's coordinates.
    static PointF heldToBody(PointF hand, PointF local, float appear, float angle) {
        double a = angle * Math.PI / 180;
        float lx = local.x * appear, ly = local.y * appear, c = (float) Math.cos(a), s = (float) Math.sin(a);
        return P(hand.x + lx * c - ly * s, hand.y + lx * s + ly * c);
    }

    static float gulpsDone(double ap) { float n = 0; for (double gt : GulpTimes) n += phase(ap, gt - 0.05, gt + 0.4); return n; }
    static float glugPhase(double ap) { for (double gt : GulpTimes) { double d = ap - gt; if (d >= 0 && d < 0.5) return (float) (d / 0.5); } return -1; }
    static float gulpBob(double ap) {
        for (double gt : GulpTimes) { double d = ap - gt; if (d > 0 && d < 0.35) return 2.5f * (float) Math.sin(Math.PI * d / 0.35); }
        return 0;
    }
    // How far the bottle is raised to the lips: 0 held in front of its chin .. 1 drinking.
    static float drinkLift(double ap) { return ap < DkLower ? phase(ap, DkCapOff, DkLift) : 1 - phase(ap, DkLower, DkDown); }

    PointF sipHandAt(double ap, boolean sitting) {
        double tilt = (20 + 10 * gulpsDone(ap)) * Math.PI / 180;   // the bottom, above level
        PointF lips = face(SipLips);
        return P(lips.x + SipNeck * (float) Math.cos(tilt), lips.y + SipNeck * (float) Math.sin(tilt) * (sitting ? 1 : -1));
    }

    // ---- Drawing helpers ----

    final Paint pf = new Paint(Paint.ANTI_ALIAS_FLAG);
    final Paint ps = new Paint(Paint.ANTI_ALIAS_FLAG);
    final TextPaint textPaint = new TextPaint(Paint.ANTI_ALIAS_FLAG);
    final Paint zPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    final Paint bmpPaint = new Paint(Paint.FILTER_BITMAP_FLAG);
    final Path tmp = new Path(), tmp2 = new Path();
    final RectF rf = new RectF();
    final DashPathEffect stitchDash = new DashPathEffect(new float[] { 2f, 1.6f }, 0);

    static int argb(int a, int c) { return (c & 0xFFFFFF) | (Math.max(0, Math.min(255, a)) << 24); }

    static int mix(int a, int b, float k) {
        return Color.argb(
            (int) (Color.alpha(a) + (Color.alpha(b) - Color.alpha(a)) * k), (int) (Color.red(a) + (Color.red(b) - Color.red(a)) * k),
            (int) (Color.green(a) + (Color.green(b) - Color.green(a)) * k), (int) (Color.blue(a) + (Color.blue(b) - Color.blue(a)) * k));
    }

    static int light(int c, float k) { return mix(c, argb(Color.alpha(c), 0xFFFFFF), k); }
    static int dark(int c, float k) { return mix(c, argb(Color.alpha(c), 0), k); }

    // A gradient across a box at 'angle' degrees (clockwise from the right), corner to corner, like GDI+'s.
    static LinearGradient grad(float x, float y, float w, float h, int c1, int c2, float angle) {
        double a = Math.toRadians(angle);
        float dx = (float) Math.cos(a), dy = (float) Math.sin(a);
        float ext = Math.abs(w / 2 * dx) + Math.abs(h / 2 * dy), cx = x + w / 2, cy = y + h / 2;
        return new LinearGradient(cx - dx * ext, cy - dy * ext, cx + dx * ext, cy + dy * ext, c1, c2, Shader.TileMode.CLAMP);
    }

    Paint fill(int col) { pf.setShader(null); pf.setColor(col); return pf; }
    Paint fill(Shader s) { pf.setColor(Color.BLACK); pf.setShader(s); return pf; }
    Paint pen(int col, float w) { ps.setShader(null); ps.setPathEffect(null); ps.setStrokeCap(Paint.Cap.ROUND); ps.setColor(col); ps.setStrokeWidth(w); return ps; }

    void oval(Canvas c, float x, float y, float w, float h, Paint p) { rf.set(x, y, x + w, y + h); c.drawOval(rf, p); }
    void ovalLine(Canvas c, float x, float y, float w, float h, int col, float width) { rf.set(x, y, x + w, y + h); c.drawOval(rf, pen(col, width)); }
    void line(Canvas c, float x1, float y1, float x2, float y2, int col, float w) { c.drawLine(x1, y1, x2, y2, pen(col, w)); }
    void line(Canvas c, PointF a, PointF b, int col, float w) { c.drawLine(a.x, a.y, b.x, b.y, pen(col, w)); }
    void arc(Canvas c, float x, float y, float w, float h, float start, float sweep, int col, float width) {
        rf.set(x, y, x + w, y + h);
        c.drawArc(rf, start, sweep, false, pen(col, width));
    }

    void bez(Canvas c, PointF a, PointF b, PointF cc, PointF d, int col, float w) {
        tmp.reset(); tmp.moveTo(a.x, a.y); tmp.cubicTo(b.x, b.y, cc.x, cc.y, d.x, d.y);
        c.drawPath(tmp, pen(col, w));
    }

    void lines(Canvas c, PointF[] pts, int col, float w) {
        tmp.reset(); tmp.moveTo(pts[0].x, pts[0].y);
        for (int i = 1; i < pts.length; i++) tmp.lineTo(pts[i].x, pts[i].y);
        c.drawPath(tmp, pen(col, w));
    }

    // A smooth curve through the points (a cardinal spline, like GDI+'s DrawCurve).
    static void addCurve(Path p, PointF[] pts, float tension, boolean move) {
        int n = pts.length;
        if (move) p.moveTo(pts[0].x, pts[0].y); else p.lineTo(pts[0].x, pts[0].y);
        for (int i = 0; i < n - 1; i++) {
            PointF p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(n - 1, i + 2)];
            p.cubicTo(p1.x + (p2.x - p0.x) * tension / 3, p1.y + (p2.y - p0.y) * tension / 3,
                      p2.x - (p3.x - p1.x) * tension / 3, p2.y - (p3.y - p1.y) * tension / 3, p2.x, p2.y);
        }
    }

    // A rounded, softly shaded shape with a cartoon outline.
    void blob(Canvas c, float x, float y, float w, float h, int col) {
        oval(c, x - OL, y - OL, w + 2 * OL, h + 2 * OL, fill(Outline));
        shade(c, x, y, w, h, col);
    }

    // Lit from the top-left.
    void shade(Canvas c, float x, float y, float w, float h, int col) {
        oval(c, x, y, w, h, fill(grad(x - 1, y - 1, w + 2, h + 2, light(col, 0.2f), dark(col, 0.14f), 60)));
    }

    void limb(Canvas c, float w, PointF... pts) {
        lines(c, pts, Outline, w + 2 * OL);
        lines(c, pts, Fur, w);
        line(c, pts[0], pts[pts.length / 2], Color.argb(50, 255, 220, 180), w * 0.3f);
    }

    static PointF[] splitBezier(PointF a, PointF b, PointF c, PointF d, float t) {
        PointF ab = lerp(a, b, t), bc = lerp(b, c, t), cd = lerp(c, d, t), abc = lerp(ab, bc, t), bcd = lerp(bc, cd, t), m = lerp(abc, bcd, t);
        return new PointF[] { a, ab, abc, m };
    }

    // An arm in a puffer sleeve: fur forearm, padded sleeve, fluffy cuff.
    void sleevedArm(Canvas c, PointF a, PointF b, PointF cc, PointF d) {
        bez(c, a, b, cc, d, Outline, 8 + 2 * OL);
        bez(c, a, b, cc, d, Fur, 8);
        PointF[] sl = splitBezier(a, b, cc, d, 0.72f);
        bez(c, sl[0], sl[1], sl[2], sl[3], Outline, 12 + 2 * OL);
        bez(c, sl[0], sl[1], sl[2], sl[3], Jacket, 12);
        bez(c, sl[0], sl[1], sl[2], sl[3], Color.argb(70, 255, 255, 255), 3);
        puff(c, sl[3].x, sl[3].y, 7.5f);
    }

    // A fluffy sherpa tuft, its shape built once per size.
    final HashMap<Float, Path[]> puffPaths = new HashMap<>();

    void puff(Canvas c, float cx, float cy, float r) {
        Path[] pp = puffPaths.get(r);
        if (pp == null) {
            Path edge = new Path(), fl = new Path();
            for (int i = 0; i < 6; i++) {
                double a = i * Math.PI / 3;
                float ox = (float) Math.cos(a) * r * 0.55f - r * 0.55f, oy = (float) Math.sin(a) * r * 0.55f - r * 0.55f;
                edge.addOval(new RectF(ox - 0.8f, oy - 0.8f, ox + r * 1.1f + 0.8f, oy + r * 1.1f + 0.8f), Path.Direction.CW);
                fl.addOval(new RectF(ox, oy, ox + r * 1.1f, oy + r * 1.1f), Path.Direction.CW);
            }
            fl.addOval(new RectF(-r * 0.6f, -r * 0.6f, r * 0.6f, r * 0.6f), Path.Direction.CW);
            puffPaths.put(r, pp = new Path[] { edge, fl });
        }
        c.save();
        c.translate(cx, cy);
        c.drawPath(pp[0], fill(Color.rgb(170, 155, 125)));
        c.drawPath(pp[1], fill(Sherpa));
        c.restore();
    }

    void handShape(Canvas c, float cx, float cy, float size) {
        blob(c, cx - size / 2, cy - size / 2, size, size, Skin);
        int k = Color.argb(150, 170, 115, 70);
        line(c, cx - size * 0.18f, cy + size * 0.05f, cx - size * 0.18f, cy + size * 0.38f, k, 1.1f);
        line(c, cx + size * 0.12f, cy + size * 0.05f, cx + size * 0.12f, cy + size * 0.38f, k, 1.1f);
    }

    void foot(Canvas c, float x, float y, float w, float h) {
        blob(c, x, y, w, h, Skin);
        int k = Color.argb(150, 170, 115, 70);
        line(c, x + w * 0.35f, y + h * 0.2f, x + w * 0.35f, y + h * 0.55f, k, 1.1f);
        line(c, x + w * 0.62f, y + h * 0.2f, x + w * 0.62f, y + h * 0.55f, k, 1.1f);
    }

    // A sleeved arm from the shoulder to wherever the hand should be.
    void armTo(Canvas c, PointF shoulder, PointF hand, int side) {
        float k = Math.max(-1, Math.min(1, (hand.y - shoulder.y) / 58f));
        sleevedArm(c, shoulder, P(shoulder.x + side * 15, shoulder.y + 16 * k), P(hand.x + side * 4, hand.y - 22 * k), hand);
        handShape(c, hand.x, hand.y + 4 * k, 13);
    }

    // A fist holding something upright at (0, 0): the palm behind it...
    void fistBack(Canvas c) { blob(c, -7.5f, -5.5f, 15, 14, Skin); }

    // ...and three curled fingers across the front, with the thumb pressed along the side.
    void fistFront(Canvas c) {
        for (int i = 2; i >= 0; i--) {
            float y = -4.2f + i * 3.7f, len = i == 2 ? 4.5f : 5.5f;
            line(c, -len, y, len, y + 0.5f, Outline, 4f + 2.2f);
            line(c, -len, y, len, y + 0.5f, light(Skin, 0.04f), 4f);
            line(c, -len + 1, y + 1.2f, len - 1, y + 1.6f, Color.argb(70, 170, 115, 70), 1f);
        }
        line(c, -7, 3.5f, -4, -6.5f, Outline, 4.2f + 2.2f);
        line(c, -7, 3.5f, -4, -6.5f, Skin, 4.2f);
    }

    // ---- The scene ----

    // The body's transform this frame (window units), so points can go between the two.
    float curHx, curHy, curSx = 1, curSy = 1;
    double curTilt;

    PointF bodyToWin(PointF p) {
        double ca = Math.cos(curTilt), sa = Math.sin(curTilt);
        float lx = p.x * curSx, ly = p.y * curSy;
        return P(curHx + (float) (lx * ca + ly * sa), curHy + (float) (-lx * sa + ly * ca));
    }

    PointF winToBody(PointF p) {
        double ca = Math.cos(curTilt), sa = Math.sin(curTilt);
        float dx = p.x - curHx, dy = p.y - curHy;
        return P((float) (dx * ca - dy * sa) / curSx, (float) (dx * sa + dy * ca) / curSy);
    }

    @Override
    protected void onDraw(Canvas c) {
        c.save();
        c.scale(S, S);
        boolean perched = perchedPose();
        float hx = PX + (float) x, hy = PY + (float) y + (perched ? wrapDy : 0);   // sitting, it rides the branch
        double r = Math.max(1, Math.sqrt(x * x + y * y));
        float dx = (float) (x / r), dy = (float) (y / r);

        if (perched) drawSitTail(c, hx, hy);   // hanging down behind the branch
        else tailCoil(c, false, tailWidth(r));   // the turns behind the branch
        drawBranch(c);
        chewing = biteOpen = false;
        if (peelFlying) drawFlyingPeel(c);
        if (!perched) drawTail(c, hx, hy, dx, dy, r);

        double tilt = perched ? -Math.PI * perchK : bodyAngle;   // swinging up over the branch turns it head-up
        double ca = Math.cos(tilt), sa = Math.sin(tilt);

        // Eyes glide toward what it looks at (in the monkey's own tilted coordinates).
        double lx = lookAtX - hx, ly = lookAtY - hy;
        double ex = lx * ca - ly * sa, ey = lx * sa + ly * ca - HeadY;
        if (perched) { ex = lookAtX - headWin.x; ey = lookAtY - headWin.y; }   // its head stays upright up there
        double el = Math.max(1, Math.sqrt(ex * ex + ey * ey));
        float k = ease(14, frameDt);
        lookX += ((float) (ex / el * Math.min(2.6, el / 30)) - lookX) * k;
        lookY += ((float) (ey / el * Math.min(2.6, el / 30)) - lookY) * k;

        // Arms trail behind when it swings.
        float lagTarget = (float) Math.max(-14, Math.min(14, -(vx * ca - vy * sa) * 0.02));
        lag += (lagTarget - lag) * ease(10, frameDt);

        // A little squash and stretch along the tail when it moves fast.
        float speed = (float) Math.sqrt(vx * vx + vy * vy);
        float stretch = Math.min(0.07f, speed / 14000f);
        curHx = hx; curHy = hy; curTilt = tilt; curSx = 1 - stretch * 0.6f; curSy = 1 + stretch;
        headWin = bodyToWin(P(0, HeadY));

        c.save();
        c.translate(hx, hy);
        c.rotate((float) (-tilt * 180 / Math.PI));
        c.scale(curSx, curSy);
        drawBody(c);
        c.restore();

        drawZzz(c);
        drawBubble(c);
        c.restore();
    }

    // ---- Branch ----

    static float branchY(float x) {
        return -0.035f * x + 1.6f * (float) Math.sin(x / 26f) + 0.0004f * x * x * (x < 0 ? 1 : 0.4f);
    }

    // The wood never changes, so it's painted once into a picture at the screen's own scale and reused.
    Bitmap woodBitmap;
    float woodScale;

    void drawBranch(Canvas c) {
        float bx = PX, by = PY;
        float bend = (float) Math.toDegrees(Math.atan2(-bendPx, PivotX));
        // The leaves bend with the branch, turning a touch around where it joins the tree.
        c.save();
        c.rotate(bend, bx + PivotX, by + PivotY);
        paintBranch(c, bx, by, 0);
        c.restore();
        if (woodBitmap == null || woodScale != S) {
            woodScale = S;
            int bw = (int) Math.ceil(W * S), bh = (int) Math.ceil((PY + 40) * S);
            woodBitmap = Bitmap.createBitmap(bw, bh, Bitmap.Config.ARGB_8888);
            Canvas wc = new Canvas(woodBitmap);
            wc.scale(S, S);
            paintBranch(wc, bx, by, 1);
        }
        c.save();
        c.scale(1 / S, 1 / S);
        c.drawBitmap(woodBitmap, 0, 0, bmpPaint);
        c.restore();
        c.save();
        c.rotate(bend, bx + PivotX, by + PivotY);
        paintBranch(c, bx, by, 2);
        c.restore();
    }

    // layer 0: leaves behind the wood, 1: the wood (branch, twigs, knot), 2: leaves in front.
    void paintBranch(Canvas c, float bx, float by, int layer) {
        int bark = Color.rgb(112, 74, 40);
        float shake = (float) Math.max(-2, Math.min(2, bendVel * 0.025));   // leaves rustle when the branch bounces
        float sway = (float) Math.sin(t * 1.6) + shake, sway2 = (float) Math.sin(t * 1.3 + 1.7) - shake;

        if (layer == 0) {
            leafCluster(c, bx - 84, by + 17, 160, sway2, 3);
            leafCluster(c, bx - 32, by + 15, 110, sway, 2);
            return;
        }
        if (layer == 2) {
            leafCluster(c, bx + 40, by + 14, 70, sway, 3);
            leafCluster(c, bx + 102, by + 15, 85, sway2, 2);
            leafCluster(c, bx + 102, by - 11, 15, sway, 2);   // sprouting from the outside of the bend
            leafCluster(c, bx - 97, by + branchY(-97), 190, sway, 2);   // leaves at the thin tip
            leaf(c, bx + 12, by - 1, 300 + sway * 4, 13, 1);
            leaf(c, bx - 70, by + 4, 120 - sway * 4, 14, 2);
            return;
        }
        twig(c, bark, bx, by, new PointF[] { P(-58, 1), P(-70, 10), P(-84, 17) }, 3.6f, 1.2f);
        twig(c, bark, bx, by, new PointF[] { P(-22, 1), P(-28, 8), P(-32, 15) }, 3f, 1f);

        // Main branch: comes down from the top of the screen on the right (the tree is up there), thickest
        // where it enters, bends over and runs left, tapering to a thin tip. It passes through (0, 0) where the tail wraps.
        final int NH = 34, NB = 22;
        PointF[] cl = new PointF[NH + NB + 1];
        float[] hw = new float[cl.length];
        for (int i = 0; i <= NH; i++) {
            float x = -98 + (56 + 98) * i / (float) NH, u = (x + 98) / 154;
            cl[i] = P(x, branchY(x));
            hw[i] = (2.2f + 8f * (float) Math.pow(u, 1.25f)) / 2;
        }
        PointF b0 = cl[NH], b1 = P(84, b0.y - 0.5f), b2 = P(99, -14), b3 = P(104, -PY - 12);   // up past the top edge
        for (int i = 1; i <= NB; i++) {
            float s = i / (float) NB, a = 1 - s;
            cl[NH + i] = P(a * a * a * b0.x + 3 * a * a * s * b1.x + 3 * a * s * s * b2.x + s * s * s * b3.x,
                           a * a * a * b0.y + 3 * a * a * s * b1.y + 3 * a * s * s * b2.y + s * s * s * b3.y);
            hw[NH + i] = hw[NH] + (9f - hw[NH]) * (float) Math.sqrt(s);   // flares out toward the trunk
        }
        int N = cl.length - 1;
        PointF[] top = new PointF[N + 1], bottom = new PointF[N + 1], ridgeLine = new PointF[N + 1], shadeLine = new PointF[N + 1];
        float[] shadeW = new float[N + 1];
        for (int i = 0; i <= N; i++) {
            PointF p0 = cl[Math.max(0, i - 1)], p1 = cl[Math.min(N, i + 1)];
            float tx = p1.x - p0.x, ty = p1.y - p0.y, tl = (float) Math.sqrt(tx * tx + ty * ty);
            float nx = -ty / tl, ny = tx / tl;   // points to the underside
            float w = hw[i] + 0.35f * (float) Math.sin(i * 1.7f) + 0.25f * (float) Math.sin(i * 0.6f + 2);
            top[i] = P(bx + cl[i].x - nx * w, by + cl[i].y - ny * w);
            bottom[N - i] = P(bx + cl[i].x + nx * w, by + cl[i].y + ny * w);
            ridgeLine[i] = P(bx + cl[i].x - nx * w * 0.5f, by + cl[i].y - ny * w * 0.5f);
            shadeLine[i] = P(bx + cl[i].x + nx * w * 0.6f, by + cl[i].y + ny * w * 0.6f);
            shadeW[i] = w;
        }
        Path wood = new Path();
        addCurve(wood, top, 0.4f, true);
        addCurve(wood, bottom, 0.4f, false);
        wood.close();
        c.drawPath(wood, pen(Outline, 3.2f));
        c.drawPath(wood, fill(bark));
        c.save();
        c.clipPath(wood);
        // Plain bark, lit along the upper side and shaded underneath.
        for (int i = 0; i < N; i++) line(c, shadeLine[i], shadeLine[i + 1], Color.argb(90, 50, 28, 12), shadeW[i] * 0.9f);
        for (int i = 0; i < N; i++) line(c, ridgeLine[i], ridgeLine[i + 1], Color.argb(55, 255, 225, 180), shadeW[i] * 0.6f);
        // Bark: thin grooves running along the wood, and a lit ridge along the upper side.
        tmp.reset();
        addCurve(tmp, ridgeLine, 0.5f, true);
        c.drawPath(tmp, pen(Color.argb(60, 255, 230, 190), 1.6f));
        for (int i = 2; i < N - 2; i += 3) {
            float off = ((i * 5) % 7 - 3) * 0.28f;
            int j = Math.min(N, i + 2);
            PointF a0 = lerp(top[i], bottom[N - i], 0.5f + off * 0.35f), a1 = lerp(top[j], bottom[N - j], 0.5f + off * 0.35f + 0.06f);
            line(c, a0, a1, Color.argb(120, 55, 32, 14), 1f);
        }
        c.restore();

        // A knot in the wood, and a short broken-off stub.
        float kx = 40, ky = branchY(kx);
        oval(c, bx + kx - 3.5f, by + ky - 2, 7, 4.2f, fill(dark(bark, 0.35f)));
        int ko = Color.argb(160, 50, 28, 12);
        ovalLine(c, bx + kx - 3.5f, by + ky - 2, 7, 4.2f, ko, 1f);
        arc(c, bx + kx - 6, by + ky - 3.5f, 12, 7.5f, 200, 140, ko, 1f);
        twig(c, bark, bx, by, new PointF[] { P(-46, -1), P(-50, -6), P(-52, -9) }, 3f, 2.6f);
        oval(c, bx - 54, by - 11.5f, 4.5f, 3.5f, fill(Color.rgb(214, 180, 130)));   // pale broken end

        // Twigs in front, hanging down and out to the side; their leaves are layer 2.
        twig(c, bark, bx, by, new PointF[] { P(22, -1), P(32, 5), P(40, 14) }, 3.4f, 1.1f);
        twig(c, bark, bx, by, new PointF[] { P(88, -4), P(98, 4), P(102, 15) }, 3.6f, 1.1f);
    }

    // A tapering twig along a smooth curve through 'pts' (relative to the branch), from width w0 to w1.
    void twig(Canvas c, int bark, float bx, float by, PointF[] pts, float w0, float w1) {
        final int segs = 8;
        PointF[] ln = new PointF[segs + 1];
        for (int i = 0; i <= segs; i++) {
            float u = i / (float) segs, a = 1 - u;
            ln[i] = P(bx + a * a * pts[0].x + 2 * a * u * pts[1].x + u * u * pts[2].x,
                      by + a * a * pts[0].y + 2 * a * u * pts[1].y + u * u * pts[2].y);
        }
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < segs; i++) {
                float w = w0 + (w1 - w0) * (i + 0.5f) / segs;
                line(c, ln[i], ln[i + 1], pass == 0 ? Outline : mix(light(bark, 0.12f), dark(bark, 0.1f), i / (float) segs), pass == 0 ? w + 2.4f : w);
            }
    }

    // A few leaves of different sizes and shades fanned out from one point.
    static final float[] LeafSpread = { 0, -34, 32, -62, 58 }, LeafSize = { 22, 17, 19, 14, 15 };

    void leafCluster(Canvas c, float x, float y, float angle, float sway, int count) {
        for (int i = count - 1; i >= 0; i--) leaf(c, x, y, angle + LeafSpread[i] + sway * (5 - i), LeafSize[i], i % 3);
    }

    // shade 0 = fresh green, 1 = deeper green, 2 = a touch yellow, so a cluster doesn't look copy-pasted.
    final HashMap<String, Object[]> leafCache = new HashMap<>();

    void leaf(Canvas c, float x, float y, float angle, float len, int shade) {
        String key = len + "_" + shade;
        Object[] lc = leafCache.get(key);
        if (lc == null) {
            int lite = shade == 1 ? Color.rgb(92, 172, 78) : shade == 2 ? Color.rgb(160, 205, 90) : Color.rgb(120, 200, 90);
            int deep = shade == 1 ? Color.rgb(38, 108, 50) : shade == 2 ? Color.rgb(82, 140, 52) : Color.rgb(50, 130, 55);
            Path p = new Path();
            // Slightly lopsided, with a pointed tip, like a real leaf.
            p.moveTo(0, 0);
            p.cubicTo(len * 0.25f, -len * 0.36f, len * 0.7f, -len * 0.3f, len, 0);
            p.cubicTo(len * 0.72f, len * 0.24f, len * 0.3f, len * 0.28f, 0, 0);
            p.close();
            lc = new Object[] { p, new LinearGradient(0, -len * 0.3f, 0, len * 0.3f, lite, deep, Shader.TileMode.CLAMP) };
            leafCache.put(key, lc);
        }
        c.save();
        c.translate(x, y);
        c.rotate(angle);
        c.drawPath((Path) lc[0], fill((Shader) lc[1]));
        c.drawPath((Path) lc[0], pen(Color.rgb(35, 90, 35), 1.2f));
        line(c, 2, 0, len - 3, 0, Color.argb(170, 35, 100, 40), 1f);
        c.restore();
    }

    // ---- Tail ----

    // Tail thins as it stretches.
    static float tailWidth(double r) { return (float) Math.max(3.5, 6.5 - (r - L) / 60); }

    // The tail is coiled tightly around the branch, about two and a half turns, starting with the tip tucked
    // behind. Angle 0 is the underside, pi/2 the front, pi the top.
    static final double CoilStart = -0.7 * Math.PI, CoilEnd = 4 * Math.PI;

    PointF coilPoint(double th, float w) {
        float R = 3.9f + w * 0.4f;   // the branch's half thickness at the wrap, plus half the tail: snug
        float x = -12 + 24 * (float) ((th - CoilStart) / (CoilEnd - CoilStart));
        return P(PX + x, PY + branchY(x) + wrapDy * (1 + x / PivotX) + R * (float) Math.cos(th));
    }

    // Draws either the turns behind the branch (in shadow, before the branch) or the ones in front.
    void tailCoil(Canvas c, boolean front, float w) {
        final int N = 72;
        double step = (CoilEnd - CoilStart) / N, taper = 0.9 * Math.PI;
        float full = w * 0.8f;   // a little squeezed where it grips
        int fillCol = front ? Fur : dark(Fur, 0.35f);
        ArrayList<PointF> run = new ArrayList<>();
        for (int pass = 0; pass < 2; pass++) {
            run.clear();
            PointF prev = coilPoint(CoilStart, w);
            for (int i = 1; i <= N + 1; i++) {
                double th = CoilStart + step * i;
                boolean mine = i <= N && (Math.sin(th - step / 2) > 0) == front;
                PointF p = i <= N ? coilPoint(th, w) : prev;
                if (mine && th - CoilStart < taper) {
                    // The tip tapers: a piece at a time.
                    float ww = full * (0.45f + 0.55f * (float) ((th - CoilStart) / taper));
                    line(c, prev, p, pass == 0 ? Outline : fillCol, pass == 0 ? ww + 2 * OL : ww);
                } else if (mine) {
                    if (run.isEmpty()) run.add(prev);
                    run.add(p);
                }
                if ((!mine || i > N) && run.size() > 1) {
                    PointF[] pts = run.toArray(new PointF[0]);
                    lines(c, pts, pass == 0 ? Outline : fillCol, pass == 0 ? full + 2 * OL : full);
                    if (pass == 1 && front) {   // a soft shine along each front turn, so it looks round
                        PointF[] shine = new PointF[pts.length];
                        for (int j = 0; j < pts.length; j++) shine[j] = P(pts[j].x - full * 0.15f, pts[j].y);
                        lines(c, shine, Color.argb(60, 255, 220, 180), full * 0.3f);
                    }
                    run.clear();
                } else if (!mine) run.clear();
                prev = p;
            }
        }
    }

    void drawTail(Canvas c, float hx, float hy, float dx, float dy, double r) {
        float w = tailWidth(r);
        PointF start = coilPoint(CoilEnd, w);   // leaves the branch from underneath
        PointF c1 = P(start.x + 8, start.y + 24);
        PointF c2 = P(hx - dx * 40 - dy * 14, hy - dy * 40 + dx * 14);
        PointF end = P(hx, hy);
        tailBez[0] = start; tailBez[1] = c1; tailBez[2] = c2; tailBez[3] = end;   // the hands climb along it
        bez(c, start, c1, c2, end, Outline, w + 2 * OL);
        bez(c, start, c1, c2, end, Fur, w);
        tailCoil(c, true, w);   // the turns across the front of the branch
        bez(c, start, c1, c2, end, Color.argb(60, 255, 220, 180), w * 0.3f);
    }

    // Sitting, the tail hangs down behind the branch, swaying, with a curl at the end.
    void drawSitTail(Canvas c, float hx, float hy) {
        float sw = (float) Math.sin(t * 1.5) * 6;
        PointF p0 = P(hx - 3, hy + 4), p1 = P(hx - 10, hy + 40), p2 = P(hx + 18 + sw, hy + 58), p3 = P(hx + 8 + sw, hy + 80);
        bez(c, p0, p1, p2, p3, Outline, 6f + 2 * OL);
        arc(c, p3.x - 12, p3.y - 6, 12, 12, 0, 230, Outline, 6f + 2 * OL);
        bez(c, p0, p1, p2, p3, Fur, 6f);
        arc(c, p3.x - 12, p3.y - 6, 12, 12, 0, 230, Fur, 6f);
    }

    static PointF bezAt(PointF[] b, float t) {
        float a = 1 - t;
        return P(a * a * a * b[0].x + 3 * a * a * t * b[1].x + 3 * a * t * t * b[2].x + t * t * t * b[3].x,
                 a * a * a * b[0].y + 3 * a * a * t * b[1].y + 3 * a * t * t * b[2].y + t * t * t * b[3].y);
    }

    // Where one hand is on the tail while it climbs, hand over hand ('offset' keeps the hands half a cycle apart).
    // Returns the hand; grip[0] says whether it's holding on, angle[0] which way the fist turns.
    static PointF climbHand(PointF[] bz, double ap, float offset, int side, PointF rest, boolean[] grip, float[] angle) {
        float len = (float) Math.hypot(bz[0].x - bz[3].x, bz[0].y - bz[3].y);
        float tHi = Math.max(0.3f, 1 - 40 / len), tLo = Math.max(0.35f, 1 - 10 / len);
        float tt, outward = 0;
        double ph = (Math.min(ap, ClimbTop) * ClimbRate + offset) % 1.0;
        grip[0] = true;
        if (ap >= ClimbTop) tt = offset == 0 ? tHi : (tHi + tLo) / 2;   // hanging at the top: one hand above the other
        else if (ph < 0.55) tt = tHi + (tLo - tHi) * (float) (ph / 0.55);  // holding on and pulling down
        else {
            float u = (float) ((ph - 0.55) / 0.45);                          // reaching up for the next grip
            tt = tLo + (tHi - tLo) * smooth(u);
            outward = side * 8 * (float) Math.sin(Math.PI * u);
            grip[0] = false;
        }
        PointF p = bezAt(bz, tt), q = bezAt(bz, Math.max(0, tt - 0.05f));
        angle[0] = (float) (Math.atan2(q.x - p.x, -(q.y - p.y)) * 180 / Math.PI);
        p = P(p.x + outward, p.y);
        if (ap >= ClimbLetGo) {
            grip[0] = false;
            return lerp(p, rest, smooth((ap - ClimbLetGo) / 0.35));   // let go and drop
        }
        return p;
    }

    // Fingers curled round the tail at 'hand'.
    void grip(Canvas c, PointF hand, float angle) {
        c.save();
        c.translate(hand.x, hand.y);
        c.rotate(angle);
        fistFront(c);
        c.restore();
    }

    // ---- The monkey ----

    void drawBody(Canvas c) {
        // Local frame: hips at (0,0), +y points away from the branch (head is at the bottom).
        float breathe = (float) Math.sin(breathPhase) * (1.2f + 1.4f * sleepW);
        Act shown = shownAct();
        float kickAmp = 2f + (shown == Act.Pump ? 6f * actW : 0);
        float kick = (float) Math.sin(2 * Math.PI * t / SwayPeriod + 1.2) * kickAmp;
        kick += ((float) Math.sin(t * 2.1) * 6 - kick) * perchK;   // sitting, its legs dangle and swing
        float beatSide = beatCount % 2 == 0 ? 1 : -1;
        kick += ((float) Math.sin(beatPhase * Math.PI) * 9 * beatSide - kick) * danceW;   // kick to the beat

        // Legs, bent up beside the tail.
        limb(c, 9, P(-8, 4), P(-22, -6), P(-14 - kick, -16));
        limb(c, 9, P(8, 4), P(22, -6), P(14 + kick, -16));
        foot(c, -21 - kick, -23, 13, 9);
        foot(c, 8 + kick, -23, 13, 9);

        // Torso in a puffer jacket (collar near the head, which hangs below).
        jacketTorso(c, -21 - breathe / 2, -4, 42 + breathe, 50, 44);

        double ap = actAge();
        // A pocket on the hip stuffed with bananas (it pulls the middle one out to eat).
        boolean bananaTaken = shown == Act.Eat && actW > 0.05f && ap >= GrabTime;
        boolean micTaken = shown == Act.Sing && actW > 0.05f && ap >= SgReach;
        if (perchK < 0.5f) {   // sitting, the jacket's upside down, so they're tucked right in
            drawBanana(c, P(PocketGrip.x - 5.5f, PocketGrip.y + 2), 165, PocketScale * 0.95f, 0, 1, false);
            drawBanana(c, P(PocketGrip.x + 5.5f, PocketGrip.y + 1.5f), 197, PocketScale * 0.95f, 0, 1, false);
            if (!bananaTaken && !micTaken) drawBanana(c, PocketGrip, 180, PocketScale, 0, 1, false);
        }
        drawPocket(c, !bananaTaken);

        // The water bottle's holder, below the pocket, with the bottle's cap peeking out (until it's taken).
        boolean bottleOut = shown == Act.Drink && actW > 0.3f && ap >= DkReach && ap < DkIn;
        if (!bottleOut) drawBottle(c, HolsterNeck, 0, HolsterScale, 1, NeckGrip, true, false, -1);
        drawHolster(c);

        // Arms in puffer sleeves: dangling below the head, unless it is busy with a habit.
        PointF restL = P(-28 + lag, 88), restR = P(28 + lag, 88);
        boolean sitting = perch == Perch.Sit;
        if (sitting) { restL = P(-24, -1); restR = P(24, -1); }   // hands on the branch beside it
        PointF tL = restL, tR = restR;
        if (shown == Act.Scratch) tR = face(P(23, 54 + (float) Math.sin(t * 22) * 3));   // scratching its head
        boolean climbing = shown == Act.Climb && ap < ClimbLetGo + 0.4;
        boolean[] gL = { false }, gR = { false };
        float[] aL = { 0 }, aR = { 0 };
        if (climbing) {
            // Hand over hand up its own tail.
            PointF[] bz = new PointF[4];
            for (int i = 0; i < 4; i++) bz[i] = winToBody(tailBez[i]);
            tL = climbHand(bz, ap, 0f, -1, restL, gL, aL);
            tR = climbHand(bz, ap, 0.5f, 1, restR, gR, aR);
        } else if (shown == Act.Wave) {
            // Waving at you with both hands, swinging out and in together.
            float wv = (float) Math.sin(t * 14) * 8;
            tL = face(P(-46 - wv, 58));
            tR = face(P(46 + wv, 58));
        }
        boolean eating = shown == Act.Eat && actW > 0.05f && ap < TossTime + 0.4;
        if (shown == Act.Eat && ap < TossTime) {
            // Hold the banana just under the chin, then bring it up so the fruit's tip goes into the mouth.
            int done = 0;
            for (double bt : BiteTimes) if (ap > bt + 0.35) done++;
            float len = 7 + 24 * (1 - 0.3f * done);   // from the hand to the tip of what's left
            PointF hold = face(lerp(P(20, 122), P(BiteMouth.x + 0.29f * len, BiteMouth.y + 0.96f * len), biteReach(ap)));
            if (ap < GrabTime) tR = PocketGrip;                                                                    // reach back to the pocket
            else if (bananaInPocket(ap)) tR = lerp(PocketGrip, PocketOut, smooth((ap - GrabTime) / PullOutTime));  // pull it out
            else tR = lerp(PocketOut, hold, carryK(ap));                                                           // bring it round
            chewing = isChewing(ap) && actW > 0.5f;
            biteOpen = biteReach(ap) > 0.75f && actW > 0.5f;
        }
        boolean drinking = shown == Act.Drink && actW > 0.05f;
        float sip = shown == Act.Drink ? drinkLift(ap) : 0;
        if (drinking) {
            // The bottle lives in a holder on its jacket (by the hip); the drinking spots are by its face.
            PointF holdF = face(DrinkHold), sipF = sipHandAt(ap, sitting);
            sipF.y += gulpBob(ap) * (sitting ? -1 : 1);
            if (ap < DkReach) tR = lerp(restR, HolsterNeck, phase(ap, 0, DkReach));
            else if (ap < DkOut) tR = lerp(HolsterNeck, HolsterOut, phase(ap, DkReach, DkOut));      // pull it out by the neck
            else if (ap < DkFront) tR = curve(HolsterOut, face(AroundHead), holdF, phase(ap, DkOut, DkFront));   // bring it round
            else if (ap < DkDown) tR = lerp(holdF, sipF, sip);                                     // cap off, up to the lips, gulp, down
            else if (ap < DkCapOn) tR = holdF;                                                     // cap back on
            else if (ap < DkBack) tR = curve(holdF, face(AroundHead), HolsterOut, phase(ap, DkCapOn, DkBack));
            else if (ap < DkIn) tR = lerp(HolsterOut, HolsterNeck, phase(ap, DkBack, DkIn));        // back in the holder
            else tR = lerp(HolsterNeck, restR, phase(ap, DkIn, DrinkLen));
            biteOpen = sip > 0.7f && actW > 0.5f;
            if (ap >= DkDown && !ahhSaid && actW > 0.5f) {
                ahhSaid = true; grinTime = 1.2;
                say(pick("Ahh! Refreshing!", "Ahh, that hits the spot!", "Mmm, nice cold water!"), true);
            }
        }
        if (shown == Act.Shades && actW > 0.05f) {
            // Reach up to the glasses, then lower the hand again.
            float up = smooth(ap / ShadesLift) * (1 - smooth((ap - ShadesSwap - 0.15) / 0.35));
            tR = lerp(restR, face(P(22, 61)), up);
        }
        boolean micOut = shown == Act.Sing && actW > 0.05f, micShown = false;
        float micAngle = 180, micStage = 0;
        if (micOut) {
            // Out of the pocket (ball first into it, so it comes out handle first), round its head, under the chin.
            double e = ap - (actLen - SgDownLen);   // putting it away, from 0
            PointF hold = face(MicHold);
            hold.x += (float) Math.sin(t * 3) * 1.5f;
            float aim = angleTo(hold, face(MicHead)), mk;
            if (ap < SgReach) { tR = lerp(restR, PocketGrip, phase(ap, 0, SgReach)); mk = 0; }
            else if (ap < SgOut) { tR = lerp(PocketGrip, PocketOut, phase(ap, SgReach, SgOut)); mk = 0; }
            else if (ap < SgUp) { mk = phase(ap, SgOut, SgUp); tR = curve(PocketOut, face(AroundHead), hold, mk); }
            else if (e < 0) { tR = hold; mk = 1; }
            else if (e < 0.5) { mk = 1 - phase(e, 0, 0.5); tR = curve(hold, face(AroundHead), PocketOut, 1 - mk); }
            else if (e < 0.75) { tR = lerp(PocketOut, PocketGrip, phase(e, 0.5, 0.75)); mk = 0; }
            else { tR = lerp(PocketGrip, restR, phase(e, 0.75, SgDownLen)); mk = 0; }
            float turn = ((aim - 180) % 360 + 540) % 360 - 180;   // the short way round
            micAngle = 180 + turn * mk;
            micStage = mk;
            micShown = ap >= SgReach && e < 0.75 && actW > 0.3f;
            // The other arm out wide, like a star on stage.
            tL = lerp(restL, face(P(-46, 70 + 5 * (float) Math.sin(t * 2.5))), mk);
        }
        PointF hL = lerp(restL, tL, actW), hR = lerp(restR, tR, actW);
        if (shown == Act.Eat && ap >= GrabTime && ap < TossTime && actW > 0.9f) hR = tR;   // the banana stays in its hand
        if (climbing && actW > 0.9f) { hL = tL; hR = tR; }                                  // hands stay on the tail
        if (drinking && actW > 0.9f && ap >= DkReach && ap < DkIn) hR = tR;                // the bottle stays in its hand
        if (micShown && actW > 0.9f) hR = tR;                                               // and so does the mic

        // The other hand twists the cap off, holds it while it drinks, and screws it back on.
        float bottleAngle = 0, bottleScale = HolsterScale, bottleGrip = NeckGrip;
        boolean capOn = true;
        if (drinking) {
            float restA = angleTo(hR, face(BiteMouth)), sipA = angleTo(sipHandAt(ap, sitting), face(SipLips));   // neck to the lips
            if (ap < DkOut) bottleAngle = 0;
            else if (ap < DkFront) bottleAngle = restA * phase(ap, DkOut, DkFront);
            else if (ap < DkCapOn) bottleAngle = restA + (sipA - restA) * sip;
            else if (ap < DkBack) bottleAngle = restA * (1 - phase(ap, DkCapOn, DkBack));
            bottleScale = ap < DkOut ? HolsterScale : ap < DkFront ? HolsterScale + (1 - HolsterScale) * phase(ap, DkOut, DkFront)
                        : ap < DkCapOn ? 1 : 1 - (1 - HolsterScale) * phase(ap, DkCapOn, DkBack);
            bottleGrip = ap < DkOut ? NeckGrip : ap < DkFront ? NeckGrip * (1 - phase(ap, DkOut, DkFront))
                       : ap < DkCapOn ? 0 : NeckGrip * phase(ap, DkCapOn, DkBack);
            capOn = ap < (DkFront + DkCapOff) / 2 + 0.1 || ap >= DkCapOn - 0.15;
            boolean capWork = ap >= DkFront - 0.15 && ap < DkCapOn + 0.15;
            capW += ((capWork && actW > 0.5f ? 1 : 0) - capW) * ease(10, frameDt);
            if (capW > 0.01f) {
                PointF top = heldToBody(hR, P(0, -29), 1, bottleAngle);   // the cap, on the bottle in front of it
                boolean twisting = (ap >= DkFront && ap < DkCapOff) || (ap >= DkDown && ap < DkCapOn);
                if (twisting) top.x += (float) Math.sin(t * 28) * 1.6f;   // twist, twist
                PointF capHold = face(P(-18, 106));
                PointF want = twisting || (ap >= DkCapOff - 0.1 && ap < DkCapOff + 0.1) || (ap >= DkDown - 0.1 && ap < DkDown + 0.05) ? top : capHold;
                capHand = lerp(capHand, want, ease(16, frameDt));
                hL = lerp(hL, capHand, capW);
            }
        } else capW = 0;
        if (perchK > 0.01f && !sitting) {   // swinging up or down: hands go to the branch
            hL = lerp(hL, P(-24, -1), perchK);
            hR = lerp(hR, P(24, -1), perchK);
        }
        // The free hand peels the banana: it pinches the end of the strip being peeled and pulls it down.
        boolean peelingNow = shown == Act.Eat && actW > 0.5f && ap > PeelStart - 0.35 && ap < PeelStart + 3 * PeelSlot + 0.1;
        peelW += ((peelingNow ? 1 : 0) - peelW) * ease(9, frameDt);
        if (shown == Act.Eat && ap < TossTime) {
            float pp = peelProgress(ap);
            float sp = Math.max(0, Math.min(1, pp - peelStrip));
            PointF want = heldToBody(hR, lerp(PeelClosed[peelStrip], PeelOpen[peelStrip], sp), bananaScale(ap), bananaAngle(ap, hR, face(BiteMouth)));
            if (peelW < 0.05f) peelHand = hL;
            peelHand = lerp(peelHand, want, ease(peelReaching ? 14 : 30, frameDt));   // quick to follow the strip once pinched
        }
        if (peelW > 0.01f) hL = lerp(hL, peelHand, peelW);
        if (danceW > 0.01f) {
            // Throw one arm out to the side, tuck the other in; swap on every beat.
            float e = (float) (1 - Math.pow(1 - Math.min(1, beatPhase * 2.5), 3));
            float wig = (float) Math.sin(t * 12) * 4;
            PointF rUp = P(46, 50 + wig), rTuck = P(22, 98), lUp = P(-46, 50 - wig), lTuck = P(-22, 98);
            PointF rr = beatSide > 0 ? lerp(rTuck, rUp, e) : lerp(rUp, rTuck, e);
            PointF ll = beatSide > 0 ? lerp(lUp, lTuck, e) : lerp(lTuck, lUp, e);
            hL = lerp(hL, ll, danceW);
            hR = lerp(hR, rr, danceW);
        }
        boolean scratchInFront = (shown == Act.Scratch || shown == Act.Shades) && actW > 0.3f;

        armTo(c, P(-16, 30), hL, -1);
        if (eating || drinking || micOut)
            sleevedArm(c, P(16, 30), P(36, 44), P(hR.x + 16, hR.y - 14), hR);   // arm behind the head
        else if (!scratchInFront)
            armTo(c, P(16, 30), hR, 1);
        if (climbing && actW > 0.5f) {
            if (gL[0]) grip(c, hL, aL[0]);
            if (gR[0]) grip(c, hR, aR[0]);
        }

        float headCY = HeadY + danceW * 3f * (float) Math.sin(beatPhase * Math.PI);   // nod to the beat
        if (drinking) headCY += gulpBob(ap);                                         // and bob with each gulp
        if (micOut) headCY += 2.5f * micStage * (float) Math.sin(beatPhase * Math.PI);  // nod along to its song
        gulping = drinking && ap >= DkLift - 0.05 && ap < DkLower;
        c.save();
        if (perchK > 0) c.rotate(-180 * perchK, 0, headCY);   // the body turns over to sit, but the head stays upright
        drawHead(c, 0, headCY);
        c.restore();
        if (scratchInFront) armTo(c, P(16, 30), hR, 1);

        if (eating) {
            if (ap < TossTime) {
                float appear = bananaScale(ap) * Math.min(1, actW / 0.9f);
                float peel = peelProgress(ap);
                int bitten = 0;
                for (double bt : BiteTimes) if (ap > bt) bitten++;
                if (ap < GrabTime) handShape(c, hR.x, hR.y + 3, 13);   // still reaching for the pocket
                else {
                    // Hide the part of the fruit above the middle of the mouth, so the tip looks like it's inside,
                    // and, while it's being pulled out, the part still in the pocket.
                    c.save();
                    float lipsY = BiteMouth.y + 3;
                    if (sitting) c.clipOutRect(-40, 2 * HeadY - lipsY, 40, 2 * HeadY - 20);
                    else c.clipOutRect(-40, 20, 40, lipsY);
                    if (bananaInPocket(ap)) c.clipOutRect(PocketRect);
                    drawBanana(c, hR, bananaAngle(ap, hR, face(BiteMouth)), appear, peel, 1 - 0.3f * bitten, true);
                    c.restore();
                }
                if (peelW > 0.3f) handShape(c, hL.x, hL.y + 2, 11);   // pinching fingers, in front of the peel
            } else if (!peelTossed) {
                // Toss the empty peel away.
                peelTossed = true;
                peelPos = bodyToWin(hR);
                peelVel = P((rnd.nextBoolean() ? -1 : 1) * (150 + rnd.nextFloat() * 120), -320);
                peelAngle = 0;
                peelSpin = (rnd.nextBoolean() ? -1 : 1) * (5 + rnd.nextFloat() * 4);
                peelAge = 0;
                peelFlying = true;
                grinTime = 1.0;
                say(pick("Yum!", "Delicious!", "Mmm, banana!"), true);
            }
            if (ap >= TossTime) handShape(c, hR.x, hR.y + 3, 13);   // empty-handed again
        }
        if (drinking) {
            if (ap >= DkReach && ap < DkIn && actW > 0.3f) {
                float left = 1 - 0.24f * gulpsDone(ap);
                c.save();
                if (ap < DkOut || ap >= DkBack) c.clipOutRect(HolsterRect);   // the part still in the holder
                drawBottle(c, hR, bottleAngle, bottleScale, left, bottleGrip, capOn, true, sip > 0.9f ? glugPhase(ap) : -1);
                c.restore();
                if (!capOn) drawCap(c, hL, bottleAngle);   // the cap, in its other hand
            } else handShape(c, hR.x, hR.y + 3, 13);
            double dd = ap - DkLower;
            if (dd > 0 && dd < 1.0 && actW > 0.5f) {
                // A last drop runs from the corner of its mouth, then falls.
                float run = 4 * smooth(dd / 0.45) + (dd > 0.45 ? 48 * (float) Math.pow((dd - 0.45) / 0.55, 2) : 0);
                PointF dp = face(P(5.5f, 88 + run));
                int al = (int) (230 * Math.min(1, (1.0 - dd) / 0.25));
                float sy = sitting ? -1 : 1;   // the drop's tail points back up toward the mouth
                Path drop = new Path();
                drop.moveTo(dp.x, dp.y - 3.4f * sy);
                drop.cubicTo(dp.x + 1.2f, dp.y - 1.4f * sy, dp.x + 2.1f, dp.y, dp.x + 2.1f, dp.y + 0.8f * sy);
                drop.cubicTo(dp.x + 2.1f, dp.y + 2.6f * sy, dp.x - 2.1f, dp.y + 2.6f * sy, dp.x - 2.1f, dp.y + 0.8f * sy);
                drop.cubicTo(dp.x - 2.1f, dp.y, dp.x - 1.2f, dp.y - 1.4f * sy, dp.x, dp.y - 3.4f * sy);
                drop.close();
                c.drawPath(drop, fill(Color.argb(al, 120, 195, 250)));
                c.drawPath(drop, pen(Color.argb(al, 40, 110, 190), 0.9f));
                oval(c, dp.x - 1.2f, dp.y - 0.2f * sy - 0.6f, 1.1f, 1.2f, fill(Color.argb(al, 255, 255, 255)));
            }
        }
        if (micOut) {
            if (micShown) {
                c.save();
                if (micStage < 0.01f) c.clipOutRect(PocketRect);   // the part still in the pocket
                drawMic(c, hR, micAngle);
                c.restore();
            } else handShape(c, hR.x, hR.y + 3, 13);
        }
    }

    void jacketTorso(Canvas c, float x, float y, float w, float h, float neckY) {
        blob(c, x, y, w, h, Jacket);
        c.save();
        tmp2.reset();
        tmp2.addOval(x, y, x + w, y + h, Path.Direction.CW);
        c.clipPath(tmp2);
        // Quilted puffer seams.
        for (float sy = y + 8; sy < y + h; sy += 10) {
            arc(c, x - 4, sy - 4, w + 8, 10, 0, 180, dark(Jacket, 0.35f), 1.4f);
            arc(c, x - 4, sy - 7, w + 8, 10, 20, 140, Color.argb(55, 255, 255, 255), 2f);
        }
        // Open front showing the chest fur, and the zipper edges.
        float cx = x + w / 2;
        oval(c, cx - 6, y - 4, 12, h + 8, fill(light(Fur, 0.45f)));
        line(c, cx - 6, y, cx - 6, y + h, Color.rgb(190, 190, 195), 1.3f);
        line(c, cx + 6, y, cx + 6, y + h, Color.rgb(190, 190, 195), 1.3f);
        c.restore();

        // Sherpa collar around the neck.
        for (int i = -3; i <= 3; i++) puff(c, x + w / 2 + i * (w / 7.5f), neckY - Math.abs(i) * 1.6f, 6.5f);
    }

    // The jacket's hip pocket: a stitched patch a shade darker than the jacket, with a hem along the opening.
    void drawPocket(Canvas c, boolean full) {
        RectF r = PocketRect;
        Path p = new Path();
        p.moveTo(r.left, r.top);
        p.lineTo(r.right, r.top);
        p.arcTo(new RectF(r.right - 8, r.bottom - 8, r.right, r.bottom), 0, 90);
        p.arcTo(new RectF(r.left, r.bottom - 8, r.left + 8, r.bottom), 90, 90);
        p.close();
        c.drawPath(p, fill(dark(Jacket, 0.14f)));
        c.drawPath(p, pen(Outline, 1.4f));
        Paint st = pen(Color.argb(150, 200, 225, 220), 0.9f);
        st.setStrokeCap(Paint.Cap.BUTT);
        st.setPathEffect(stitchDash);
        tmp.reset();
        tmp.moveTo(r.left + 2.5f, r.top + 4); tmp.lineTo(r.left + 2.5f, r.bottom - 4); tmp.lineTo(r.left + 5, r.bottom - 2.5f);
        tmp.lineTo(r.right - 5, r.bottom - 2.5f); tmp.lineTo(r.right - 2.5f, r.bottom - 4); tmp.lineTo(r.right - 2.5f, r.top + 4);
        c.drawPath(tmp, st);
        // The bananas inside push the fabric out: soft curved bulges with a shadow along one side.
        float[] bxs = { 8, 18, 13 };
        for (int i = full ? 2 : 1; i >= 0; i--) {
            float x0 = bxs[i], lean = i == 0 ? -1.5f : i == 1 ? 1.5f : 0;
            PointF a = P(x0, r.top + 3), bb = P(x0 - 2 + lean, r.top + 8), cc = P(x0 - 2 + lean * 1.6f, r.top + 13), d = P(x0 + lean * 2, r.bottom - 3);
            bez(c, a, bb, cc, d, Color.argb(60, 255, 255, 255), 5f);
            bez(c, P(a.x + 2.8f, a.y), P(bb.x + 3, bb.y), P(cc.x + 3, cc.y), P(d.x + 2.6f, d.y), Color.argb(80, 10, 40, 40), 1.3f);
        }
        line(c, r.left + 1, r.top + 1.2f, r.right - 1, r.top + 1.2f, dark(Jacket, 0.3f), 2.4f);
        arc(c, r.left + 2, r.top - 0.5f, r.width() - 4, 4, 0, 180, Color.argb(160, 15, 35, 35), 1.6f);
    }

    // A little sleeve for the water bottle, sewn on the jacket.
    void drawHolster(Canvas c) {
        RectF r = HolsterRect;
        Path p = new Path();
        p.moveTo(r.left, r.top);
        p.lineTo(r.right, r.top);
        p.arcTo(new RectF(r.right - 7, r.bottom - 7, r.right, r.bottom), 0, 90);
        p.arcTo(new RectF(r.left, r.bottom - 7, r.left + 7, r.bottom), 90, 90);
        p.close();
        c.drawPath(p, fill(dark(Jacket, 0.22f)));
        c.drawPath(p, pen(Outline, 1.3f));
        line(c, r.left + 1.5f, r.top + 1.6f, r.right - 1.5f, r.top + 1.6f, Sherpa, 2.6f);   // elastic top
        Paint st = pen(Color.argb(140, 200, 225, 220), 0.8f);
        st.setStrokeCap(Paint.Cap.BUTT);
        st.setPathEffect(stitchDash);
        c.drawLine(r.centerX(), r.top + 5, r.centerX(), r.bottom - 3, st);
    }

    // The yellow skin: bottom part plus three strips that lie closed along the fruit and fold open as it is peeled.
    // 'peel' is 0 (closed) .. 3 (all strips down). With 'fist', the fingers go between the skin's bottom and the strips.
    void drawPeelSkin(Canvas c, float peel, boolean withTip, float fistScale) {
        PointF s0 = P(0, 4), s1 = P(2, 0), s2 = P(2, -4), s3 = P(1, -9);
        bez(c, s0, s1, s2, s3, Outline, 8.5f + 2.6f);
        bez(c, s0, s1, s2, s3, BananaYellow, 8.5f);
        c.drawRect(-1.8f, 3, 1.8f, 7, fill(BananaBrown));   // stem
        if (fistScale > 0) { c.save(); c.scale(fistScale, fistScale); fistFront(c); c.restore(); }
        for (int i = 2; i >= 0; i--) {
            float pi = Math.max(0, Math.min(1, peel - i));   // strips come down one after another
            if (i == 2 && pi > 0.5f) continue;   // the back strip hides behind once open
            PointF end = lerp(PeelClosed[i], PeelOpen[i], pi);
            PointF mid = P((PeelRoots[i].x + end.x) / 2 + (i == 0 ? -3 : 3) * (1 - pi) + (i == 0 ? -4 : 4) * pi,
                           (PeelRoots[i].y + end.y) / 2 - 6 * pi);
            bez(c, PeelRoots[i], mid, mid, end, Outline, 4.6f + 2.4f);
            bez(c, PeelRoots[i], mid, mid, end, i == 2 ? dark(BananaYellow, 0.08f) : BananaYellow, 4.6f);
        }
        // The brown tip while it is still closed.
        if (withTip && peel < 0.3f) oval(c, 0, -32.5f, 3.6f, 3.6f, fill(BananaBrown));
    }

    // A banana held at 'hand', turned 'angle' degrees. 'peel' opens the skin, 'remain' is how much fruit is left.
    void drawBanana(Canvas c, PointF hand, float angle, float appear, float peel, float remain, boolean held) {
        c.save();
        c.translate(hand.x, hand.y);
        c.rotate(angle);
        if (held) fistBack(c);   // the palm, behind the banana (the hand doesn't grow with the banana)
        if (appear >= 0.02f) {
            c.scale(appear, appear);
            // The fruit: a curved stroke that gets shorter with each bite.
            PointF[] fruit = splitBezier(P(1, -7), P(4.5f, -14), P(4.5f, -23), P(1, -31), Math.max(0.05f, remain));
            bez(c, fruit[0], fruit[1], fruit[2], fruit[3], Color.rgb(200, 175, 110), 8.5f);
            bez(c, fruit[0], fruit[1], fruit[2], fruit[3], BananaFruit, 6.5f);
            drawPeelSkin(c, peel, true, held ? 1 / appear : 0);
        } else if (held) fistFront(c);
        c.restore();
    }

    void drawFlyingPeel(Canvas c) {
        float fade = Math.max(0, Math.min(1, (H - peelPos.y) / 70f)) * (float) Math.min(1, (1.8 - peelAge) / 0.4);
        if (fade <= 0.02f) return;
        c.save();
        c.translate(peelPos.x, peelPos.y);
        c.rotate(peelAngle * 57.3f);
        oval(c, -10, -6, 20, 12, fill(Color.argb((int) (40 * fade), 0, 0, 0)));
        drawPeelSkin(c, 3, false, 0);
        c.restore();
    }

    // A small water bottle held in a fist at 'hand', turned 'angle' degrees from upright.
    // 'grip' slides the bottle so the fist holds it higher up (22 = by the neck); without 'held', no hand at all.
    void drawBottle(Canvas c, PointF hand, float angle, float appear, float waterLeft, float gripAt, boolean capOn, boolean held, float glug) {
        if (appear < 0.02f) { if (held) handShape(c, hand.x, hand.y + 3, 13); return; }
        c.save();
        c.translate(hand.x, hand.y);
        c.rotate(angle);
        if (held) fistBack(c);
        c.save();
        c.scale(appear, appear);
        c.translate(0, gripAt);
        Path body = new Path();
        body.arcTo(new RectF(-5.5f, -26, 5.5f, -18), 180, 180);
        body.lineTo(5.5f, 2);
        body.arcTo(new RectF(-5.5f, -2, 5.5f, 4), 0, 180);
        body.close();
        c.drawPath(body, fill(Color.argb(150, 205, 235, 255)));
        c.save();
        c.clipPath(body);
        // Upright the water sits at the bottom; tipped up to drink, it runs down to the cap end.
        boolean tipped = Math.abs(angle) > 75;
        float top = tipped ? -28 : 4 - 30 * waterLeft, depth = tipped ? 30 * waterLeft : 34;
        c.drawRect(-6, top, 6, top + depth, fill(new LinearGradient(0, top - 1, 0, top + 35, Color.argb(220, 90, 175, 245), Color.argb(230, 40, 120, 220), Shader.TileMode.CLAMP)));
        if (glug >= 0 && tipped)
            // Air glugging in at the neck and wobbling up to the bottom (which is the top now).
            for (int i = 0; i < 4; i++) {
                float u = glug * 1.5f - i * 0.17f;
                if (u <= 0 || u >= 1) continue;
                float by = -24 + 26 * u, bx = (float) Math.sin(u * 10 + i * 2) * 1.8f, br = 1.1f + 0.35f * (i % 3);
                oval(c, bx - br, by - br, 2 * br, 2 * br, fill(Color.argb(150, 235, 248, 255)));
                ovalLine(c, bx - br, by - br, 2 * br, 2 * br, Color.argb(200, 255, 255, 255), 0.6f);
            }
        c.drawRect(-3.5f, -21, -1.9f, -3, fill(Color.argb(150, 255, 255, 255)));   // shine
        c.drawRect(-6, -13, 6, -9, fill(Color.argb(230, 255, 255, 255)));          // label
        c.restore();
        c.drawPath(body, pen(Color.rgb(40, 90, 150), 1.4f));
        if (capOn) drawCapShape(c, 0, -28);
        else line(c, -2.8f, -26.2f, 2.8f, -26.2f, Color.rgb(40, 90, 150), 1.6f);   // open neck
        c.restore();
        if (held) fistFront(c);   // fingers around the bottle
        c.restore();
    }

    // The bottle's screw cap, centred at (x, y), with ridges.
    void drawCapShape(Canvas c, float x, float y) {
        c.drawRect(x - 3.5f, y - 3, x + 3.5f, y + 3, fill(Color.rgb(30, 85, 175)));
        for (float rx = -2; rx <= 2; rx += 2) line(c, x + rx, y - 2.2f, x + rx, y + 2.2f, Color.argb(120, 255, 255, 255), 0.8f);
        Paint o = pen(Outline, 1f);
        o.setStrokeJoin(Paint.Join.MITER);
        c.drawRect(x - 3.5f, y - 3, x + 3.5f, y + 3, o);
        o.setStrokeJoin(Paint.Join.ROUND);
    }

    // The cap held between its fingers, while it drinks.
    void drawCap(Canvas c, PointF hand, float angle) {
        c.save();
        c.translate(hand.x, hand.y);
        c.rotate(angle);
        drawCapShape(c, 0, -6);
        c.restore();
    }

    // A handheld microphone in a fist at 'hand', turned 'angle' degrees from upright (ball at the top).
    void drawMic(Canvas c, PointF hand, float angle) {
        c.save();
        c.translate(hand.x, hand.y);
        c.rotate(angle);
        fistBack(c);
        Path handle = new Path();
        handle.moveTo(-3.2f, -13); handle.lineTo(3.2f, -13); handle.lineTo(2.2f, 10); handle.lineTo(-2.2f, 10); handle.close();
        c.drawPath(handle, fill(new LinearGradient(-3.5f, 0, 3.5f, 0, Color.rgb(70, 70, 82), Color.rgb(25, 25, 32), Shader.TileMode.CLAMP)));
        c.drawPath(handle, pen(Outline, 1.1f));
        c.drawRect(-4.2f, -15.5f, 4.2f, -12, fill(Color.rgb(200, 205, 215)));   // the collar
        c.drawRect(-4.2f, -15.5f, 4.2f, -12, pen(Outline, 1f));
        RectF ball = new RectF(-6.8f, -28.5f, 6.8f, -14.9f);
        c.drawOval(ball, fill(grad(ball.left, ball.top, ball.width(), ball.height(), Color.rgb(240, 242, 248), Color.rgb(140, 145, 158), 45)));
        Path round = new Path();
        round.addOval(ball, Path.Direction.CW);
        c.save();
        c.clipPath(round);
        for (float m = -6; m <= 6; m += 2.4f) {
            line(c, m, ball.top, m, ball.bottom, Color.argb(150, 90, 95, 108), 0.7f);
            line(c, ball.left, -21.7f + m, ball.right, -21.7f + m, Color.argb(150, 90, 95, 108), 0.7f);
        }
        c.restore();
        oval(c, -4.5f, -26.5f, 4, 3, fill(Color.argb(170, 255, 255, 255)));
        c.drawOval(ball, pen(Outline, 1.2f));
        fistFront(c);
        c.restore();
    }

    boolean eyeRest() { return shownAct() == Act.EyeRest && actW > 0.5f && !grabbed; }

    boolean restBlinkNow() {
        if (!eyeRest()) return false;
        double ap = actAge();
        for (double b : RestBlinks) if (ap > b && ap < b + 0.28) return true;
        return false;
    }

    boolean swappingShades() {
        double ap = actAge();
        return shownAct() == Act.Shades && actW > 0.3f && ap >= ShadesLift && ap < ShadesSwap + 0.05;
    }

    // Pushed up on its head to sleep, rest its eyes, when you grab it, or taken off.
    boolean shadesLifted() { return !shadesOn || grabbed || sleepW >= 0.5f || eyeRest() || swappingShades(); }

    final Path tuftPath = new Path(), maskPath = new Path(), grinPath = new Path(), lensPath = new Path(), shinePath = new Path();

    void drawHead(Canvas c, float cx, float cy) {
        final float hr = 25;
        boolean closed = (blinkTime > 0 || gulping) && !grabbed;   // eyes shut while it gulps
        boolean grin = grinTime > 0 && !grabbed;

        // Ears.
        int earIn = mix(Skin, Color.rgb(240, 150, 150), 0.35f);
        blob(c, cx - hr - 10, cy - 13, 19, 19, Fur);
        blob(c, cx + hr - 9, cy - 13, 19, 19, Fur);
        shade(c, cx - hr - 5, cy - 8, 10, 10, earIn);
        shade(c, cx + hr - 4, cy - 8, 10, 10, earIn);

        // Head.
        blob(c, cx - hr, cy - hr, 2 * hr, 2 * hr, Fur);

        // Hair tuft.
        Path tuft = tuftPath;
        tuft.reset();
        tuft.moveTo(cx - 7, cy - hr + 4);
        tuft.cubicTo(cx - 9, cy - hr - 4, cx - 4, cy - hr - 9, cx - 2, cy - hr - 10);
        tuft.cubicTo(cx - 1, cy - hr - 5, cx + 1, cy - hr - 4, cx + 3, cy - hr - 3);
        tuft.cubicTo(cx + 5, cy - hr - 8, cx + 9, cy - hr - 9, cx + 11, cy - hr - 8);
        tuft.cubicTo(cx + 8, cy - hr - 3, cx + 7, cy - hr + 1, cx + 6, cy - hr + 4);
        tuft.close();
        tuft.moveTo(cx - 13, cy - hr + 6);
        tuft.cubicTo(cx - 16, cy - hr - 2, cx - 12, cy - hr - 6, cx - 8, cy - hr - 7);
        tuft.cubicTo(cx - 9, cy - hr - 2, cx - 8, cy - hr + 1, cx - 6, cy - hr + 5);
        tuft.close();
        c.drawPath(tuft, fill(dark(Fur, 0.15f)));
        c.drawPath(tuft, pen(Outline, 1.4f));

        c.save();
        c.translate(faceShift, 0);   // the face turns a little

        // Face mask: two eye patches and a muzzle, merged into one soft shape.
        Path mask = maskPath;
        mask.reset();
        mask.addOval(cx - 19, cy - 16, cx + 1, cy + 4, Path.Direction.CW);
        mask.addOval(cx - 1, cy - 16, cx + 19, cy + 4, Path.Direction.CW);
        mask.addOval(cx - 17, cy - 3, cx + 17, cy + 20, Path.Direction.CW);
        c.drawPath(mask, fill(grad(cx - 19, cy - 17, 38, 41, light(Skin, 0.35f), dark(Skin, 0.05f), 90)));
        oval(c, cx - 8, cy, 16, 6, fill(Color.argb(70, 255, 255, 255)));

        if (chewing) {
            // Cheeks puffed out with banana.
            shade(c, cx - 19, cy - 1, 12, 11, Skin);
            shade(c, cx + 7, cy - 1, 12, 11, Skin);
        }

        // Eyebrows (raised in surprise when grabbed), above the shades.
        float lift = grabbed ? 3.5f : grin ? 1.5f : 0;
        int brow = dark(Fur, 0.3f);
        arc(c, cx - 17, cy - 22 - lift, 13, 6, 200, 120, brow, 2.4f);
        arc(c, cx + 4, cy - 22 - lift, 13, 6, 220, 120, brow, 2.4f);

        // Eyes are hidden behind the shades, unless they're up.
        boolean lifted = shadesLifted();
        float eh = grabbed ? 14.5f : 12.5f;
        if (lifted)
            for (float ex : new float[] { cx - 8.5f, cx + 8.5f }) {
                boolean sleepy = sleepW >= 0.5f && !grabbed;
                float ey = cy - 6;
                if (sleepy) {
                    // Peacefully closed eyes with little lashes.
                    arc(c, ex - 5, ey - 5, 10, 7, 20, 140, Ink, 2.2f);
                    line(c, ex - 4.5f, ey - 0.5f, ex - 6.5f, ey + 1.5f, Ink, 2.2f);
                    continue;
                }
                if (closed || (grin && ex > cx) || restBlinkNow()) {
                    // Closed (blink), or a happy wink with the right eye while grinning.
                    arc(c, ex - 5, ey - 3, 10, 7, 200, 140, Ink, 2.2f);
                    continue;
                }
                oval(c, ex - 6, ey - eh / 2 - 1, 12, eh + 2, fill(argb(150, Outline)));
                oval(c, ex - 5, ey - eh / 2, 10, eh, fill(Color.WHITE));
                float ix = ex + lookX, iy = ey + lookY + 0.5f;
                oval(c, ix - 3.8f, iy - 4.2f, 7.6f, 8.4f, fill(Color.rgb(120, 72, 35)));
                oval(c, ix - 2.3f, iy - 2.6f, 4.6f, 5.2f, fill(Ink));
                oval(c, ix - 2.6f, iy - 3.6f, 2.8f, 2.8f, fill(Color.WHITE));
                oval(c, ix + 1f, iy + 1.2f, 1.3f, 1.3f, fill(Color.WHITE));
            }

        shades(c, cx, cy - 6, lifted);

        // Nose.
        oval(c, cx - 4.2f, cy + 3.5f, 3.2f, 2.4f, fill(Ink));
        oval(c, cx + 1f, cy + 3.5f, 3.2f, 2.4f, fill(Ink));

        // Mouth: "ooh" when grabbed or biting, chomping while chewing, singing, talking, big grin when tapped, smirk otherwise.
        if (sleepW >= 0.5f && !grabbed) {
            // Mouth slightly open, breathing slowly.
            float m = 3.4f + 1.3f * (float) Math.sin(breathPhase);
            oval(c, cx - m / 2, cy + 10.5f, m, m * 0.9f, fill(Mouth));
            ovalLine(c, cx - m / 2, cy + 10.5f, m, m * 0.9f, Outline, 1.1f);
        } else if (singLevel > 0.05f && !grabbed && !chewing) {
            // Singing: two syllables a beat, rounder ("ooh") the wider it opens.
            float syl = Math.abs((float) Math.sin(beatPhase * Math.PI * 2));
            float open = 2.5f + (4 + 5 * singLevel) * syl * singLevel;
            float w = 11 - open * 0.3f;
            oval(c, cx - w / 2, cy + 9, w, open, fill(Mouth));
            if (open > 5) oval(c, cx - w / 2 + 2, cy + 9 + open - 3.5f, w - 4, 3.5f, fill(Tongue));
            ovalLine(c, cx - w / 2, cy + 9, w, open, Outline, 1.2f);
        } else if (talking() && !grabbed && !chewing && !biteOpen) {
            // Mouth flaps while it talks.
            float open = 2 + 5 * Math.abs((float) Math.sin(t * 16));
            oval(c, cx - 5, cy + 9.5f, 10, open, fill(Mouth));
            if (open > 4) oval(c, cx - 3, cy + 9.5f + open - 3, 6, 3, fill(Tongue));
            ovalLine(c, cx - 5, cy + 9.5f, 10, open, Outline, 1.2f);
        } else if (chewing && !grabbed) {
            if (Math.sin(t * 17) > 0) {
                oval(c, cx - 3.5f, cy + 10, 7, 4.5f, fill(Mouth));
                ovalLine(c, cx - 3.5f, cy + 10, 7, 4.5f, Outline, 1.2f);
            } else line(c, cx - 4, cy + 12, cx + 4, cy + 11.5f, Ink, 2.2f);
        } else if (grabbed || biteOpen) {
            oval(c, cx - 4.5f, cy + 9, 9, 10, fill(Mouth));
            oval(c, cx - 3, cy + 14, 6, 4, fill(Tongue));
            ovalLine(c, cx - 4.5f, cy + 9, 9, 10, Outline, 1.3f);
        } else if (grin) {
            Path p = grinPath;
            p.reset();
            p.arcTo(cx - 11, cy + 1, cx + 11, cy + 18, 0, 180, true);
            p.close();
            c.drawPath(p, fill(Mouth));
            c.save();
            c.clipPath(p);
            oval(c, cx - 6, cy + 11, 12, 9, fill(Tongue));
            c.drawRect(cx - 11, cy + 9, cx + 11, cy + 12, fill(Color.WHITE));
            c.restore();
            c.drawPath(p, pen(Outline, 1.4f));
        } else {
            // A confident little smirk, higher on one side.
            bez(c, P(cx - 8, cy + 11), P(cx - 3, cy + 14.5f), P(cx + 4, cy + 13.5f), P(cx + 9, cy + 8.5f), Ink, 2.3f);
            arc(c, cx + 8, cy + 6.5f, 4, 4, 150, 90, Ink, 2.3f);   // dimple
        }

        // Soft blush.
        int a = grabbed || grin ? 80 : 55;
        for (float bx : new float[] { cx - 13.5f, cx + 13.5f }) {
            oval(c, bx - 5, cy + 5, 10, 6, fill(Color.argb(a, 255, 120, 130)));
            oval(c, bx - 3.5f, cy + 6, 7, 4, fill(Color.argb(a, 255, 120, 130)));
        }
        c.restore();
    }

    // Cool aviator shades with an iridescent green-to-purple tint; pushed up onto its forehead when it needs its eyes.
    void shades(Canvas c, float cx, float ey, boolean pushedUp) {
        float y = pushedUp ? ey - 13 : ey;
        float glint = lookX * 0.8f;
        line(c, cx - 17, y - 3, cx - 24, y - 6, Silver, 1.6f);
        line(c, cx + 17, y - 3, cx + 24, y - 6, Silver, 1.6f);
        for (int side = -1; side <= 1; side += 2) {
            float lx = cx + side * 9;
            c.save();
            c.scale(1.18f, 1.18f, lx, y);     // nice big lenses
            Path lens = lensPath;
            lens.reset();
            // Teardrop aviator lens: flat-ish top, rounder and lower toward the outside.
            lens.moveTo(lx - side * 7.5f, y - 5);
            lens.cubicTo(lx - side * 2, y - 6.5f, lx + side * 4, y - 6.5f, lx + side * 8, y - 4.5f);
            lens.cubicTo(lx + side * 9.5f, y + 2, lx + side * 6, y + 7.5f, lx + side * 1, y + 7);
            lens.cubicTo(lx - side * 5, y + 6.5f, lx - side * 8, y + 1, lx - side * 7.5f, y - 5);
            lens.close();
            c.drawPath(lens, fill(grad(lx - 9, y - 7, 18, 15, Color.argb(252, 20, 175, 105), Color.argb(252, 125, 40, 195), 35)));
            c.save();
            c.clipPath(lens);
            Path shine = shinePath;
            shine.reset();
            shine.moveTo(lx - 6 + glint, y - 7); shine.lineTo(lx - 2 + glint, y - 7);
            shine.lineTo(lx - 7 + glint, y + 8); shine.lineTo(lx - 10 + glint, y + 8); shine.close();
            c.drawPath(shine, fill(Color.argb(125, 255, 255, 255)));
            oval(c, lx + 2 + glint * 0.5f, y - 4.5f, 3, 2.2f, fill(Color.argb(125, 255, 255, 255)));
            c.restore();
            c.drawPath(lens, pen(Outline, 2.6f));
            c.drawPath(lens, pen(Silver, 1.4f));
            c.restore();
        }
        arc(c, cx - 3, y - 7, 6, 4, 180, 180, Outline, 2.8f);
        arc(c, cx - 3, y - 7, 6, 4, 180, 180, Silver, 1.5f);
    }

    // ---- Zs, notes and the speech bubble ----

    void drawZzz(Canvas c) {
        for (Zz z : zzz) {
            float fade = (float) Math.min(Math.min(1, z.age / 0.4), (2.6 - z.age) / 0.9);
            if (fade <= 0) continue;
            float size = z.note ? 15 + (float) z.age * 3 : 10 + (float) z.age * 5;
            int fillCol = z.note ? z.col : Color.rgb(235, 240, 255);
            zPaint.setTextSize(size * 1.3f);
            zPaint.setStyle(Paint.Style.STROKE);
            zPaint.setStrokeWidth(2.6f);
            zPaint.setStrokeJoin(Paint.Join.ROUND);
            zPaint.setColor(Color.argb((int) (200 * fade), 30, 40, 90));
            c.drawText(z.ch, z.p.x, z.p.y, zPaint);
            zPaint.setStyle(Paint.Style.FILL);
            zPaint.setColor(argb((int) (255 * fade), fillCol));
            c.drawText(z.ch, z.p.x, z.p.y, zPaint);
        }
    }

    final Path bubblePath = new Path(), bubbleTail = new Path();

    void drawBubble(Canvas c) {
        if (sayText == null) return;
        double age = clock - sayStart;
        float fade = (float) Math.max(0, Math.min(1, Math.min(age / 0.15, (sayLen - age) / 0.3)));
        if (fade <= 0.02f) return;
        // Pops in with a little overshoot.
        double u = Math.min(1, age / 0.22);
        float pop = (float) (0.6 + 0.4 * (1 + 2.2 * Math.pow(u - 1, 3) + 1.2 * Math.pow(u - 1, 2)));

        if (sayLayout == null) {
            float tw = Math.min(160, textPaint.measureText(sayText));
            sayLayout = StaticLayout.Builder.obtain(sayText, 0, sayText.length(), textPaint, (int) Math.ceil(tw) + 1)
                .setAlignment(Layout.Alignment.ALIGN_CENTER).setLineSpacing(0, 1.05f).build();
        }
        float tw = 0;
        for (int i = 0; i < sayLayout.getLineCount(); i++) tw = Math.max(tw, sayLayout.getLineWidth(i));
        float w = tw + 24, h = sayLayout.getHeight() + 14;
        // Beside the head: on the left if there is room (it hangs near the right edge), else on the right.
        boolean left = headWin.x - 34 - w > 4;
        float bx = left ? headWin.x - 34 - w : headWin.x + 34;
        float by = headWin.y - 50 - h / 2;
        bx = Math.max(4, Math.min(W - w - 4, bx));
        by = Math.max(14, Math.min(H - h - 4, by));   // clear of the status bar
        PointF tip = P(headWin.x + (left ? -18 : 18), headWin.y - 20);

        c.save();
        c.scale(pop, pop, tip.x, tip.y);
        Path p = bubblePath;
        p.reset();
        p.addRoundRect(bx, by, bx + w, by + h, 12, 12, Path.Direction.CW);
        // The little tail pointing at its mouth.
        float baseX = Math.max(bx + 14, Math.min(bx + w - 14, left ? bx + w - 18 : bx + 18));
        float baseY = by + h - 2;
        bubbleTail.reset();
        bubbleTail.moveTo(baseX - 7, baseY); bubbleTail.lineTo(baseX + 7, baseY); bubbleTail.lineTo(tip.x, tip.y); bubbleTail.close();
        p.op(bubbleTail, Path.Op.UNION);
        c.save();
        c.translate(1.5f, 3);
        c.drawPath(p, fill(Color.argb((int) (45 * fade), 0, 0, 0)));
        c.restore();
        c.drawPath(p, fill(Color.argb((int) (252 * fade), 255, 253, 247)));
        c.drawPath(p, pen(argb((int) (255 * fade), Outline), 2f));
        textPaint.setColor(argb((int) (255 * fade), Ink));
        c.save();
        c.translate(bx + 12 + (w - 24 - sayLayout.getWidth()) / 2, by + 7);
        sayLayout.draw(c);
        c.restore();
        c.restore();
    }
}
