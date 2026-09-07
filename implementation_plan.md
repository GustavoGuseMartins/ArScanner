# Plano de Implementação: Arquitetura Multissensorial, Otimização de Processamento e Visualização AR (TCC UEPG)

**Autores:** Gustavo Guse Martins & Kevin Kurpias Rodrigues  
**Orientador:** Jonathan de Matos  
**Instituição:** Universidade Estadual de Ponta Grossa (UEPG) - Engenharia de Computação  

---

## 1. Visão Geral da Arquitetura Físico-Digital

O sistema opera com três nós computacionais trabalhando em tempo real:
1. **Nó Móvel (Scanner Aéreo no Drone):** ESP32-S3 N16R8 acoplado ao LiDAR Roborock, Motor NEMA 14 Pancake (TMC2209), Câmera Óptica OV2640 (superior), Câmera Térmica MLX90640ESF-BAA (inferior), IMU MPU6050 (traseira) e 1x Tag UWB DWM1000.
2. **Nó Base (Visualizador USB-C):** ESP32-WROOM-32 conectado via USB-C OTG ao celular, controlando 3x Âncoras UWB DWM1000 em base triangular para trilateração métrica absoluta.
3. **Nó Visualizador (Celular Android - Samsung Galaxy S23+):** Aplicativo Unity 6 com Universal Render Pipeline (URP) e ARCore, executando fusão de dados, simplificação geométrica planar (LOD) e visualização termo-espacial através de paredes.

```mermaid
graph TD
    subgraph Scanner Aéreo (ESP32-S3 @ 240MHz)
        LIDAR[LiDAR Roborock 2kHz] --> FPU[Processador Vetorial SIMD / FPU]
        NEMA[NEMA 14: Passo Timer] --> FPU
        IMU[MPU6050: Pitch e Roll] --> FPU
        CAM_RGB[OV2640: Superior DMA PSRAM] --> FPU
        CAM_TH[MLX90640: Inferior 110°x75°] --> FPU
        FPU --> BATCH[Buffer Agrupador 15-20 pts]
        BATCH -->|Wi-Fi SoftAP TCP 100pkts/s| NET[Wi-Fi ArScanner_Net]
    end

    subgraph Base Receptora (ESP32-WROOM-32 @ 240MHz)
        UWB_A1[DWM1000 Ancora 1] --> SPI[Barramento SPI 16MHz]
        UWB_A2[DWM1000 Ancora 2] --> SPI
        UWB_A3[DWM1000 Ancora 3] --> SPI
        SPI --> EKF[Trilateracao 3D + Filtro EKF]
        EKF -->|USB-C CDC Serial 50Hz| USBC[Cabo USB-C OTG]
    end

    subgraph Visualizador AR (Samsung Galaxy S23+)
        NET --> UNITY_NET[Receptor TCP Zero-GC]
        USBC --> UNITY_UWB[Receptor UWB USB-C]
        UNITY_UWB & UNITY_NET --> AR_FUSION[Fusao Espacial ARCore]
        AR_FUSION --> LOD[Filtro Voxel Grid + LOD Planar]
        LOD --> XRAY[Shader Termo-Visual Raio-X]
        XRAY --> SCREEN[Tela: Pontos Termicos + Transparencia de Paredes]
    end
```

---

## 2. Dimensionamento Técnico e Cálculos de CPU

Os cálculos abaixo têm como base os manuais de referência do **ESP32-S3**, **Melexis MLX90640** e benchmarks do **Snapdragon 8 Gen 2**:

| Módulo | Processamento / Frequência | Carga Estimada de CPU | Margem Livre | Função Alocada |
| :--- | :--- | :--- | :--- | :--- |
| **ESP32-S3 Core 1** | 240 MHz (Xtensa LX7) | **$\approx 26\%$** | **$> 70\%$** | Leitura LiDAR, pulso TMC2209, I2C MLX90640, DMA OV2640, atitude MPU6050 e projeção de coordenadas. |
| **ESP32-S3 Core 0** | 240 MHz (Xtensa LX7) | **$\approx 4\%$** | **$> 90\%$** | Pilha Wi-Fi TCP, buffer de agrupamento (15 pts/pacote), servidor de rede. |
| **ESP32-WROOM Base** | 240 MHz (Xtensa LX6) | **$\approx 8\%$** | **$> 90\%$** | Leitura ToF dos 3 DWM1000, trilateração analítica 3D, filtro de ruído EKF e transmissão USB-C. |
| **Galaxy S23+ (GPU)** | Adreno 740 (3.5 TFLOPS) | **$< 5\%$** | **$> 95\%$** | Renderização de até 100.000 pontos em lote único a 60-120 FPS cravados. |

### 2.1. Alocação de Processamento no ESP32-S3 do Scanner
Com a sobra de mais de 70% de processamento no Core 1, alocamos para o ESP32-S3:
1. **Conversão Esférico $\rightarrow$ Cartesiano:** Utiliza a extensão vetorial (PIE / FPU) do ESP32-S3 para calcular $(x, y, z)$ diretamente no chip.
2. **Correção de Inclinação em Voo:** O MPU6050 (instalado no verso da placa) fornece Pitch e Roll instantâneos. O ESP32-S3 aplica a rotação inversa no feixe para que o ponto saia do drone nivelado com o horizonte.
3. **Lookup de Textura e Calor:** 
   * Câmera normal em cima: indexa cor $(R, G, B)$ no buffer DMA da OV2640.
   * Câmera térmica embaixo ($110^\circ \times 75^\circ$): indexa a temperatura $T$ nos 768 pixels da MLX90640.
4. **Agrupamento de Pacotes (Batching Anti-Latência):** Em vez de enviar 2.000 pacotes avulsos por segundo (que travariam a pilha TCP), o ESP32-S3 agrupa 15 medições por quadro ($\sim 450\text{ bytes}$), transmitindo a apenas $\sim 120\text{ pacotes/s}$, reduzindo a latência de rede para $< 10\text{ ms}$.

### 2.2. Alocação de Processamento no ESP32-WROOM da Base
O microcontrolador do visor executará:
1. **Trilateração 3D Analítica (Fang / Gauss-Newton):** Resolve a posição tridimensional $(X, Y, Z)$ da Tag do drone a partir dos 3 tempos de voo.
2. **Filtro de Kalman Estendido (EKF):** Amortece o ruído de rádio milimétrico e rejeita saltos causados por reflexões metálicas (*multipath*).
3. **Transmissão Contínua a 50Hz via USB-C CDC:** Entrega ao celular um pacote enxuto de 28 bytes sem onerar o processador do celular.

---

## 3. Disposição Física e Alinhamento Mecânico

Conforme validado no modelo CAD:
1. **Câmera Normal (OV2640):** Posição **SUPERIOR**, apontada para a frente ($+Z$).
2. **Câmera Térmica (MLX90640-BAA):** Posição **INFERIOR**, apontada para a frente ($+Z$).
   * Ambas compartilham o mesmo eixo azimutal central ($\Delta \phi = 0^\circ$).
   * O deslocamento vertical entre as lentes ($\Delta Y \approx 18\text{ mm}$) é calibrado na equação de projeção angular.
3. **MPU6050:** Montado **de costas com o LiDAR** na parte traseira da placa.
   * Inversão física dos eixos no firmware: $\text{Pitch}_{\text{feixe}} = -\text{Pitch}_{\text{mpu}}$.

---

## 4. Algoritmo de Otimização Geométrica (LOD) e Visão Através de Paredes (X-Ray)

### 4.1. Fusão de Pontos e Simplificação Planar (O Exemplo do Armário)
Para evitar que milhares de pontos sobrecarreguem o visor ao escanear superfícies contínuas:
1. **Voxel Grid Hash:** O espaço é discretizado em voxels de $2.5\text{ cm}$. Feixes múltiplos na mesma célula apenas atualizam a média de cor/temperatura sem alocar memória extra.
2. **Segmentação Planar (RANSAC / Normal Clustering):** Pontos vizinhos coplanares com mesma orientação angular de normal são agrupados. Em superfícies amplas (como portas de armário ou paredes), a nuvem densa de milhares de pontos é convertida em **quadriláteros poligonais simplificados (Quads)**, reduzindo o custo gráfico em até 95%.

### 4.2. Visão Através de Paredes (Modo Raio-X Térmico)
Para que a parede física ou superfícies escaneadas não tapem objetos quentes e assinaturas térmicas internas:
1. **Transparência Holográfica com Slider:** As superfícies classificadas como paredes/estruturas recebem shader translúcido com controle de opacidade de $0\%$ (invisível) a $100\%$ (sólido).
2. **Thermal Depth-Priority (Bypass de Oclusão):** Pontos cuja temperatura ultrapasse o limiar definido (ex: $T > 28^\circ\text{C}$ ou fontes térmicas de interesse) são renderizados no canal emissivo sobrepondo a geometria fria da parede. O usuário vê a silhueta de calor brilhando através da parede no celular!

---

## 5. Estrutura do Pacote de Dados de Alta Performance

Para garantir máxima eficiência de transmissão, o pacote binário compartilhado entre o ESP32-S3 e o Unity passa a ter **28 bytes fixos**:

```cpp
#pragma pack(push, 1)
struct ScanPointPacket {
    float posX_mm;           // Coordenada X calculada no ESP32-S3 (mm)
    float posY_mm;           // Coordenada Y calculada no ESP32-S3 (mm)
    float posZ_mm;           // Coordenada Z calculada no ESP32-S3 (mm)
    float temperatureC;      // Temperatura da MLX90640 no ponto (°C)
    uint8_t r, g, b;         // Cor real amostrada da OV2640
    uint8_t surfaceFlags;    // Flags: 0=Ponto livre, 1=Superficie planar, 2=Hotspot
    int16_t pitchCentiDeg;   // Pitch do drone em centigraus (MPU6050)
    int16_t rollCentiDeg;    // Roll do drone em centigraus (MPU6050)
    uint32_t timestampMs;    // Timestamp do pacote
};
#pragma pack(pop)
```

---

## 6. Plano de Implementação por Etapas

### Etapa 1: Atualização do Firmware do Scanner (`firmware/scanner/`)
* [MODIFY] `include/config.h`: Atualizar `ScanPointPacket` com os campos de coordenadas pré-calculadas, cor RGB, temperatura e atitude.
* [MODIFY] `src/main.cpp`: Implementar agrupamento de 15 pontos por pacote TCP no Core 0.
* [NEW] `src/camera_ov2640.cpp` e `include/camera_ov2640.h`: Driver da câmera superior em PSRAM.
* [MODIFY] `src/thermal_mlx90640.cpp`: Lookup com matriz $110^\circ \times 75^\circ$ para a câmera inferior.
* [MODIFY] `src/imu_mpu6050.cpp`: Correção de atitude com inversão de eixos de montagem traseira.

### Etapa 2: Atualização do Firmware da Base (`firmware/viewer/`)
* [MODIFY] `src/main.cpp` e `src/trilateration_3d.cpp`: Implementação do filtro EKF e saída em pacote binário contínuo a 50Hz via USB-C.

### Etapa 3: Atualização do Aplicativo Unity (`Assets/Scripts/`)
* [MODIFY] `PointCloudTcpReceiver.cs`: Parsing dos pacotes agrupados.
* [MODIFY] `ThermalPointCloudRenderer.cs`: Suporte ao modo Raio-X (paredes transparentes + destaque de calor) e integração do filtro planar.
* [MODIFY] `ArScannerHUD.cs`: Controles de transparência de parede e alternância de modos de visualização.

---

## 7. Verificação e Testes

1. **Teste de Carga e Latência do ESP32-S3:** Verificar via osciloscópio / log de rede a entrega contínua dos lotes de 15 pontos em $< 10\text{ ms}$.
2. **Teste de Compensação de Inclinação:** Inclinar a placa na bancada em $\pm 20^\circ$ e comprovar que os pontos gerados na Unity permanecem perfeitamente horizontais.
3. **Teste do Modo Raio-X no Unity:** Simular uma parede fria ($20^\circ\text{C}$) na frente de um cilindro quente ($38^\circ\text{C}$) e verificar se a parede pode ser tornada transparente com o cilindro quente visível através dela.
