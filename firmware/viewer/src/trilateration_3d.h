#ifndef TRILATERATION_3D_H
#define TRILATERATION_3D_H

#include "config.h"

class Trilateration3D {
public:
    Trilateration3D();
    bool calculatePosition(float d1, float d2, float d3, Vector3D &outPos);
    void reset();
    float estimatedPositionSigma = 0.0f;

private:
    Vector3D filtered;
    float variance;
    bool initialized;
};

#endif // TRILATERATION_3D_H
