#include "thermal_mlx90640.h"
#include "scan_geometry.h"
#include <Preferences.h>
#include <math.h>
#include <string.h>

static const uint32_t MAX_FRAME_AGE_MS = ThermalAcquisition::MaxFrameAgeMs;

ThermalSensor::ThermalSensor() {}

void ThermalSensor::loadOrientationProfile() {
    Preferences prefs;
    // Create the namespace on first boot; a missing saved profile uses the default.
    if (!prefs.begin("arscanner", false)) return;
    int saved = prefs.isKey("thermal_dir")
        ? prefs.getInt("thermal_dir", THERMAL_ORIENTATION_PROFILE_DEFAULT)
        : THERMAL_ORIENTATION_PROFILE_DEFAULT;
    prefs.end();
    if (saved >= 0 && saved <= 3)
        orientationProfile = (uint8_t)saved;
    // Frame rate is fixed at 8 fps; a legacy thermal_fps preference has no effect.
    Serial.printf("[TERMICA] Perfil de orientacao: %u (-X/+X, espelho horizontal).\n",
                  (unsigned)orientationProfile);
}

bool ThermalSensor::saveOrientationProfile(uint8_t profile) {
    if (!ScanGeometry::validThermalOrientation(profile)) return false;
    if (profile == orientationProfile) return true;
    Preferences prefs;
    if (!prefs.begin("arscanner", false)) return false;
    bool saved = prefs.putInt("thermal_dir", profile) == sizeof(int32_t);
    prefs.end();
    if (saved) orientationProfile = profile;
    return saved;
}

bool ThermalSensor::begin() {
    discardPartial();
    portENTER_CRITICAL(&frameMux);
    initialized = false;
    frameValid = false;
    lastError = -100;
    diagnostic.state = "not_initialized";
    diagnostic.lastSubpage = -1;
    diagnostic.partialCalibration = false;
    diagnostic.maskedPixels = 0;
    diagnostic.calibrationWarning = 0;
    portEXIT_CRITICAL(&frameMux);
    // A failed EEPROM/configuration transfer can prevent runtime fallback from
    // ever becoming reachable. Try once at the current speed, then at most once
    // at 100 kHz. Keep the slower settings for the externally backed-off retry.
    for (uint8_t attempt = 0; attempt < 2; ++attempt) {
        Wire.setClock(i2cHz); // The probe must use the same speed as initialization.
        portENTER_CRITICAL(&frameMux);
        diagnostic.refreshHz = refreshHz;
        diagnostic.requestedFrameRateHz = requestedFullFps;
        diagnostic.targetFrameRateHz = refreshHz / 2;
        diagnostic.i2cHz = i2cHz;
        portEXIT_CRITICAL(&frameMux);
        Wire.beginTransmission(0x33);
        int error = Wire.endTransmission() == 0 ? mlx.begin(Wire, refreshCode()) : -102;
        int rawError = error == -102 ? -1 : mlx.rawError();
        if (!error) break;
        // These wrappers can report a failed read while loading/calibration
        // verification/configuration. A differing EEPROM or register readback
        // (-2), or invalid calibration coefficients, is NOT a transport retry.
        bool transport = error == -102 ||
            ((error == -103 || error == ThermalAcquisition::InvalidCalibration ||
              error == ThermalAcquisition::InvalidConfiguration) &&
             (rawError == -1 || rawError == ThermalAcquisition::ReadDeadline));
        if (transport) {
            portENTER_CRITICAL(&frameMux);
            ++diagnostic.readErrors;
            portEXIT_CRITICAL(&frameMux);
        }
        if (!transport || i2cHz == I2C_FREQ || attempt == 1) {
            setError(transport ? "i2c_error" :
                error == ThermalAcquisition::InvalidCalibration ? "invalid_calibration" :
                error == ThermalAcquisition::InvalidConfiguration ? "invalid_configuration" : "i2c_error",
                error, rawError);
            Serial.printf("[TERMICA] Inicializacao recusada: erro=%d raw=%d; sem quadro publicado.\n",
                error, rawError);
            return false;
        }
        refreshHz = 4;
        i2cHz = I2C_FREQ;
        Serial.printf("[TERMICA] Transporte falhou na inicializacao: erro=%d raw=%d; uma tentativa a 100 kHz, 4 subpaginas/s.\n",
            error, rawError);
    }
    assembler.setDeadline(pairDeadlineMs());
    failedReads = 0;
    overrunStreak = 0;
    lastSubpageMs = lastTimeoutMs = lastCompleteMs = millis();
    portENTER_CRITICAL(&frameMux);
    lastError = 0;
    diagnostic.rawError = 0;
    diagnostic.state = "waiting_subpage";
    diagnostic.refreshHz = refreshHz;
    diagnostic.requestedFrameRateHz = requestedFullFps;
    diagnostic.targetFrameRateHz = refreshHz / 2;
    diagnostic.i2cHz = i2cHz;
    diagnostic.partialCalibration = mlx.partialCalibration();
    diagnostic.maskedPixels = mlx.maskedPixelCount();
    diagnostic.calibrationWarning = mlx.calibrationWarning();
    initialized = true;
    portEXIT_CRITICAL(&frameMux);
    resetRateWindow();
    Serial.printf("[TERMICA] Aquisicao pronta: %u subpaginas/s (%u quadros/s), solicitado=%u fps, I2C %lu Hz; prazo par=%lu ms; parcial=%d pixels_excluidos=%u aviso_calibracao=%d.\n",
        (unsigned)refreshHz, (unsigned)(refreshHz / 2), (unsigned)requestedFullFps,
        (unsigned long)i2cHz, (unsigned long)pairDeadlineMs(),
        mlx.partialCalibration(), (unsigned)mlx.maskedPixelCount(), mlx.calibrationWarning());
    return true;
}

void ThermalSensor::discardPartial() {
    for (int i = 0; i < 768; i++) nextFrame[i] = NAN;
    portENTER_CRITICAL(&frameMux);
    assembler.reset();
    diagnostic.subpageMask = 0;
    portEXIT_CRITICAL(&frameMux);
}

void ThermalSensor::setError(const char *state, int error, int rawError) {
    portENTER_CRITICAL(&frameMux);
    diagnostic.state = state;
    diagnostic.rawError = rawError;
    lastError = error;
    portEXIT_CRITICAL(&frameMux);
}

bool ThermalSensor::reduceRate(bool busError) {
    if (!busError) return true; // Alvo fixo de 8 quadros completos/s: nunca rebaixa taxa por overruns de RAM.
    if (refreshHz <= 4 && (!busError || i2cHz == I2C_FREQ)) return true;
    refreshHz = busError ? 4 : refreshHz >= 16 ? 8 : 4;
    // A RAM overrun means transfer timing, not proof of an electrical fault.
    // Reduce sensor rate without slowing I2C. Only transport errors slow I2C.
    if (busError) i2cHz = I2C_FREQ;
    Wire.setClock(i2cHz);
    int error = mlx.setRefreshRate(refreshCode());
    discardPartial();
    assembler.setDeadline(pairDeadlineMs());
    portENTER_CRITICAL(&frameMux);
    diagnostic.refreshHz = refreshHz;
    diagnostic.targetFrameRateHz = refreshHz / 2;
    diagnostic.i2cHz = i2cHz;
    portEXIT_CRITICAL(&frameMux);
    resetRateWindow();
    overrunStreak = 0;
    if (error) {
        portENTER_CRITICAL(&frameMux);
        initialized = false;
        portEXIT_CRITICAL(&frameMux);
        setError("i2c_error", error, mlx.rawError());
        return false;
    }
    Serial.printf("[TERMICA] %s: %u subpaginas/s, I2C %lu Hz; quadros parciais descartados.\n",
        busError ? "Erros de transporte I2C" : "Subpagina mudou durante leitura RAM",
        (unsigned)refreshHz, (unsigned long)i2cHz);
    return true;
}

void ThermalSensor::resetRateWindow() {
    portENTER_CRITICAL(&frameMux);
    rateWindowStartedMs = millis();
    rateWindowFrames = frameCount;
    diagnostic.measuredFrameRateHz = 0;
    diagnostic.frameRateWindowMs = 0;
    portEXIT_CRITICAL(&frameMux);
}

bool ThermalSensor::requestFrameRate(uint8_t fullFps) {
    if (fullFps != THERMAL_DEFAULT_FULL_FPS) return false;
    discardPartial();
    if (fullFps == requestedFullFps && refreshHz == fullFps * 2 &&
        i2cHz == I2C_FAST_FREQ && isInitialized()) return true;
    requestedFullFps = fullFps;
    refreshHz = uint8_t(fullFps * 2);
    i2cHz = I2C_FAST_FREQ;
    Wire.setClock(i2cHz);
    discardPartial();
    assembler.setDeadline(pairDeadlineMs());
    portENTER_CRITICAL(&frameMux);
    frameValid = false; // Never present the old-rate snapshot as a new frame.
    diagnostic.refreshHz = refreshHz;
    diagnostic.requestedFrameRateHz = requestedFullFps;
    diagnostic.targetFrameRateHz = fullFps;
    portEXIT_CRITICAL(&frameMux);
    resetRateWindow();
    int error = isInitialized() ? mlx.setRefreshRate(refreshCode()) : 0;
    if (error) {
        portENTER_CRITICAL(&frameMux);
        initialized = false;
        portEXIT_CRITICAL(&frameMux);
        setError("i2c_error", error, mlx.rawError());
        return false;
    }
    failedReads = overrunStreak = 0;
    lastSubpageMs = lastTimeoutMs = lastCompleteMs = millis();
    Serial.printf("[TERMICA] Solicitados %u quadros completos/s (%u subpaginas/s).\n",
        (unsigned)fullFps, (unsigned)refreshHz);
    return true;
}

bool ThermalSensor::updateFrame() {
    if (!isInitialized()) return false;
    uint32_t now = millis();
    // A healthy ACK with no complete image can mean a stalled conversion or
    // repeated single subpage. Recover after a bounded grace period; the Aux
    // task already backs off initialization attempts by two seconds.
    if (uint32_t(now - lastCompleteMs) > 5000) {
        discardPartial();
        portENTER_CRITICAL(&frameMux);
        initialized = false;
        ++diagnostic.frameTimeouts;
        portEXIT_CRITICAL(&frameMux);
        setError("frame_timeout", ThermalAcquisition::FrameTimeout, 0);
        Serial.println("[TERMICA] Sem quadro completo por 5 s; nova inicializacao sera tentada com intervalo de 2 s.");
        return false;
    }
    if (assembler.expired(now)) {
        discardPartial();
        portENTER_CRITICAL(&frameMux);
        ++diagnostic.frameTimeouts;
        portEXIT_CRITICAL(&frameMux);
        lastTimeoutMs = now;
        setError("frame_timeout", ThermalAcquisition::FrameTimeout, 0);
    }
    uint8_t page = 0;
    uint32_t sampleMs = now;
    int error = mlx.readSubpage(nextFrame, page, sampleMs);
    if (error == ThermalAcquisition::NotReady) {
        if (uint32_t(now - lastSubpageMs) > 1000 && uint32_t(now - lastTimeoutMs) > 1000) {
            discardPartial();
            portENTER_CRITICAL(&frameMux);
            ++diagnostic.frameTimeouts;
            portEXIT_CRITICAL(&frameMux);
            lastTimeoutMs = now;
            setError("frame_timeout", ThermalAcquisition::FrameTimeout, 0);
        } else if (lastError == 0) {
            portENTER_CRITICAL(&frameMux);
            diagnostic.state = assembler.mask() ? "assembling" : frameValid ?
                (diagnostic.partialCalibration ? "ready_partial" : "ready") : "waiting_subpage";
            portEXIT_CRITICAL(&frameMux);
        }
        return false;
    }
    portENTER_CRITICAL(&frameMux);
    diagnostic.readDurationMs = mlx.readDurationMs();
    diagnostic.ramReadDurationMs = mlx.ramReadDurationMs();
    diagnostic.rawError = mlx.rawError();
    portEXIT_CRITICAL(&frameMux);
    if (error) {
        discardPartial();
        bool transport = error == -1 || error == -2 || error == ThermalAcquisition::ReadDeadline;
        portENTER_CRITICAL(&frameMux);
        if (transport) ++diagnostic.readErrors;
        else if (error == -8) ++diagnostic.overruns;
        else ++diagnostic.invalidFrames;
        portEXIT_CRITICAL(&frameMux);
        setError(transport ? "i2c_error" : error == -8 ? "frame_overrun" :
            error == ThermalAcquisition::InvalidConfiguration ? "invalid_configuration" : "invalid_frame", error, mlx.rawError());
        if (transport) {
            if (++failedReads >= 3 && (refreshHz > 4 || i2cHz != I2C_FREQ)) reduceRate(true);
            if (failedReads >= 5) {
                portENTER_CRITICAL(&frameMux);
                initialized = false;
                portEXIT_CRITICAL(&frameMux);
            }
        } else if (error == -8) {
            // Mantem fixo em 8 FPS (16 subpaginas/s); descarta a leitura incompleta e continua
            ++overrunStreak;
        } else if (++failedReads >= 5) {
            portENTER_CRITICAL(&frameMux);
            initialized = false;
            portEXIT_CRITICAL(&frameMux);
        }
        return false;
    }
    failedReads = 0;
    overrunStreak = 0;
    lastSubpageMs = sampleMs;
    bool duplicate = false;
    portENTER_CRITICAL(&frameMux);
    bool complete = assembler.accept(page, sampleMs, duplicate);
    diagnostic.subpageMask = assembler.mask();
    diagnostic.lastSubpage = page;
    if (duplicate) ++diagnostic.duplicateSubpages;
    diagnostic.state = "assembling";
    lastError = 0;
    portEXIT_CRITICAL(&frameMux);
    if (!complete) return false;
    mlx.correctBadPixels(nextFrame);
    if (!ThermalAcquisition::FrameAssembler::finiteFrame(nextFrame, mlx.validityMask())) {
        discardPartial();
        portENTER_CRITICAL(&frameMux);
        ++diagnostic.invalidFrames;
        portEXIT_CRITICAL(&frameMux);
        setError("invalid_frame", ThermalAcquisition::InvalidFrame, 0);
        return false;
    }
    // HTTP and fusion see one complete 0+1 pair, published atomically. Use its
    // midpoint timestamp instead of pretending both halves were captured now.
    portENTER_CRITICAL(&frameMux);
    uint32_t timestamp = assembler.timestampMs(), span = assembler.spanMs();
    memcpy(frame, nextFrame, sizeof(frame));
    memcpy(frameValidity, mlx.validityMask(), sizeof(frameValidity));
    frameTimestampMs = timestamp;
    frameValid = true;
    lastError = 0;
    ++frameCount;
    lastCompleteMs = millis();
    uint32_t rateElapsed = uint32_t(lastCompleteMs - rateWindowStartedMs);
    if (rateElapsed >= 2000) {
        diagnostic.measuredFrameRateHz = float(frameCount - rateWindowFrames) * 1000.0f / float(rateElapsed);
        diagnostic.frameRateWindowMs = rateElapsed;
        rateWindowStartedMs = lastCompleteMs;
        rateWindowFrames = frameCount;
    }
    diagnostic.frameSpanMs = span;
    diagnostic.state = diagnostic.partialCalibration ? "ready_partial" : "ready";
    portEXIT_CRITICAL(&frameMux);
    discardPartial();
    return true;
}

bool ThermalSensor::isInitialized() {
    portENTER_CRITICAL(&frameMux);
    bool result = initialized;
    portEXIT_CRITICAL(&frameMux);
    return result;
}

ThermalAcquisitionHealth ThermalSensor::acquisitionHealth() {
    portENTER_CRITICAL(&frameMux);
    ThermalAcquisitionHealth result = diagnostic;
    result.frameReady = frameValid && uint32_t(millis() - frameTimestampMs) <= MAX_FRAME_AGE_MS;
    if (!result.frameReady) result.measuredFrameRateHz = 0;
    if (frameValid && !result.frameReady && lastError == 0) result.state = "stale";
    portEXIT_CRITICAL(&frameMux);
    return result;
}

void ThermalSensor::health(int &error, uint32_t &frames, uint32_t &ageMs) {
    portENTER_CRITICAL(&frameMux);
    error = lastError;
    frames = frameCount;
    ageMs = frameValid ? millis()-frameTimestampMs : UINT32_MAX;
    portEXIT_CRITICAL(&frameMux);
}

float ThermalSensor::getPointTemperature(float angleHorizDeg, float angleVertDeg) {
    float temperatureC = 21.0f; // Compatibilidade do pacote: valor de referência, não medição
    tryGetPointTemperature(angleHorizDeg, angleVertDeg, temperatureC);
    return temperatureC;
}

bool ThermalSensor::tryGetPointTemperature(float angleHorizDeg, float angleVertDeg,
                                            float &temperatureC, uint32_t sampleTimeMs) {
    return tryGetPointTemperatureForFrame(angleHorizDeg, angleVertDeg, temperatureC,
        sampleTimeMs, orientationProfile, false, 0);
}

bool ThermalSensor::tryGetHeadPointTemperature(ScanGeometry::Vec3 headPoint,
        ScanGeometry::Vec3 lidarOrigin, ScanGeometry::Vec3 thermalOffset,
        bool invertLidarY, float &temperatureC, uint32_t sampleTimeMs,
        PanAtTimestamp panAtTimestamp, void *poseContext) {
    if (!panAtTimestamp || sampleTimeMs == UINT32_MAX) return false;
    // Snapshot the exact published image. Looking up pan can take another
    // mutex, so do it outside frameMux and reject a publication race below.
    portENTER_CRITICAL(&frameMux);
    uint32_t timestamp = frameTimestampMs, count = frameCount;
    uint8_t profile = orientationProfile;
    int32_t skewMs = (int32_t)(sampleTimeMs-timestamp);
    bool available = frameValid && uint32_t(millis()-timestamp) <= MAX_FRAME_AGE_MS &&
        skewMs >= -200 && skewMs <= 200;
    portEXIT_CRITICAL(&frameMux);
    if (!available) return false;
    float lidarPan, framePan;
    if (!panAtTimestamp(poseContext, sampleTimeMs, lidarPan) ||
        !panAtTimestamp(poseContext, timestamp, framePan)) return false;
    float horizontal, vertical;
    if (!ScanGeometry::thermalAnglesAtFramePose(headPoint, lidarPan, framePan,
            lidarOrigin, thermalOffset, invertLidarY, horizontal, vertical, profile)) return false;
    return tryGetPointTemperatureForFrame(horizontal, vertical, temperatureC,
        sampleTimeMs, profile, true, count);
}

bool ThermalSensor::tryGetPointTemperatureForFrame(float angleHorizDeg, float angleVertDeg,
        float &temperatureC, uint32_t sampleTimeMs, uint8_t projectionProfile,
        bool requireFrameMatch, uint32_t projectionFrameCount) {
    if (!isfinite(angleHorizDeg) || !isfinite(angleVertDeg)) return false;
    // A correcao de 90 graus a direita troca o FOV nativo de 110 x 75
    // para 75 horizontal x 110 vertical na cabeca. A amostragem segue
    // a mesma rotacao da previa 24 x 32 no aplicativo.
    float col, row;
    if (!ScanGeometry::thermalRawPixelForRightCorrection90(angleHorizDeg, angleVertDeg,
            THERMAL_SENSOR_FOV_X_DEG, THERMAL_SENSOR_FOV_Y_DEG, col, row,
            projectionProfile)) return false;
    if (!isfinite(col) || !isfinite(row)) return false;
    int x0 = constrain((int)floorf(col), 0, 31), y0 = constrain((int)floorf(row), 0, 23);
    int x1 = min(x0+1, 31), y1 = min(y0+1, 23);
    float tx = col-x0, ty = row-y0;
    portENTER_CRITICAL(&frameMux);
    bool available = frameValid && (uint32_t)(millis() - frameTimestampMs) <= MAX_FRAME_AGE_MS &&
        projectionProfile == orientationProfile &&
        (!requireFrameMatch || frameCount == projectionFrameCount);
    if (available && sampleTimeMs != UINT32_MAX) {
        int32_t skewMs = (int32_t)(sampleTimeMs-frameTimestampMs);
        available = skewMs >= -200 && skewMs <= 200;
    }
    if (available) {
        const unsigned pixels[4] = {unsigned(y0*32+x0), unsigned(y0*32+x1),
                                    unsigned(y1*32+x0), unsigned(y1*32+x1)};
        const float weights[4] = {(1.0f-tx)*(1.0f-ty), tx*(1.0f-ty),
                                  (1.0f-tx)*ty, tx*ty};
        float value = 0;
        for (unsigned i = 0; i < 4; ++i) {
            if (weights[i] <= 0) continue; // NaN * 0 is still NaN.
            unsigned pixel = pixels[i];
            if (!(frameValidity[pixel >> 3] & (1U << (pixel & 7U))) || !isfinite(frame[pixel])) {
                available = false;
                break;
            }
            value += frame[pixel] * weights[i];
        }
        if (available && isfinite(value)) temperatureC = value;
        else available = false;
    }
    portEXIT_CRITICAL(&frameMux);
    return available;
}

bool ThermalSensor::getNormalizedFrame(uint8_t *dest, float &minT, float &maxT,
                                       uint8_t *validityMask) {
    if (dest == nullptr) return false;
    float snapshot[768];
    uint8_t validity[96];
    portENTER_CRITICAL(&frameMux);
    bool available = frameValid && (uint32_t)(millis() - frameTimestampMs) <= MAX_FRAME_AGE_MS &&
        (validityMask != nullptr || !diagnostic.partialCalibration);
    if (available) {
        memcpy(snapshot, frame, sizeof(snapshot));
        memcpy(validity, frameValidity, sizeof(validity));
    }
    portEXIT_CRITICAL(&frameMux);
    if (!available) return false;

    minT = INFINITY;
    maxT = -INFINITY;
    for (int i = 0; i < 768; i++) {
        if (!(validity[i >> 3] & (1U << (i & 7)))) continue;
        if (!isfinite(snapshot[i])) return false;
        if (snapshot[i] < minT) minT = snapshot[i];
        if (snapshot[i] > maxT) maxT = snapshot[i];
    }
    if (!isfinite(minT) || !isfinite(maxT)) return false;
    if (maxT <= minT) maxT = minT + 1.0f;
    float range = maxT - minT;
    for (int i = 0; i < 768; i++) {
        if (!(validity[i >> 3] & (1U << (i & 7)))) {
            dest[i] = 0; // No temperature: the mandatory mask marks this byte unusable.
            continue;
        }
        float norm = (snapshot[i] - minT) / range;
        dest[i] = (uint8_t)(constrain(norm * 255.0f, 0.0f, 255.0f));
    }
    if (validityMask) memcpy(validityMask, validity, sizeof(validity));
    return true;
}
