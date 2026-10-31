import struct
import unittest
from datetime import datetime, timezone

from abl100we import protocol as p
from abl100we.timezones import resolve


def basic(b1=0x00, b2=0x00):
    return bytes([0x13, b1, b2]) + bytes(9)


class BasicSettingsTest(unittest.TestCase):
    def test_decode(self):
        s = p.decode_basic(basic(0x01 | 0x02, 0x01))
        self.assertEqual(s.time_format, "24h")
        self.assertFalse(s.button_tone)
        self.assertEqual(s.light_duration, "long")
        self.assertTrue(s.power_saving)

    def test_encode_only_touches_known_bits(self):
        raw = basic(0x20 | 0x10, 0x80)          # 0x20 / 0x80 are undocumented bits
        new = p.encode_basic(raw, time_format="24h", button_tone=False, power_saving=True, light_duration="3s")
        self.assertEqual(new[1], 0x20 | 0x01 | 0x02)
        self.assertEqual(new[2], 0x80 | 0x01)
        self.assertEqual(new[3:], raw[3:])

    def test_round_trip(self):
        new = p.encode_basic(basic(), time_format="12h", button_tone=True, power_saving=False, light_duration="short")
        s = p.decode_basic(new)
        self.assertEqual((s.time_format, s.button_tone, s.power_saving, s.light_duration), ("12h", True, False, "short"))

    def test_date_format(self):
        raw = bytes([0x13, 0, 0, 0, 0x40]) + bytes(7)              # 0x40 = unknown bit in byte 4
        self.assertEqual(p.decode_basic(raw).date_format, "MM-DD")
        new = p.encode_basic(raw, date_format="dd/mm")
        self.assertEqual(new[4], 0x41)
        self.assertEqual(new[:4] + new[5:], raw[:4] + raw[5:])
        self.assertEqual(p.decode_basic(new).date_format, "DD-MM")
        self.assertEqual(p.encode_basic(new, date_format="MM-DD"), raw)
        self.assertIsNone(p.decode_basic(bytes([0x13, 0, 0])).date_format)   # short register
        with self.assertRaises(ValueError):
            p.encode_basic(raw, date_format="YYYY")

    def test_rejects_wrong_register(self):
        with self.assertRaises(p.ProtocolError):
            p.decode_basic(bytes([0x15, 0, 0]))
        with self.assertRaises(ValueError):
            p.encode_basic(basic(), time_format="25h")


class AlarmTest(unittest.TestCase):
    A1 = bytes([0x15, 0x40, 0x40, 7, 0])
    A2 = bytes([0x16, 0x00, 0x40, 6, 0, 0x40, 0x40, 8, 30, 0x00, 0x40, 12, 0, 0x00, 0x40, 13, 5])

    def test_decode(self):
        alarms, chime = p.decode_alarms(self.A1, self.A2)
        self.assertFalse(chime)
        self.assertEqual([(a.enabled, a.hour, a.minute) for a in alarms],
                         [(True, 7, 0), (False, 6, 0), (True, 8, 30), (False, 12, 0), (False, 13, 5)])

    def test_alarm1_and_chime_share_a_byte(self):
        new = p.encode_alarm1(self.A1, {"enabled": False, "hour": 6, "minute": 45}, hourly_chime=True)
        self.assertEqual(new, bytes([0x15, 0x80, 0x40, 6, 45]))

    def test_encode_alarms_2_5(self):
        new = p.encode_alarms_2_5(self.A2, {5: {"enabled": True, "hour": 22, "minute": 15}, 2: {"enabled": True}})
        alarms, _ = p.decode_alarms(self.A1, new)
        self.assertEqual((alarms[1].enabled, alarms[1].hour), (True, 6))
        self.assertEqual((alarms[4].enabled, alarms[4].hour, alarms[4].minute), (True, 22, 15))

    def test_range(self):
        with self.assertRaises(ValueError):
            p.encode_alarm1(self.A1, {"hour": 24})
        with self.assertRaises(ValueError):
            p.encode_alarms_2_5(self.A2, {1: {"hour": 1}})


class TimerAndSyncTest(unittest.TestCase):
    def test_timer(self):
        raw = bytes([0x18, 0, 3, 0, 0, 0, 0])
        self.assertEqual(p.decode_timer(raw), 180)
        self.assertEqual(p.encode_timer(raw, 3600), bytes([0x18, 1, 0, 0, 0, 0, 0]))
        self.assertEqual(p.decode_timer(p.encode_timer(raw, 125)), 125)
        with self.assertRaises(ValueError):
            p.encode_timer(raw, 3601)

    def test_duration_parsing(self):
        self.assertEqual(p.parse_duration("90"), 90)
        self.assertEqual(p.parse_duration("5:00"), 300)
        self.assertEqual(p.parse_duration("1:00:00"), 3600)
        self.assertEqual(p.format_duration(300), "05:00")
        with self.assertRaises(ValueError):
            p.parse_duration("5m")

    def test_time_sync(self):
        raw = bytes.fromhex("1100000006000000000001008f1e00")
        self.assertEqual(p.decode_time_sync(raw), (False, 30))
        new = p.encode_time_sync(raw, enabled=True, minute=10)
        self.assertEqual(new[12], 0x0F)            # only bit 0x80 cleared
        self.assertEqual(p.decode_time_sync(new), (True, 10))


class TimeTest(unittest.TestCase):
    def test_current_time(self):
        dt = datetime(2026, 10, 31, 13, 58, 7, 500_000)  # a Saturday
        self.assertEqual(p.encode_current_time(dt), bytes([0x09, 0xEA, 0x07, 10, 31, 13, 58, 7, 6, 128, 1]))

    def test_dst_registers(self):
        state = bytes.fromhex("1d0001030 00c010c01ffffffffffff".replace(" ", ""))
        self.assertEqual(p.decode_dst_watch_state(state), [3, 0])
        self.assertEqual(p.decode_dst_watch_state(p.encode_dst_watch_state(state, 1, 2)), [3, 2])

        setting = bytes.fromhex("1e00a10000 0402".replace(" ", ""))
        new = p.encode_dst_setting(setting, -20, 4, 1)   # UTC-5, +1h DST
        self.assertEqual(new, bytes.fromhex("1e00a100ec0401"))
        z = p.decode_dst_setting(new)
        self.assertEqual((z.utc_offset_minutes, z.dst_offset_minutes, z.dst_rules), (-300, 60, 1))
        self.assertEqual(p.format_utc_offset(-300), "UTC-05:00")


class ZoneTest(unittest.TestCase):
    def test_listed_zone(self):
        z = resolve("America/New_York", datetime(2026, 1, 10, tzinfo=timezone.utc))
        self.assertEqual((z.offset_q, z.dst_offset_q, z.rules, z.in_dst), (-20, 4, 1, False))
        self.assertEqual(z.dst_flags, p.DST_AUTO)

    def test_equivalent_zone_borrows_rules(self):
        z = resolve("Europe/Lisbon", datetime(2026, 7, 1, tzinfo=timezone.utc))
        self.assertEqual((z.offset_q, z.dst_offset_q, z.rules, z.in_dst), (0, 4, 2, True))
        self.assertEqual(z.dst_flags, p.DST_ON | p.DST_AUTO)

    def test_zone_without_dst(self):
        z = resolve("Asia/Tokyo")
        self.assertEqual((z.offset_q, z.dst_offset_q, z.rules, z.dst_flags), (36, 0, 0, 0))

    def test_half_hour_zone(self):
        self.assertEqual(resolve("Asia/Kolkata").offset_q, 22)

    def test_unknown_zone(self):
        with self.assertRaises(ValueError):
            resolve("Mars/Olympus_Mons")


class MiscRegistersTest(unittest.TestCase):
    def test_app_registration(self):
        self.assertTrue(p.app_needs_registration(bytes([0x22]) + b"\xff" * 10 + b"\x00"))
        self.assertFalse(p.app_needs_registration(p.encode_app_info()))
        self.assertEqual(len(p.encode_app_info()), 12)

    def test_watch_name(self):
        self.assertEqual(p.decode_watch_name(b"\x23CASIO ABL-100WE\x00\x00\x00"), "CASIO ABL-100WE")

    def test_error_frame(self):
        self.assertEqual(p.parse_error(bytes([0xFF, 0x81, 0x1F])), 0x1F)
        self.assertIsNone(p.parse_error(bytes([0x13, 0x00, 0x00])))

    def test_step_goal(self):
        raw = bytes([0x43, 0x10, 0x27]) + bytes(9)
        self.assertEqual(p.decode_step_goal(raw), 10_000)
        self.assertEqual(p.decode_step_goal(p.encode_step_goal(raw, 50_000)), 50_000)
        with self.assertRaises(ValueError):
            p.encode_step_goal(raw, 500)

    def test_user_profile_round_trip(self):
        raw = bytes([0x45, 0x00]) + b"\xff" * 10
        new = p.encode_user_profile(raw, gender="female", wrist="right", height_cm=172, weight_kg=68, birthday="1990-04-23")
        self.assertEqual(new[2], ~0x72 & 0xFF)   # inverted BCD "72"
        self.assertEqual(new[3], ~0x01 & 0xFF)   # inverted BCD "01" (hundreds)
        self.assertEqual(new[10:], raw[10:])     # bytes past the known fields untouched
        prof = p.decode_user_profile(new)
        self.assertEqual((prof.gender, prof.wrist, prof.height_cm, prof.weight_kg, prof.birthday),
                         ("female", "right", 172, 68, "1990-04-23"))


def make_step_log(today=4321, distance=3000, history=((1000, 700),), when=b"\x26\x10\x31\x13\x58\x07"):
    payload = bytearray(400)
    payload[0:6] = when
    for i in range(7):
        steps, dist = history[i] if i < len(history) else (0xFFFFFFFE, 0xFFFFFFFE)
        struct.pack_into("<II", payload, 318 + 8 * i, steps, dist)
    struct.pack_into("<II", payload, 374, today, distance)
    return bytes(payload)


class StepLogTest(unittest.TestCase):
    def test_parse(self):
        log = p.parse_step_log(make_step_log())
        self.assertEqual(log.timestamp, "2026-10-31T13:58:07")
        self.assertEqual((log.today_steps, log.today_distance_m), (4321, 3000))
        self.assertEqual(log.history[0], {"days_ago": 1, "steps": 1000, "distance_m": 700})
        self.assertEqual(log.history[1], {"days_ago": 2, "steps": None, "distance_m": None})
        self.assertEqual(len(log.history), 7)

    def test_announcement(self):
        self.assertEqual(p.parse_step_announcement(bytes([0x00, 0x11, 0x90, 0x01, 0x00])), 400)
        self.assertIsNone(p.parse_step_announcement(bytes([0x04, 0x11, 0, 0, 0])))


if __name__ == "__main__":
    unittest.main()
