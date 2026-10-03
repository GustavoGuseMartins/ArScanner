package com.arscanner.usb;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.hardware.usb.UsbConstants;
import android.hardware.usb.UsbDevice;
import android.hardware.usb.UsbDeviceConnection;
import android.hardware.usb.UsbEndpoint;
import android.hardware.usb.UsbInterface;
import android.hardware.usb.UsbManager;

import java.util.ArrayDeque;
import java.util.HashMap;

/** CP210x receive-only bridge for the UWB base at 115200 8N1.
 *  The CP210x setup requests follow Silicon Labs' interface and the MIT-licensed
 *  usb-serial-for-android Cp21xxSerialDriver by Google Inc. and Mike Wakerly.
 */
public final class UwbUsbSerial {
    private static final int SILABS_VID = 0x10c4;
    private static final int CP210X_PID = 0xea60;
    private static final int REQUEST_TYPE = 0x41;
    private static final int TIMEOUT_MS = 1000;
    private final Activity activity;
    private final UsbManager manager;
    private final ArrayDeque<String> lines = new ArrayDeque<>();
    private final StringBuilder partial = new StringBuilder(256);
    private volatile UsbDeviceConnection connection;
    private String openedDeviceName;
    private UsbInterface claimedInterface;
    private UsbEndpoint input;
    private Thread reader;
    private volatile boolean running;
    private boolean opening;
    private String permissionRequestedFor;
    private String status = "USB: aguardando base CP210x";

    public UwbUsbSerial(Activity activity) {
        this.activity = activity;
        this.manager = (UsbManager) activity.getSystemService(Context.USB_SERVICE);
    }

    private UsbDevice findBase() {
        if (manager == null) return null;
        HashMap<String, UsbDevice> devices = manager.getDeviceList();
        for (UsbDevice d : devices.values())
            if (d.getVendorId() == SILABS_VID && d.getProductId() == CP210X_PID) return d;
        return null;
    }

    public synchronized boolean ensureStarted() {
        UsbDevice device = findBase();
        if (running && connection != null && device != null &&
            device.getDeviceName().equals(openedDeviceName)) return true;
        if (running || connection != null) closePort();
        if (opening) return false;
        if (device == null) {
            status = "USB: base não conectada ao celular";
            permissionRequestedFor = null;
            return false;
        }
        if (!manager.hasPermission(device)) {
            status = "USB: autorize a base na janela do Android";
            if (!device.getDeviceName().equals(permissionRequestedFor)) {
                permissionRequestedFor = device.getDeviceName();
                Intent intent = new Intent(activity.getPackageName() + ".UWB_USB_PERMISSION")
                    .setPackage(activity.getPackageName());
                int flags = PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_MUTABLE;
                PendingIntent pending = PendingIntent.getBroadcast(activity, 0, intent, flags);
                manager.requestPermission(device, pending);
            }
            return false;
        }
        opening = true;
        status = "USB: abrindo base...";
        new Thread(() -> {
            synchronized (UwbUsbSerial.this) {
                try {
                    open(device);
                    status = "USB: base conectada (CP210x, 115200)";
                } catch (Exception ex) {
                    closePort();
                    status = "USB: falha ao abrir base: " + ex.getMessage();
                } finally { opening = false; }
            }
        }, "UwbUsbCp210xOpen").start();
        return false;
    }

    public synchronized void requestPermissionAgain() {
        permissionRequestedFor = null;
    }

    private void request(UsbDeviceConnection c, int code, int value, int index, byte[] data)
        throws Exception {
        int length = data == null ? 0 : data.length;
        int result = c.controlTransfer(REQUEST_TYPE, code, value, index, data, length, TIMEOUT_MS);
        if (result != length) throw new Exception("controle CP210x " + code + ": " + result);
    }

    private void open(UsbDevice device) throws Exception {
        closePort();
        UsbDeviceConnection c = manager.openDevice(device);
        if (c == null) throw new Exception("permissão ou abertura recusada");
        connection = c;
        UsbInterface chosen = null;
        UsbEndpoint endpoint = null;
        for (int i = 0; i < device.getInterfaceCount() && endpoint == null; i++) {
            UsbInterface iface = device.getInterface(i);
            for (int j = 0; j < iface.getEndpointCount(); j++) {
                UsbEndpoint candidate = iface.getEndpoint(j);
                if (candidate.getType() == UsbConstants.USB_ENDPOINT_XFER_BULK &&
                    candidate.getDirection() == UsbConstants.USB_DIR_IN) {
                    chosen = iface;
                    endpoint = candidate;
                    break;
                }
            }
        }
        if (chosen == null || endpoint == null || !c.claimInterface(chosen, true))
            throw new Exception("interface serial ausente");
        claimedInterface = chosen;
        input = endpoint;
        int index = chosen.getId();
        request(c, 0x00, 1, index, null); // UART enable
        request(c, 0x07, 0x300, index, null); // DTR and RTS disabled
        request(c, 0x13, 0, index, new byte[16]); // no flow control
        request(c, 0x1e, 0, index, new byte[] {0x00, (byte)0xc2, 0x01, 0x00}); // 115200
        request(c, 0x03, 0x0800, index, null); // 8 data, no parity, 1 stop
        openedDeviceName = device.getDeviceName();
        running = true;
        final UsbEndpoint readEndpoint = endpoint;
        reader = new Thread(() -> readLoop(c, readEndpoint), "UwbUsbCp210xReader");
        reader.setDaemon(true);
        reader.start();
    }

    private void readLoop(UsbDeviceConnection c, UsbEndpoint endpoint) {
        byte[] buffer = new byte[512];
        long lastByteNs = System.nanoTime();
        while (running && connection == c) {
            int count = c.bulkTransfer(endpoint, buffer, buffer.length, 250);
            if (count <= 0) {
                if (System.nanoTime() - lastByteNs > 5_000_000_000L) {
                    synchronized (this) {
                        if (connection == c) {
                            closePort();
                            status = "USB: sem dados; reconectando...";
                        }
                    }
                    break;
                }
                continue;
            }
            lastByteNs = System.nanoTime();
            synchronized (lines) {
                for (int i = 0; i < count; i++) {
                    int ch = buffer[i] & 0xff;
                    if (ch == '\n') {
                        if (partial.length() > 0) {
                            if (lines.size() >= 100) lines.removeFirst();
                            lines.addLast(partial.toString());
                            partial.setLength(0);
                        }
                    } else if (ch != '\r' && ch >= 32 && ch < 127) {
                        if (partial.length() < 1024) partial.append((char) ch);
                        else partial.setLength(0);
                    }
                }
            }
        }
    }

    public String pollLine() {
        synchronized (lines) { return lines.pollFirst(); }
    }

    public synchronized boolean isOpen() {
        return running && connection != null && findBase() != null;
    }

    public synchronized String getStatus() { return status; }

    private void closePort() {
        running = false;
        UsbDeviceConnection c = connection;
        connection = null;
        if (c != null) {
            try { if (claimedInterface != null) c.releaseInterface(claimedInterface); }
            catch (Exception ignored) { }
            c.close();
        }
        claimedInterface = null;
        input = null;
        openedDeviceName = null;
        synchronized (lines) { lines.clear(); partial.setLength(0); }
    }

    public synchronized void close() { closePort(); }
}
