"""End-to-end tests of the BLE conversation against a simulated ABL-100WE."""

import asyncio
import unittest
from types import SimpleNamespace

from abl100we import protocol as p
from abl100we.cli import build_parser, config_from_args
from abl100we.config import apply_config, read_state, validate_config
from abl100we.watch import WatchError, WatchSession
from tests.test_protocol import make_step_log


def h(text: str) -> bytes:
    return bytes.fromhex(text.replace(" ", ""))


class FakeWatch:
    """Bleak-like client that answers like the watch does."""

    def __init__(self, unsupported=()):
        self.is_connected = True
        self.unsupported = set(unsupported)
        self.writes: list[bytes] = []
        self.callbacks = {}
        self.registers = {
            h("13"): h("13 21 00 00 00 00 00 00 00 00 00 00"),
            h("15"): h("15 40 40 07 00"),
            h("16"): h("16 00 40 06 00 00 40 08 1E 00 40 0C 00 00 40 0D 00"),
            h("18"): h("18 00 03 00 00 00 00"),
            h("11"): h("11 00 00 00 06 00 00 00 00 00 01 00 80 1E 00"),
            h("1D 00"): h("1D 00 01 00 00 0C 01 0C 01 FF FF FF FF FF FF"),
            h("1E 00"): h("1E 00 A1 00 00 04 02"),
            h("1E 01"): h("1E 01 CA 00 EC 04 01"),
            h("22"): h("22 FF FF FF FF FF FF FF FF FF FF 00"),
            h("23"): b"\x23CASIO ABL-100WE\x00\x00\x00",
            h("43"): h("43 10 27 00 00 00 00 00 00 00 00 00"),
            h("45"): h("45 00") + bytes(~b & 0xFF for b in h("75 01 70 00 90 19 01 01")) + h("FF FF"),
        }
        chars = [
            SimpleNamespace(uuid=p.REQUEST_UUID, properties=["write-without-response"]),
            SimpleNamespace(uuid=p.ALL_FEATURES_UUID, properties=["write", "notify"]),
            SimpleNamespace(uuid=p.DATA_REQUEST_UUID, properties=["write", "notify"]),
            SimpleNamespace(uuid=p.CONVOY_UUID, properties=["write-without-response", "notify"]),
        ]
        self.chars = {c.uuid: c for c in chars}
        self.services = [SimpleNamespace(characteristics=chars)]

    async def start_notify(self, char, callback):
        self.callbacks[char.uuid] = callback

    def _notify(self, uuid, data):
        asyncio.get_running_loop().call_soon(self.callbacks[uuid], self.chars[uuid], bytearray(data))

    async def write_gatt_char(self, uuid, data, response):
        data = bytes(data)
        if uuid == p.REQUEST_UUID:
            assert response is False
            if data[0] in self.unsupported or data not in self.registers:
                self._notify(p.ALL_FEATURES_UUID, bytes([0xFF, 0x81, data[0]]))
            else:
                self._notify(p.ALL_FEATURES_UUID, self.registers[data])
        elif uuid == p.ALL_FEATURES_UUID:
            assert response is True
            self.writes.append(data)
            key = data[:2] if data[0] in (0x1D, 0x1E) else data[:1]
            if key in self.registers and data[0] != p.Reg.APP_INFO:
                self.registers[key] = data
        elif uuid == p.DATA_REQUEST_UUID:
            self.writes.append(data)
            if data == p.STEP_LOG_START:
                payload = make_step_log(today=1234)
                self._notify(p.DATA_REQUEST_UUID, bytes([0x00, 0x11]) + len(payload).to_bytes(3, "little"))
                for i in range(0, len(payload), 20):
                    self._notify(p.CONVOY_UUID, payload[i:i + 20])

    async def disconnect(self):
        self.is_connected = False


def run(coro):
    return asyncio.run(coro)


async def session_for(fake) -> WatchSession:
    s = WatchSession(fake, address="AA:BB:CC:DD:EE:FF")
    await s.start()
    return s


class SessionTest(unittest.TestCase):
    def test_start_registers_app_and_reads_name(self):
        fake = FakeWatch()
        s = run(session_for(fake))
        self.assertEqual(fake.writes, [p.encode_app_info()])
        self.assertEqual(s.name, "CASIO ABL-100WE")

    def test_read_state(self):
        async def go():
            return await read_state(await session_for(FakeWatch()))

        state = run(go())
        self.assertEqual(state["time_format"], "24h")
        self.assertEqual(state["date_format"], "MM-DD")
        self.assertEqual(state["power_saving"], True)
        self.assertEqual(state["alarms"][0], {"enabled": True, "hour": 7, "minute": 0})
        self.assertEqual(state["alarms"][2], {"enabled": False, "hour": 8, "minute": 30})
        self.assertEqual(state["timer_seconds"], 180)
        self.assertEqual((state["auto_time_sync"], state["time_sync_minute"]), (False, 30))
        self.assertEqual(state["step_goal"], 10_000)
        self.assertEqual(state["user_profile"]["height_cm"], 175)
        self.assertEqual(state["user_profile"]["birthday"], "1990-01-01")
        self.assertEqual(state["_info"]["home_time"]["utc_offset"], "UTC+00:00")
        self.assertEqual(state["_info"]["dual_time"]["utc_offset"], "UTC-05:00")

    def test_dump_then_apply_changes_nothing(self):
        async def go():
            fake = FakeWatch()
            s = await session_for(fake)
            fake.writes.clear()
            state = await read_state(s)
            report = await apply_config(s, validate_config(state))
            return fake, report

        fake, report = run(go())
        self.assertEqual(fake.writes, [])
        self.assertTrue(all("already set" in line for line in report), report)

    def test_apply_changes_every_setting(self):
        cfg = validate_config({
            "time_format": "12h", "button_tone": False, "light_duration": "long", "power_saving": False,
            "date_format": "DD-MM",
            "hourly_chime": True, "auto_time_sync": "on", "time_sync_minute": 5,
            "alarms": [{"enabled": False}, None, {"time": "06:15", "enabled": True}, None, {"hour": 23, "minute": 59}],
            "timer_seconds": "10:00", "step_goal": 8000,
            "user_profile": {"height_cm": 180, "weight_kg": 80, "gender": "female"},
        })

        async def go():
            fake = FakeWatch()
            s = await session_for(fake)
            report = await apply_config(s, cfg)
            return fake, await read_state(s), report

        fake, state, report = run(go())
        self.assertFalse(any("[!]" in line for line in report), report)
        self.assertEqual(fake.registers[h("13")][1], 0x20 | 0x02 | 0x10)      # 12h, tone off, PS off; unknown bit 0x20 kept
        self.assertEqual((state["time_format"], state["button_tone"], state["power_saving"]), ("12h", False, False))
        self.assertEqual(state["light_duration"], "long")
        self.assertEqual(state["date_format"], "DD-MM")
        self.assertEqual(fake.registers[h("13")][4], 0x01)
        self.assertTrue(state["hourly_chime"])
        self.assertEqual(state["alarms"][0], {"enabled": False, "hour": 7, "minute": 0})
        self.assertEqual(state["alarms"][2], {"enabled": True, "hour": 6, "minute": 15})
        self.assertEqual(state["alarms"][4], {"enabled": False, "hour": 23, "minute": 59})
        self.assertEqual(state["timer_seconds"], 600)
        self.assertEqual((state["auto_time_sync"], state["time_sync_minute"]), (True, 5))
        self.assertEqual(state["step_goal"], 8000)
        self.assertEqual(state["user_profile"]["gender"], "female")
        self.assertEqual(state["user_profile"]["height_cm"], 180)
        self.assertEqual(state["user_profile"]["birthday"], "1990-01-01")  # untouched

    def test_sync_time_sequence(self):
        async def go():
            fake = FakeWatch()
            s = await session_for(fake)
            fake.writes.clear()
            await apply_config(s, validate_config({"sync_time": {"timezone": "America/New_York"}}))
            return fake

        fake = run(go())
        self.assertEqual([w[0] for w in fake.writes], [0x1D, 0x1E, 0x1E, 0x09])
        self.assertEqual([fake.writes[1][1], fake.writes[2][1]], [0, 1])
        self.assertEqual(fake.writes[1][4:7], bytes([0xEC, 0x04, 0x01]))     # UTC-5, 1h DST, US rules
        self.assertEqual(fake.writes[0][3] & p.DST_AUTO, p.DST_AUTO)
        self.assertEqual(len(fake.writes[-1]), 11)

    def test_dual_time(self):
        async def go():
            fake = FakeWatch()
            s = await session_for(fake)
            await apply_config(s, validate_config({"dual_time": "Asia/Tokyo"}))
            return fake

        fake = run(go())
        self.assertEqual(fake.registers[h("1E 01")][4:7], bytes([36, 0, 0]))
        self.assertEqual(fake.registers[h("1D 00")][4], 0)

    def test_rejected_register(self):
        async def go():
            s = await session_for(FakeWatch(unsupported={0x43}))
            self.assertIsNone(await s.read(0x43, optional=True))
            with self.assertRaises(WatchError):
                await s.read(0x43)
            state = await read_state(s)
            self.assertNotIn("step_goal", state)

        run(go())

    def test_step_log(self):
        async def go():
            fake = FakeWatch()
            s = await session_for(fake)
            payload = await s.read_step_log(timeout=2)
            return fake, payload

        fake, payload = run(go())
        self.assertEqual(len(payload), 400)
        self.assertEqual(p.parse_step_log(payload).today_steps, 1234)
        self.assertEqual(fake.writes[-1], p.STEP_LOG_END)


class ValidationAndCliTest(unittest.TestCase):
    def test_validation_errors(self):
        for bad in ({"colour": "red"}, {"timer_seconds": 4000}, {"alarms": [{"time": "25:00"}]},
                    {"button_tone": "maybe"}, {"step_goal": 10}, {"sync_time": {"timezone": "Nowhere/Land"}},
                    {"dual_time": True}, {"alarms": [None] * 6}):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                validate_config(bad)

    def test_ignores_dump_metadata(self):
        self.assertEqual(validate_config({"watch": {}, "_raw": {}, "_info": {}}), {})

    def test_cli_alarm(self):
        args = build_parser().parse_args(["alarm", "3", "--time", "06:30"])
        cfg = validate_config(config_from_args(args))
        self.assertEqual(cfg["alarms"], [None, None, {"enabled": True, "hour": 6, "minute": 30}])

    def test_cli_set(self):
        args = build_parser().parse_args(["set", "--time-format", "24h", "--timer", "5:00", "--hourly-chime", "off",
                                          "--date-format", "DD-MM"])
        cfg = validate_config(config_from_args(args))
        self.assertEqual(cfg, {"time_format": "24h", "date_format": "DD-MM", "hourly_chime": False, "timer_seconds": 300})


if __name__ == "__main__":
    unittest.main()
