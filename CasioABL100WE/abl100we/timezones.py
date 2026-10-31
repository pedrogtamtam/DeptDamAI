"""Map an IANA time zone (e.g. "Europe/Lisbon") to what the watch stores.

The watch keeps, per clock slot: the standard UTC offset and the DST offset
(both in 15-minute steps) plus a Casio "DST rules" code that lets it switch
summer time on its own.  Rule codes come from the gshock-api / Gadgetbridge
tables; zones that are not listed borrow the code of a zone with an identical
offset/DST calendar, otherwise DST is just switched on/off manually.
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from .protocol import DST_AUTO, DST_ON

# Only zones that observe DST need a rule code (no-DST zones always use 0).
CASIO_DST_RULES: dict[str, int] = {
    "America/Anchorage": 0x01,
    "America/Los_Angeles": 0x01,
    "America/Denver": 0x01,
    "America/Chicago": 0x01,
    "America/New_York": 0x01,
    "America/Halifax": 0x01,
    "America/St_Johns": 0x01,
    "America/Havana": 0x15,
    "America/Santiago": 0x1B,
    "America/Asuncion": 0x09,
    "Atlantic/Azores": 0x02,
    "Europe/London": 0x02,
    "Europe/Paris": 0x02,
    "Europe/Athens": 0x02,
    "Africa/Casablanca": 0x0F,
    "Asia/Beirut": 0x0C,
    "Asia/Jerusalem": 0x2A,
    "Asia/Tehran": 0x2B,
    "Australia/Adelaide": 0x04,
    "Australia/Sydney": 0x04,
    "Australia/Lord_Howe": 0x12,
    "Pacific/Norfolk": 0x04,
    "Pacific/Auckland": 0x05,
    "Pacific/Chatham": 0x17,
    "Pacific/Easter": 0x1C,
}


@dataclass
class CasioZone:
    name: str
    offset_q: int          # standard UTC offset, 15-minute units (signed)
    dst_offset_q: int      # DST shift, 15-minute units (0 if the zone has no DST)
    rules: int             # Casio DST rule code (0 = none)
    in_dst: bool

    @property
    def dst_flags(self) -> int:
        return (DST_ON if self.in_dst else 0) | (DST_AUTO if self.rules else 0)


def _zone(name: str) -> ZoneInfo:
    try:
        return ZoneInfo(name)
    except (ZoneInfoNotFoundError, ValueError) as exc:
        raise ValueError(
            f"unknown time zone {name!r}; use an IANA name such as 'Europe/Lisbon' "
            "(on Windows run: pip install tzdata)"
        ) from exc


def _signature(tz: ZoneInfo, start: datetime) -> list[tuple]:
    """Offsets sampled weekly for a year: identical lists => identical DST calendar."""
    return [
        ((start + timedelta(days=7 * i)).astimezone(tz).utcoffset(),
         (start + timedelta(days=7 * i)).astimezone(tz).dst())
        for i in range(53)
    ]


def _dst_duration(tz: ZoneInfo, now: datetime) -> timedelta:
    current = now.astimezone(tz).dst() or timedelta(0)
    if current:
        return current
    samples = [datetime(now.year, month, 15, tzinfo=tz).dst() or timedelta(0) for month in range(1, 13)]
    return max(samples, key=abs)


def _rules_for(name: str, tz: ZoneInfo, now: datetime) -> int:
    if name in CASIO_DST_RULES:
        return CASIO_DST_RULES[name]
    start = datetime(now.year, 1, 1, tzinfo=timezone.utc)
    sig = _signature(tz, start)
    for candidate, rules in CASIO_DST_RULES.items():
        if _signature(ZoneInfo(candidate), start) == sig:
            return rules
    return 0


def resolve(name: str, now: datetime | None = None) -> CasioZone:
    tz = _zone(name)
    now = (now or datetime.now(timezone.utc)).astimezone(tz)
    dst_now = now.dst() or timedelta(0)
    standard = (now.utcoffset() or timedelta(0)) - dst_now
    dst_len = _dst_duration(tz, now)
    rules = _rules_for(name, tz, now) if dst_len else 0
    return CasioZone(
        name=name,
        offset_q=int(standard.total_seconds() // 900),
        dst_offset_q=int(dst_len.total_seconds() // 900),
        rules=rules,
        in_dst=bool(dst_now),
    )


def local_zone_name() -> str:
    """Best-effort IANA name of this computer's time zone."""
    try:
        from tzlocal import get_localzone_name  # optional dependency

        name = get_localzone_name()
        if name:
            return name
    except Exception:  # noqa: BLE001 - fall through to the manual probes
        pass
    if os.environ.get("TZ"):
        return os.environ["TZ"].lstrip(":")
    try:
        link = os.path.realpath("/etc/localtime")
        if "zoneinfo/" in link:
            return link.split("zoneinfo/", 1)[1]
    except OSError:
        pass
    return "UTC"
