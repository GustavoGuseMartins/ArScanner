import struct
import unittest
from serial_to_udp_bridge import LineDecoder, decode_line


class SerialBridgeTest(unittest.TestCase):
    def test_timestamped_status(self):
        status = b'{"kind":"uwb_status","version":2,"cycleValid":true,"t1Ms":100,"t2Ms":130,"t3Ms":160}'
        self.assertEqual(decode_line(status), status)
        self.assertIsNone(decode_line(b'{"kind":"uwb_status","version":3}'))

    def test_fragments_logs_and_status(self):
        packet = struct.pack('<6fI', 1, 2, 3, 4, 5, 6, 123)
        status = b'{"kind":"uwb_status","version":1,"state":1}'
        stream = b'boot log\r\n@UWB28:' + packet.hex().encode() + b'\r\n[debug] x\n' + status + b'\n'
        for chunk_size in (1, 7, 28, len(stream)):
            decoder = LineDecoder()
            received = []
            for start in range(0,len(stream),chunk_size):
                received.extend(decoder.feed(stream[start:start+chunk_size]))
            self.assertEqual(received, [packet,status])

    def test_invalid_and_oversize(self):
        self.assertIsNone(decode_line(b'@UWB28:'+b'z'*56))
        self.assertIsNone(decode_line(b'@UWB28:'+struct.pack('<6fI',float('nan'),0,0,1,1,1,0).hex().encode()))
        self.assertIsNone(decode_line(b'{invalid json'))
        decoder = LineDecoder()
        self.assertEqual(list(decoder.feed(b'x'*2000+b'\n')), [])
        status = b'{"kind":"uwb_status","version":1}'
        self.assertEqual(list(decoder.feed(status+b'\n')), [status])


if __name__ == '__main__':
    unittest.main()
