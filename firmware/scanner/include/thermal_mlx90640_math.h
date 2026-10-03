/**
 * @copyright (C) 2017 Melexis N.V.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */
// Calculation-only subset adapted from Adafruit MLX90640 1.1.2 / Melexis.
// Register I/O and scheduling belong to thermal_mlx90640_device.cpp.
#pragma once
#include <stdint.h>
namespace ThermalMlxMath {
typedef struct {
  int16_t kVdd;
  int16_t vdd25;
  float KvPTAT;
  float KtPTAT;
  uint16_t vPTAT25;
  float alphaPTAT;
  int16_t gainEE;
  float tgc;
  float cpKv;
  float cpKta;
  uint8_t resolutionEE;
  uint8_t calibrationModeEE;
  float KsTa;
  float ksTo[5];
  int16_t ct[5];
  uint16_t alpha[768];
  uint8_t alphaScale;
  int16_t offset[768];
  int8_t kta[768];
  uint8_t ktaScale;
  int8_t kv[768];
  uint8_t kvScale;
  float cpAlpha[2];
  int16_t cpOffset[2];
  float ilChessC[3];
  uint16_t brokenPixels[5];
  uint16_t outlierPixels[5];
} paramsMLX90640;
int MLX90640_ExtractParameters(uint16_t*, paramsMLX90640*, const uint8_t *validityMask = nullptr);
int MLX90640_ClassifyDeviatingPixels(uint16_t*, paramsMLX90640*);
bool MLX90640_ValidGlobalParameters(const paramsMLX90640*);
float MLX90640_GetTa(uint16_t*, const paramsMLX90640*);
void MLX90640_CalculateTo(uint16_t*, const paramsMLX90640*, float, float, float*, const uint8_t *validityMask = nullptr);
void MLX90640_BadPixelsCorrection(uint16_t*, float*, int, paramsMLX90640*);
}
