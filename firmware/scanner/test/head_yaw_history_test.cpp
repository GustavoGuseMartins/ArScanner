#include "../include/head_yaw_history.h"
#include "../src/imu_mpu6050.h"
#include <cassert>
#include <cmath>
#include <cstdio>

static bool near(float a, float b) { return std::fabs(a - b) < .0001f; }

int main() {
    HeadYawHistory<4> history;
    float yaw = 99;
    assert(!history.at(1000, yaw) && yaw == 99);
    history.push(1000, 358);
    history.push(11000, 360);
    history.push(21000, 362);
    assert(!history.at(999, yaw));
    assert(history.at(6000, yaw) && near(yaw, 359));
    assert(history.at(16000, yaw) && near(yaw, 361));
    assert(history.at(21000, yaw) && near(yaw, 362));
    assert(history.at(41000, yaw) && near(yaw, 362));
    assert(!history.at(41001, yaw));
    // Duplicate processing does not replace the acquired orientation.
    history.push(21000, -999);
    assert(history.at(21000, yaw) && near(yaw, 362));
    history.push(31000, 364);
    history.push(41000, 366);
    assert(!history.at(1000, yaw));
    assert(history.at(26000, yaw) && near(yaw, 363));

    // Clock wrap preserves interpolation and a bounded newest-sample hold.
    history.clear();
    history.push(UINT32_MAX - 9999U, -1);
    history.push(10000, 1);
    assert(history.at(0, yaw) && near(yaw, 0));
    assert(history.at(30000, yaw) && near(yaw, 1));
    assert(!history.at(30001, yaw));
    // Gaps, invalid samples and a new reference must not bridge old poses.
    history.push(120001, 5);
    assert(!history.at(20000, yaw));
    history.push(130001, NAN);
    assert(!history.at(130001, yaw));
    history.push(140001, 6);
    history.clear();
    assert(!history.at(140001, yaw));
    history.push(150001, 1);
    history.push(140001, 2);
    assert(history.at(140001, yaw) && near(yaw, 2));
    assert(!history.at(130001, yaw));

    // Textual readiness must work even when the state comes from a different
    // allocation; a pointer comparison would reject this valid snapshot.
    ImuOrientationSnapshot pose;
    char state[] = {'r', 'e', 'a', 'd', 'y', '\0'};
    pose.state = state;
    pose.enabled = pose.referenceValid = true;
    pose.ageMs = 150;
    assert(pose.canApplyYaw());
    pose.ageMs = 151; assert(!pose.canApplyYaw());
    pose.ageMs = 1;
    pose.gaps = 1; assert(!pose.canApplyYaw());
    pose.gaps = 0;
    pose.enabled = false; assert(!pose.canApplyYaw());
    pose.enabled = true;
    pose.referenceValid = false; assert(!pose.canApplyYaw());
    pose.referenceValid = true;
    pose.state = "referenced"; assert(!pose.canApplyYaw());
    pose.state = nullptr; assert(!pose.canApplyYaw());
    puts("Head yaw history and textual orientation readiness: PASS");
}
