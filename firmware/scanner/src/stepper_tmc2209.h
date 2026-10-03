#ifndef STEPPER_TMC2209_H
#define STEPPER_TMC2209_H

#include "config.h"
#include "step_history.h"
#include "pan_reference.h"

struct PanReferenceStatus {
    bool valid, restored, dirty, moving;
    int32_t steps;
    const char *state;
};

class StepperController {
private:
    int currentStep;
    int direction;
    int resumeDirection = 1;
    int scanMode; // 0 = Contínuo 360°, 1 = Setor 180° Ping-Pong
    float currentRpm;
    float runningRpm = 0.0f;
    uint32_t lastRampUs = 0;
    hw_timer_t *timer = nullptr;
    bool running = false;
    bool parking = false;
    portMUX_TYPE mux = portMUX_INITIALIZER_UNLOCKED;
    StepHistory<8192> history;
    PanReference::Tracker reference{ {PAN_REFERENCE_CALIBRATION_VERSION,
        STEPS_PER_REV, (int32_t)(STEPPER_PAN_SIGN * 1000.0f),
        (int32_t)(STEPPER_ZERO_DEG * 1000.0f)} };
    uint32_t lastPersistenceAttemptMs = 0;
    bool persistenceAttempted = false;
    bool saveStoppedReference();
    static StepperController *instance;
    static void IRAM_ATTR onTimer();

public:
    StepperController();
    void begin();
    void setSpeedRpm(float rpm);
    float getSpeedRpm();
    void setDirection(int dir);
    void setScanMode(int mode);
    int getScanMode();
    void update();
    bool setRunning(bool active);
    bool getAngleAt(uint32_t sampleUs, float &angle);
    float getCurrentAngle();
    // Operator confirmation of alignment to a physical mark; does not move.
    bool confirmPhysicalZero();
    bool park();
    bool isParking();
    bool hasPanReference() const { return reference.valid(); }
    PanReferenceStatus getPanReferenceStatus();
};

#endif // STEPPER_TMC2209_H
