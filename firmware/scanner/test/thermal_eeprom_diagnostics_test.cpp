#include "../include/thermal_eeprom_diagnostics.h"
#include <cassert>
#include <cstdio>

int main() {
    uint16_t ee[832] = {}, copy[832] = {};
    auto zero = ThermalEepromDiagnostics::summarize(ee);
    assert(zero.crc32 == 0xBFEF95C0U && zero.broken == 768 && zero.outlier == 0);
    for (unsigned i = 0; i < 832; ++i) ee[i] = copy[i] = uint16_t(i);
    auto sequence = ThermalEepromDiagnostics::summarize(ee);
    // Golden CRC independently generated with Python zlib/struct big-endian.
    assert(sequence.crc32 == 0x243C5ED5U && sequence.broken == 0 && sequence.outlier == 384);
    assert(ThermalEepromDiagnostics::differences(ee, copy) == 0);
    copy[75] = 0;
    auto changed = ThermalEepromDiagnostics::summarize(copy);
    assert(changed.crc32 != sequence.crc32 && changed.broken == 1 && changed.outlier == 383);
    assert(ThermalEepromDiagnostics::differences(ee, copy) == 1);
    puts("Thermal EEPROM diagnostics PASS: all 832 words, canonical CRC32, full pixel-flag counts and exact differences.");
}
