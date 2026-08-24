#ifndef STEPPER_TMC2209_H
#define STEPPER_TMC2209_H

#include "config.h"

class StepperController {
private:
    float currentAngle;
    int currentStep;
    int direction;
    unsigned long lastStepTimeUs;
    unsigned long stepIntervalUs;

public:
    StepperController();
    void begin();
    void setSpeedRpm(float rpm);
    void update();
    float getCurrentAngle();
    void stepOnce();
};

#endif // STEPPER_TMC2209_H
