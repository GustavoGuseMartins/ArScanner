#ifndef TRILATERATION_3D_H
#define TRILATERATION_3D_H

#include "config.h"

class Trilateration3D {
public:
    Trilateration3D();
    static bool calculatePosition(float d1, float d2, float d3, Vector3D &outPos);
};

#endif // TRILATERATION_3D_H
