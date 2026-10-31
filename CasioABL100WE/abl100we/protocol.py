"""Pure encode/decode helpers for the Casio ABL-100WE Bluetooth LE protocol.

Nothing in this module talks to Bluetooth, so it can be unit-tested anywhere.

How the watch protocol works
----------------------------
* The watch exposes a vendor GATT service (26eb000d-...).
* To READ a register, write its 1-2 byte key (e.g. ``13`` or ``1D 00``) to the
  *request* characteristic (26eb002c, write-without-response).  The watch
  answers with a notification on the *all-features* characteristic (26eb002d)
  whose payload starts with the same key.
* To WRITE a register, write the full register payload (key + data) to the
  *all-features* characteristic (26eb002d, write-with-response).
* An error is reported as a notification ``FF 81 <register>``.

Every setter in this module is *read-modify-write*: it takes the raw bytes
read from the watch and only flips the bits it knows about, so undocumented
bytes are always preserved.

Byte layouts come from the open-source projects that reverse-engineered the
Casio protocol: GShockAPI / gshock-api by Ivo Zivkov (MIT) and Gadgetbridge.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from datetime import date, datetime
from enum import IntEnum

# --------------------------------------------------------------------------
# GATT identifiers
# --------------------------------------------------------------------------
ADVERTISED_SERVICE_UUID = "00001804-0000-1000-8000-00805f9b34fb"
REQUEST_UUID = "26eb002c-b012-49a8-b1f8-394fb2032b0f"        # write w/o response: "please send register X"
ALL_FEATURES_UUID = "26eb002d-b012-49a8-b1f8-394fb2032b0f"   # notify = register value, write = set register
DATA_REQUEST_UUID = "26eb0023-b012-49a8-b1f8-394fb2032b0f"   # step-log transfer control
CONVOY_UUID = "26eb0024-b012-49a8-b1f8-394fb2032b0f"         # step-log data stream


class Reg(IntEnum):
    """Register (feature) codes used by the ABL-100WE."""

    CURRENT_TIME = 0x09
    BLE_FEATURES = 0x10
    SETTING_FOR_BLE = 0x11       # auto time-sync on/off + minute
    SETTING_FOR_BASIC = 0x13     # 12/24h, button tone, light, power saving
    ALARM_1 = 0x15               # alarm 1 + hourly chime
    ALARMS_2_5 = 0x16            # alarms 2..5
    TIMER = 0x18
    DST_WATCH_STATE = 0x1D       # DST on/auto flags for home + dual time
    DST_SETTING = 0x1E           # UTC offset / DST offset / DST rules per clock
    VERSION_INFORMATION = 0x20
    APP_INFO = 0x22
    WATCH_NAME = 0x23
    WATCH_CONDITION = 0x28
    TARGET_VALUE = 0x43          # step goal (experimental on ABL-100WE)
    USER_PROFILE = 0x45          # height/weight/... (experimental on ABL-100WE)
    ERROR = 0xFF


class ProtocolError(ValueError):
    """Raised when a payload from the watch cannot be decoded."""


ALARM_COUNT = 5
TIMER_MAX_SECONDS = 60 * 60          # ABL-100WE countdown timer: up to 60 minutes
STEP_GOAL_MIN, STEP_GOAL_MAX = 1_000, 50_000

# Light duration labels.  Bit set = long.
LIGHT_SHORT, LIGHT_LONG = "short", "long"
LIGHT_SECONDS = {LIGHT_SHORT: "1.5s", LIGHT_LONG: "3s"}

# App registration (sent when the watch reports an empty app id).
APP_ID = bytes.fromhex("3488F4E5D5AFC829E06D")


# --------------------------------------------------------------------------
# Small helpers
# --------------------------------------------------------------------------
def hexstr(data: bytes | bytearray) -> str:
    return " ".join(f"{b:02X}" for b in data)


def parse_hex(text: str) -> bytes:
    cleaned = text.replace(" ", "").replace(":", "").replace("0x", "").replace("0X", "")
    if len(cleaned) % 2:
        raise ValueError(f"hex string has odd length: {text!r}")
    return bytes.fromhex(cleaned)


def _set_bit(value: int, mask: int, on: bool) -> int:
    return (value | mask) if on else (value & ~mask & 0xFF)


def _require(raw: bytes, reg: int, min_len: int) -> bytearray:
    if not raw or raw[0] != reg:
        raise ProtocolError(f"expected register 0x{reg:02X}, got {hexstr(raw[:1]) or 'nothing'}")
    if len(raw) < min_len:
        raise ProtocolError(f"register 0x{reg:02X} too short ({len(raw)} bytes, need {min_len}): {hexstr(raw)}")
    return bytearray(raw)


def _to_bcd(value: int) -> int:
    if not 0 <= value <= 99:
        raise ValueError(f"BCD value out of range: {value}")
    return ((value // 10) << 4) | (value % 10)


def _from_bcd(byte: int) -> int:
    high, low = byte >> 4, byte & 0x0F
    if high > 9 or low > 9:
        raise ProtocolError(f"invalid BCD byte 0x{byte:02X}")
    return high * 10 + low


def parse_error(raw: bytes) -> int | None:
    """Return the failing register for an ``FF 81 <reg>`` error frame, else None."""
    if len(raw) >= 3 and raw[0] == Reg.ERROR:
        return raw[2]
    return None


# --------------------------------------------------------------------------
# 0x13 basic settings
# --------------------------------------------------------------------------
_MASK_24H = 0x01
_MASK_TONE_OFF = 0x02
_MASK_POWER_SAVING_OFF = 0x10
_MASK_LIGHT_LONG = 0x01
_DATE_IDX, _MASK_DAY_FIRST = 4, 0x01   # date order; other Casio modules use it, unconfirmed on ABL-100WE

DATE_MONTH_FIRST, DATE_DAY_FIRST = "MM-DD", "DD-MM"


@dataclass
class BasicSettings:
    time_format: str        # "12h" | "24h"
    button_tone: bool
    light_duration: str     # "short" | "long"
    power_saving: bool
    date_format: str | None = None   # "MM-DD" | "DD-MM" (experimental)


def decode_basic(raw: bytes) -> BasicSettings:
    r = _require(raw, Reg.SETTING_FOR_BASIC, 3)
    return BasicSettings(
        time_format="24h" if r[1] & _MASK_24H else "12h",
        button_tone=not r[1] & _MASK_TONE_OFF,
        light_duration=LIGHT_LONG if r[2] & _MASK_LIGHT_LONG else LIGHT_SHORT,
        power_saving=not r[1] & _MASK_POWER_SAVING_OFF,
        date_format=(DATE_DAY_FIRST if r[_DATE_IDX] & _MASK_DAY_FIRST else DATE_MONTH_FIRST)
        if len(r) > _DATE_IDX else None,
    )


def encode_basic(
    raw: bytes,
    *,
    time_format: str | None = None,
    button_tone: bool | None = None,
    light_duration: str | None = None,
    power_saving: bool | None = None,
    date_format: str | None = None,
) -> bytes:
    r = _require(raw, Reg.SETTING_FOR_BASIC, 3)
    if time_format is not None:
        if time_format not in ("12h", "24h"):
            raise ValueError("time_format must be '12h' or '24h'")
        r[1] = _set_bit(r[1], _MASK_24H, time_format == "24h")
    if button_tone is not None:
        r[1] = _set_bit(r[1], _MASK_TONE_OFF, not button_tone)
    if power_saving is not None:
        r[1] = _set_bit(r[1], _MASK_POWER_SAVING_OFF, not power_saving)
    if light_duration is not None:
        light_duration = normalize_light(light_duration)
        r[2] = _set_bit(r[2], _MASK_LIGHT_LONG, light_duration == LIGHT_LONG)
    if date_format is not None:
        date_format = normalize_date_format(date_format)
        if len(r) <= _DATE_IDX:
            raise ProtocolError(f"register 0x13 too short for the date format byte: {hexstr(r)}")
        r[_DATE_IDX] = _set_bit(r[_DATE_IDX], _MASK_DAY_FIRST, date_format == DATE_DAY_FIRST)
    return bytes(r)


def normalize_date_format(value: str) -> str:
    v = str(value).strip().upper().replace(":", "-").replace(".", "-").replace("/", "-").replace(" ", "")
    if v in ("MM-DD", "M-DD", "M-D", "MONTH-DAY", "MONTHFIRST"):
        return DATE_MONTH_FIRST
    if v in ("DD-MM", "DD-M", "D-M", "DAY-MONTH", "DAYFIRST"):
        return DATE_DAY_FIRST
    raise ValueError("date_format must be 'MM-DD' (month first) or 'DD-MM' (day first)")


def normalize_light(value: str) -> str:
    v = str(value).strip().lower()
    if v in (LIGHT_SHORT, "1.5s", "1.5"):
        return LIGHT_SHORT
    if v in (LIGHT_LONG, "3s", "3"):
        return LIGHT_LONG
    raise ValueError("light_duration must be 'short' (1.5s) or 'long' (3s)")


# --------------------------------------------------------------------------
# 0x15 / 0x16 alarms + hourly chime
# --------------------------------------------------------------------------
_ALARM_ENABLED = 0x40
_HOURLY_CHIME = 0x80   # only meaningful in alarm 1's flag byte


@dataclass
class Alarm:
    enabled: bool
    hour: int
    minute: int


def _alarm_at(r: bytes, offset: int) -> Alarm:
    return Alarm(enabled=bool(r[offset] & _ALARM_ENABLED), hour=r[offset + 2], minute=r[offset + 3])


def decode_alarms(alarm1_raw: bytes, alarms_raw: bytes) -> tuple[list[Alarm], bool]:
    """Return ([alarm1..alarm5], hourly_chime)."""
    a1 = _require(alarm1_raw, Reg.ALARM_1, 5)
    a2 = _require(alarms_raw, Reg.ALARMS_2_5, 1 + 4 * (ALARM_COUNT - 1))
    alarms = [_alarm_at(a1, 1)] + [_alarm_at(a2, 1 + 4 * i) for i in range(ALARM_COUNT - 1)]
    return alarms, bool(a1[1] & _HOURLY_CHIME)


def _apply_alarm(r: bytearray, offset: int, update: dict) -> None:
    if update.get("enabled") is not None:
        r[offset] = _set_bit(r[offset], _ALARM_ENABLED, bool(update["enabled"]))
    if update.get("hour") is not None:
        r[offset + 2] = check_range("alarm hour", update["hour"], 0, 23)
    if update.get("minute") is not None:
        r[offset + 3] = check_range("alarm minute", update["minute"], 0, 59)


def encode_alarm1(raw: bytes, update: dict | None = None, hourly_chime: bool | None = None) -> bytes:
    r = _require(raw, Reg.ALARM_1, 5)
    if update:
        _apply_alarm(r, 1, update)
    if hourly_chime is not None:
        r[1] = _set_bit(r[1], _HOURLY_CHIME, hourly_chime)
    return bytes(r)


def encode_alarms_2_5(raw: bytes, updates: dict[int, dict]) -> bytes:
    """``updates`` maps alarm number (2..5) to {enabled?, hour?, minute?}."""
    r = _require(raw, Reg.ALARMS_2_5, 1 + 4 * (ALARM_COUNT - 1))
    for number, update in updates.items():
        if not 2 <= number <= ALARM_COUNT:
            raise ValueError(f"alarm number must be 2..{ALARM_COUNT} here, got {number}")
        _apply_alarm(r, 1 + 4 * (number - 2), update)
    return bytes(r)


# --------------------------------------------------------------------------
# 0x18 countdown timer
# --------------------------------------------------------------------------
def decode_timer(raw: bytes) -> int:
    r = _require(raw, Reg.TIMER, 4)
    return r[1] * 3600 + r[2] * 60 + r[3]


def encode_timer(raw: bytes, seconds: int) -> bytes:
    r = _require(raw, Reg.TIMER, 4)
    seconds = check_range("timer seconds", seconds, 1, TIMER_MAX_SECONDS)
    r[1], rest = divmod(seconds, 3600)
    r[2], r[3] = divmod(rest, 60)
    return bytes(r)


def parse_duration(text: str) -> int:
    """'90' -> 90 s, '5:00' -> 300 s, '1:00:00' -> 3600 s."""
    parts = str(text).strip().split(":")
    if not 1 <= len(parts) <= 3 or not all(p.strip().isdigit() for p in parts):
        raise ValueError(f"invalid duration {text!r}; use SECONDS, MM:SS or HH:MM:SS")
    total = 0
    for p in parts:
        total = total * 60 + int(p)
    return total


def format_duration(seconds: int) -> str:
    h, rest = divmod(seconds, 3600)
    m, s = divmod(rest, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m:02d}:{s:02d}"


# --------------------------------------------------------------------------
# 0x11 BLE settings -> automatic time sync
# --------------------------------------------------------------------------
_SYNC_IDX, _SYNC_MINUTE_IDX, _SYNC_OFF = 12, 13, 0x80


def decode_time_sync(raw: bytes) -> tuple[bool, int]:
    r = _require(raw, Reg.SETTING_FOR_BLE, 14)
    return not r[_SYNC_IDX] & _SYNC_OFF, r[_SYNC_MINUTE_IDX]


def encode_time_sync(raw: bytes, *, enabled: bool | None = None, minute: int | None = None) -> bytes:
    r = _require(raw, Reg.SETTING_FOR_BLE, 14)
    if enabled is not None:
        r[_SYNC_IDX] = _set_bit(r[_SYNC_IDX], _SYNC_OFF, not enabled)
    if minute is not None:
        r[_SYNC_MINUTE_IDX] = check_range("time sync minute", minute, 0, 59)
    return bytes(r)


# --------------------------------------------------------------------------
# 0x09 current time
# --------------------------------------------------------------------------
def encode_current_time(dt: datetime) -> bytes:
    """Wall-clock time of the home time zone -> 11-byte 0x09 register."""
    return bytes([Reg.CURRENT_TIME]) + struct.pack(
        "<HBBBBBBBB",
        dt.year,
        dt.month,
        dt.day,
        dt.hour,
        dt.minute,
        dt.second,
        dt.isoweekday(),            # Mon=1 .. Sun=7
        dt.microsecond * 256 // 1_000_000,
        1,
    )


# --------------------------------------------------------------------------
# 0x1D / 0x1E time zone + DST (slot 0 = home time, slot 1 = dual time)
# --------------------------------------------------------------------------
DST_ON, DST_AUTO = 0x01, 0x02


def encode_dst_watch_state(raw: bytes, slot: int, flags: int) -> bytes:
    """Response to request ``1D 00`` holds DST flags for slot 0 at [3], slot 1 at [4]."""
    r = _require(raw, Reg.DST_WATCH_STATE, 5)
    if slot not in (0, 1):
        raise ValueError("slot must be 0 (home) or 1 (dual time)")
    r[3 + slot] = flags & 0xFF
    return bytes(r)


def decode_dst_watch_state(raw: bytes) -> list[int]:
    r = _require(raw, Reg.DST_WATCH_STATE, 5)
    return [r[3], r[4]]


def encode_dst_setting(raw: bytes, offset_q: int, dst_offset_q: int, rules: int) -> bytes:
    """Offsets are in 15-minute units (signed for the UTC offset)."""
    r = _require(raw, Reg.DST_SETTING, 7)
    r[4] = offset_q & 0xFF
    r[5] = dst_offset_q & 0xFF
    r[6] = rules & 0xFF
    return bytes(r)


@dataclass
class ZoneInfoOnWatch:
    slot: int
    utc_offset_minutes: int
    dst_offset_minutes: int
    dst_rules: int


def decode_dst_setting(raw: bytes) -> ZoneInfoOnWatch:
    r = _require(raw, Reg.DST_SETTING, 7)
    offset = r[4] - 256 if r[4] >= 128 else r[4]
    return ZoneInfoOnWatch(slot=r[1], utc_offset_minutes=offset * 15, dst_offset_minutes=r[5] * 15, dst_rules=r[6])


def format_utc_offset(minutes: int) -> str:
    sign = "+" if minutes >= 0 else "-"
    h, m = divmod(abs(minutes), 60)
    return f"UTC{sign}{h:02d}:{m:02d}"


# --------------------------------------------------------------------------
# 0x22 app info / 0x23 watch name
# --------------------------------------------------------------------------
def app_needs_registration(raw: bytes) -> bool:
    return len(raw) >= 12 and raw[0] == Reg.APP_INFO and raw[1:11] == b"\xFF" * 10 and raw[11] == 0x00


def encode_app_info() -> bytes:
    return bytes([Reg.APP_INFO]) + APP_ID + b"\x02"


def decode_watch_name(raw: bytes) -> str:
    r = _require(raw, Reg.WATCH_NAME, 1)
    return bytes(b for b in r[1:] if 32 <= b < 127).decode("ascii").strip()


# --------------------------------------------------------------------------
# 0x43 step goal (experimental on the ABL-100WE; layout from Gadgetbridge)
# --------------------------------------------------------------------------
def decode_step_goal(raw: bytes) -> int:
    r = _require(raw, Reg.TARGET_VALUE, 3)
    return r[1] | (r[2] << 8)


def encode_step_goal(raw: bytes, steps: int) -> bytes:
    r = _require(raw, Reg.TARGET_VALUE, 3)
    steps = check_range("step goal", steps, STEP_GOAL_MIN, STEP_GOAL_MAX)
    r[1], r[2] = steps & 0xFF, (steps >> 8) & 0xFF
    return bytes(r)


# --------------------------------------------------------------------------
# 0x45 user profile (experimental; bytes 2..9 are bit-inverted BCD)
# --------------------------------------------------------------------------
_PROFILE_FEMALE, _PROFILE_RIGHT_WRIST = 0x01, 0x02


@dataclass
class UserProfile:
    gender: str                    # "male" | "female"
    wrist: str                     # "left" | "right"
    height_cm: int | None
    weight_kg: int | None
    birthday: str | None           # ISO date


def _inv_bcd_pair(r: bytes, idx: int) -> int | None:
    try:
        return _from_bcd(~r[idx] & 0xFF) + _from_bcd(~r[idx + 1] & 0xFF) * 100
    except ProtocolError:
        return None


def decode_user_profile(raw: bytes) -> UserProfile:
    r = _require(raw, Reg.USER_PROFILE, 10)
    birthday = None
    year = _inv_bcd_pair(r, 6)
    try:
        if year:
            birthday = date(year, _from_bcd(~r[8] & 0xFF), _from_bcd(~r[9] & 0xFF)).isoformat()
    except (ProtocolError, ValueError):
        birthday = None
    return UserProfile(
        gender="female" if r[1] & _PROFILE_FEMALE else "male",
        wrist="right" if r[1] & _PROFILE_RIGHT_WRIST else "left",
        height_cm=_inv_bcd_pair(r, 2),
        weight_kg=_inv_bcd_pair(r, 4),
        birthday=birthday,
    )


def _put_inv_bcd_pair(r: bytearray, idx: int, value: int) -> None:
    r[idx] = ~_to_bcd(value % 100) & 0xFF
    r[idx + 1] = ~_to_bcd(value // 100) & 0xFF


def encode_user_profile(
    raw: bytes,
    *,
    gender: str | None = None,
    wrist: str | None = None,
    height_cm: int | None = None,
    weight_kg: int | None = None,
    birthday: str | date | None = None,
) -> bytes:
    r = _require(raw, Reg.USER_PROFILE, 10)
    if gender is not None:
        if gender not in ("male", "female"):
            raise ValueError("gender must be 'male' or 'female'")
        r[1] = _set_bit(r[1], _PROFILE_FEMALE, gender == "female")
    if wrist is not None:
        if wrist not in ("left", "right"):
            raise ValueError("wrist must be 'left' or 'right'")
        r[1] = _set_bit(r[1], _PROFILE_RIGHT_WRIST, wrist == "right")
    if height_cm is not None:
        _put_inv_bcd_pair(r, 2, check_range("height_cm", height_cm, 50, 300))
    if weight_kg is not None:
        _put_inv_bcd_pair(r, 4, check_range("weight_kg", weight_kg, 10, 500))
    if birthday is not None:
        d = birthday if isinstance(birthday, date) else date.fromisoformat(str(birthday))
        _put_inv_bcd_pair(r, 6, d.year)
        r[8] = ~_to_bcd(d.month) & 0xFF
        r[9] = ~_to_bcd(d.day) & 0xFF
    return bytes(r)


# --------------------------------------------------------------------------
# Step log (life log) streamed over the convoy characteristic
# --------------------------------------------------------------------------
STEP_LOG_START = bytes([0x00, 0x11, 0x00, 0x00, 0x00])
STEP_LOG_END = bytes([0x04, 0x11, 0x00, 0x00, 0x00])
STEP_LOG_FALLBACK_LENGTH = 400
_STEP_TODAY_OFFSET = 374
_STEP_TODAY_DISTANCE_OFFSET = 378
_STEP_DAILY_OFFSET = 318
_STEP_DAILY_COUNT = 7
_UNUSED = (0xFFFFFFFE, 0xFFFFFFFF)


def parse_step_announcement(data: bytes) -> int | None:
    """``00 11 L0 L1 L2`` on the data-request characteristic announces the payload length."""
    if len(data) >= 5 and data[0] == 0x00 and data[1] == 0x11:
        return data[2] | (data[3] << 8) | (data[4] << 16)
    return None


@dataclass
class StepLog:
    timestamp: str | None
    today_steps: int | None
    today_distance_m: int | None
    history: list[dict] = field(default_factory=list)   # [{days_ago, steps, distance_m}]


def parse_step_log(payload: bytes) -> StepLog:
    timestamp = None
    if len(payload) >= 6:
        try:
            timestamp = datetime(
                2000 + _from_bcd(payload[0]), _from_bcd(payload[1]), _from_bcd(payload[2]),
                _from_bcd(payload[3]), _from_bcd(payload[4]), _from_bcd(payload[5]),
            ).isoformat()
        except (ProtocolError, ValueError):
            timestamp = None

    def u32(offset: int) -> int | None:
        if len(payload) < offset + 4:
            return None
        value = struct.unpack_from("<I", payload, offset)[0]
        return None if value in _UNUSED else value

    history = []
    for i in range(_STEP_DAILY_COUNT):
        off = _STEP_DAILY_OFFSET + 8 * i
        if len(payload) < off + 8:
            break
        history.append({"days_ago": i + 1, "steps": u32(off), "distance_m": u32(off + 4)})

    return StepLog(
        timestamp=timestamp,
        today_steps=u32(_STEP_TODAY_OFFSET),
        today_distance_m=u32(_STEP_TODAY_DISTANCE_OFFSET),
        history=history,
    )


# --------------------------------------------------------------------------
def check_range(name: str, value, low: int, high: int) -> int:
    try:
        ivalue = int(value)
    except (TypeError, ValueError):
        raise ValueError(f"{name} must be a whole number, got {value!r}") from None
    if ivalue != value and not isinstance(value, str):
        raise ValueError(f"{name} must be a whole number, got {value!r}")
    if not low <= ivalue <= high:
        raise ValueError(f"{name} must be between {low} and {high}, got {ivalue}")
    return ivalue
