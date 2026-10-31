"""Command-line interface: ``python -m abl100we <command> ...``"""

from __future__ import annotations

import argparse
import asyncio
import json
import logging
import sys
from dataclasses import asdict
from pathlib import Path

from . import __version__
from . import protocol as p
from .config import apply_config, format_state, parse_clock, read_state, to_bool, validate_config
from .watch import WatchError, WatchSession, scan

ON_OFF = ("on", "off")


# --------------------------------------------------------------------------
# Argument parsing
# --------------------------------------------------------------------------
def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="abl100we",
        description="Read and change every setting of a Casio ABL-100WE watch over Bluetooth LE.",
        epilog="Before each command, short-press SEARCH on the watch so it starts advertising.",
    )
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    parser.add_argument("-a", "--address", help="watch Bluetooth address (or macOS UUID); skips the name search")
    parser.add_argument("-n", "--name", default="ABL", help="match watches whose name contains this text (default: ABL)")
    parser.add_argument("-t", "--timeout", type=float, default=30.0, help="seconds to wait for the watch (default: 30)")
    parser.add_argument("-v", "--verbose", action="store_true", help="print every Bluetooth packet")
    sub = parser.add_subparsers(dest="command", required=True, metavar="COMMAND")

    sub.add_parser("scan", help="list Casio watches that are advertising nearby")

    show = sub.add_parser("show", help="print every setting currently stored on the watch")
    show.add_argument("--json", action="store_true", help="print JSON instead of a table")

    s = sub.add_parser("set", help="change one or more settings")
    s.add_argument("--time-format", choices=("12h", "24h"))
    s.add_argument("--date-format", choices=(p.DATE_MONTH_FIRST, p.DATE_DAY_FIRST),
                   help="date order on the display (experimental, see README)")
    s.add_argument("--button-tone", choices=ON_OFF, help="key beep")
    s.add_argument("--light", choices=(p.LIGHT_SHORT, p.LIGHT_LONG), help="backlight duration: short=1.5s, long=3s")
    s.add_argument("--power-saving", choices=ON_OFF)
    s.add_argument("--hourly-chime", choices=ON_OFF)
    s.add_argument("--auto-sync", choices=ON_OFF, help="automatic time sync with the phone app")
    s.add_argument("--sync-minute", type=int, metavar="0-59", help="minute past the hour for automatic sync")
    s.add_argument("--timer", metavar="MM:SS", help="countdown timer start value (max 60:00)")
    s.add_argument("--step-goal", type=int, metavar="STEPS", help=f"daily step goal {p.STEP_GOAL_MIN}-{p.STEP_GOAL_MAX} (experimental)")

    al = sub.add_parser("alarm", help="change one alarm")
    al.add_argument("number", type=int, choices=range(1, p.ALARM_COUNT + 1), metavar="N", help="alarm number 1-5")
    al.add_argument("--time", metavar="HH:MM", help="alarm time (24-hour clock)")
    state = al.add_mutually_exclusive_group()
    state.add_argument("--on", dest="enabled", action="store_const", const=True, help="enable the alarm")
    state.add_argument("--off", dest="enabled", action="store_const", const=False, help="disable the alarm")

    st = sub.add_parser("sync-time", help="set the time, home time zone and DST rules from this computer")
    st.add_argument("--tz", help="IANA time zone, e.g. Europe/Lisbon (default: this computer's zone)")
    st.add_argument("--offset", type=int, default=0, metavar="SECONDS", help="add this many seconds to the time sent")

    dt = sub.add_parser("dual-time", help="set the dual-time zone (experimental)")
    dt.add_argument("--tz", required=True, help="IANA time zone, e.g. America/New_York")

    pr = sub.add_parser("profile", help="set the user profile used for step distance (experimental)")
    pr.add_argument("--height", type=int, metavar="CM")
    pr.add_argument("--weight", type=int, metavar="KG")
    pr.add_argument("--gender", choices=("male", "female"))
    pr.add_argument("--birthday", metavar="YYYY-MM-DD")
    pr.add_argument("--wrist", choices=("left", "right"))

    steps = sub.add_parser("steps", help="read today's steps and the 7-day history")
    steps.add_argument("--json", action="store_true")
    steps.add_argument("--raw", action="store_true", help="also print the raw life-log bytes")

    dump = sub.add_parser("dump", help="save every setting to a JSON file (edit it, then use 'apply')")
    dump.add_argument("-o", "--output", default="-", help="file to write (default: print to screen)")

    ap = sub.add_parser("apply", help="apply a JSON settings file (see example-config.json)")
    ap.add_argument("file", help="JSON file; '-' reads from stdin")
    ap.add_argument("--dry-run", action="store_true", help="only validate the file, do not connect")

    raw = sub.add_parser("raw", help="advanced: read or write a register as hex")
    raw_sub = raw.add_subparsers(dest="raw_command", required=True, metavar="ACTION")
    rr = raw_sub.add_parser("read", help="read a register, e.g. 'raw read 13' or 'raw read 1E 01'")
    rr.add_argument("key", nargs="+", help="register byte(s) in hex")
    rw = raw_sub.add_parser("write", help="write a full register payload, e.g. 'raw write 13 01 00 ...'")
    rw.add_argument("data", nargs="+", help="payload bytes in hex, starting with the register byte")
    rw.add_argument("--yes", action="store_true", help="do not ask for confirmation")
    return parser


def config_from_args(args: argparse.Namespace) -> dict:
    """Turn set/alarm/sync-time/dual-time/profile arguments into a config dict."""
    cfg: dict = {}
    if args.command == "set":
        mapping = {
            "time_format": args.time_format,
            "date_format": args.date_format,
            "button_tone": args.button_tone,
            "light_duration": args.light,
            "power_saving": args.power_saving,
            "hourly_chime": args.hourly_chime,
            "auto_time_sync": args.auto_sync,
            "time_sync_minute": args.sync_minute,
            "timer_seconds": args.timer,
            "step_goal": args.step_goal,
        }
        cfg = {k: v for k, v in mapping.items() if v is not None}
        if not cfg:
            raise ValueError("nothing to change; run 'set --help' to see the options")
    elif args.command == "alarm":
        alarm: dict = {}
        if args.time:
            alarm["hour"], alarm["minute"] = parse_clock(args.time)
            if args.enabled is None:
                alarm["enabled"] = True       # setting a time implies turning the alarm on
        if args.enabled is not None:
            alarm["enabled"] = args.enabled
        if not alarm:
            raise ValueError("use --time HH:MM and/or --on/--off")
        cfg["alarms"] = [None] * (args.number - 1) + [alarm]
    elif args.command == "sync-time":
        cfg["sync_time"] = {"timezone": args.tz, "offset_seconds": args.offset}
    elif args.command == "dual-time":
        cfg["dual_time"] = {"timezone": args.tz}
    elif args.command == "profile":
        profile = {
            "height_cm": args.height, "weight_kg": args.weight, "gender": args.gender,
            "birthday": args.birthday, "wrist": args.wrist,
        }
        profile = {k: v for k, v in profile.items() if v is not None}
        if not profile:
            raise ValueError("nothing to change; run 'profile --help' to see the options")
        cfg["user_profile"] = profile
    return cfg


# --------------------------------------------------------------------------
# Commands
# --------------------------------------------------------------------------
async def cmd_scan(args) -> int:
    print(f"Scanning for {args.timeout:.0f}s... (short-press SEARCH on the watch)")
    found = await scan(args.timeout)
    if not found:
        print("No Casio watch found.")
        return 1
    for dev, adv in found:
        print(f"{dev.address}  {adv.local_name or dev.name or '?':<24} RSSI {adv.rssi} dBm")
    return 0


async def connect(args) -> WatchSession:
    print("Waiting for the watch... (short-press SEARCH on the watch)", file=sys.stderr)
    session = await WatchSession.connect(args.address, args.name, args.timeout)
    if args.verbose:
        session.on_trace = lambda line: print(f"  [ble] {line}", file=sys.stderr)
    print(f"Connected to {session.name or 'watch'} [{session.address}]", file=sys.stderr)
    return session


async def run(args) -> int:
    if args.command == "scan":
        return await cmd_scan(args)

    # Validate everything *before* connecting so mistakes fail fast.
    cfg = None
    if args.command in ("set", "alarm", "sync-time", "dual-time", "profile"):
        cfg = validate_config(config_from_args(args))
    elif args.command == "apply":
        text = sys.stdin.read() if args.file == "-" else Path(args.file).read_text(encoding="utf-8")
        cfg = validate_config(json.loads(text))
        if not cfg:
            print("The file contains no settings to change.")
            return 0
        if args.dry_run:
            print("Configuration is valid. It would change:", ", ".join(cfg))
            return 0
    elif args.command == "raw" and args.raw_command == "write":
        data = p.parse_hex(" ".join(args.data))
        if not args.yes:
            answer = input(f"Write {p.hexstr(data)} to register 0x{data[0]:02X}? [y/N] ")
            if answer.strip().lower() not in ("y", "yes"):
                print("Cancelled.")
                return 1

    async with await connect(args) as session:
        if args.command == "show":
            state = await read_state(session)
            print(json.dumps(state, indent=2) if args.json else format_state(state))
        elif args.command == "dump":
            text = json.dumps(await read_state(session), indent=2) + "\n"
            if args.output == "-":
                print(text, end="")
            else:
                Path(args.output).write_text(text, encoding="utf-8")
                print(f"Saved to {args.output}", file=sys.stderr)
        elif args.command == "steps":
            payload = await session.read_step_log()
            log = p.parse_step_log(payload)
            if args.json:
                print(json.dumps(asdict(log) | ({"raw": payload.hex()} if args.raw else {}), indent=2))
            else:
                print(f"Recorded at      {log.timestamp or '?'}")
                print(f"Steps today      {_num(log.today_steps)}")
                print(f"Distance today   {_num(log.today_distance_m)} m")
                for day in log.history:
                    print(f"  {day['days_ago']} day(s) ago   {_num(day['steps']):>7} steps  {_num(day['distance_m']):>7} m")
                if args.raw:
                    print(p.hexstr(payload))
        elif args.command == "raw":
            if args.raw_command == "read":
                key = p.parse_hex(" ".join(args.key))
                print(p.hexstr(await session.read(key[0], *key[1:])))
            else:
                await session.write(data)
                check = await session.read(data[0], timeout=3.0, optional=True)
                print("Written." + (f" Watch now reports: {p.hexstr(check)}" if check else ""))
        else:
            print("Applying changes:")
            for line in await apply_config(session, cfg):
                print(line)
    return 0


def _num(value) -> str:
    return "-" if value is None else f"{value:,}"


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.WARNING, format="%(levelname)s %(message)s")
    try:
        return asyncio.run(run(args))
    except (ValueError, WatchError, p.ProtocolError, OSError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 2
    except ImportError as exc:
        print(f"Error: {exc}. Install the requirements first: pip install -r requirements.txt", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        return 130
    except Exception as exc:  # bleak raises its own error types (adapter off, permission denied, ...)
        if type(exc).__module__.startswith("bleak"):
            print(f"Bluetooth error: {exc}", file=sys.stderr)
            return 2
        raise


if __name__ == "__main__":
    sys.exit(main())
