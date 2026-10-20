package com.binumadhavan.monkeyplay;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.provider.Settings;

// Brings the monkey back after the phone restarts, if you asked for that and it was out when the phone went off.
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context c, Intent i) {
        if (!Intent.ACTION_BOOT_COMPLETED.equals(i.getAction())) return;
        SharedPreferences p = c.getSharedPreferences("monkey", Context.MODE_PRIVATE);
        if (p.getBoolean("boot", false) && p.getBoolean("on", true) && Settings.canDrawOverlays(c))
            c.startForegroundService(new Intent(c, MonkeyService.class));
    }
}
