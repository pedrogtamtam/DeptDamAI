"""Bluetooth LE transport for the ABL-100WE (built on the `bleak` library).

``WatchSession`` wraps any bleak-compatible client, so tests can pass a fake.
"""

from __future__ import annotations

import asyncio
import logging
from typing import Any, Callable

from . import protocol as p

log = logging.getLogger("abl100we")


class WatchError(RuntimeError):
    """The watch rejected a request, timed out or disconnected."""


def is_casio(name: str | None, service_uuids: list[str] | None) -> bool:
    return (name or "").upper().startswith("CASIO") or p.ADVERTISED_SERVICE_UUID in (service_uuids or [])


def _no_bluetooth(exc: Exception) -> WatchError:
    return WatchError(
        f"Bluetooth is not available ({exc}). Turn Bluetooth on; on Linux check that the "
        "'bluetooth' (BlueZ) service is running: systemctl status bluetooth"
    )


async def scan(timeout: float = 10.0) -> list[tuple[Any, Any]]:
    """Return [(device, advertisement)] for Casio watches currently advertising."""
    from bleak import BleakScanner

    try:
        found = await BleakScanner.discover(timeout=timeout, return_adv=True)
    except (FileNotFoundError, ConnectionRefusedError) as exc:
        raise _no_bluetooth(exc) from exc
    return [(dev, adv) for dev, adv in found.values() if is_casio(adv.local_name or dev.name, adv.service_uuids)]


async def find_device(address: str | None, name_filter: str, timeout: float):
    from bleak import BleakScanner

    wanted = name_filter.upper()

    def match(dev, adv) -> bool:
        name = adv.local_name or dev.name or ""
        return is_casio(name, adv.service_uuids) and wanted in name.upper()

    try:
        if address:
            device = await BleakScanner.find_device_by_address(address, timeout=timeout)
        else:
            device = await BleakScanner.find_device_by_filter(match, timeout=timeout)
    except (FileNotFoundError, ConnectionRefusedError) as exc:
        raise _no_bluetooth(exc) from exc
    if device is None:
        raise WatchError(
            "watch not found. Short-press the SEARCH button on the watch so it starts advertising, "
            "keep it close to this computer, and turn off Bluetooth on your phone so the "
            "CASIO WATCHES app does not grab the connection first."
        )
    return device


class WatchSession:
    """Request/response layer on top of the Casio GATT characteristics."""

    def __init__(self, client: Any, *, address: str = "", name: str = "") -> None:
        self.client = client
        self.address = address
        self.name = name
        self._pending: list[tuple[bytes, asyncio.Future]] = []
        self._lock = asyncio.Lock()
        self._step_buffer: bytearray | None = None
        self._step_expected: int | None = None
        self._step_done: asyncio.Future | None = None
        self.on_trace: Callable[[str], None] | None = None

    # ---------------------------------------------------------------- lifecycle
    @classmethod
    async def connect(cls, address: str | None = None, name_filter: str = "ABL", timeout: float = 30.0) -> "WatchSession":
        from bleak import BleakClient

        device = await find_device(address, name_filter, timeout)
        session: WatchSession | None = None

        def on_disconnect(_client) -> None:
            if session is not None:
                session._fail_all(WatchError("watch disconnected"))

        client = BleakClient(device, disconnected_callback=on_disconnect, timeout=timeout)
        await client.connect()
        session = cls(client, address=device.address, name=device.name or "")
        try:
            await session.start()
        except BaseException:
            await session.close()
            raise
        return session

    async def start(self) -> None:
        """Subscribe to notifications and register this app with the watch."""
        subscribed = set()
        for service in self.client.services:
            for char in service.characteristics:
                if {"notify", "indicate"} & set(char.properties):
                    try:
                        await self.client.start_notify(char, self._on_notify)
                        subscribed.add(str(char.uuid).lower())
                    except Exception as exc:  # noqa: BLE001 - optional characteristics
                        log.debug("could not subscribe to %s: %s", char.uuid, exc)
        if p.ALL_FEATURES_UUID not in subscribed:
            raise WatchError("this device does not expose the Casio settings characteristic; is it an ABL-100WE?")

        app_info = await self.read(p.Reg.APP_INFO, timeout=3.0, optional=True)
        if app_info and p.app_needs_registration(app_info):
            await self.write(p.encode_app_info())
        if not self.name or self.name.upper() == "CASIO":
            raw = await self.read(p.Reg.WATCH_NAME, timeout=3.0, optional=True)
            if raw:
                self.name = p.decode_watch_name(raw)

    async def close(self) -> None:
        try:
            if getattr(self.client, "is_connected", False):
                await self.client.disconnect()
        finally:
            self._fail_all(WatchError("session closed"))

    async def __aenter__(self) -> "WatchSession":
        return self

    async def __aexit__(self, *exc) -> None:
        await self.close()

    # ---------------------------------------------------------------- registers
    async def read(self, reg: int, *args: int, timeout: float = 5.0, optional: bool = False) -> bytes | None:
        """Ask the watch for a register; returns the raw payload (key included).

        With ``optional=True`` a timeout or error returns ``None`` instead of raising.
        """
        key = bytes([int(reg), *args])
        async with self._lock:
            future = asyncio.get_running_loop().create_future()
            self._pending.append((key, future))
            try:
                self._trace(f"-> request {p.hexstr(key)}")
                await self.client.write_gatt_char(p.REQUEST_UUID, key, response=False)
                return await asyncio.wait_for(future, timeout)
            except (asyncio.TimeoutError, WatchError) as exc:
                if optional:
                    log.debug("optional register %s unavailable: %s", p.hexstr(key), exc)
                    return None
                if isinstance(exc, asyncio.TimeoutError):
                    raise WatchError(f"no answer from watch for register {p.hexstr(key)}") from None
                raise
            finally:
                self._pending = [(k, f) for k, f in self._pending if f is not future]

    async def write(self, data: bytes) -> None:
        self._trace(f"-> write   {p.hexstr(data)}")
        await self.client.write_gatt_char(p.ALL_FEATURES_UUID, bytes(data), response=True)

    # ---------------------------------------------------------------- step log
    async def read_step_log(self, timeout: float = 20.0) -> bytes:
        loop = asyncio.get_running_loop()
        self._step_buffer, self._step_expected = bytearray(), None
        self._step_done = loop.create_future()
        try:
            self._trace(f"-> step log start {p.hexstr(p.STEP_LOG_START)}")
            await self.client.write_gatt_char(p.DATA_REQUEST_UUID, p.STEP_LOG_START, response=True)
            return await asyncio.wait_for(self._step_done, timeout)
        except asyncio.TimeoutError:
            raise WatchError("timed out waiting for the step log") from None
        finally:
            self._step_buffer, self._step_done = None, None
            try:
                await self.client.write_gatt_char(p.DATA_REQUEST_UUID, p.STEP_LOG_END, response=True)
            except Exception as exc:  # noqa: BLE001 - best effort
                log.debug("step log end failed: %s", exc)

    # ---------------------------------------------------------------- notifications
    def _on_notify(self, sender: Any, data: bytearray) -> None:
        uuid = str(getattr(sender, "uuid", sender)).lower()
        data = bytes(data)
        self._trace(f"<- {uuid[4:8]}    {p.hexstr(data)}")
        if uuid == p.ALL_FEATURES_UUID:
            self._on_feature(data)
        elif uuid == p.DATA_REQUEST_UUID:
            length = p.parse_step_announcement(data)
            if length is not None and self._step_buffer is not None:
                self._step_expected = length
                self._maybe_finish_step_log()
        elif uuid == p.CONVOY_UUID and self._step_buffer is not None:
            self._step_buffer.extend(data)
            self._maybe_finish_step_log()

    def _on_feature(self, data: bytes) -> None:
        if not data:
            return
        failed = p.parse_error(data)
        if failed is not None:
            for key, future in self._pending:
                if key[0] == failed and not future.done():
                    future.set_exception(WatchError(f"watch rejected register 0x{failed:02X} ({p.hexstr(data)})"))
                    return
            log.warning("watch reported an error: %s", p.hexstr(data))
            return
        for key, future in self._pending:
            if data.startswith(key) and not future.done():
                future.set_result(data)
                return
        log.debug("unsolicited notification: %s", p.hexstr(data))

    def _maybe_finish_step_log(self) -> None:
        expected = self._step_expected or p.STEP_LOG_FALLBACK_LENGTH
        if self._step_buffer is not None and len(self._step_buffer) >= expected:
            if self._step_done is not None and not self._step_done.done():
                self._step_done.set_result(bytes(self._step_buffer[:expected]))

    def _fail_all(self, exc: Exception) -> None:
        for _, future in self._pending:
            if not future.done():
                future.set_exception(exc)
        if self._step_done is not None and not self._step_done.done():
            self._step_done.set_exception(exc)

    def _trace(self, message: str) -> None:
        if self.on_trace:
            self.on_trace(message)
