#pragma once
#include <stdint.h>
#include <map>
#include <string>
class Preferences {
    std::string namespaceName;
    static std::map<std::string, int> &values() {
        static std::map<std::string, int> saved;
        return saved;
    }
    std::string key(const char *name) const { return namespaceName + "/" + name; }
public:
    static void clear() { values().clear(); }
    bool begin(const char *name, bool) { namespaceName = name; return true; }
    bool isKey(const char *name) { return values().count(key(name)) != 0; }
    int getInt(const char *name, int fallback) {
        auto found = values().find(key(name));
        return found == values().end() ? fallback : found->second;
    }
    unsigned putInt(const char *name, int value) {
        values()[key(name)] = value;
        return sizeof(int32_t);
    }
    void end() {}
};
