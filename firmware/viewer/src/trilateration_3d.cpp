#include "trilateration_3d.h"
#include <math.h>

Trilateration3D::Trilateration3D() {}

bool Trilateration3D::calculatePosition(float d1, float d2, float d3, Vector3D &outPos) {
    // Algoritmo de Trilateração 3D por Interseção de Esferas
    // Âncoras: P1=(0,0,0), P2=(x2,0,0), P3=(x3,y3,0)
    float x2 = ANCHOR_2_POS.x;
    float x3 = ANCHOR_3_POS.x;
    float y3 = ANCHOR_3_POS.y;

    // Cálculo da coordenada X
    float x = (d1 * d1 - d2 * d2 + x2 * x2) / (2.0f * x2);

    // Cálculo da coordenada Y
    float y = (d1 * d1 - d3 * d3 + x3 * x3 + y3 * y3 - 2.0f * x3 * x) / (2.0f * y3);

    // Cálculo da coordenada Z (altura do drone em relação às âncoras)
    float zSquared = d1 * d1 - x * x - y * y;
    float z = (zSquared > 0.0f) ? sqrt(zSquared) : 0.0f;

    outPos.x = x;
    outPos.y = y;
    outPos.z = z;

    return true;
}
