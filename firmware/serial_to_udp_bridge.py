"""Forward framed viewer serial records to Unity UDP, ignoring boot/debug logs.
Usage: python firmware/serial_to_udp_bridge.py COM3 --host 127.0.0.1
For a phone, use --host <phone-Wi-Fi-IP> on the same network.
Requires the new viewer firmware. This PC bridge is not an Android USB driver.
"""
import argparse
import json
import math
import socket
import struct


def decode_line(line):
    line = line.strip()
    try:
        if line.startswith(b"@UWB28:") and len(line) == 63:
            data = bytes.fromhex(line[7:].decode("ascii"))
            values = struct.unpack("<6fI", data)
            if all(math.isfinite(v) for v in values[:6]) and all(v >= 0 for v in values[3:6]):
                return data
        elif line.startswith(b'{') and len(line) <= 1024:
            status = json.loads(line)
            if status.get("kind") == "uwb_status" and status.get("version") in (1, 2):
                return line
    except (ValueError, UnicodeError, struct.error, AttributeError):
        pass
    return None


class LineDecoder:
    def __init__(self):
        self.buffer = bytearray()
        self.discard = False

    def feed(self, chunk):
        for byte in chunk:
            if byte == 10:
                if not self.discard:
                    packet = decode_line(bytes(self.buffer))
                    if packet is not None:
                        yield packet
                self.buffer.clear()
                self.discard = False
            elif not self.discard:
                self.buffer.append(byte)
                if len(self.buffer) > 1024:
                    self.buffer.clear()
                    self.discard = True


def main():
    import serial
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("port")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--udp-port", type=int, default=9999)
    args = parser.parse_args()
    decoder = LineDecoder()
    with serial.Serial(args.port, 115200, timeout=.2) as source, socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        print(f"Serial {args.port} -> UDP {args.host}:{args.udp_port}")
        try:
            while True:
                for packet in decoder.feed(source.read(512)):
                    sock.sendto(packet, (args.host, args.udp_port))
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
