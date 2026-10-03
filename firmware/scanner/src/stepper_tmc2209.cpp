#include "stepper_tmc2209.h"
#include "pan_motion.h"
#include <math.h>
#include <esp_timer.h>
#include <esp_rom_sys.h>
#include <hal/gpio_ll.h>
#include <soc/gpio_struct.h>
#include <Preferences.h>

namespace {
const char *ReferenceNamespace = "ars_pan";
const char *ReferenceKey = "pose";
bool writeReference(const PanReference::Snapshot &snapshot) {
    // Called only by the sensor task with pulses disabled; never from the ISR.
    Preferences prefs;
    if (!prefs.begin(ReferenceNamespace, false)) return false;
    bool saved = prefs.putBytes(ReferenceKey, &snapshot, sizeof(snapshot)) == sizeof(snapshot);
    prefs.end();
    return saved;
}
}

StepperController *StepperController::instance = nullptr;
StepperController::StepperController()
    : currentStep(0), direction(1), scanMode(0), currentRpm(2.0f) {}

void StepperController::begin() {
    pinMode(STEPPER_STEP_PIN, OUTPUT);
    pinMode(STEPPER_DIR_PIN, OUTPUT);
    digitalWrite(STEPPER_STEP_PIN, LOW);
    digitalWrite(STEPPER_DIR_PIN, HIGH); // Positive steps; physical direction given by STEPPER_PAN_SIGN.
    setSpeedRpm(2.0f); // Velocidade padrão inicial suave (2.0 RPM)
    instance = this;
    Preferences prefs;
    if (prefs.begin(ReferenceNamespace, false)) {
        size_t length = prefs.isKey(ReferenceKey) ? prefs.getBytesLength(ReferenceKey) : 0;
        PanReference::Snapshot saved = {};
        bool malformed = length != 0 && (length != sizeof(saved) ||
            prefs.getBytes(ReferenceKey, &saved, sizeof(saved)) != sizeof(saved));
        currentStep = reference.restore(length ? &saved : nullptr, malformed);
        prefs.end();
    } else reference.storageError();
    history.push(micros(), currentStep);
    timer = timerBegin(0, 80, true);
    timerAttachInterrupt(timer, &StepperController::onTimer, true);
    timerAlarmWrite(timer, 10000, true);
    timerAlarmDisable(timer);
    Serial.printf("[PAN] %d pulsos/volta; micro=%d reducao=%d; referencia=%s passos=%d, sem encoder.\n",
                  STEPS_PER_REV, STEPPER_MICROSTEPS, STEPPER_GEAR_RATIO,
                  PanReference::stateName(reference.state(false)), currentStep);
}

void StepperController::setSpeedRpm(float rpm) {
    if (!isfinite(rpm)) return;
    if (rpm <= 0.1f) rpm = 0.1f;
    if (rpm > 10.0f) rpm = 10.0f;
    currentRpm = rpm;
}

float StepperController::getSpeedRpm() {
    return currentRpm;
}

void StepperController::setDirection(int dir) {
    portENTER_CRITICAL(&mux);
    direction = (dir >= 0) ? 1 : -1;
    digitalWrite(STEPPER_DIR_PIN, (direction > 0) ? HIGH : LOW);
    portEXIT_CRITICAL(&mux);
}

void StepperController::setScanMode(int mode) {
    if (mode != 0 && mode != 1) return;
    portENTER_CRITICAL(&mux);
    scanMode = mode;
    if (!parking && mode == 1 && currentStep >= STEPS_PER_REV / 2) {
        direction = -1;
        digitalWrite(STEPPER_DIR_PIN, LOW);
    }
    portEXIT_CRITICAL(&mux);
}

int StepperController::getScanMode() {
    return scanMode;
}

void IRAM_ATTR StepperController::onTimer() {
    StepperController &s = *instance;
    portENTER_CRITICAL_ISR(&s.mux);
    // Arrival at zero only gates pulses in ISR; Arduino timer calls stay in task context.
    if (!s.running) { portEXIT_CRITICAL_ISR(&s.mux); return; }
    // ISR path must not call flash-resident Arduino digitalWrite/micros wrappers.
    gpio_ll_set_level(&GPIO, (gpio_num_t)STEPPER_STEP_PIN, 1);
    uint32_t stepUs = (uint32_t)esp_timer_get_time();
    esp_rom_delay_us(2);
    gpio_ll_set_level(&GPIO, (gpio_num_t)STEPPER_STEP_PIN, 0);
    advancePan(s.currentStep, s.direction, s.running, s.parking,
        s.resumeDirection, s.scanMode, STEPS_PER_REV);
    s.history.push(stepUs, s.currentStep);
    gpio_ll_set_level(&GPIO, (gpio_num_t)STEPPER_DIR_PIN, s.direction > 0 ? 1 : 0);
    portEXIT_CRITICAL_ISR(&s.mux);
}

bool StepperController::setRunning(bool active) {
    if (!timer) return false;
    if (!active) {
        timerAlarmDisable(timer);
        portENTER_CRITICAL(&mux);
        running = false;
        if (parking) {
            direction = scanMode == 1 && currentStep >= STEPS_PER_REV/2 ? -1 : resumeDirection;
            digitalWrite(STEPPER_DIR_PIN, direction > 0 ? HIGH : LOW);
        }
        parking = false;
        portEXIT_CRITICAL(&mux);
        return saveStoppedReference();
    }
    portENTER_CRITICAL(&mux);
    bool alreadyRunning = running;
    int step = currentStep;
    portEXIT_CRITICAL(&mux);
    if (alreadyRunning) return true;
    if (!reference.valid()) return false;
    // Commit the dirty record before enabling any pulse. A failed commit keeps
    // the motor stopped so a previous clean position cannot be restored falsely.
    if (!reference.beforeMotion(step, writeReference)) {
        Serial.println("[PAN] Movimento recusado: falha ao salvar checkpoint dirty.");
        return false;
    }
    persistenceAttempted = false;
    runningRpm = 0.0f;
    lastRampUs = micros();
    if (active) {
        portENTER_CRITICAL(&mux);
        history.clear();
        history.push(lastRampUs, currentStep);
        running = true;
        portEXIT_CRITICAL(&mux);
        runningRpm = 0.1f;
        timerAlarmWrite(timer, (uint64_t)(60000000.0f/(STEPS_PER_REV*runningRpm)), true);
        timerWrite(timer, 0);
        timerAlarmEnable(timer);
    }
    return true;
}

bool StepperController::saveStoppedReference() {
    if (!reference.isDirty()) return reference.state(false) != PanReference::State::StorageError;
    uint32_t now = millis();
    if (persistenceAttempted && now-lastPersistenceAttemptMs < 1000U)
        return reference.state(false) != PanReference::State::StorageError;
    lastPersistenceAttemptMs = now;
    persistenceAttempted = true;
    portENTER_CRITICAL(&mux);
    int step = currentStep;
    bool active = running;
    portEXIT_CRITICAL(&mux);
    if (active) return false;
    bool saved = reference.stopped(step, writeReference);
    if (!saved) Serial.println("[PAN] Falha ao salvar parada; referencia nao restauravel ate novo checkpoint.");
    return saved;
}

void StepperController::update() {
    portENTER_CRITICAL(&mux);
    bool active = running;
    portEXIT_CRITICAL(&mux);
    if (!active) {
        // Parking reaches zero in the ISR. Disable its timer and save the final
        // position here, after the ISR has stopped pulses.
        timerAlarmDisable(timer);
        saveStoppedReference();
        return;
    }
    uint32_t now = micros();
    float dt = (uint32_t)(now-lastRampUs)*1e-6f;
    if (dt < 0.01f) return;
    lastRampUs = now;
    // 1 output RPM/s ramp; only the hardware timer emits STEP pulses.
    float delta = fminf(dt, 0.05f);
    runningRpm += fmaxf(-delta, fminf(delta, currentRpm-runningRpm));
    timerAlarmWrite(timer, (uint64_t)(60000000.0f/(STEPS_PER_REV*runningRpm)), true);
}

float StepperController::getCurrentAngle() {
    portENTER_CRITICAL(&mux);
    int step = currentStep;
    portEXIT_CRITICAL(&mux);
    return STEPPER_ZERO_DEG + STEPPER_PAN_SIGN*step*360.0f/STEPS_PER_REV;
}

bool StepperController::getAngleAt(uint32_t sampleUs, float &angle) {
    int32_t step;
    portENTER_CRITICAL(&mux);
    bool valid = history.at(sampleUs, step);
    portEXIT_CRITICAL(&mux);
    if (valid) angle = STEPPER_ZERO_DEG + STEPPER_PAN_SIGN*step*360.0f/STEPS_PER_REV;
    return valid;
}
bool StepperController::confirmPhysicalZero() {
    if (!timer) return false;
    portENTER_CRITICAL(&mux);
    bool active = running || parking;
    portEXIT_CRITICAL(&mux);
    if (active) return false;
    timerAlarmDisable(timer);
    if (!reference.confirmZero(writeReference)) {
        Serial.println("[PAN] Zero fisico nao confirmado: falha NVS.");
        return false;
    }
    portENTER_CRITICAL(&mux);
    currentStep = 0;
    history.clear();
    history.push(micros(), 0);
    portEXIT_CRITICAL(&mux);
    persistenceAttempted = false;
    Serial.println("[PAN] Zero fisico confirmado pelo operador e salvo.");
    return true;
}
bool StepperController::park() {
    if (!reference.valid() || !timer) return false;
    if (!setRunning(false)) return false;
    portENTER_CRITICAL(&mux);
    bool needsMove = currentStep != 0;
    if (needsMove) {
        resumeDirection = direction;
        direction = panDirectionToZero(currentStep, STEPS_PER_REV);
        digitalWrite(STEPPER_DIR_PIN, direction > 0 ? HIGH : LOW);
        parking = true;
    }
    portEXIT_CRITICAL(&mux);
    if (needsMove && !setRunning(true)) {
        portENTER_CRITICAL(&mux);
        parking = false;
        portEXIT_CRITICAL(&mux);
        return false;
    }
    return true;
}
bool StepperController::isParking() {
    portENTER_CRITICAL(&mux);
    bool result = parking;
    portEXIT_CRITICAL(&mux);
    return result;
}

PanReferenceStatus StepperController::getPanReferenceStatus() {
    portENTER_CRITICAL(&mux);
    bool active = running;
    int step = currentStep;
    portEXIT_CRITICAL(&mux);
    return {reference.valid(), reference.wasRestored(), reference.isDirty(), active,
        step, PanReference::stateName(reference.state(active))};
}
