"""
Bridge Serial -> UDP para Testes de Hardware no PC
Lê os pacotes binários UwbPositionPacket enviados pelo ESP32 via cabo USB (COM)
e repassa para a porta UDP 9999 consumida pelo Unity.

Uso:
    python serial_to_udp_bridge.py COM3
"""

import sys
import socket
import serial

DEFAULT_PORT = "COM3"
BAUD_RATE = 115200
UDP_IP = "127.0.0.1"
UDP_PORT = 9999
PACKET_SIZE = 28 # sizeof(UwbPositionPacket)

def main():
    port = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_PORT
    print(f"[*] Iniciando Bridge Serial ({port}) -> UDP ({UDP_IP}:{UDP_PORT})...")
    
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    
    try:
        ser = serial.Serial(port, BAUD_RATE, timeout=1)
        print(f"[OK] Conectado à porta serial {port} a {BAUD_RATE} baud.")
    except Exception as e:
        print(f"[ERRO] Não foi possível abrir porta serial {port}: {e}")
        return

    try:
        while True:
            data = ser.read(PACKET_SIZE)
            if len(data) == PACKET_SIZE:
                sock.sendto(data, (UDP_IP, UDP_PORT))
    except KeyboardInterrupt:
        print("\n[*] Encerrando bridge...")
    finally:
        ser.close()
        sock.close()

if __name__ == "__main__":
    main()
