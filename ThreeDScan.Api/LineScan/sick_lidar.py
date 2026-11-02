"""LMS4000 CoLa B streaming reader. Standard library only, Python 3.8+.

Wire layout: SICK LMS4000 operating instructions 8023198, pp. 90-95.
The only transmitted commands subscribe/unsubscribe to LMDscandata events.
No login, configuration write, laser/motor control or EEPROM save is used.

由 Hank IP_control/sick_lidar.py 搬入，邏輯不改。
"""

import math
import socket
import struct
import threading
import time
from dataclasses import dataclass, replace
from functools import reduce
from operator import xor


MAGIC = b"\x02\x02\x02\x02"
STREAM_ON = b"sEN LMDscandata \x01"
STREAM_OFF = b"sEN LMDscandata \x00"
SCAN_PREFIXES = (b"sSN LMDscandata ", b"sRA LMDscandata ")
HEADER = struct.Struct(">HHIBBHHIIHHHIIH")
CHANNEL = struct.Struct(">5sffiHH")
MAX_PAYLOAD = 1024 * 1024


def inside_roi(point, bounds):
    """Physical XY bounds in mm; invalid measurements never become valid."""
    return point is not None and (bounds is None or
        (bounds[0] <= point[0] <= bounds[1] and bounds[2] <= point[1] <= bounds[3]))


def rectangle_bounds(first, second):
    return (min(first[0], second[0]), max(first[0], second[0]),
            min(first[1], second[1]), max(first[1], second[1]))


def encode_frame(payload):
    return MAGIC + struct.pack(">I", len(payload)) + payload + bytes([reduce(xor, payload, 0)])


def send_subscription(connection, enabled):
    """An event subscription only; callers cannot pass arbitrary commands."""
    if type(enabled) is not bool:
        raise ValueError("Subscription must be True or False")
    connection.sendall(encode_frame(STREAM_ON if enabled else STREAM_OFF))


class FrameDecoder:
    """Handle TCP fragmentation/coalescing and reject invalid XOR checksums."""

    def __init__(self):
        self.buffer = bytearray()
        self.errors = 0

    def feed(self, data):
        self.buffer.extend(data)
        frames = []
        while True:
            start = self.buffer.find(MAGIC)
            if start < 0:
                if len(self.buffer) > 3:
                    del self.buffer[:-3]
                break
            if start:
                del self.buffer[:start]
            if len(self.buffer) < 8:
                break
            size = struct.unpack_from(">I", self.buffer, 4)[0]
            if not 1 <= size <= MAX_PAYLOAD:
                self.errors += 1
                del self.buffer[:4]
                continue
            end = size + 9
            if len(self.buffer) < end:
                break
            payload = bytes(self.buffer[8:end - 1])
            checksum = self.buffer[end - 1]
            del self.buffer[:end]
            if reduce(xor, payload, 0) != checksum:
                self.errors += 1
                continue
            frames.append(payload)
        return frames


@dataclass(frozen=True)
class Scan:
    counter: int
    telegram_counter: int
    frequency_hz: float
    device_status: int
    timestamp_us: int
    angles: tuple
    distances: tuple
    xy: tuple
    angle_corrected: bool
    channels: tuple


class ScanAverager:
    """Running mean of every ray over many scans, to settle the noise down.

    A single scan carries a couple of millimetres of range noise per point,
    which is the same size as the features being measured. Averaging N scans
    divides that by the square root of N. Kept as running totals rather than a
    list of scans so a long capture costs no more memory than one scan.
    """

    def __init__(self):
        self.totals = []
        self.counts = []
        self.frames = 0
        self.latest = None

    def add(self, scan):
        if not self.totals:
            self.totals = [0.0] * len(scan.distances)
            self.counts = [0] * len(scan.distances)
        for index in range(min(len(self.totals), len(scan.distances))):
            value = scan.distances[index]
            if value is not None:
                self.totals[index] += value
                self.counts[index] += 1
        self.frames += 1
        self.latest = scan

    def result(self):
        """The averaged scan, or None when nothing was collected."""
        if self.latest is None or not self.frames:
            return None
        distances, xy = [], []
        for index, angle in enumerate(self.latest.angles):
            seen = self.counts[index] if index < len(self.counts) else 0
            # A ray that reads nothing most of the time is a gap, not a point.
            if seen * 2 < self.frames:
                distances.append(None)
                xy.append(None)
                continue
            mean = self.totals[index] / seen
            radians = math.radians(angle)
            distances.append(mean)
            xy.append((mean * math.cos(radians), mean * math.sin(radians)))
        return replace(self.latest, distances=tuple(distances), xy=tuple(xy))


def scan_header(payload):
    if not payload.startswith(SCAN_PREFIXES):
        raise ValueError("不是 LMDscandata 量測封包")
    if len(payload) < 16 + HEADER.size:
        raise ValueError("量測封包表頭不完整")
    header = HEADER.unpack_from(payload, 16)
    if header[0] != 1:
        raise ValueError("不支援的 LMDscandata 格式版本：{}".format(header[0]))
    return header


def parse_scan(payload):
    """Decode only the latest frame for display; preserve invalid-point gaps.

    DIST1 raw values below 16 are reserved, not millimetres. Apply the wire
    scale/offset, including LMS4000's 0.1 mm scale and ANGL1 correction.
    Optional trailing name/time metadata is not needed for contour display.
    """
    header = scan_header(payload)
    offset = 16 + HEADER.size
    encoder_count = header[-1]
    if encoder_count > 16:
        raise ValueError("不合理的 encoder 數量")
    offset += encoder_count * 6
    channels = {}
    try:
        for width in (2, 1):
            count = struct.unpack_from(">H", payload, offset)[0]
            offset += 2
            if count > 32:
                raise ValueError("不合理的量測通道數量")
            for _ in range(count):
                name, scale, bias, start, step, size = CHANNEL.unpack_from(payload, offset)
                offset += CHANNEL.size
                if size > 16384 or offset + size * width > len(payload):
                    raise ValueError("量測通道資料不完整或過大")
                if not math.isfinite(scale) or not math.isfinite(bias):
                    raise ValueError("量測通道的倍率或偏移值無效")
                values = struct.unpack_from(">{}{}".format(size, "H" if width == 2 else "B"), payload, offset)
                offset += size * width
                name = name.decode("ascii")
                if name in channels:
                    raise ValueError("重複的量測通道：" + name)
                channels[name] = (scale, bias, start, step, values)
    except (struct.error, UnicodeDecodeError) as exc:
        raise ValueError("量測通道格式錯誤：{}".format(exc)) from exc

    if "DIST1" not in channels:
        raise ValueError("設備未輸出 DIST1 距離通道；程式未修改輸出設定")
    scale, bias, start, step, values = channels["DIST1"]
    if scale <= 0 or (len(values) > 1 and step == 0):
        raise ValueError("距離通道的倍率或角度間距無效")
    correction = channels.get("ANGL1")
    quality = channels.get("QLTY1")
    for channel in (correction, quality):
        if channel and (len(channel[4]) != len(values) or channel[2:4] != (start, step)):
            raise ValueError("距離／角度／品質通道的點數或角度範圍不一致")
    angles, distances, xy = [], [], []
    for index, value in enumerate(values):
        angle = (start + index * step) / 10000.0
        if correction:
            angle += (correction[4][index] * correction[0] + correction[1]) / 10000.0
        distance = value * scale + bias
        valid = value >= 16 and distance > 0 and not (quality and quality[4][index] & 0x0F)
        angles.append(angle)
        distances.append(distance if valid else None)
        radians = math.radians(angle)
        xy.append((distance * math.cos(radians), distance * math.sin(radians)) if valid else None)
    return Scan(
        counter=header[6], telegram_counter=header[5], frequency_hz=header[12] / 100.0,
        device_status=header[3] | (header[4] << 8), timestamp_us=header[7],
        angles=tuple(angles), distances=tuple(distances), xy=tuple(xy),
        angle_corrected=bool(correction), channels=tuple(channels),
    )


class StreamReader(threading.Thread):
    """Drain the TCP stream continuously; keep a single latest-frame slot.

    Reception never waits for plotting. The UI can skip intermediate display
    frames while this thread counts every valid scan telegram it receives.
    """

    def __init__(self, ip="192.168.0.1", port=2111):
        super().__init__(name="sick-stream", daemon=True)
        self.ip, self.port = ip, port
        self.stop_event = threading.Event()
        self.lock = threading.Lock()
        self.latest = None
        self.received = 0
        self.received_bytes = 0
        self.bad_frames = 0
        self.scan_gaps = 0
        self.duplicates = 0
        self.reconnections = 0
        self.last_received = 0.0
        self.first_received = 0.0
        self.last_counter = None
        self.frequency_hz = 0.0
        self.state = "尚未連線"
        self.connected = False
        self.recorder = None

    def stop(self):
        self.stop_event.set()

    def set_recorder(self, recorder):
        with self.lock:
            self.recorder = recorder

    def snapshot(self):
        with self.lock:
            return {
                "payload": self.latest, "received": self.received,
                "bytes": self.received_bytes, "bad_frames": self.bad_frames,
                "scan_gaps": self.scan_gaps, "duplicates": self.duplicates,
                "reconnections": self.reconnections, "last_received": self.last_received,
                "first_received": self.first_received, "frequency_hz": self.frequency_hz,
                "state": self.state, "connected": self.connected,
            }

    def set_state(self, text, connected=None):
        with self.lock:
            self.state = text
            if connected is not None:
                self.connected = connected

    def accept_payload(self, payload):
        if not payload.startswith(SCAN_PREFIXES):
            if payload.startswith(b"sFA"):
                raise ValueError("設備拒絕訂閱，錯誤回覆：" + payload.hex(" "))
            if payload.startswith(b"sEA LMDscandata ") and payload[16:] != b"\x01":
                raise ValueError("設備未接受連續資料訂閱")
            return
        header = scan_header(payload)
        now = time.monotonic()
        with self.lock:
            if self.last_counter is not None:
                delta = (header[6] - self.last_counter) & 0xFFFF
                if delta == 0:
                    self.duplicates += 1
                elif delta < 0x8000:
                    self.scan_gaps += delta - 1
            self.last_counter = header[6]
            self.latest = payload
            self.received += 1
            self.last_received = now
            if not self.first_received:
                self.first_received = now
            self.frequency_hz = header[12] / 100.0
            self.state = "持續接收中" if not (header[3] or header[4]) else "設備回報異常狀態"
            recorder = self.recorder
        if recorder is not None:
            recorder.submit(payload, now)

    def run(self):
        attempts = 0
        while not self.stop_event.is_set():
            connection = None
            subscribed = False
            self.set_state("連線中 {}:{}".format(self.ip, self.port), False)
            with self.lock:
                self.latest = None
                self.last_counter = None
            decoder = FrameDecoder()
            accounted_errors = 0
            try:
                connection = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                connection.settimeout(2.0)
                connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                connection.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1024 * 1024)
                connection.connect((self.ip, self.port))
                connection.settimeout(0.25)
                if self.stop_event.is_set():
                    break
                send_subscription(connection, True)
                subscribed = True
                self.set_state("已連線，等待量測資料", True)
                if attempts:
                    with self.lock:
                        self.reconnections += 1
                began = last_data = time.monotonic()
                while not self.stop_event.is_set():
                    try:
                        data = connection.recv(65536)
                    except socket.timeout:
                        if time.monotonic() - last_data > 3:
                            self.set_state("等待量測資料；請確認設備正在量測，或是否有其他軟體登入")
                        if time.monotonic() - last_data > 10:
                            raise TimeoutError("10 秒未收到資料，重新連線")
                        continue
                    if not data:
                        raise ConnectionError("對方已關閉連線")
                    last_data = time.monotonic()
                    with self.lock:
                        self.received_bytes += len(data)
                    for payload in decoder.feed(data):
                        self.accept_payload(payload)
                    if decoder.errors != accounted_errors:
                        with self.lock:
                            self.bad_frames += decoder.errors - accounted_errors
                        accounted_errors = decoder.errors
                    if time.monotonic() - began > 3 and self.snapshot()["payload"] is None:
                        self.set_state("已連線但未收到有效量測；請確認使用 CoLa B 連接埠")
            except (OSError, ValueError) as exc:
                self.set_state("連線中斷：{}；1 秒後重試".format(exc), False)
            finally:
                if connection is not None:
                    if subscribed:
                        try:
                            connection.settimeout(0.25)
                            send_subscription(connection, False)
                        except OSError:
                            pass
                    connection.close()
                with self.lock:
                    self.connected = False
            attempts += 1
            if self.stop_event.wait(1.0):
                break
        self.set_state("已停止接收", False)
