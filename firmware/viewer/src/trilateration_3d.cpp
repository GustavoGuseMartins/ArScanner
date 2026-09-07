#include "trilateration_3d.h"
#include <math.h>

Trilateration3D::Trilateration3D() {}

// Variáveis internas do Filtro de Kalman / Suavização Temporal (EKF Simplificado)
static float kX = 0.0f, kY = 0.0f, kZ = 1.0f;
static float pVar = 0.1f;
static const float qNoise = 0.005f; // Ruído do processo
static const float rNoise = 0.02f;  // Ruído da medição ToF

bool Trilateration3D::calculatePosition(float d1, float d2, float d3, Vector3D &outPos) {
    // Rejeição de Outliers: validação por desigualdade triangular
    if (d1 <= 0.05f || d2 <= 0.05f || d3 <= 0.05f || d1 > 35.0f || d2 > 35.0f || d3 > 35.0f) {
        return false;
    }

    // Âncoras físicas no plano de referência: P1=(0,0,0), P2=(x2,0,0), P3=(x3,y3,0)
    float x2 = ANCHOR_2_POS.x;
    float x3 = ANCHOR_3_POS.x;
    float y3 = ANCHOR_3_POS.y;

    if (fabsf(x2) < 1e-4f || fabsf(y3) < 1e-4f) return false;

    // 1. Solução Analítica Exata (Interseção de Esferas)
    float x = (d1 * d1 - d2 * d2 + x2 * x2) / (2.0f * x2);
    float y = (d1 * d1 - d3 * d3 + x3 * x3 + y3 * y3 - 2.0f * x3 * x) / (2.0f * y3);

    float zSquared = d1 * d1 - x * x - y * y;
    float z = (zSquared > 0.0f) ? sqrtf(zSquared) : 0.0f;

    // 2. Filtro de Estimação de Estado (EKF / Kalman 1D por eixo)
    // Predição
    pVar += qNoise;

    // Atualização de Ganho
    float kGain = pVar / (pVar + rNoise);
    kX += kGain * (x - kX);
    kY += kGain * (y - kY);
    kZ += kGain * (z - kZ);
    pVar *= (1.0f - kGain);

    outPos.x = kX;
    outPos.y = kY;
    outPos.z = kZ;

    return true;
}
