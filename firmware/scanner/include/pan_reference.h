#pragma once

#include <stdint.h>

// The saved reference describes only head pan relative to the stationary base.
// It contains neither world/AR heading nor a measurement of actual shaft motion.
namespace PanReference {
constexpr uint32_t SnapshotMagic = 0x50414e52U;
constexpr uint32_t SnapshotFormat = 1U;
constexpr uint32_t KnownFlag = 1U;
constexpr uint32_t CleanFlag = 2U;

struct Calibration {
    uint32_t version;
    int32_t stepsPerRevolution;
    int32_t signMilli;
    int32_t zeroMilliDegrees;
};

struct Snapshot {
    uint32_t magic, format, calibrationVersion;
    int32_t stepsPerRevolution, signMilli, zeroMilliDegrees, step;
    uint32_t flags, checksum;
};
static_assert(sizeof(Snapshot) == 36, "Pan checkpoint layout must stay explicit");

inline uint32_t checksum(const Snapshot &s) {
    const uint32_t words[] = {s.magic, s.format, s.calibrationVersion,
        (uint32_t)s.stepsPerRevolution, (uint32_t)s.signMilli,
        (uint32_t)s.zeroMilliDegrees, (uint32_t)s.step, s.flags};
    uint32_t hash = 2166136261U;
    for (uint32_t word : words) {
        for (unsigned byte = 0; byte < 4; ++byte) {
            hash = (hash ^ (word & 0xffU)) * 16777619U;
            word >>= 8;
        }
    }
    return hash;
}

enum class State {
    Unreferenced, Confirmed, Restored, Moving, Interrupted,
    CalibrationChanged, InvalidSnapshot, StorageError
};

inline const char *stateName(State state) {
    switch (state) {
        case State::Confirmed: return "confirmed";
        case State::Restored: return "restored";
        case State::Moving: return "moving";
        case State::Interrupted: return "interrupted";
        case State::CalibrationChanged: return "calibration_changed";
        case State::InvalidSnapshot: return "invalid_snapshot";
        case State::StorageError: return "storage_error";
        default: return "unreferenced";
    }
}

// Writers must commit one complete NVS blob synchronously. A dirty checkpoint
// is committed BEFORE pulses are enabled. A clean checkpoint is only committed
// with the timer stopped. Keeping these transitions independent of Arduino lets
// regression tests simulate power loss and failed writes without hardware.
class Tracker {
public:
    explicit Tracker(Calibration c) : calibration(c) {}

    int32_t restore(const Snapshot *saved, bool malformed = false) {
        known = restored = dirty = storageFailed = false;
        restingState = State::Unreferenced;
        if (malformed) { restingState = State::InvalidSnapshot; return 0; }
        if (!saved) return 0;
        if (saved->magic != SnapshotMagic || saved->format != SnapshotFormat ||
            (saved->flags & ~(KnownFlag | CleanFlag)) != 0 ||
            saved->checksum != checksum(*saved)) {
            restingState = State::InvalidSnapshot;
            return 0;
        }
        if (saved->calibrationVersion != calibration.version ||
            saved->stepsPerRevolution != calibration.stepsPerRevolution ||
            saved->signMilli != calibration.signMilli ||
            saved->zeroMilliDegrees != calibration.zeroMilliDegrees) {
            restingState = State::CalibrationChanged;
            return 0;
        }
        if (saved->step < 0 || saved->step >= calibration.stepsPerRevolution) {
            restingState = State::InvalidSnapshot;
            return 0;
        }
        if ((saved->flags & CleanFlag) == 0) {
            dirty = true;
            restingState = State::Interrupted;
            return 0;
        }
        if ((saved->flags & KnownFlag) == 0) return 0;
        known = restored = true;
        restingState = State::Restored;
        return saved->step;
    }

    bool valid() const { return known && !storageFailed; }
    bool wasRestored() const { return restored; }
    bool isDirty() const { return dirty; }
    void storageError() { storageFailed = true; }
    State state(bool moving) const {
        return storageFailed ? State::StorageError : moving ? State::Moving : restingState;
    }

    template <typename Writer> bool confirmZero(Writer write) {
        // A failed write must not claim that the physical reference was saved.
        if (!write(makeSnapshot(0, true, true))) {
            storageFailed = true;
            return false;
        }
        known = true;
        restored = dirty = storageFailed = false;
        restingState = State::Confirmed;
        return true;
    }

    template <typename Writer> bool beforeMotion(int32_t step, Writer write) {
        // Even after an interrupted boot, write a fresh dirty record before a
        // new motion. An old dirty record can belong to a different reference.
        if (!write(makeSnapshot(step, false, known))) {
            storageFailed = true;
            return false;
        }
        dirty = true;
        storageFailed = false;
        return true;
    }

    template <typename Writer> bool stopped(int32_t step, Writer write) {
        // Interrupted checkpoints stay invalid until the physical zero is
        // confirmed. Merely booting in standby must never clean that record.
        if (!dirty || restingState == State::Interrupted) return !storageFailed;
        if (!write(makeSnapshot(step, true, known))) {
            storageFailed = true;
            return false;
        }
        dirty = storageFailed = false;
        return true;
    }

private:
    Calibration calibration;
    bool known = false, restored = false, dirty = false, storageFailed = false;
    State restingState = State::Unreferenced;

    Snapshot makeSnapshot(int32_t step, bool clean, bool referenceKnown) const {
        Snapshot result = {SnapshotMagic, SnapshotFormat, calibration.version,
            calibration.stepsPerRevolution, calibration.signMilli,
            calibration.zeroMilliDegrees, step,
            (referenceKnown ? KnownFlag : 0U) | (clean ? CleanFlag : 0U), 0U};
        result.checksum = checksum(result);
        return result;
    }
};
}
