#include "stepper_tmc2209.h"

StepperController::StepperController() 
    : currentAngle(0.0f), currentStep(0), direction(1), lastStepTimeUs(0), stepIntervalUs(1000) {}

void StepperController::begin() {
    pinMode(STEPPER_STEP_PIN, OUTPUT);
    pinMode(STEPPER_DIR_PIN, OUTPUT);
    digitalWrite(STEPPER_STEP_PIN, LOW);
    digitalWrite(STEPPER_DIR_PIN, HIGH); // Sentido inicial horário
}

void StepperController::setSpeedRpm(float rpm) {
    if (rpm <= 0) rpm = 1.0f;
    float stepsPerSec = (STEPS_PER_REV * rpm) / 60.0f;
    stepIntervalUs = (unsigned long)(1000000.0f / stepsPerSec);
}

void StepperController::setDirection(int dir) {
    direction = (dir >= 0) ? 1 : -1;
    digitalWrite(STEPPER_DIR_PIN, (direction > 0) ? HIGH : LOW);
}

void StepperController::stepOnce() {
    digitalWrite(STEPPER_STEP_PIN, HIGH);
    delayMicroseconds(2);
    digitalWrite(STEPPER_STEP_PIN, LOW);

    currentStep = (currentStep + direction) % STEPS_PER_REV;
    if (currentStep < 0) currentStep += STEPS_PER_REV;
    currentAngle = ((float)currentStep / (float)STEPS_PER_REV) * 360.0f;
}

void StepperController::update() {
    unsigned long nowUs = micros();
    if (nowUs - lastStepTimeUs >= stepIntervalUs) {
        lastStepTimeUs = nowUs;
        stepOnce();
    }
}

float StepperController::getCurrentAngle() {
    return currentAngle;
}

void StepperController::resetAngle() {
    currentStep = 0;
    currentAngle = 0.0f;
}
