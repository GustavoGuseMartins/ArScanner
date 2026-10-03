#include <WiFi.h>
#include <math.h>
#include "config.h"
#include "stepper_tmc2209.h"
#include "lidar_driver.h"
#include "thermal_mlx90640.h"
#include "camera_ov2640.h"
#include "imu_mpu6050.h"
#include "uwb_tag.h"
#include "control_command_parser.h"
#include "scan_geometry.h"
#include "scan_trace.h"

#ifndef THERMAL_SERIAL_FRAME_DIAGNOSTIC
#define THERMAL_SERIAL_FRAME_DIAGNOSTIC 0
#endif

// Instâncias dos Sensores e Periféricos
StepperController stepper;
LidarDriver lidar;
ThermalSensor thermal;
CameraOv2640 camera;
ImuSensor imu;
UwbTag uwbTag;

// Servidor TCP Socket (Controle e Nuvem de Pontos)
WiFiServer tcpServer(TCP_PORT);
WiFiClient tcpClient;

// Servidor HTTP Auxiliar para Visualização das Câmeras (RGB e Térmica)
WiFiServer cameraServer(8889);

// Fila do FreeRTOS para transporte inter-tarefas entre Core 1 e Core 0
QueueHandle_t scanQueue;
QueueHandle_t controlQueue;

// Estado do Escaneamento e Segurança (Inicia DESLIGADO no boot)
volatile bool isScanningActive = false;
volatile bool controllerConnected = false;
volatile bool stopPanRequested = false;
volatile unsigned long lastHeartbeatMs = 0;
volatile uint32_t queueDropCount = 0;

portMUX_TYPE attitudeMux = portMUX_INITIALIZER_UNLOCKED;
float latestPitch = 0.0f;
float latestRoll = 0.0f;
uint32_t latestAttitudeUs = 0;
ImuRawData latestImuRaw = {};
ImuHealth statusImuHealth;
ImuOrientationSnapshot statusImuOrientation;
UwbTagDiagnostics statusTag;
LidarDiagnostics statusLidarDiagnostics;
volatile uint32_t poseDropCount = 0;
volatile uint32_t thermalFusedPoints = 0;
float statusLidarRpm = 0.0f, statusPanDeg = 0.0f;
uint32_t statusUpdatedMs = 0;
bool statusPanEnabled = true, statusParking = false;
bool statusPanReferenceValid = false, statusPanReferenceRestored = false;
bool statusPanReferenceDirty = false, statusPanMoving = false;
int32_t statusPanSteps = 0;
const char *statusPanReferenceState = "unreferenced";
float statusStepperRpm = 2.0f;
uint8_t statusLidarPwm = 160;
int statusScanMode = 0;
bool statusImuAck = false, statusThermalAck = false;
volatile bool imuOrientationReferenceRequest = false;
volatile int8_t imuOrientationModeRequest = -1;
volatile uint8_t thermalFrameRateRequest = 0;
static ScanTrace<1024> scanTrace;
portMUX_TYPE traceMux = portMUX_INITIALIZER_UNLOCKED;

// I2C e captura de imagens podem bloquear por um quadro inteiro. Nunca devem
// executar na tarefa responsável pela UART e pelos pulsos do motor.
void TaskAuxSensorsCore1(void *pvParameters) {
    unsigned long lastThermalReadMs = 0;
    unsigned long lastI2cRetryMs = 0;
    unsigned long lastThermalLogMs = 0;

    for (;;) {
        // Retry device initialization after an absent ACK or acquisition fault.
        if (millis() - lastI2cRetryMs >= 2000) {
            lastI2cRetryMs = millis();
            Wire.beginTransmission(0x68);
            bool imuAck = Wire.endTransmission() == 0;
            Wire.beginTransmission(0x33);
            bool thermalAck = Wire.endTransmission() == 0;
            portENTER_CRITICAL(&attitudeMux);
            statusImuAck = imuAck;
            statusThermalAck = thermalAck;
            bool parking = statusParking;
            portEXIT_CRITICAL(&attitudeMux);
            if (thermalAck && !thermal.isInitialized()) {
                if (thermal.begin()) {
                    Serial.println("[TERMICA] MLX90640 conectado e pronto!");
                }
            }
            if (imuAck && !imu.isInitialized() && !isScanningActive && !parking) {
                if (imu.begin()) {
                    Serial.println("[IMU] MPU6050 conectado e pronto!");
                }
            }
        }

        const bool imuSampleUpdated = imu.isInitialized() && imu.update();
        float pitch = 0, roll = 0;
        ImuRawData raw = {};
        if (imuSampleUpdated) {
            pitch = imu.getPitch();
            roll = imu.getRoll();
            raw = imu.getRawAxes();
        }
        // The Aux task is the sole owner of the shared I2C bus. HTTP only sets
        // these requests; applying them here prevents a concurrent Wire call.
        bool referenceRequest = false;
        int8_t orientationMode = -1;
        uint8_t frameRateRequest = 0;
        float orientationPan = 0;
        portENTER_CRITICAL(&attitudeMux);
        referenceRequest = imuOrientationReferenceRequest;
        imuOrientationReferenceRequest = false;
        orientationMode = imuOrientationModeRequest;
        imuOrientationModeRequest = -1;
        frameRateRequest = thermalFrameRateRequest;
        thermalFrameRateRequest = 0;
        orientationPan = statusPanDeg;
        portEXIT_CRITICAL(&attitudeMux);
        if (referenceRequest) imu.requestOrientationReference(orientationPan);
        if (orientationMode >= 0) imu.setOrientationEnabled(orientationMode != 0);
        if (frameRateRequest) thermal.requestFrameRate(frameRateRequest);
        if (imuSampleUpdated) imu.updateOrientation(orientationPan);
        // Publish sample and health atomically; HTTP never reads the mutable
        // I2C driver from the other core.
        ImuHealth imuHealth = imu.getHealth();
        portENTER_CRITICAL(&attitudeMux);
        if (imuSampleUpdated) {
            latestImuRaw = raw;
            latestPitch = pitch;
            latestRoll = roll;
            latestAttitudeUs = micros();
        }
        statusImuHealth = imuHealth;
        statusImuOrientation = imu.getOrientation();
        portEXIT_CRITICAL(&attitudeMux);

        // Prévia também disponível em standby, sem ligar os motores.
        if (thermal.isInitialized() && (millis() - lastThermalReadMs >= 10)) {
            thermal.updateFrame();
            lastThermalReadMs = millis();
        }
        if (millis() - lastThermalLogMs >= 5000) {
            lastThermalLogMs = millis();
            portENTER_CRITICAL(&attitudeMux);
            uint32_t imuAgeMs = latestAttitudeUs ? uint32_t(micros()-latestAttitudeUs)/1000U : UINT32_MAX;
            portEXIT_CRITICAL(&attitudeMux);
            Serial.printf("[IMU] v14 estado=%s pronta=%d identidade=%d bias=%d tilt=%d tentativas=%lu erros_leitura=%lu idade_ms=%lu\n",
                imuHealth.state,imuHealth.ready,(int)imuHealth.identity,imuHealth.biasCalibrated,
                IMU_APPLY_TILT,(unsigned long)imuHealth.initAttempts,
                (unsigned long)imuHealth.readErrors,(unsigned long)imuAgeMs);
            if (imuSampleUpdated) {
                Serial.printf("[IMU] sensor_XYZ accel_g=%.3f/%.3f/%.3f gyro_dps=%.2f/%.2f/%.2f angles_deg=%.2f/%.2f/%.2f intervalo_us=%lu lacunas=%lu\n",
                    raw.accelX,raw.accelY,raw.accelZ,raw.gyroX,raw.gyroY,raw.gyroZ,
                    raw.angleX,raw.angleY,raw.angleZ,(unsigned long)raw.sampleIntervalUs,
                    (unsigned long)raw.integrationGaps);
            }
            int error; uint32_t frames, age;
            thermal.health(error, frames, age);
            auto acquisition = thermal.acquisitionHealth();
            Serial.printf("[TERMICA] v14 estado=%s frame=%d quadros=%lu idade_ms=%lu mask=%u dup=%lu timeout=%lu i2c_erros=%lu overruns=%lu erro=%d raw=%d leitura_ms=%lu span_ms=%lu parcial=%d pixels_excluidos=%u aviso_calibracao=%d fps=%.2f alvo=%u\n",
                acquisition.state, acquisition.frameReady, (unsigned long)frames, (unsigned long)age,
                (unsigned)acquisition.subpageMask, (unsigned long)acquisition.duplicateSubpages,
                (unsigned long)acquisition.frameTimeouts, (unsigned long)acquisition.readErrors,
                (unsigned long)acquisition.overruns, error, acquisition.rawError,
                (unsigned long)acquisition.readDurationMs, (unsigned long)acquisition.frameSpanMs,
                acquisition.partialCalibration, (unsigned)acquisition.maskedPixels,
                acquisition.calibrationWarning, acquisition.measuredFrameRateHz,
                (unsigned)acquisition.targetFrameRateHz);
#if THERMAL_SERIAL_FRAME_DIAGNOSTIC
            static bool emitted = false;
            uint8_t payload[872];
            float low, high;
            if (!emitted && thermal.getNormalizedFrame(payload + 8, low, high, payload + 776)) {
                memcpy(payload, &low, 4); memcpy(payload + 4, &high, 4);
                uint32_t crc = 0xFFFFFFFFU;
                for (uint8_t byte : payload) {
                    crc ^= byte;
                    for (unsigned bit = 0; bit < 8; ++bit)
                        crc = (crc >> 1) ^ ((crc & 1U) ? 0xEDB88320U : 0U);
                }
                crc ^= 0xFFFFFFFFU;
                Serial.printf("@THERMALFRAME:BEGIN:bytes=872 crc32=%08lX min=%.4f max=%.4f partial=%d masked=%u\n",
                    (unsigned long)crc, low, high, acquisition.partialCalibration,
                    (unsigned)acquisition.maskedPixels);
                for (unsigned offset = 0; offset < sizeof(payload); offset += 32) {
                    Serial.printf("@THERMALFRAME:%03u:", offset);
                    for (unsigned i = offset; i < offset + 32 && i < sizeof(payload); ++i)
                        Serial.printf("%02X", (unsigned)payload[i]);
                    Serial.println();
                }
                Serial.printf("@THERMALFRAME:END:crc32=%08lX\n", (unsigned long)crc);
                emitted = true;
            }
#endif
        }
        vTaskDelay(pdMS_TO_TICKS(5));
    }
}

// RGB uses its own capture task: waiting for a thermal subpage must not starve it.
// The camera SCCB driver uses I2C port 1; MLX/MPU use Wire on port 0.
void TaskRgbCore1(void *pvParameters) {
    for (;;) {
        camera.updateFrame();
        vTaskDelay(pdMS_TO_TICKS(65));
    }
}

// Task Core 1: Coleta, Fusão Multissensorial e Projeção Matemática 3D
void TaskSensorCore1(void *pvParameters) {
    stepper.begin();
    bool motorRunning = false;
    bool panEnabled = true;
    unsigned long lastLidarLogMs = 0;
    uint32_t lastStatusMs = 0;
    uint8_t currentLidarPwm = 160; // Velocidade moderada e suave padrão (~60% PWM)

    for (;;) {
        if (stopPanRequested) {
            stepper.setRunning(false);
            stopPanRequested = false;
        }
        if (millis()-lastStatusMs >= 100) {
            lastStatusMs = millis();
            portENTER_CRITICAL(&attitudeMux);
            statusLidarRpm = motorRunning && lidar.hasRecentPacket() ? lidar.diagnostics().lastRpm : 0.0f;
            statusLidarDiagnostics = lidar.diagnostics();
            statusUpdatedMs = lastStatusMs;
            statusPanEnabled = panEnabled;
            statusStepperRpm = stepper.getSpeedRpm();
            statusLidarPwm = currentLidarPwm;
            statusScanMode = stepper.getScanMode();
            portEXIT_CRITICAL(&attitudeMux);
            float pan = stepper.getCurrentAngle();
            bool parking = stepper.isParking();
            auto reference = stepper.getPanReferenceStatus();
            portENTER_CRITICAL(&attitudeMux);
            statusPanDeg = pan;
            statusParking = parking;
            statusPanReferenceValid = reference.valid;
            statusPanReferenceRestored = reference.restored;
            statusPanReferenceDirty = reference.dirty;
            statusPanMoving = reference.moving;
            statusPanSteps = reference.steps;
            statusPanReferenceState = reference.state;
            portEXIT_CRITICAL(&attitudeMux);
        }
        ControlCommand control;
        while (xQueueReceive(controlQueue, &control, 0) == pdTRUE) {
            if (control.type == CMD_SET_SPEED) {
                float rpm;
                memcpy(&rpm, control.payload, sizeof(rpm));
                stepper.setSpeedRpm(rpm);
            } else if (control.type == CMD_SET_MODE) {
                stepper.setScanMode(control.payload[0]);
            } else if (control.type == CMD_SET_LIDAR_SPEED) {
                currentLidarPwm = control.payload[0];
                if (motorRunning) lidar.setMotorSpeed(currentLidarPwm);
            } else if (control.type == CMD_PARK_PAN) {
                if (!isScanningActive && controllerConnected && millis()-lastHeartbeatMs <= 2500)
                    if (!stepper.park()) Serial.println("[PAN] Retorno recusado: confirme o zero fisico ou verifique NVS.");
            } else if (control.type == CMD_CONFIRM_PAN_ZERO) {
                // This is an operator assertion that the head is on its physical
                // mark. Recheck the current motor state in its owning task.
                if (!isScanningActive && controllerConnected && !stepper.isParking()) {
                    if (!stepper.confirmPhysicalZero())
                        Serial.println("[PAN] Confirmacao de zero recusada: eixo em movimento ou falha NVS.");
                }
            } else if (control.type == CMD_SET_PAN_ENABLED) {
                panEnabled = control.payload[0] != 0;
            }
        }
        // Motor e parser têm um único proprietário; rede só solicita start/stop.
        if (!controllerConnected || millis() - lastHeartbeatMs > 2500) {
            isScanningActive = false;
            stepper.setRunning(false);
        }
        bool scanning = isScanningActive;
        if (scanning && !stepper.hasPanReference()) {
            Serial.println("[PAN] Scan recusado: alinhe a cabeca a marca e confirme o zero fisico.");
            isScanningActive = scanning = false;
        }
        if (scanning != motorRunning) {
            if (scanning) {
                if (stepper.isParking()) stepper.setRunning(false);
                lidar.resetInput();
                lidar.setMotorSpeed(currentLidarPwm);
            } else {
                lidar.stopMotor();
            }
            motorRunning = scanning;
        }
        if (!stepper.isParking()) {
            if (!stepper.setRunning(scanning && panEnabled) && scanning) {
                // Never start acquisition if the pre-motion NVS checkpoint failed.
                isScanningActive = scanning = false;
                lidar.stopMotor();
                motorRunning = false;
            }
        }
        stepper.update(); // Also ramp explicit parking while acquisition is paused.
        portENTER_CRITICAL(&attitudeMux);
        float dronePitch = latestPitch;
        float droneRoll = latestRoll;
        uint32_t attitudeUs = latestAttitudeUs;
        ImuOrientationSnapshot orientation = statusImuOrientation;
        portEXIT_CRITICAL(&attitudeMux);
        if (!IMU_APPLY_TILT) dronePitch = droneRoll = 0.0f;

        // In the opt-in GY-25 mode the integrated head yaw replaces the
        // mechanical pan angle. At the reference pose both are equal, so this
        // is also the exact base-rotation correction without double counting.
        if (scanning && orientation.enabled &&
            (!orientation.referenceValid || orientation.state != "ready" ||
             orientation.ageMs > 150 || orientation.gaps != 0)) {
            Serial.println("[IMU] Orientacao GY-25 perdeu validade; captura interrompida.");
            isScanningActive = false;
            scanning = false;
            stepper.setRunning(false);
            lidar.stopMotor();
            motorRunning = false;
        }

        // Operação ativa: somente executa passos do motor e leitura do LiDAR se o scan estiver ativo
        if (scanning) {
            // Drena várias amostras sem monopolizar o núcleo indefinidamente.
            LidarMeasurement measurement;
            for (int n = 0; n < 64 && isScanningActive && lidar.readPacket(measurement); ++n) {
                float currentBaseAngle;
                if (!stepper.getAngleAt(measurement.sampleTimeUs, currentBaseAngle) ||
                    (IMU_APPLY_TILT && (attitudeUs == 0 ||
                     abs((int32_t)(measurement.sampleTimeUs-attitudeUs)) > 20000))) {
                    ++poseDropCount;
                    continue;
                }
                if (orientation.enabled) {
                    if (!orientation.referenceValid || orientation.state != "ready" ||
                        orientation.ageMs > 150 || orientation.gaps != 0) {
                        ++poseDropCount;
                        continue;
                    }
                    currentBaseAngle = orientation.relativeHeadYawDeg;
                }
                using namespace ScanGeometry;
                auto headDebug = inHeadDebug(measurement.distanceMm*0.001f,
                    measurement.angleDeg, LIDAR_ANGLE_SIGN, LIDAR_ZERO_DEG,
                    LIDAR_MOUNT_YAW_DEG,
                    {LIDAR_ORIGIN_X_M, LIDAR_ORIGIN_Y_M, LIDAR_ORIGIN_Z_M});
                Vec3 headPoint = headDebug.point;
                Vec3 point = project(headPoint, currentBaseAngle, dronePitch, droneRoll,
                    IMU_ON_ROTATING_HEAD, {TAG_OFFSET_X_M, TAG_OFFSET_Y_M, TAG_OFFSET_Z_M},
                    TAG_ON_ROTATING_HEAD);

                // Cameras rotate with the head. Pan is NOT vertical image angle.
                // RGB correspondence stays disabled until its separate calibration.
                float relAngleH = atan2f(headPoint.x, headPoint.z) / radians;
                float relAngleV = atan2f(headPoint.y-LIDAR_ORIGIN_Y_M,
                    hypotf(headPoint.x, headPoint.z)) / radians;

                // Amostragem da Câmera Superior (OV2640 RGB)
                uint8_t r = 255, g = 255, b = 255;
                if (CAMERA_EXTRINSICS_CALIBRATED) camera.getPixelColor(relAngleH, relAngleV, r, g, b);

                // The lens has a measured head-frame offset. Its optical side
                // is selected by the persisted bench-calibration profile;
                // reject rays outside FOV, near-field parallax and old frames.
                uint32_t sampleTimeMs = millis() -
                    (uint32_t)(micros()-measurement.sampleTimeUs)/1000U;
                float thermalH = 0.0f, thermalV = 0.0f;
                float tempC = 21.0f; // Placeholder de transporte, não uma medição.
                bool hasThermal = measurement.distanceMm*0.001f >= THERMAL_FUSION_MIN_RANGE_M &&
                    thermalAngles(headPoint,
                        {LIDAR_ORIGIN_X_M,LIDAR_ORIGIN_Y_M,LIDAR_ORIGIN_Z_M},
                        {THERMAL_OFFSET_X_M,THERMAL_OFFSET_Y_M,THERMAL_OFFSET_Z_M},
                        THERMAL_INVERT_LIDAR_Y,thermalH,thermalV,
                        thermal.getOrientationProfile()) &&
                    thermal.tryGetPointTemperature(thermalH,thermalV,tempC,sampleTimeMs);
                if (hasThermal) ++thermalFusedPoints;

                // Temperatura baixa não demonstra que a superfície seja planar.
                uint8_t flags = !hasThermal ? SURFACE_FLAG_THERMAL_MISSING
                    : (tempC >= 28.0f ? SURFACE_FLAG_HOTSPOT : 0);
                if (measurement.strengthWarning) flags |= SURFACE_FLAG_LIDAR_WEAK;

                ScanPointPacket pkt;
                pkt.posX_mm = point.x * 1000.0f;
                pkt.posY_mm = point.y * 1000.0f;
                pkt.posZ_mm = point.z * 1000.0f;
                pkt.temperatureC = tempC;
                pkt.r = r;
                pkt.g = g;
                pkt.b = b;
                pkt.surfaceFlags = flags;
                pkt.pitchCentiDeg = (int16_t)(dronePitch * 100.0f);
                pkt.rollCentiDeg = (int16_t)(droneRoll * 100.0f);
                pkt.timestampMs = sampleTimeMs;

                // Envia para a fila inter-tarefas
                bool queued = xQueueSend(scanQueue, &pkt, 0) == pdTRUE;
                if (!queued) {
                    queueDropCount++;
                }
                if (CALIBRATION_DIAGNOSTIC_MODE) {
                    portENTER_CRITICAL(&traceMux);
                    scanTrace.push(measurement.sampleTimeUs,measurement.angleDeg,
                        measurement.distanceMm,currentBaseAngle,headDebug.angleDeg,
                        dronePitch,droneRoll,point,queued,measurement.signalStrength,
                        measurement.strengthWarning);
                    portEXIT_CRITICAL(&traceMux);
                }
            }

            if (millis() - lastLidarLogMs >= 5000) {
                const auto &d = lidar.diagnostics();
                Serial.printf("[LiDAR] bytes=%lu pacotes=%lu checksum_erros=%lu amostras=%lu invalidas=%lu sinal_fraco=%lu drops=%lu rpm=%.1f\n",
                    (unsigned long)d.bytesReceived, (unsigned long)d.validPackets,
                    (unsigned long)d.checksumErrors, (unsigned long)d.validSamples,
                    (unsigned long)d.invalidSamples, (unsigned long)d.weakSamples,
                    (unsigned long)queueDropCount, d.lastRpm);
                lastLidarLogMs = millis();
                Serial.printf("[POSE] pan_pulsos=%.3f graus pose_drops=%lu imu_tilt=%d\n",
                    stepper.getCurrentAngle(), (unsigned long)poseDropCount, IMU_APPLY_TILT);
            }

            vTaskDelay(pdMS_TO_TICKS(1)); // Cede tempo para watchdog
        } else {
            // Em Standby/Repouso: consumo elétrico mínimo
            vTaskDelay(pdMS_TO_TICKS(15));
        }
    }
}

// Task Core 0: Wi-Fi, Servidor TCP com Comandos e Watchdog de Segurança
void TaskNetworkCore0(void *pvParameters) {
    WiFi.softAP(WIFI_SSID, WIFI_PASS);
    IPAddress apIP = WiFi.softAPIP();
    Serial.print("Servidor TCP ArScanner iniciado no IP: ");
    Serial.println(apIP);

    tcpServer.begin();

    ScanPointPacket batchBuffer[POINTS_PER_BATCH];
    int batchCount = 0;
    ControlCommandParser commandParser;

    for (;;) {
        // 1. Gerenciamento da Conexão TCP Principal (Porta 8888)
        if (!tcpClient || !tcpClient.connected()) {
            // Uma nova sessão nunca herda a varredura nem dados da anterior.
            isScanningActive = false;
            controllerConnected = false;
            stopPanRequested = true;
            xQueueReset(controlQueue);
            commandParser.reset();
            xQueueReset(scanQueue);
            batchCount = 0;
            tcpClient = tcpServer.available();
            if (tcpClient) {
                controllerConnected = true;
                commandParser.reset();
                Serial.println("[TCP Server] Novo cliente Unity conectado!");
                tcpClient.setNoDelay(true);
                lastHeartbeatMs = millis();
            }
        }

        if (tcpClient && tcpClient.connected()) {
            // Leitura de Comandos de Controle enviados pelo Celular
            while (tcpClient.available()) {
                ControlCommand control;
                if (!commandParser.feed(tcpClient.read(), control)) continue;
                uint8_t cmd = control.type;

                if (cmd == CMD_START_SCAN) {
                    Serial.println("[CMD] Iniciar Escaneamento recebido!");
                    lastHeartbeatMs = millis();
                    if (!isScanningActive) xQueueReset(scanQueue);
                    isScanningActive = true;
                } 
                else if (cmd == CMD_STOP_SCAN) {
                    Serial.println("[CMD] Parar Escaneamento recebido!");
                    isScanningActive = false;
                    xQueueReset(controlQueue); // Cancel pending park/settings on explicit stop.
                    stopPanRequested = true;
                } 
                else if (cmd == CMD_HEARTBEAT) {
                    lastHeartbeatMs = millis();
                } 
                else if (cmd == CMD_PARK_PAN) {
                    if (!isScanningActive) {
                        lastHeartbeatMs = millis();
                        xQueueSend(controlQueue, &control, 0);
                    }
                }
                else if (cmd == CMD_CONFIRM_PAN_ZERO) {
                    if (!isScanningActive) {
                        lastHeartbeatMs = millis();
                        xQueueSend(controlQueue, &control, 0);
                    }
                }
                else if (cmd == CMD_SET_SPEED) {
                    float rpm;
                    memcpy(&rpm, control.payload, sizeof(rpm));
                    if (isfinite(rpm)) {
                        xQueueSend(controlQueue, &control, 0);
                    }
                } 
                else if (cmd == CMD_SET_MODE) {
                    if (control.payload[0] <= 1) {
                        xQueueSend(controlQueue, &control, 0);
                    }
                }
                else if (cmd == CMD_SET_LIDAR_SPEED) {
                    xQueueSend(controlQueue, &control, 0);
                    Serial.printf("[CMD] Velocidade LiDAR PWM ajustada para: %u\n", control.payload[0]);
                }
                else if (cmd == CMD_SET_PAN_ENABLED && control.payload[0] <= 1) {
                    xQueueSend(controlQueue, &control, 0);
                }
            }

            // Watchdog de Segurança: se não receber heartbeat por mais de 2.5s, desliga motores
            if (isScanningActive && (millis() - lastHeartbeatMs > 2500)) {
                Serial.println("[SEGURANCA] Watchdog: Perda de comunicacao (>2.5s)! Parando motores...");
                isScanningActive = false;
            }

            // Transmissão dos pontos capturados (somente quando ativo)
            if (isScanningActive) {
                ScanPointPacket pkt;
                for (int sent = 0; sent < 250 && xQueueReceive(scanQueue, &pkt, 0) == pdTRUE; ++sent) {
                    batchBuffer[batchCount++] = pkt;

                    if (batchCount >= POINTS_PER_BATCH) {
                        size_t expected = sizeof(ScanPointPacket) * batchCount;
                        if (tcpClient.write((const uint8_t*)batchBuffer, expected) != expected) {
                            // Sem cabeçalho, continuar após escrita parcial desalinha
                            // todos os registros de 28 bytes da conexão.
                            tcpClient.stop();
                            isScanningActive = false;
                            batchCount = 0;
                            break;
                        }
                        batchCount = 0;
                    }
                }

                if (batchCount > 0) {
                    size_t expected = sizeof(ScanPointPacket) * batchCount;
                    if (tcpClient.write((const uint8_t*)batchBuffer, expected) != expected) {
                        tcpClient.stop();
                        isScanningActive = false;
                    }
                    batchCount = 0;
                }
            } else {
                ScanPointPacket discardPkt;
                while (xQueueReceive(scanQueue, &discardPkt, 0) == pdTRUE);
                batchCount = 0;
            }
        } else {
            commandParser.reset();
            // Se desconectado do celular, garante motores 100% desligados
            if (isScanningActive) {
                Serial.println("[SEGURANCA] Celular desconectado! Parando motores...");
                isScanningActive = false;
            }

            ScanPointPacket discardPkt;
            while (xQueueReceive(scanQueue, &discardPkt, 0) == pdTRUE);
            batchCount = 0;
        }

        vTaskDelay(pdMS_TO_TICKS(2));
    }
}

void sendHttpHeaders(WiFiClient &client, const char *status, const char *type, size_t length) {
    client.printf("HTTP/1.1 %s\r\nContent-Type: %s\r\nContent-Length: %u\r\n"
                  "Connection: close\r\nCache-Control: no-store\r\n"
                  "Access-Control-Allow-Origin: *\r\n\r\n", status, type, (unsigned)length);
}

void sendHttpError(WiFiClient &client, const char *status, const char *message) {
    sendHttpHeaders(client, status, "text/plain; charset=utf-8", strlen(message));
    client.print(message);
}

// Um cliente HTTP lento não deve impedir a leitura do heartbeat/TWR.
void TaskCameraHttpCore0(void *pvParameters) {
    while (WiFi.softAPIP() == IPAddress(0, 0, 0, 0)) vTaskDelay(pdMS_TO_TICKS(20));
    cameraServer.begin();
    for (;;) {
        WiFiClient client = cameraServer.available();
        if (!client) {
            vTaskDelay(pdMS_TO_TICKS(5));
            continue;
        }
        String request;
        request.reserve(128);
        unsigned long started = millis();
        size_t headerBytes = 0;
        uint32_t tail = 0;
        bool firstLineDone = false;
        bool headersDone = false;
        while (client.connected() && millis() - started < 500 && headerBytes < 2048) {
            if (!client.available()) { vTaskDelay(pdMS_TO_TICKS(1)); continue; }
            int value = client.read();
            if (value < 0) continue;
            ++headerBytes;
            tail = (tail << 8) | (uint8_t)value;
            if (!firstLineDone) {
                if (value == '\n') firstLineDone = true;
                else if (value != '\r' && request.length() < 128) request += (char)value;
            }
            if (tail == 0x0D0A0D0A) { headersDone = true; break; }
        }
        bool isGet = request.startsWith("GET ");
        bool isPost = request.startsWith("POST ");
        int pathStart = isGet ? 4 : (isPost ? 5 : 0);
        int pathEnd = pathStart ? request.indexOf(' ', pathStart) : -1;
        String path = pathEnd > pathStart ? request.substring(pathStart,pathEnd) : "";
        int query = path.indexOf('?');
        String arguments = query >= 0 ? path.substring(query+1) : "";
        if (query >= 0) path = path.substring(0,query);
        if (!headersDone) {
            sendHttpError(client, "408 Request Timeout", "Cabecalhos HTTP incompletos.");
        } else if (path == "/status") {
            portENTER_CRITICAL(&attitudeMux);
            float rpm = statusLidarRpm, pan = statusPanDeg;
            uint32_t updated = statusUpdatedMs;
            bool panEnabled = statusPanEnabled, parking = statusParking;
            bool panReferenceValid = statusPanReferenceValid;
            bool panReferenceRestored = statusPanReferenceRestored;
            bool panReferenceDirty = statusPanReferenceDirty, panMoving = statusPanMoving;
            int32_t panSteps = statusPanSteps;
            const char *panReferenceState = statusPanReferenceState;
            float stepperRpm = statusStepperRpm;
            uint8_t lidarPwm = statusLidarPwm;
            int scanMode = statusScanMode;
            bool imuAck = statusImuAck, thermalAck = statusThermalAck;
            auto tag = statusTag;
            auto lidarDiagnostics = statusLidarDiagnostics;
            ImuRawData imuRaw = latestImuRaw;
            ImuHealth imuHealth = statusImuHealth;
            ImuOrientationSnapshot imuOrientation = statusImuOrientation;
            uint32_t imuAgeMs = latestAttitudeUs ? (uint32_t)(micros()-latestAttitudeUs)/1000U : UINT32_MAX;
            portEXIT_CRITICAL(&attitudeMux);
            int thermalError;
            uint32_t thermalFrames, thermalAge;
            thermal.health(thermalError, thermalFrames, thermalAge);
            auto thermalAcquisition = thermal.acquisitionHealth();
            // Only this HTTP task owns the buffer. Keep it off the task stack:
            // the thermal preview also needs a 768-float normalization snapshot.
            static char body[8192];
            int length = snprintf(body, sizeof(body),
                "{\"lidarRpm\":%.2f,\"panDegrees\":%.3f,\"stepsPerRevolution\":%d,"
                "\"timestampMs\":%lu,\"poseDrops\":%lu,\"imuCalibrated\":%s,\"encoder\":false,"
                "\"diagnosticVersion\":14,\"panEnabled\":%s,\"rgbReady\":%s,\"thermalReady\":%s,"
                "\"imuReady\":%s,\"tagReady\":%s,\"thermalError\":%d,\"thermalFrames\":%lu,"
                "\"thermalAgeMs\":%lu,\"thermalFusedPoints\":%lu,"
                "\"rgbMessage\":\"%s\",\"tagEnabled\":%s,\"rgbProfile\":%d,"
                "\"imuRaw\":{\"accelX\":%.3f,\"accelY\":%.3f,\"accelZ\":%.3f,"
                "\"gyroX\":%.1f,\"gyroY\":%.1f,\"gyroZ\":%.1f,"
                "\"angleX\":%.2f,\"angleY\":%.2f,\"angleZ\":%.2f},\"imuAgeMs\":%lu,"
                "\"imuSampleIntervalUs\":%lu,\"imuIntegrationGaps\":%lu,"
                "\"imuState\":\"%s\",\"imuIdentity\":%d,\"imuInitAttempts\":%lu,\"imuReadErrors\":%lu,"
                "\"imuBiasCalibrated\":%s,\"imuTiltApplied\":%s,"
                "\"imuOrientationState\":\"%s\",\"imuOrientationReferenceValid\":%s,"
                "\"imuOrientationEnabled\":%s,\"imuOrientationStationary\":%s,"
                "\"imuGravityValid\":%s,\"imuOrientationGeneration\":%lu,"
                "\"imuOrientationAgeMs\":%lu,\"imuOrientationGaps\":%lu,"
                "\"imuStationaryMs\":%lu,\"imuRelativeHeadYawDeg\":%.3f,"
                "\"imuRelativeBaseYawDeg\":%.3f,\"imuPitchDeg\":%.3f,\"imuRollDeg\":%.3f,"
                "\"imuYawUncertaintyDeg\":%.3f,\"imuBaseQw\":%.6f,\"imuBaseQx\":%.6f,"
                "\"imuBaseQy\":%.6f,\"imuBaseQz\":%.6f,"
                "\"lidarWeakSamples\":%lu,\"lidarValidSamples\":%lu,\"lidarInvalidSamples\":%lu,\"lidarChecksumErrors\":%lu,"
                "\"isScanning\":%s,\"panParking\":%s,\"stepperRpm\":%.2f,\"lidarPwm\":%u,\"scanMode\":%d,"
                "\"imuI2cAck\":%s,\"thermalI2cAck\":%s,"
                "\"tagPolls\":%lu,\"tagResponses\":%lu,\"tagFinals\":%lu,\"tagReports\":%lu,"
                "\"tagRxErrors\":%lu,\"tagStage\":%u,\"tagLastPollAgeMs\":%lu,"
                "\"thermalOrientationProfile\":%u,"
                "\"panReferenceValid\":%s,\"panReferenceRestored\":%s,\"panReferenceDirty\":%s,"
                "\"panReferenceState\":\"%s\",\"panMoving\":%s,\"panSteps\":%ld,"
                "\"panReferenceCalibrationVersion\":%lu,"
                "\"thermalFrameReady\":%s,\"thermalState\":\"%s\",\"thermalSubpageMask\":%u,"
                "\"thermalDuplicateSubpages\":%lu,\"thermalFrameTimeouts\":%lu,\"thermalReadErrors\":%lu,"
                "\"thermalOverruns\":%lu,\"thermalInvalidFrames\":%lu,\"thermalLastSubpage\":%d,"
                "\"thermalRefreshHz\":%u,\"thermalI2cHz\":%lu,\"thermalReadDurationMs\":%lu,"
                "\"thermalFrameSpanMs\":%lu,\"thermalRawError\":%d,"
                "\"thermalPartialCalibration\":%s,\"thermalMaskedPixels\":%u,\"thermalCalibrationWarning\":%d,"
                "\"thermalRequestedFrameRateHz\":%u,\"thermalTargetFrameRateHz\":%u,"
                "\"thermalFrameRateHz\":%.3f,\"thermalFrameRateWindowMs\":%lu}",
                rpm,pan,STEPS_PER_REV,(unsigned long)updated,(unsigned long)poseDropCount,
                IMU_APPLY_TILT ? "true" : "false", panEnabled ? "true" : "false",
                camera.isInitialized() ? "true" : "false", thermal.isInitialized() ? "true" : "false",
                imuHealth.ready ? "true" : "false", uwbTag.isInitialized() ? "true" : "false",
                thermalError,(unsigned long)thermalFrames,(unsigned long)thermalAge,
                (unsigned long)thermalFusedPoints,camera.statusMessage(),
                UWB_TAG_ENABLED ? "true" : "false", RGB_BOARD_PROFILE,
                imuRaw.accelX,imuRaw.accelY,imuRaw.accelZ,
                imuRaw.gyroX,imuRaw.gyroY,imuRaw.gyroZ,
                imuRaw.angleX,imuRaw.angleY,imuRaw.angleZ,(unsigned long)imuAgeMs,
                (unsigned long)imuRaw.sampleIntervalUs,(unsigned long)imuRaw.integrationGaps,
                imuHealth.state,(int)imuHealth.identity,(unsigned long)imuHealth.initAttempts,
                (unsigned long)imuHealth.readErrors,imuHealth.biasCalibrated ? "true" : "false",
                IMU_APPLY_TILT ? "true" : "false",
                imuOrientation.state,
                imuOrientation.referenceValid ? "true" : "false",
                imuOrientation.enabled ? "true" : "false",
                imuOrientation.stationary ? "true" : "false",
                imuOrientation.gravityValid ? "true" : "false",
                (unsigned long)imuOrientation.generation,
                (unsigned long)imuOrientation.ageMs,
                (unsigned long)imuOrientation.gaps,
                (unsigned long)imuOrientation.stationaryMs,
                imuOrientation.relativeHeadYawDeg, imuOrientation.relativeBaseYawDeg,
                imuOrientation.pitchDeg, imuOrientation.rollDeg, imuOrientation.yawUncertaintyDeg,
                imuOrientation.qw, imuOrientation.qx, imuOrientation.qy, imuOrientation.qz,
                (unsigned long)lidarDiagnostics.weakSamples,(unsigned long)lidarDiagnostics.validSamples,
                (unsigned long)lidarDiagnostics.invalidSamples,(unsigned long)lidarDiagnostics.checksumErrors,
                isScanningActive ? "true" : "false",parking ? "true" : "false",stepperRpm,lidarPwm,scanMode,
                imuAck ? "true" : "false",thermalAck ? "true" : "false",
                (unsigned long)tag.polls,(unsigned long)tag.responses,(unsigned long)tag.finals,
                (unsigned long)tag.reports,(unsigned long)tag.rxErrors,tag.stage,
                (unsigned long)(tag.polls ? millis()-tag.lastPollMs : UINT32_MAX),
                (unsigned)thermal.getOrientationProfile(),
                panReferenceValid ? "true" : "false", panReferenceRestored ? "true" : "false",
                panReferenceDirty ? "true" : "false", panReferenceState,
                panMoving ? "true" : "false", (long)panSteps,
                (unsigned long)PAN_REFERENCE_CALIBRATION_VERSION,
                thermalAcquisition.frameReady ? "true" : "false", thermalAcquisition.state,
                (unsigned)thermalAcquisition.subpageMask,
                (unsigned long)thermalAcquisition.duplicateSubpages,
                (unsigned long)thermalAcquisition.frameTimeouts,
                (unsigned long)thermalAcquisition.readErrors,
                (unsigned long)thermalAcquisition.overruns,
                (unsigned long)thermalAcquisition.invalidFrames, thermalAcquisition.lastSubpage,
                (unsigned)thermalAcquisition.refreshHz, (unsigned long)thermalAcquisition.i2cHz,
                (unsigned long)thermalAcquisition.readDurationMs,
                (unsigned long)thermalAcquisition.frameSpanMs, thermalAcquisition.rawError,
                thermalAcquisition.partialCalibration ? "true" : "false",
                (unsigned)thermalAcquisition.maskedPixels, thermalAcquisition.calibrationWarning,
                (unsigned)thermalAcquisition.requestedFrameRateHz,
                (unsigned)thermalAcquisition.targetFrameRateHz,
                thermalAcquisition.measuredFrameRateHz,
                (unsigned long)thermalAcquisition.frameRateWindowMs);
            if (length < 0 || (size_t)length >= sizeof(body)) {
                sendHttpError(client,"500 Internal Server Error","Status excedeu o buffer.");
                client.stop();
                continue;
            }
            sendHttpHeaders(client, "200 OK", "application/json", length);
            client.write((const uint8_t*)body, length);
        } else if (path == "/scan.csv") {
            if (!CALIBRATION_DIAGNOSTIC_MODE) {
                sendHttpError(client,"503 Service Unavailable","Diagnostico CSV desativado em config.h.");
                client.stop();
                continue;
            }
            auto *snapshot = (ScanTraceSample*)malloc(1024*sizeof(ScanTraceSample));
            if (!snapshot) {
                sendHttpError(client,"503 Service Unavailable","Sem memoria para snapshot.");
            } else {
                portENTER_CRITICAL(&traceMux);
                size_t count = scanTrace.copy(snapshot);
                portEXIT_CRITICAL(&traceMux);
                const char *header = scanTraceCsvHeader;
                size_t length = strlen(header);
                char line[384];
                for (size_t i=0;i<count;++i) {
                    const auto &s = snapshot[i];
                    length += formatScanTrace(line,sizeof(line),s);
                }
                sendHttpHeaders(client,"200 OK","text/csv",length);
                client.print(header);
                for (size_t i=0;i<count && client.connected();++i) {
                    const auto &s = snapshot[i];
                    int n = formatScanTrace(line,sizeof(line),s);
                    client.write((uint8_t*)line,n);
                }
                free(snapshot);
            }
        } else if (path == "/rgb") {
            uint8_t *jpeg = nullptr;
            size_t length = 0;
            if (camera.getJpegFrame(&jpeg, &length)) {
                sendHttpHeaders(client, "200 OK", "image/jpeg", length);
                client.write(jpeg, length);
                camera.freeJpegFrame(jpeg);
            } else {
                sendHttpError(client, "503 Service Unavailable", camera.statusMessage());
            }
        } else if (path == "/thermal" || path == "/thermal/masked") {
            bool withValidity = path == "/thermal/masked";
            uint8_t data[872];
            float minT, maxT;
            if (thermal.getNormalizedFrame(data + 8, minT, maxT,
                    withValidity ? data + 776 : nullptr)) {
                memcpy(data, &minT, sizeof(float));
                memcpy(data + 4, &maxT, sizeof(float));
                size_t payloadLength = withValidity ? 872 : 776;
                sendHttpHeaders(client, "200 OK", "application/octet-stream", payloadLength);
                client.write(data, payloadLength);
            } else {
                int error; uint32_t frames, age;
                thermal.health(error,frames,age);
                char message[256];
                auto acquisition = thermal.acquisitionHealth();
                if (!withValidity && acquisition.partialCalibration && acquisition.frameReady)
                    snprintf(message, sizeof(message), "Imagem termica parcial: %u pixels sem calibracao. Atualize o aplicativo para visualizar com mascara (/thermal/masked).",
                        (unsigned)acquisition.maskedPixels);
                else
                    snprintf(message,sizeof(message),"MLX90640: estado=%s, init=%d, erro=%d, raw=%d, quadros=%lu, idade_ms=%lu, subpaginas=%u, timeouts=%lu, erros_i2c=%lu.",
                        acquisition.state,thermal.isInitialized(),error,acquisition.rawError,
                        (unsigned long)frames,(unsigned long)age,(unsigned)acquisition.subpageMask,
                        (unsigned long)acquisition.frameTimeouts,(unsigned long)acquisition.readErrors);
                sendHttpError(client, "503 Service Unavailable", message);
            }
        } else if (path == "/thermal/orientation") {
            if (!isPost) {
                sendHttpError(client,"405 Method Not Allowed","Use POST /thermal/orientation?profile=0..3.");
            } else if (arguments.length() != 9 || !arguments.startsWith("profile=") ||
                       arguments[8] < '0' || arguments[8] > '3') {
                sendHttpError(client,"400 Bad Request","Perfil esperado: 0=-X, 1=+X, 2=-X espelhado, 3=+X espelhado.");
            } else if (isScanningActive) {
                sendHttpError(client,"409 Conflict","Pare a captura antes de mudar a orientacao termica.");
            } else if (!thermal.saveOrientationProfile((uint8_t)(arguments[8]-'0'))) {
                sendHttpError(client,"500 Internal Server Error","Nao foi possivel salvar a orientacao no ESP.");
            } else {
                char body[48];
                int length = snprintf(body,sizeof(body),"{\"thermalOrientationProfile\":%u}",
                    (unsigned)thermal.getOrientationProfile());
                sendHttpHeaders(client,"200 OK","application/json",length);
                client.write((const uint8_t*)body,length);
            }
        } else if (path == "/imu/orientation/reference" || path == "/imu/orientation/mode") {
            if (!isPost) {
                sendHttpError(client,"405 Method Not Allowed","Use POST para controlar a referencia GY-25.");
            } else if (isScanningActive || statusParking || statusPanMoving) {
                sendHttpError(client,"409 Conflict","Pare o scanner e mantenha o pan parado antes de alterar a referencia GY-25.");
            } else if (!statusImuHealth.ready || !statusImuHealth.biasCalibrated || !statusPanReferenceValid) {
                sendHttpError(client,"409 Conflict","GY-25 ou zero do pan ainda nao estao prontos.");
            } else if (path == "/imu/orientation/reference") {
                portENTER_CRITICAL(&attitudeMux);
                imuOrientationReferenceRequest = true;
                portEXIT_CRITICAL(&attitudeMux);
                const char *body = "{\"imuOrientationState\":\"reference_collecting\"}";
                sendHttpHeaders(client,"202 Accepted","application/json",strlen(body));
                client.print(body);
            } else if (arguments != "enabled=0" && arguments != "enabled=1") {
                sendHttpError(client,"400 Bad Request","Use enabled=0 ou enabled=1.");
            } else if (arguments == "enabled=1" && !statusImuOrientation.referenceValid) {
                sendHttpError(client,"409 Conflict","Colete a referencia GY-25 antes de habilitar o acompanhamento.");
            } else {
                portENTER_CRITICAL(&attitudeMux);
                imuOrientationModeRequest = arguments == "enabled=1" ? 1 : 0;
                portEXIT_CRITICAL(&attitudeMux);
                const char *body = arguments == "enabled=1"
                    ? "{\"imuOrientationState\":\"enabling\"}"
                    : "{\"imuOrientationState\":\"disabling\"}";
                sendHttpHeaders(client,"202 Accepted","application/json",strlen(body));
                client.print(body);
            }
        } else if (path == "/thermal/frame-rate") {
            if (!isPost) {
                sendHttpError(client,"405 Method Not Allowed","Use POST /thermal/frame-rate?fps=4 ou fps=8.");
            } else if (isScanningActive || statusParking || statusPanMoving) {
                sendHttpError(client,"409 Conflict","Pare o scanner antes de mudar a taxa termica.");
            } else if (arguments != "fps=4" && arguments != "fps=8") {
                sendHttpError(client,"400 Bad Request","Taxa esperada: fps=4 ou fps=8.");
            } else {
                uint8_t requested = arguments == "fps=8" ? 8 : 4;
                portENTER_CRITICAL(&attitudeMux);
                thermalFrameRateRequest = requested;
                portEXIT_CRITICAL(&attitudeMux);
                auto acquisition = thermal.acquisitionHealth();
                char body[192];
                int length = snprintf(body,sizeof(body),
                    "{\"thermalRequestedFrameRateHz\":%u,\"thermalTargetFrameRateHz\":%u,\"thermalFrameRateHz\":%.3f}",
                    (unsigned)requested,(unsigned)acquisition.targetFrameRateHz,
                    acquisition.measuredFrameRateHz);
                sendHttpHeaders(client,"202 Accepted","application/json",length);
                client.write((const uint8_t*)body,length);
            }
        } else if (path == "/geometry") {
            char body[1024];
            int length = snprintf(body, sizeof(body),
                "{\"lidarOrigin\":[%.4f,%.4f,%.4f],\"lidarMountYaw\":%.1f,"
                "\"lidarAngleSign\":%.0f,\"lidarZeroDeg\":%.1f,"
                "\"panSign\":%.0f,\"panZeroDeg\":%.1f,\"stepsPerRev\":%d,"
                "\"gearRatio\":%d,\"microsteps\":%d,"
                "\"imuApplyTilt\":%s,\"imuOnHead\":%s,\"tagOnHead\":%s,"
                "\"tagOffset\":[%.4f,%.4f,%.4f],"
                "\"tagPhysicalOffset\":[%.4f,%.4f,%.4f],"
                "\"diagnosticMode\":%s,\"imuPitchAxis\":%d,\"imuRollAxis\":%d,"
                "\"imuPitchSign\":%.0f,\"imuRollSign\":%.0f,"
                "\"coordinateFrame\":\"X-right Y-up Z-forward; pan CW+; operator-confirmed physical zero, no encoder\","
                "\"thermalOrientationProfile\":%u}",
                LIDAR_ORIGIN_X_M,LIDAR_ORIGIN_Y_M,LIDAR_ORIGIN_Z_M,
                LIDAR_MOUNT_YAW_DEG,LIDAR_ANGLE_SIGN,LIDAR_ZERO_DEG,
                STEPPER_PAN_SIGN,STEPPER_ZERO_DEG,STEPS_PER_REV,
                STEPPER_GEAR_RATIO,STEPPER_MICROSTEPS,
                IMU_APPLY_TILT ? "true" : "false",
                IMU_ON_ROTATING_HEAD ? "true" : "false",
                TAG_ON_ROTATING_HEAD ? "true" : "false",
                TAG_OFFSET_X_M,TAG_OFFSET_Y_M,TAG_OFFSET_Z_M,
                TAG_PHYSICAL_OFFSET_X_M,TAG_PHYSICAL_OFFSET_Y_M,TAG_PHYSICAL_OFFSET_Z_M,
                CALIBRATION_DIAGNOSTIC_MODE ? "true" : "false",
                IMU_PITCH_AXIS,IMU_ROLL_AXIS,IMU_PITCH_SIGN,IMU_ROLL_SIGN,
                (unsigned)thermal.getOrientationProfile());
            if (length < 0 || (size_t)length >= sizeof(body)) {
                sendHttpError(client,"500 Internal Server Error","Geometry buffer exceeded.");
                client.stop();
                continue;
            }
            sendHttpHeaders(client, "200 OK", "application/json", length);
            client.write((const uint8_t*)body, length);
        } else {
            sendHttpError(client, "404 Not Found", "Use /status, /scan.csv, /geometry, /rgb, /thermal ou POST /thermal/orientation, /imu/orientation/* ou /thermal/frame-rate.");
        }
        client.stop();
        vTaskDelay(pdMS_TO_TICKS(2));
    }
}

// Task dedicada para responder requisições UWB TWR com precisão e mínimo jitter
void TaskUwbCore0(void *pvParameters) {
    uwbTag.begin();
    uint32_t lastRetry = millis();
    for (;;) {
        if (!uwbTag.isInitialized() && millis()-lastRetry >= 3000) {
            uwbTag.begin();
            lastRetry = millis();
        }
        uwbTag.updateRanging();
        portENTER_CRITICAL(&attitudeMux);
        statusTag = uwbTag.diagnostics();
        portEXIT_CRITICAL(&attitudeMux);
        vTaskDelay(pdMS_TO_TICKS(1));
    }
}

void setup() {
    Serial.begin(115200);
    delay(1000);

    Serial.println("==============================================");
    Serial.println("Iniciando Scanner 3D Aereo (ESP32-S3)...");
    Serial.println("==============================================");

    // With the isolated RGB profile, deselect DW1000 before touching shared pins.
    pinMode(UWB_CS_PIN, OUTPUT);
    digitalWrite(UWB_CS_PIN, HIGH);
    lidar.begin();
    // Inicialize o barramento compartilhado uma vez. Repetir Wire.begin() a
    // cada tentativa de sensor ausente reinicia o driver enquanto o MPU opera.
    Wire.begin(I2C_SDA_PIN, I2C_SCL_PIN, I2C_FREQ);
    Wire.setTimeOut(100);
    thermal.loadOrientationProfile();
    thermal.begin();
    camera.begin();
    imu.begin();

    // Capacidade da fila FreeRTOS ampliada para 600 medições (~16.8 KB, margem de 300ms de buffer)
    scanQueue = xQueueCreate(600, sizeof(ScanPointPacket));
    controlQueue = xQueueCreate(16, sizeof(ControlCommand));
    if (scanQueue == nullptr || controlQueue == nullptr) {
        Serial.println("[ERRO] Memoria insuficiente para inicializar as filas.");
        return;
    }

    xTaskCreatePinnedToCore(TaskSensorCore1, "SensorCore1", 10240, NULL, 2, NULL, 1);
    xTaskCreatePinnedToCore(TaskAuxSensorsCore1, "AuxSensorsCore1", 12288, NULL, 1, NULL, 1);
    if (camera.isInitialized() &&
        xTaskCreatePinnedToCore(TaskRgbCore1, "RgbCore1", 4096, NULL, 1, NULL, 1) != pdPASS) {
        Serial.println("[RGB] Sem memoria para iniciar a tarefa de captura.");
    }
    xTaskCreatePinnedToCore(TaskNetworkCore0, "NetworkCore0", 8192, NULL, 1, NULL, 0);
#if UWB_TAG_ENABLED
    xTaskCreatePinnedToCore(TaskUwbCore0, "UwbCore0", 4096, NULL, 2, NULL, 0);
#else
    Serial.println("[UWB] Desativado neste perfil de teste RGB; CS permanece HIGH.");
#endif
    xTaskCreatePinnedToCore(TaskCameraHttpCore0, "CameraHttpCore0", 8192, NULL, 1, NULL, 0);
}

void loop() {
    vTaskDelay(pdMS_TO_TICKS(1000));
}
