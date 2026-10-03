#include "pan_reference.h"
#include "pan_motion.h"
#include "control_command_parser.h"
#include <cassert>
#include <cstdio>

using namespace PanReference;
static const Calibration calibration{1, 28800, -1000, 0};

struct MemoryNvs {
    Snapshot saved{};
    bool exists = false, fail = false;
    unsigned writes = 0;
    bool operator()(const Snapshot &snapshot) {
        ++writes;
        if (fail) return false;
        saved = snapshot;
        exists = true;
        return true;
    }
};

static void cleanAndInterruptedRestarts() {
    MemoryNvs nvs;
    auto write = [&nvs](const Snapshot &s) { return nvs(s); };
    Tracker first(calibration);
    assert(first.restore(nullptr) == 0 && !first.valid() && !first.wasRestored());
    assert(first.state(false) == State::Unreferenced);
    assert(first.stopped(0, write) && nvs.writes == 0);
    assert(first.confirmZero(write) && first.valid() && !first.isDirty());
    assert(nvs.saved.step == 0 && nvs.saved.flags == (KnownFlag | CleanFlag));

    // A stopped nonzero pan is recovered without declaring a new zero.
    assert(first.beforeMotion(0, write) && first.valid() && first.isDirty());
    assert(first.state(true) == State::Moving);
    assert(nvs.saved.flags == KnownFlag);
    assert(first.stopped(7312, write) && !first.isDirty());
    Tracker restart(calibration);
    assert(restart.restore(&nvs.saved) == 7312);
    assert(restart.valid() && restart.wasRestored() && !restart.isDirty());
    assert(restart.state(false) == State::Restored);
    unsigned previousWrites = nvs.writes;
    assert(restart.stopped(7312, write) && nvs.writes == previousWrites);

    // Power loss after checkpointing but before, during, or after pulses must
    // never recover the pre-motion counter as a known shaft position.
    assert(restart.beforeMotion(7312, write));
    Tracker interrupted(calibration);
    assert(interrupted.restore(&nvs.saved) == 0);
    assert(!interrupted.valid() && !interrupted.wasRestored() && interrupted.isDirty());
    assert(interrupted.state(false) == State::Interrupted);
    previousWrites = nvs.writes;
    assert(interrupted.stopped(0, write) && nvs.writes == previousWrites);
    assert((nvs.saved.flags & CleanFlag) == 0);
    assert(interrupted.confirmZero(write) && interrupted.valid() && !interrupted.isDirty());
    assert(!interrupted.wasRestored() && interrupted.state(false) == State::Confirmed);
}

static void failedWrites() {
    MemoryNvs nvs;
    auto write = [&nvs](const Snapshot &s) { return nvs(s); };
    Tracker tracker(calibration);
    nvs.fail = true;
    assert(!tracker.confirmZero(write) && !tracker.valid());
    assert(tracker.state(false) == State::StorageError);
    nvs.fail = false;
    assert(tracker.confirmZero(write));
    Snapshot previousClean = nvs.saved;
    nvs.fail = true;
    assert(!tracker.beforeMotion(0, write));
    assert(!tracker.valid() && !tracker.isDirty());
    assert(nvs.saved.checksum == previousClean.checksum);
    // The caller must keep pulses disabled when beforeMotion returns false.
    nvs.fail = false;
    assert(tracker.beforeMotion(0, write) && tracker.valid() && tracker.isDirty());
    Snapshot dirty = nvs.saved;
    nvs.fail = true;
    assert(!tracker.stopped(42, write) && !tracker.valid() && tracker.isDirty());
    assert(nvs.saved.checksum == dirty.checksum);
    Tracker powerCut(calibration);
    assert(powerCut.restore(&nvs.saved) == 0 && !powerCut.valid());
    nvs.fail = false;
    assert(tracker.stopped(42, write) && tracker.valid() && !tracker.isDirty());
    Tracker clean(calibration);
    assert(clean.restore(&nvs.saved) == 42 && clean.valid());
}

static void incompatibleRecords() {
    MemoryNvs nvs;
    Tracker first(calibration);
    assert(first.confirmZero([&nvs](const Snapshot &s) { return nvs(s); }));
    Snapshot saved = nvs.saved;
    Tracker malformed(calibration);
    assert(malformed.restore(nullptr, true) == 0 && !malformed.valid());
    assert(malformed.state(false) == State::InvalidSnapshot);
    saved.step = 2; // Simulated corrupt/torn data with its old checksum.
    Tracker corrupted(calibration);
    assert(corrupted.restore(&saved) == 0 && !corrupted.valid());
    assert(corrupted.state(false) == State::InvalidSnapshot);
    saved = nvs.saved;
    saved.step = 28800;
    saved.checksum = checksum(saved);
    Tracker outside(calibration);
    assert(outside.restore(&saved) == 0 && outside.state(false) == State::InvalidSnapshot);
    const Calibration changed[] = {{2,28800,-1000,0}, {1,14400,-1000,0},
        {1,28800,1000,0}, {1,28800,-1000,90000}};
    for (const auto &c : changed) {
        Tracker mismatch(c);
        assert(mismatch.restore(&nvs.saved) == 0 && !mismatch.valid());
        assert(mismatch.state(false) == State::CalibrationChanged);
    }
}

static void parkKeepsReference() {
    MemoryNvs nvs;
    auto write = [&nvs](const Snapshot &s) { return nvs(s); };
    Tracker reference(calibration);
    assert(reference.confirmZero(write));
    // Resume a saved angle and return by counted steps; the reference remains
    // the same. Parking must not silently redefine zero at a nonzero position.
    assert(reference.beforeMotion(0, write));
    assert(reference.stopped(28796, write));
    Tracker restored(calibration);
    int step = restored.restore(&nvs.saved);
    int direction = panDirectionToZero(step, 28800);
    int resumeDirection = -1;
    bool running = true, parking = true;
    assert(direction == 1 && restored.beforeMotion(step, write));
    unsigned pulses = 0;
    while (running) {
        advancePan(step,direction,running,parking,resumeDirection,0,28800);
        ++pulses;
        assert(pulses <= 4);
    }
    assert(step == 0 && !parking && pulses == 4);
    assert(restored.stopped(step, write) && restored.valid() && restored.wasRestored());
    assert(restored.state(false) == State::Restored);
    Tracker next(calibration);
    assert(next.restore(&nvs.saved) == 0 && next.valid() && next.wasRestored());
}

static void commandStream() {
    ControlCommandParser parser;
    ControlCommand command;
    assert(parser.feed(0x09,command) && command.type == 0x09);
    assert(!parser.feed(0x04,command));
    assert(!parser.feed(0x00,command));
    assert(!parser.feed(0x00,command));
    assert(!parser.feed(0x00,command));
    assert(parser.feed(0x40,command) && command.type == 0x04);
    assert(parser.feed(0x09,command) && command.type == 0x09);
    assert(parser.feed(0x08,command) && command.type == 0x08);
    assert(parser.feed(0x01,command) && command.type == 0x01);
}

int main() {
    cleanAndInterruptedRestarts();
    failedWrites();
    incompatibleRecords();
    parkKeepsReference();
    commandStream();
    std::puts("pan reference persistence tests passed");
}
