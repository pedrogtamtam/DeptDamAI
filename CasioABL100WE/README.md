# Casio ABL-100WE Bluetooth tool

A command-line tool that connects to a **Casio ABL-100WE** watch over Bluetooth Low Energy and reads or changes its settings. You don't need the CASIO WATCHES phone app.

It is written in Python and uses [`bleak`](https://github.com/hbldh/bleak), a Bluetooth library that works on Windows 10/11, macOS and Linux.

> This folder is separate from the DeptDam .NET app; the main `DeptDam.csproj` ignores it.

## What it can change

| Setting | Command | Status |
|---|---|---|
| 12/24-hour format | `set --time-format 12h\|24h` | supported |
| Date order (month-day / day-month) | `set --date-format MM-DD\|DD-MM` | experimental, [see below](#date-display-format) |
| Button tone (key beep) | `set --button-tone on\|off` | supported |
| Backlight duration (1.5 s / 3 s) | `set --light short\|long` | supported |
| Power saving | `set --power-saving on\|off` | supported |
| Hourly chime | `set --hourly-chime on\|off` | supported |
| Alarms 1–5 (time, on/off) | `alarm N --time HH:MM --on\|--off` | supported |
| Countdown timer (up to 60 min) | `set --timer MM:SS` | supported |
| Automatic time sync, and the minute it runs | `set --auto-sync on\|off --sync-minute 0-59` | supported |
| Time, home time zone and summer time (DST) rules | `sync-time [--tz Europe/Lisbon]` | supported |
| Dual-time zone | `dual-time --tz America/New_York` | experimental |
| Daily step goal (1,000–50,000) | `set --step-goal 8000` | experimental |
| User profile (height, weight, gender, birthday, wrist) | `profile --height 178 ...` | experimental |
| Read today's steps and the last 7 days | `steps` | read-only |
| Everything at once from a JSON file | `dump` / `apply` | supported |

"Experimental" means the byte layout comes from other Casio models (via Gadgetbridge) and hasn't been confirmed on an ABL-100WE. Every write reads the register back first and changes only the known bits. If the watch doesn't support a register, the tool reports it and stops; nothing is changed.

**Not supported yet:** the inactivity alert. Its register hasn't been documented. You can look for it with `raw read` (see [Advanced](#advanced-raw-registers)).

## Use it from your phone (web version)

[`web/index.html`](web/index.html) is the same tool as a single web page. It uses Web Bluetooth, so it runs in **Chrome on Android** (and in Chrome on Windows, macOS and ChromeOS) with nothing to install:

**https://raw.githack.com/pedrogtamtam/DeptDamAI/claude/abl-100we-bluetooth-tool-vnwwex/CasioABL100WE/web/index.html**

1. Force-stop the CASIO WATCHES app (Settings > Apps > CASIO WATCHES > Force stop), so it doesn't take the connection.
2. Open the link above in Chrome. Chrome may ask for Bluetooth and Nearby devices permission.
3. On the watch, in Timekeeping Mode, hold (C) for 3 seconds until `CNCT` flashes.
4. Tap **Connect watch** and pick the watch. Change settings, then tap **Save to watch**.

Before you connect, the page shows a demo watch, so you can try every control. It doesn't work in Firefox, Samsung Internet or in-app browsers (including the Claude app). Those can't use Bluetooth, so open the link in Chrome.

## Install

Requires Python 3.10 or newer.

```bash
cd CasioABL100WE
python -m venv .venv
# Windows: .venv\Scripts\activate    macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt
```

Optionally, run `pip install .` to get an `abl100we` command. Otherwise use `python -m abl100we`.

On **Linux** the BlueZ `bluetooth` service must be running.
On **macOS** the first run asks for Bluetooth permission for your terminal app.

## Connecting

The watch only accepts a connection for a short time after a button press:

1. **Turn off Bluetooth on your phone**, or close the CASIO WATCHES app. Otherwise the phone takes the connection first.
2. Run a command, for example `python -m abl100we show`.
3. While it says `Waiting for the watch...`, put the watch in Timekeeping Mode and **hold (C) for at least 3 seconds**,
   until the Bluetooth icon and `CNCT` start to flash.
4. Don't press any button except (B) (the light) while the tool works: other buttons end the connection.

Repeat step 3 for every command. Each command connects, does its work and disconnects.

## Usage

```bash
# Find watches nearby (shows the Bluetooth address)
python -m abl100we scan

# Show every setting
python -m abl100we show
python -m abl100we show --json

# Change several settings in one connection
python -m abl100we set --time-format 24h --button-tone off --light long --hourly-chime on

# Alarms (setting a time also switches the alarm on)
python -m abl100we alarm 1 --time 07:00
python -m abl100we alarm 3 --off

# Countdown timer and automatic time sync
python -m abl100we set --timer 25:00 --auto-sync on --sync-minute 15

# Set the time and time zone from this computer (or choose a zone)
python -m abl100we sync-time
python -m abl100we sync-time --tz Europe/Lisbon

# Steps
python -m abl100we steps
```

Global options go **before** the command:

| Option | Meaning |
|---|---|
| `-a, --address XX:XX:...` | connect to this address (on macOS, the UUID shown by `scan`) |
| `-n, --name TEXT` | match watch names containing TEXT (default `ABL`) |
| `-t, --timeout SEC` | how long to wait for the watch (default 30) |
| `-v, --verbose` | print every Bluetooth packet |

## Date display format

The watch shows the date as year · month-day, for example `24· 6-30` for 30 June 2024.
The ABL-100WE's **own setup menu has no option to change this**. Its manual (Operation Guide 3565) lists every on-watch setting, and date order isn't one of them. Some other Casio watches have this option in their menus (`M.DD` / `DD.M`).

Over Bluetooth, other Casio watches store the date order as one bit in the basic-settings register (`13`, byte 4, bit `0x01`: set = day first). The ABL-100WE has that byte too, so this tool can write the bit. Nobody has confirmed yet whether this firmware uses it for the display, so test it:

```bash
python -m abl100we set --date-format DD-MM    # day first
# Look at the watch: does "6-30" now read "30-6"?
python -m abl100we set --date-format MM-DD    # back to the default (month first)
```

* **If the display changes**, the setting works. Keep it, or put `"date_format": "DD-MM"` in your JSON config.
* **If the display doesn't change** (even after pressing (C) to leave and re-enter the time screen), this firmware ignores the bit. The date order is then fixed. Set it back with `--date-format MM-DD` to keep the register at its factory value.

`show` reports what is stored in the register, which may not be what the display uses.

**Searching for a hidden switch.** The web version has a **Register lab** (bottom of the page). It backs up register `13` and flips each of its bits once. For each bit it reads the register back to see whether the watch stores it, then restores it. Bits the firmware doesn't implement are dropped on write, so the scan shows exactly which bytes hold real settings. You can then step through values on those bytes and watch the date line, and **Restore backup** puts everything back.

If the scan finds only the known settings, the firmware has no day-first layout. Bluetooth can't add it, and Casio offers no way to update this watch's firmware. That matches the manual (no date-order item in the watch's menu) and the GShockAPI project, which marks this model as having no date format. On the command line, `raw read 13` / `raw write ...` do the same by hand.

## Change everything at once (backup / restore)

```bash
python -m abl100we dump -o my-watch.json      # save the current settings
# edit my-watch.json
python -m abl100we apply my-watch.json --dry-run   # check the file without connecting
python -m abl100we apply my-watch.json             # write it to the watch
```

`apply` writes only what differs from the watch, then reads each setting back to confirm it. Keys you leave out, or set to `null`, are not changed. See [`example-config.json`](example-config.json):

| Key | Values |
|---|---|
| `time_format` | `"12h"` / `"24h"` |
| `date_format` | `"MM-DD"` / `"DD-MM"` (experimental) |
| `button_tone`, `power_saving`, `hourly_chime`, `auto_time_sync` | `true` / `false` |
| `light_duration` | `"short"` (1.5 s) / `"long"` (3 s) |
| `time_sync_minute` | 0–59 |
| `alarms` | list of up to 5 items `{ "enabled": true, "time": "07:00" }` (or `hour`/`minute`); `null` skips that alarm |
| `timer_seconds` | seconds, or `"MM:SS"` (max 60:00) |
| `step_goal` | 1000–50000 (experimental) |
| `user_profile` | `{ "height_cm", "weight_kg", "gender": "male"/"female", "birthday": "YYYY-MM-DD", "wrist": "left"/"right" }` (experimental) |
| `dual_time` | `{ "timezone": "America/New_York" }` (experimental) |
| `sync_time` | `true` (this computer's zone) or `{ "timezone": "Europe/Lisbon", "offset_seconds": 0 }` |

`apply` ignores the `watch`, `_info` and `_raw` sections that `dump` adds, so a dump can be applied as it is.

## Advanced: raw registers

```bash
python -m abl100we raw read 13          # basic settings register
python -m abl100we raw read 1E 01       # dual-time zone register
python -m abl100we raw write 13 01 00 00 00 00 00 00 00 00 00 00   # asks for confirmation
```

Register map used by this tool:

| Register | Content |
|---|---|
| `09` | current time (write only) |
| `11` | Bluetooth settings: byte 12 bit `0x80` = auto sync **off**, byte 13 = sync minute |
| `13` | byte 1: `0x01` 24h, `0x02` tone off, `0x10` power saving off; byte 2: `0x01` long light; byte 4: `0x01` day-first date (experimental) |
| `15` | alarm 1 (`flags, 0x40, hour, minute`); flag `0x40` = on, `0x80` = hourly chime |
| `16` | alarms 2–5, same 4-byte layout |
| `18` | timer: hours, minutes, seconds |
| `1D 00` | DST flags, home (byte 3) and dual time (byte 4): `0x01` DST on, `0x02` auto |
| `1E 00` / `1E 01` | home / dual zone: byte 4 UTC offset, byte 5 DST offset (15-min steps), byte 6 Casio DST rule |
| `22`, `23` | app registration, watch name |
| `43` | step goal (experimental) |
| `45` | user profile (experimental) |

## How it works

All settings travel over one vendor GATT service (Bluetooth data channel):

* **Read**: write the register key (e.g. `13`) to characteristic `26eb002c-…`. The watch replies with a notification on `26eb002d-…`.
* **Write**: write the full register to `26eb002d-…`.
* **Steps**: a transfer is started on `26eb0023-…`; the data streams in on `26eb0024-…`.

Code layout:

```
abl100we/protocol.py   byte encoding/decoding only (no Bluetooth), fully unit-tested
abl100we/timezones.py  IANA zone -> UTC offset, DST offset and Casio DST rule code
abl100we/watch.py      Bluetooth session: request/response matching, step-log transfer
abl100we/config.py     read the whole configuration, validate and apply changes
abl100we/cli.py        command-line interface
web/index.html         the same tool as a phone-friendly web page (Web Bluetooth)
tests/                 unit tests plus a simulated watch for end-to-end tests
```

Run the tests (no watch or Bluetooth needed):

```bash
python -m unittest discover -s . -p "test_*.py"
```

## Troubleshooting

* **`watch not found`**: hold (C) for 3 seconds again (in Timekeeping Mode), move the watch closer, and make sure your phone's Bluetooth is off.
* **`Bluetooth is not available`**: Bluetooth is off. On Linux, run `sudo systemctl start bluetooth`.
* **`no answer from watch` or `watch disconnected`**: the watch closed its connection window, or a button other than (B) was pressed. Run the command again and hold (C) for 3 seconds.
* **The watch rejects a register (`FF 81 xx`)**: this model doesn't have that feature. Experimental features fail this way.
* To see exactly what is sent and received, add `-v`.

## Credits

The protocol details come from these open-source projects:
[GShockAPI / gshock-api](https://github.com/izivkov/GShockAPI) by Ivo Zivkov (MIT) and
[Gadgetbridge](https://codeberg.org/Freeyourgadget/Gadgetbridge) (AGPL; used only as a protocol reference, no code copied).
This tool is not affiliated with Casio.
