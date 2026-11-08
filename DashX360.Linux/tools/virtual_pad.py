#!/usr/bin/env python3
"""Creates a virtual Xbox 360 controller through /dev/uinput and plays a button script.

Used to test DashX360's SDL2 controller path end-to-end without real hardware.
Usage: virtual_pad.py "delay_ms:BUTTON,delay_ms:BUTTON,..."   (BUTTON in A,B,X,Y,LB,RB,BACK,START,GUIDE,UP,DOWN,LEFT,RIGHT)
"""
import fcntl
import os
import struct
import sys
import time

UI_SET_EVBIT = 0x40045564
UI_SET_KEYBIT = 0x40045565
UI_SET_ABSBIT = 0x40045567
UI_DEV_CREATE = 0x5501
UI_DEV_DESTROY = 0x5502
EV_SYN, EV_KEY, EV_ABS = 0, 1, 3
ABS_X, ABS_Y, ABS_Z, ABS_RX, ABS_RY, ABS_RZ, ABS_HAT0X, ABS_HAT0Y = 0, 1, 2, 3, 4, 5, 0x10, 0x11
BUTTONS = {"A": 0x130, "B": 0x131, "X": 0x133, "Y": 0x134, "LB": 0x136, "RB": 0x137,
           "BACK": 0x13a, "START": 0x13b, "GUIDE": 0x13c, "LS": 0x13d, "RS": 0x13e}
HAT = {"UP": (ABS_HAT0Y, -1), "DOWN": (ABS_HAT0Y, 1), "LEFT": (ABS_HAT0X, -1), "RIGHT": (ABS_HAT0X, 1)}


def emit(fd, etype, code, value):
    os.write(fd, struct.pack("llHHi", 0, 0, etype, code, value))


def sync(fd):
    emit(fd, EV_SYN, 0, 0)


def main():
    script = sys.argv[1] if len(sys.argv) > 1 else ""
    fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
    fcntl.ioctl(fd, UI_SET_EVBIT, EV_KEY)
    fcntl.ioctl(fd, UI_SET_EVBIT, EV_ABS)
    for code in BUTTONS.values():
        fcntl.ioctl(fd, UI_SET_KEYBIT, code)
    axes = [ABS_X, ABS_Y, ABS_Z, ABS_RX, ABS_RY, ABS_RZ, ABS_HAT0X, ABS_HAT0Y]
    for axis in axes:
        fcntl.ioctl(fd, UI_SET_ABSBIT, axis)
    absmax = [0] * 64
    absmin = [0] * 64
    for axis in (ABS_X, ABS_Y, ABS_RX, ABS_RY):
        absmin[axis], absmax[axis] = -32768, 32767
    for axis in (ABS_Z, ABS_RZ):
        absmin[axis], absmax[axis] = 0, 255
    for axis in (ABS_HAT0X, ABS_HAT0Y):
        absmin[axis], absmax[axis] = -1, 1
    name = b"Microsoft X-Box 360 pad (virtual)".ljust(80, b"\0")
    dev = name + struct.pack("HHHHi", 0x03, 0x045E, 0x028E, 0x0114, 0)
    dev += struct.pack("64i", *absmax) + struct.pack("64i", *absmin) + struct.pack("64i", *([0] * 64)) + struct.pack("64i", *([0] * 64))
    os.write(fd, dev)
    fcntl.ioctl(fd, UI_DEV_CREATE)
    print("virtual pad created", flush=True)
    try:
        for step in filter(None, script.split(",")):
            delay, button = step.split(":")
            time.sleep(int(delay) / 1000.0)
            button = button.upper()
            if button in HAT:
                axis, value = HAT[button]
                emit(fd, EV_ABS, axis, value); sync(fd)
                time.sleep(0.08)
                emit(fd, EV_ABS, axis, 0); sync(fd)
            elif button == "GUIDECHORD":
                emit(fd, EV_KEY, BUTTONS["BACK"], 1); emit(fd, EV_KEY, BUTTONS["START"], 1); sync(fd)
                time.sleep(0.12)
                emit(fd, EV_KEY, BUTTONS["BACK"], 0); emit(fd, EV_KEY, BUTTONS["START"], 0); sync(fd)
            else:
                code = BUTTONS[button]
                emit(fd, EV_KEY, code, 1); sync(fd)
                time.sleep(0.08)
                emit(fd, EV_KEY, code, 0); sync(fd)
            print("pressed", button, flush=True)
        time.sleep(3)
    finally:
        fcntl.ioctl(fd, UI_DEV_DESTROY)
        os.close(fd)


if __name__ == "__main__":
    main()
