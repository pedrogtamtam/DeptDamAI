"""Whole-watch configuration: read it as a dict, validate a change set, apply it.

The same JSON shape is produced by ``dump`` and accepted by ``apply``, so the
usual workflow is: dump -> edit the file -> apply.  Every key is optional; a
missing key (or ``null``) means "leave this setting as it is".
"""

from __future__ import annotations

from dataclasses import asdict
from datetime import datetime, timedelta
from zoneinfo import ZoneInfo

from . import protocol as p
from .timezones import local_zone_name, resolve
from .watch import WatchSession

BASIC_KEYS = ("time_format", "button_tone", "light_duration", "power_saving", "date_format")
SETTING_KEYS = BASIC_KEYS + (
    "hourly_chime", "auto_time_sync", "time_sync_minute", "alarms", "timer_seconds",
    "step_goal", "user_profile",
)
ACTION_KEYS = ("dual_time", "sync_time")
IGNORED_KEYS = ("watch",)   # plus anything starting with "_"
PROFILE_KEYS = ("gender", "wrist", "height_cm", "weight_kg", "birthday")

# Dummy register images used to validate values before touching the watch.
_DUMMY = {
    p.Reg.SETTING_FOR_BASIC: bytes([p.Reg.SETTING_FOR_BASIC]) + bytes(11),
    p.Reg.ALARM_1: bytes([p.Reg.ALARM_1, 0, 0x40, 0, 0]),
    p.Reg.ALARMS_2_5: bytes([p.Reg.ALARMS_2_5]) + bytes([0, 0x40, 0, 0]) * 4,
    p.Reg.TIMER: bytes([p.Reg.TIMER]) + bytes(6),
    p.Reg.SETTING_FOR_BLE: bytes([p.Reg.SETTING_FOR_BLE]) + bytes(14),
    p.Reg.TARGET_VALUE: bytes([p.Reg.TARGET_VALUE]) + bytes(11),
    p.Reg.USER_PROFILE: bytes([p.Reg.USER_PROFILE]) + bytes(11),
}


# --------------------------------------------------------------------------
# Value parsing
# --------------------------------------------------------------------------
def to_bool(value, name: str) -> bool:
    if isinstance(value, bool):
        return value
    text = str(value).strip().lower()
    if text in ("on", "true", "yes", "1", "enabled"):
        return True
    if text in ("off", "false", "no", "0", "disabled"):
        return False
    raise ValueError(f"{name} must be on/off (or true/false), got {value!r}")


def _parse_alarm(item, number: int) -> dict | None:
    if item is None:
        return None
    if not isinstance(item, dict):
        raise ValueError(f"alarm {number} must be an object like {{\"enabled\": true, \"hour\": 7, \"minute\": 0}}")
    unknown = set(item) - {"enabled", "hour", "minute", "time"}
    if unknown:
        raise ValueError(f"alarm {number}: unknown field(s) {sorted(unknown)}")
    alarm = {"enabled": None, "hour": item.get("hour"), "minute": item.get("minute")}
    if item.get("enabled") is not None:
        alarm["enabled"] = to_bool(item["enabled"], f"alarm {number} enabled")
    if item.get("time") is not None:
        alarm["hour"], alarm["minute"] = parse_clock(item["time"])
    return alarm


def parse_clock(text: str) -> tuple[int, int]:
    try:
        hour, minute = (int(x) for x in str(text).strip().split(":"))
    except ValueError:
        raise ValueError(f"time must look like HH:MM, got {text!r}") from None
    if not (0 <= hour <= 23 and 0 <= minute <= 59):
        raise ValueError(f"time out of range: {text!r}")
    return hour, minute


def _parse_zone_action(value, key: str) -> dict | None:
    if value is None or value is False:
        return None
    if value is True:
        return {"timezone": None, "offset_seconds": 0}
    if isinstance(value, str):
        value = {"timezone": value}
    if not isinstance(value, dict):
        raise ValueError(f"{key} must be true, a time zone name, or an object with a 'timezone' field")
    unknown = set(value) - {"timezone", "offset_seconds"}
    if unknown:
        raise ValueError(f"{key}: unknown field(s) {sorted(unknown)}")
    return {"timezone": value.get("timezone"), "offset_seconds": int(value.get("offset_seconds") or 0)}


# --------------------------------------------------------------------------
# Validation (runs before connecting, so typos fail fast)
# --------------------------------------------------------------------------
def validate_config(cfg: dict) -> dict:
    if not isinstance(cfg, dict):
        raise ValueError("configuration must be a JSON object")
    unknown = [k for k in cfg if k not in SETTING_KEYS + ACTION_KEYS + IGNORED_KEYS and not k.startswith("_")]
    if unknown:
        raise ValueError(f"unknown setting(s): {', '.join(sorted(unknown))}")

    out: dict = {}
    for key in ("button_tone", "power_saving", "hourly_chime", "auto_time_sync"):
        if cfg.get(key) is not None:
            out[key] = to_bool(cfg[key], key)
    if cfg.get("time_format") is not None:
        out["time_format"] = str(cfg["time_format"]).lower()
    if cfg.get("light_duration") is not None:
        out["light_duration"] = p.normalize_light(cfg["light_duration"])
    if cfg.get("date_format") is not None:
        out["date_format"] = p.normalize_date_format(cfg["date_format"])
    if cfg.get("time_sync_minute") is not None:
        out["time_sync_minute"] = cfg["time_sync_minute"]
    if cfg.get("timer_seconds") is not None:
        value = cfg["timer_seconds"]
        out["timer_seconds"] = p.parse_duration(value) if isinstance(value, str) else value
    if cfg.get("step_goal") is not None:
        out["step_goal"] = cfg["step_goal"]

    if cfg.get("alarms") is not None:
        alarms = cfg["alarms"]
        if not isinstance(alarms, list) or len(alarms) > p.ALARM_COUNT:
            raise ValueError(f"alarms must be a list of up to {p.ALARM_COUNT} entries (use null to skip one)")
        parsed = [_parse_alarm(item, i + 1) for i, item in enumerate(alarms)]
        if any(parsed):
            out["alarms"] = parsed

    if cfg.get("user_profile") is not None:
        prof = cfg["user_profile"]
        if not isinstance(prof, dict) or set(prof) - set(PROFILE_KEYS):
            raise ValueError(f"user_profile must be an object with fields {list(PROFILE_KEYS)}")
        prof = {k: v for k, v in prof.items() if v is not None}
        if prof:
            out["user_profile"] = prof

    for key in ACTION_KEYS:
        action = _parse_zone_action(cfg.get(key), key)
        if action:
            if action["timezone"]:
                resolve(action["timezone"])  # raises on unknown zone
            elif key == "dual_time":
                raise ValueError("dual_time needs a time zone, e.g. \"America/New_York\"")
            out[key] = action

    # Dry-run every encoder against dummy registers to range-check values.
    p.encode_basic(_DUMMY[p.Reg.SETTING_FOR_BASIC], **{k: out[k] for k in BASIC_KEYS if k in out})
    alarms = out.get("alarms") or []
    p.encode_alarm1(_DUMMY[p.Reg.ALARM_1], alarms[0] if alarms else None, out.get("hourly_chime"))
    p.encode_alarms_2_5(_DUMMY[p.Reg.ALARMS_2_5], {i + 1: a for i, a in enumerate(alarms) if a and i})
    if "timer_seconds" in out:
        p.encode_timer(_DUMMY[p.Reg.TIMER], out["timer_seconds"])
    p.encode_time_sync(_DUMMY[p.Reg.SETTING_FOR_BLE], enabled=out.get("auto_time_sync"), minute=out.get("time_sync_minute"))
    if "step_goal" in out:
        p.encode_step_goal(_DUMMY[p.Reg.TARGET_VALUE], out["step_goal"])
    if "user_profile" in out:
        p.encode_user_profile(_DUMMY[p.Reg.USER_PROFILE], **out["user_profile"])

    for key, low, high in (
        ("time_sync_minute", 0, 59),
        ("timer_seconds", 1, p.TIMER_MAX_SECONDS),
        ("step_goal", p.STEP_GOAL_MIN, p.STEP_GOAL_MAX),
    ):
        if key in out:
            out[key] = p.check_range(key, out[key], low, high)
    return out


# --------------------------------------------------------------------------
# Reading
# --------------------------------------------------------------------------
async def read_state(s: WatchSession) -> dict:
    raw: dict[str, str] = {}

    async def get(reg: p.Reg, *args: int, optional: bool = False, timeout: float = 5.0) -> bytes | None:
        data = await s.read(reg, *args, optional=optional, timeout=timeout)
        if data is not None:
            raw[p.hexstr(bytes([reg, *args]))] = p.hexstr(data)
        return data

    basic = p.decode_basic(await get(p.Reg.SETTING_FOR_BASIC))
    alarms, chime = p.decode_alarms(await get(p.Reg.ALARM_1), await get(p.Reg.ALARMS_2_5))
    sync_on, sync_minute = p.decode_time_sync(await get(p.Reg.SETTING_FOR_BLE))
    timer = p.decode_timer(await get(p.Reg.TIMER))

    state: dict = {
        "watch": {"name": s.name, "address": s.address},
        **asdict(basic),
        "hourly_chime": chime,
        "auto_time_sync": sync_on,
        "time_sync_minute": sync_minute,
        "alarms": [asdict(a) for a in alarms],
        "timer_seconds": timer,
    }

    goal_raw = await get(p.Reg.TARGET_VALUE, optional=True, timeout=3.0)
    profile_raw = await get(p.Reg.USER_PROFILE, optional=True, timeout=3.0)
    try:
        if goal_raw:
            state["step_goal"] = p.decode_step_goal(goal_raw)
        if profile_raw:
            state["user_profile"] = asdict(p.decode_user_profile(profile_raw))
    except p.ProtocolError:
        pass

    info: dict = {}
    dst_raw = await get(p.Reg.DST_WATCH_STATE, 0, optional=True)
    dst_flags = p.decode_dst_watch_state(dst_raw) if dst_raw else [None, None]
    for slot, label in ((0, "home_time"), (1, "dual_time")):
        zone_raw = await get(p.Reg.DST_SETTING, slot, optional=True)
        if zone_raw:
            z = p.decode_dst_setting(zone_raw)
            flags = dst_flags[slot]
            info[label] = {
                "utc_offset": p.format_utc_offset(z.utc_offset_minutes),
                "dst_offset_minutes": z.dst_offset_minutes,
                "dst_rules": z.dst_rules,
                "dst_active": None if flags is None else bool(flags & p.DST_ON),
                "dst_auto": None if flags is None else bool(flags & p.DST_AUTO),
            }
    state["_info"] = info
    state["_raw"] = raw
    return state


def format_state(state: dict) -> str:
    def onoff(v) -> str:
        return "on" if v else "off"

    lines = [f"Watch            {state['watch']['name']}  [{state['watch']['address']}]", ""]
    lines += [
        f"Time format      {state['time_format']}",
        f"Button tone      {onoff(state['button_tone'])}",
        f"Light duration   {state['light_duration']} ({p.LIGHT_SECONDS[state['light_duration']]})",
        f"Date format      {state.get('date_format') or '?'}  (experimental)",
        f"Power saving     {onoff(state['power_saving'])}",
        f"Hourly chime     {onoff(state['hourly_chime'])}",
        f"Auto time sync   {onoff(state['auto_time_sync'])} (minute :{state['time_sync_minute']:02d})",
        f"Timer            {p.format_duration(state['timer_seconds'])}",
    ]
    for i, a in enumerate(state["alarms"], 1):
        lines.append(f"Alarm {i}          {onoff(a['enabled']):<3}  {a['hour']:02d}:{a['minute']:02d}")
    for label, title in (("home_time", "Home time zone"), ("dual_time", "Dual time zone")):
        z = state.get("_info", {}).get(label)
        if z:
            dst = "DST auto" if z["dst_auto"] else "DST manual"
            active = "active" if z["dst_active"] else "inactive"
            lines.append(f"{title:<17}{z['utc_offset']}, {dst} ({active}, +{z['dst_offset_minutes']} min, rules 0x{z['dst_rules']:02X})")
    if "step_goal" in state:
        lines.append(f"Step goal        {state['step_goal']:,}  (experimental)")
    if "user_profile" in state:
        u = state["user_profile"]
        lines.append(
            f"User profile     {u['gender']}, {u['wrist']} wrist, {u['height_cm']} cm, {u['weight_kg']} kg, "
            f"born {u['birthday']}  (experimental)"
        )
    return "\n".join(lines)


# --------------------------------------------------------------------------
# Applying
# --------------------------------------------------------------------------
async def _update(s: WatchSession, report: list[str], label: str, key: tuple[int, ...], encode,
                  force: bool = False) -> None:
    """Read-modify-write one register, then read it back to confirm."""
    current = await s.read(*key)
    new = encode(current)
    if new == current and not force:
        report.append(f"  [=]  {label}: already set")
        return
    await s.write(new)
    check = await s.read(*key, optional=True)
    if check is not None and check != new:
        report.append(f"  [!]  {label}: written, but the watch reports {p.hexstr(check)} (sent {p.hexstr(new)})")
    else:
        report.append(f"  [ok] {label}: {'updated' if new != current else 'confirmed'}")


async def _set_zone(s: WatchSession, slot: int, zone_name: str, report: list[str], force: bool = False) -> None:
    zone = resolve(zone_name)
    label = "home time zone" if slot == 0 else "dual time zone"
    await _update(s, report, f"{label} DST flags", (p.Reg.DST_WATCH_STATE, 0),
                  lambda raw: p.encode_dst_watch_state(raw, slot, zone.dst_flags), force)
    await _update(s, report, f"{label} ({zone_name})", (p.Reg.DST_SETTING, slot),
                  lambda raw: p.encode_dst_setting(raw, zone.offset_q, zone.dst_offset_q, zone.rules), force)


async def apply_config(s: WatchSession, cfg: dict) -> list[str]:
    """Apply an already-validated config. Returns a human-readable report."""
    report: list[str] = []

    basic = {k: cfg[k] for k in BASIC_KEYS if k in cfg}
    if basic:
        await _update(s, report, "basic settings (" + ", ".join(basic) + ")", (p.Reg.SETTING_FOR_BASIC,),
                      lambda raw: p.encode_basic(raw, **basic))

    alarms = cfg.get("alarms") or []
    if (alarms and alarms[0]) or "hourly_chime" in cfg:
        what = ", ".join(x for x, on in (("alarm 1", alarms and alarms[0]), ("hourly chime", "hourly_chime" in cfg)) if on)
        await _update(s, report, what, (p.Reg.ALARM_1,),
                      lambda raw: p.encode_alarm1(raw, alarms[0] if alarms else None, cfg.get("hourly_chime")))
    later = {i + 1: a for i, a in enumerate(alarms) if a and i}
    if later:
        await _update(s, report, "alarm " + ", ".join(map(str, later)), (p.Reg.ALARMS_2_5,),
                      lambda raw: p.encode_alarms_2_5(raw, later))

    if "timer_seconds" in cfg:
        await _update(s, report, f"timer ({p.format_duration(cfg['timer_seconds'])})", (p.Reg.TIMER,),
                      lambda raw: p.encode_timer(raw, cfg["timer_seconds"]))

    if "auto_time_sync" in cfg or "time_sync_minute" in cfg:
        await _update(s, report, "auto time sync", (p.Reg.SETTING_FOR_BLE,),
                      lambda raw: p.encode_time_sync(raw, enabled=cfg.get("auto_time_sync"), minute=cfg.get("time_sync_minute")))

    if "step_goal" in cfg:
        await _update(s, report, f"step goal ({cfg['step_goal']:,})", (p.Reg.TARGET_VALUE,),
                      lambda raw: p.encode_step_goal(raw, cfg["step_goal"]))

    if "user_profile" in cfg:
        await _update(s, report, "user profile", (p.Reg.USER_PROFILE,),
                      lambda raw: p.encode_user_profile(raw, **cfg["user_profile"]))

    if "dual_time" in cfg:
        await _set_zone(s, 1, cfg["dual_time"]["timezone"], report)

    if "sync_time" in cfg:
        action = cfg["sync_time"]
        zone_name = action["timezone"] or local_zone_name()
        # Same sequence as the reference apps: DST flags, home zone, dual zone, then the time.
        await _set_zone(s, 0, zone_name, report, force=True)
        dual = await s.read(p.Reg.DST_SETTING, 1, optional=True)
        if dual:
            await s.write(dual)
        now = datetime.now(ZoneInfo(zone_name)) + timedelta(seconds=action["offset_seconds"])
        try:
            await s.write(p.encode_current_time(now))
        except Exception:
            if getattr(s.client, "is_connected", True):
                raise
            report.append("  [ok] time sent; the watch closed the connection afterwards (normal)")
        report.append(f"  [ok] time set to {now:%Y-%m-%d %H:%M:%S} ({zone_name})")

    return report
