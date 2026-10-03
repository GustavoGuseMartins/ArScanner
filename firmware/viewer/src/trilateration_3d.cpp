#include "trilateration_3d.h"
#include <math.h>
#include "../../common/trilateration_math.h"

Trilateration3D::Trilateration3D() { reset(); }

void Trilateration3D::reset() {
    filtered = {0.0f, 0.0f, 0.0f};
    variance = 0.1f;
    initialized = false;
}

// Suavização escalar independente por eixo, com a mesma variância. Não é um EKF.
static const float qNoise = 0.005f; // Ruído do processo
static const float rNoise = 0.02f;  // Ruído da medição ToF

bool Trilateration3D::calculatePosition(float d1, float d2, float d3, Vector3D &outPos) {
    // Rejeição de Outliers: validação por alcance físico (10cm a 35m)
    if (!isfinite(d1) || !isfinite(d2) || !isfinite(d3) ||
        d1 < 0.08f || d2 < 0.08f || d3 < 0.08f || d1 > 35.0f || d2 > 35.0f || d3 > 35.0f) {
        return false;
    }

    const UwbMath::Vec anchors[] = {
        {ANCHOR_1_POS.x,ANCHOR_1_POS.y,ANCHOR_1_POS.z},
        {ANCHOR_2_POS.x,ANCHOR_2_POS.y,ANCHOR_2_POS.z},
        {ANCHOR_3_POS.x,ANCHOR_3_POS.y,ANCHOR_3_POS.z}};
    const double ranges[] = {d1,d2,d3};
    UwbMath::Vec position;
    double gdop;
    estimatedPositionSigma = INFINITY;
    if (!UwbMath::solve(anchors, ranges, UWB_PLANE_SIDE, position, gdop)) {
        reset();
        return false;
    }
    estimatedPositionSigma = gdop*UWB_RANGE_SIGMA_M;
    if (estimatedPositionSigma > UWB_MAX_POSITION_SIGMA_M) {
        reset();
        return false;
    }
    float xCentered = position.x, y = position.y, z = position.z;

    // 2. Filtro de Estimação de Estado (Kalman 1D por eixo para amortecer ruído ToF)
    if (!initialized) {
        filtered = {xCentered, y, z};
        initialized = true;
    } else {
        variance += qNoise;
        float kGain = variance / (variance + rNoise);
        filtered.x += kGain * (xCentered - filtered.x);
        filtered.y += kGain * (y - filtered.y);
        filtered.z += kGain * (z - filtered.z);
        variance *= (1.0f - kGain);
    }

    outPos = filtered;

    return true;
}
